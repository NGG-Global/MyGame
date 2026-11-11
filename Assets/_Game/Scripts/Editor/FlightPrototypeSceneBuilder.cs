using System;
using System.IO;
using System.Linq;
using OpenSkies.Assistance;
using OpenSkies.Audio;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.Cockpit;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.Gestures;
using OpenSkies.Gameplay;
using OpenSkies.Missions;
using OpenSkies.Platform;
using OpenSkies.Simulation;
using OpenSkies.UI;
using OpenSkies.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Generates Assets/_Game/Scenes/FlightPrototype.unity from code.
    ///
    /// The scene is rebuilt from scratch on every run, so running it twice never
    /// duplicates objects. Change the scene by changing this builder, not by
    /// editing the scene in the Editor or its YAML: manual edits are overwritten.
    ///
    /// Editor menu: Open Skies > Rebuild Flight Prototype Scene
    /// Batch mode:  -executeMethod OpenSkies.Editor.FlightPrototypeSceneBuilder.BuildFromCommandLine
    /// </summary>
    public static class FlightPrototypeSceneBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/FlightPrototype.unity";
        const string MaterialsFolder = "Assets/_Game/Art/Materials/Prototype";
        const string LitShader = "Universal Render Pipeline/Lit";
        const string UnlitShader = "Universal Render Pipeline/Unlit";
        const string SkyShader = "Skybox/Procedural";
        const string BuiltinFont = "LegacyRuntime.ttf";
        const string AircraftConfigurationPath = "Assets/_Game/Settings/Aircraft/TrainerConfiguration.asset";
        const string AwarenessConfigurationPath = "Assets/_Game/Settings/HUD/FlightAwarenessConfiguration.asset";
        const string AssistConfigurationPath = "Assets/_Game/Settings/Assistance/FlightAssistConfiguration.asset";
        const string MissionConfigurationPath = "Assets/_Game/Settings/Missions/MissionConfiguration.asset";
        const int AircraftLayer = 2; // built-in "Ignore Raycast": wheel raycasts must not hit the aircraft itself

        // Layout, in metres (PrototypeWorld). The runway runs south to north along +Z.
        const float RunwayLength = PrototypeWorld.RunwayLengthMetres;  // take-off needs ~350 m, landing roll ~250 m with brakes
        const float RunwayWidth = PrototypeWorld.RunwayWidthMetres;
        const float RunwayThickness = 0.1f;
        static readonly Vector3 AircraftStart = new Vector3(0f, 0f, -RunwayLength / 2f + 40f);

        [MenuItem("Open Skies/Rebuild Flight Prototype Scene")]
        static void RebuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        /// <summary>Entry point for -executeMethod. Exits with code 1 on failure.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            RenderPipelineSetup.EnsureUrp();
            var materials = new PrototypeMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = CreateLighting(materials.Sky);
            // Scenery steps are isolated: one failing step is logged and skipped, so the scene always ends up
            // with the ground, the runway, the aircraft, the camera and the session (a half-built scene fails at launch).
            Step("post-processing", CreatePostProcessing);
            if (!Step("terrain", () => CreateGround(materials))) CreateFlatGroundFallback(materials);
            var beacons = CreateCoast(materials);
            CreateRunway(materials);
            PrototypeEnvironment.CreateApron(materials.Taxiway, materials.Concrete, materials.HangarWalls, materials.HangarRoof, RunwayWidth);
            Step("airfield dressing", () => SceneryBuilder.CreateAirfieldDressing(materials.Scenery, RunwayWidth));
            Step("town", () => SceneryBuilder.CreateTown(materials.Scenery));
            Step("clouds", () => SceneryBuilder.CreateClouds(materials.Cloud));
            Step("trees", () => TerrainBuilder.CreateTrees(materials.Scenery.Palette));
            // The owner's licensed display aircraft on the apron (static scenery, not flyable).
            Step("display aircraft", () => DisplayAircraft.Create(materials.DisplayRoles, new[] { materials.DisplayPaintF16, materials.DisplayPaintSpitfire, materials.DisplayPaintBeech }));
            var configuration = LoadOrCreateAircraftConfiguration();
            var aircraft = CreateAircraft(configuration, materials, out var cockpitAnchor, out var cockpit, out var lights, out var airframe);
            var cameraRig = CreateCamera(aircraft.transform, cockpitAnchor);
            var hud = CreateHud(cameraRig.ViewCamera, out var touchControls, out var contextualHud, out var cockpitInteraction, out var focusPanel,
                out var commandCluster, out var flightSettings, out var guidanceLine, out var missionUi, out var interactionSurface, out var gestureHud,
                out var flightInfo);
            var audioManager = CreateAudio();
            cockpitInteraction.cameraRig = cameraRig;
            CreateEventSystem();

            // Wired while inactive so FlightSession.Awake sees every reference.
            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            var session = sessionGo.AddComponent<FlightSession>();
            session.aircraft = aircraft;
            session.telemetry = aircraft.GetComponent<AircraftTelemetry>();
            session.cameraRig = cameraRig;
            session.hud = hud;
            session.commandCluster = commandCluster;
            session.touchControls = touchControls;
            session.contextualHud = contextualHud;
            session.flightInfo = flightInfo;
            session.cockpit = cockpit;
            session.cockpitInteraction = cockpitInteraction;
            session.focusPanel = focusPanel;
            session.cockpitViewControls = commandCluster.lookForward;
            session.aircraftLights = lights;
            session.airframeView = airframe;
            session.awarenessConfiguration = LoadOrCreateAwarenessConfiguration();
            session.assistConfiguration = LoadOrCreateAssistConfiguration();
            session.flightSettings = flightSettings;
            session.guidanceLine = guidanceLine;
            session.interactionSurface = interactionSurface;
            session.gestureHud = gestureHud;
            session.interactionConfiguration = LoadOrCreateInteractionConfiguration();
            // Runway 36/18: surface centred on the origin, primary landing direction north (+Z).
            session.homeRunway = new RunwayInfo(PrototypeWorld.RunwayCentre, PrototypeWorld.RunwayHeadingDegrees, RunwayLength, RunwayWidth);
            session.rememberCameraMode = true;
            sessionGo.SetActive(true);

            // Game modes (entry screen, First Flight, Landing Challenge) on top of the flight session.
            var modesGo = new GameObject("GameModes");
            modesGo.SetActive(false);
            var modes = modesGo.AddComponent<GameModeSession>();
            modes.flight = session;
            modes.configuration = LoadOrCreateMissionConfiguration();
            modes.modeSelect = missionUi.ModeSelect;
            modes.entryScreen = missionUi.Entry;
            modes.card = missionUi.Card;
            modes.pauseSheet = missionUi.PauseSheet;
            modes.coach = missionUi.Coach;
            modes.highlights = missionUi.Highlights;
            modes.marker = missionUi.Marker;
            modes.beacons = beacons;
            modesGo.SetActive(true);

            // Presentation: options and settings screen, sound, haptics, graphics presets, pause of all of it.
            var presentationGo = new GameObject("Presentation");
            presentationGo.SetActive(false);
            var presentation = presentationGo.AddComponent<PresentationSession>();
            presentation.flight = session;
            presentation.modes = modes;
            presentation.audioManager = audioManager;
            presentation.settings = missionUi.Settings;
            presentation.settingsInSheet = missionUi.PauseSheet != null ? missionUi.PauseSheet.settings : null;
            presentation.canvasScaler = missionUi.Scaler;
            presentation.sun = sun;
            presentationGo.SetActive(true);

            // Development builds: frame-time statistics and device counters on screen (and a CSV log on request).
            var performance = new GameObject("PerformanceMonitor").AddComponent<PerformanceMonitor>();
            performance.overlay = missionUi.PerformanceText;

            EditorFolders.Ensure(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save {ScenePath}");
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[OpenSkies] Built {ScenePath}");
        }

        /// <summary>Runs one optional scene step; a failure is logged (with its exception) and the build goes on without it.</summary>
        static bool Step(string name, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[OpenSkies] Scene step '{name}' failed and was skipped: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>The flat land plane of the earlier prototype, used only when the terrain step fails: the wheels need ground.</summary>
        static void CreateFlatGroundFallback(PrototypeMaterials m)
        {
            float width = PrototypeEnvironment.GrassEastX - PrototypeWorld.LandWestX;
            float length = PrototypeWorld.WorldNorthZ - PrototypeWorld.WorldSouthZ;
            var centre = new Vector3((PrototypeEnvironment.GrassEastX + PrototypeWorld.LandWestX) * 0.5f, PrototypeWorld.LandHeight,
                (PrototypeWorld.WorldNorthZ + PrototypeWorld.WorldSouthZ) * 0.5f);
            var ground = Primitive(PrimitiveType.Plane, "GroundFallback", null, centre, new Vector3(width / 10f, 1f, length / 10f), m.Meadow, keepCollider: true);
            ground.isStatic = true;
            Debug.LogWarning("[OpenSkies] Terrain failed: a flat ground plane stands in for it.");
        }

        /// <summary>Puts the prototype scene first in the build, keeping any other scenes.</summary>
        public static void RegisterInBuildSettings()
        {
            var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
        }

        static Light CreateLighting(Material sky)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Hard; // soft shadow filtering is costly on mobile GPUs
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.skybox = sky;
            PrototypeEnvironment.ApplyAtmosphere(sun, sky); // haze, daylight ambient, sky tuning
            return sun;
        }

        /// <summary>The global post-processing volume and the start-up environment reflections.</summary>
        static void CreatePostProcessing()
        {
            var volumeGo = new GameObject("GlobalVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = RenderPipelineSetup.EnsurePostProcessProfile();
            volumeGo.AddComponent<EnvironmentLighting>();
        }

        static void CreateGround(PrototypeMaterials m)
        {
            // A height-field terrain in tiles (each with its collider) over the whole land area: flat around the
            // airfield, rolling fields, hills in the west. Painted by the land-use albedo and relief normal map,
            // with a fine grass grain every 35 m close up.
            TerrainBuilder.Create(m.Land);
            MaterialSetup.SetBaseMap(m.Land, TerrainBuilder.LoadOrCreateAlbedo(), Vector2.one);
            MaterialSetup.SetNormalMap(m.Land, TerrainBuilder.LoadOrCreateNormal(), 1f);
            MaterialSetup.SetDetail(m.Land, PrototypeEnvironment.GrainTexture("GrassGrain", 28, 0.35f), null,
                new Vector2(TerrainShape.Width / 35f, TerrainShape.Length / 35f));
            // Meadow grass for the islet and the headland: patches every 300 m of a unit texture span and grain every 30 m.
            PrototypeEnvironment.ApplyDetail(m.Meadow, "Grass", 1f, 1f, 0.3f, 0.03f, new Color(0.86f, 0.8f, 0.58f), 0.18f, 0.35f, 11);
        }

        /// <summary>
        /// PLACEHOLDER coastline and landmarks from <see cref="PrototypeWorld"/> (replaced in M5):
        /// the sea east of the shoreline, a low headland with a lighthouse, a harbour breakwater
        /// with a light at its head and an islet. Surfaces have colliders (wheels and the radio
        /// altimeter see them; there is no water physics). Returns the landmark lights.
        /// </summary>
        static LandmarkBeacon[] CreateCoast(PrototypeMaterials m)
        {
            var coast = new GameObject("Coast").transform;
            float width = PrototypeWorld.SeaEastX - PrototypeWorld.ShorelineX;
            float length = PrototypeWorld.WorldNorthZ - PrototypeWorld.WorldSouthZ;
            var seaCentre = new Vector3((PrototypeWorld.SeaEastX + PrototypeWorld.ShorelineX) * 0.5f, PrototypeWorld.SeaHeight,
                (PrototypeWorld.WorldNorthZ + PrototypeWorld.WorldSouthZ) * 0.5f);
            var sea = Primitive(PrimitiveType.Plane, "Sea", coast, seaCentre, new Vector3(width / 10f, 1f, length / 10f), m.Sea, keepCollider: true);
            // Two layers of wave normals (swell every 140 m, chop every 31 m) drifting in different directions, with a fine
            // ripple grain in the detail albedo: a moving sea with a sun glint, no water simulation.
            Step("sea surface", () =>
            {
                var waterNormal = GeneratedAssets.LoadOrCreateTexture(SceneryBuilder.WaterNormalPath, 512, 512, () => SceneryBuilder.WaterNormalPixels(512),
                    GeneratedAssets.TextureKind.Normal, 512);
                MaterialSetup.SetBaseMap(m.Sea, null, new Vector2(width / 140f, length / 140f));
                MaterialSetup.SetNormalMap(m.Sea, waterNormal, 0.55f);
                MaterialSetup.SetDetail(m.Sea, PrototypeEnvironment.GrainTexture("SeaGrain", 48, 0.2f), waterNormal, new Vector2(width / 31f, length / 31f), 1f, 0.45f);
                MaterialSetup.SetSurface(m.Sea, 0.93f, 0f);
                var waves = sea.AddComponent<ScrollingSurface>();
                waves.baseSpeed = new Vector2(0.004f, 0.0025f);
                waves.detailSpeed = new Vector2(-0.012f, 0.02f);
            });
            PrototypeEnvironment.CreateBeach(coast, m.Sand);

            // Headland: a low rocky block from the shore out to sea; its top is 3 m above the sea.
            Vector3 size = PrototypeWorld.HeadlandSize;
            float bottom = PrototypeWorld.SeaHeight - 1f;
            var headland = PrototypeWorld.HeadlandCentre;
            Primitive(PrimitiveType.Cube, "Headland", coast, new Vector3(headland.x, (size.y + bottom) * 0.5f, headland.z),
                new Vector3(size.x, size.y - bottom, size.z), m.Rock, keepCollider: true);

            // Lighthouse: a tapered white tower with red bands, gallery and lantern; the lens flashes as a checkpoint.
            var lighthouse = new GameObject("Lighthouse").transform;
            lighthouse.SetParent(coast, false);
            lighthouse.localPosition = PrototypeWorld.LighthousePosition;
            float tower = PrototypeWorld.LighthouseHeightMetres;
            if (!Step("lighthouse", () => SceneryBuilder.Place("LighthouseTower", lighthouse, SceneryBuilder.Lighthouse(tower), m.Scenery.Palette, Vector3.zero, 0f, collider: true)))
                Primitive(PrimitiveType.Cylinder, "Tower", lighthouse, new Vector3(0f, tower * 0.5f, 0f), new Vector3(7f, tower * 0.5f, 7f), m.Scenery.Palette, keepCollider: true);
            var lighthouseLens = Primitive(PrimitiveType.Sphere, "Light", lighthouse, new Vector3(0f, tower + 2.0f, 0f), Vector3.one * 3.6f, m.Beacon);
            var lighthouseBeacon = lighthouse.gameObject.AddComponent<LandmarkBeacon>();
            lighthouseBeacon.landmarkId = PrototypeWorld.LighthouseId;
            lighthouseBeacon.lens = lighthouseLens.GetComponent<Renderer>();

            // Harbour: a breakwater from the shore out to sea, a short arm at its head, a light, moored boats.
            var harbour = new GameObject("Harbour").transform;
            harbour.SetParent(coast, false);
            Vector3 start = PrototypeWorld.BreakwaterStart, head = PrototypeWorld.HarbourLightPosition;
            float run = head.x - start.x;
            Primitive(PrimitiveType.Cube, "Breakwater", harbour, new Vector3(start.x + run * 0.5f, (2f + bottom) * 0.5f, start.z),
                new Vector3(run, 2f - bottom, 12f), m.Rock, keepCollider: true);
            Primitive(PrimitiveType.Cube, "Arm", harbour, new Vector3(head.x, (2f + bottom) * 0.5f, start.z + 130f),
                new Vector3(12f, 2f - bottom, 260f), m.Rock, keepCollider: true);
            Primitive(PrimitiveType.Cylinder, "LightPost", harbour, new Vector3(head.x, head.y + 3f, head.z), new Vector3(1.4f, 3f, 1.4f), m.LighthouseRed);
            var harbourLens = Primitive(PrimitiveType.Sphere, "Light", harbour, new Vector3(head.x, head.y + 6.8f, head.z), Vector3.one * 2.2f, m.Beacon);
            var harbourBeacon = harbour.gameObject.AddComponent<LandmarkBeacon>();
            harbourBeacon.landmarkId = PrototypeWorld.HarbourId;
            harbourBeacon.lens = harbourLens.GetComponent<Renderer>();
            Step("harbour", () => SceneryBuilder.CreateHarbour(harbour, m.Scenery)); // quay, bollards and moored boats

            // Islet: a low mound rising out of the sea (mesh collider for its real shape).
            float radius = PrototypeWorld.IsletRadiusMetres, height = PrototypeWorld.IsletHeightMetres;
            var islet = Primitive(PrimitiveType.Sphere, "Islet", coast,
                new Vector3(PrototypeWorld.IsletCentre.x, PrototypeWorld.SeaHeight - height * 0.6f, PrototypeWorld.IsletCentre.z),
                new Vector3(radius * 2f, height * 3.2f, radius * 2f), m.Meadow);
            islet.AddComponent<MeshCollider>().sharedMesh = islet.GetComponent<MeshFilter>().sharedMesh;

            foreach (var renderer in coast.GetComponentsInChildren<Renderer>())
                if (renderer.gameObject.name != "Light") renderer.gameObject.isStatic = true;
            return new[] { lighthouseBeacon, harbourBeacon };
        }

        static void CreateRunway(PrototypeMaterials m)
        {
            Material marking = m.Marking;
            var runway = new GameObject("Runway").transform;
            // Top surface at y = 0, where the flight model puts the wheels.
            Primitive(PrimitiveType.Cube, "Surface", runway, new Vector3(0f, -RunwayThickness / 2f, 0f),
                new Vector3(RunwayWidth, RunwayThickness, RunwayLength), m.Asphalt, keepCollider: true);

            // Centreline dashes: 30 m dash, 20 m gap.
            for (float z = -RunwayLength / 2f + 80f; z < RunwayLength / 2f - 80f; z += 50f)
                Primitive(PrimitiveType.Cube, "Centreline", runway, new Vector3(0f, 0.01f, z), new Vector3(0.9f, 0.02f, 30f), marking);

            // Threshold bars at both ends.
            foreach (float end in new[] { -1f, 1f })
            for (int i = 0; i < 8; i++)
            {
                float x = (i < 4 ? -1f : 1f) * (3f + (i % 4) * 3f);
                Primitive(PrimitiveType.Cube, "Threshold", runway,
                    new Vector3(x, 0.01f, end * (RunwayLength / 2f - 25f)), new Vector3(1.8f, 0.02f, 30f), marking);
            }

            PrototypeEnvironment.CreateRunwayMarkings(runway, marking, RunwayLength, RunwayWidth);
            Step("runway numbers", () => SceneryBuilder.CreateRunwayNumbers(runway, marking, RunwayLength));
            Step("runway lights", () => SceneryBuilder.CreateRunwayLights(runway, m.Scenery, RunwayLength, RunwayWidth));
            foreach (Transform child in runway) child.gameObject.isStatic = true;
        }

        /// <summary>The audio manager with the generated mixer and configuration (created once, then left alone).</summary>
        static AudioManager CreateAudio()
        {
            var manager = new GameObject("Audio").AddComponent<AudioManager>();
            manager.configuration = AudioMixerSetup.LoadOrCreateConfiguration();
            return manager;
        }

        /// <summary>
        /// The configuration is created once and then left alone, so tuning done in
        /// the Inspector survives scene rebuilds.
        /// </summary>
        static AircraftConfiguration LoadOrCreateAircraftConfiguration()
        {
            var configuration = AssetDatabase.LoadAssetAtPath<AircraftConfiguration>(AircraftConfigurationPath);
            if (configuration != null) return configuration;

            EditorFolders.Ensure(Path.GetDirectoryName(AircraftConfigurationPath));
            configuration = ScriptableObject.CreateInstance<AircraftConfiguration>();
            AssetDatabase.CreateAsset(configuration, AircraftConfigurationPath);
            Debug.Log($"[OpenSkies] Created {AircraftConfigurationPath}");
            return configuration;
        }

        /// <summary>Phase, warning and HUD disclosure tuning: created once, then left alone like the aircraft configuration.</summary>
        static FlightAwarenessConfiguration LoadOrCreateAwarenessConfiguration()
        {
            var configuration = AssetDatabase.LoadAssetAtPath<FlightAwarenessConfiguration>(AwarenessConfigurationPath);
            if (configuration != null) return configuration;

            EditorFolders.Ensure(Path.GetDirectoryName(AwarenessConfigurationPath));
            configuration = ScriptableObject.CreateInstance<FlightAwarenessConfiguration>();
            AssetDatabase.CreateAsset(configuration, AwarenessConfigurationPath);
            Debug.Log($"[OpenSkies] Created {AwarenessConfigurationPath}");
            return configuration;
        }

        /// <summary>Tutorial, landing challenge and scoring tuning: created once, then left alone like the other configurations.</summary>
        /// <summary>The gesture language's feel, created once at Assets/_Game/Settings/HUD and never overwritten.</summary>
        static InteractionConfiguration LoadOrCreateInteractionConfiguration()
        {
            const string path = "Assets/_Game/Settings/HUD/InteractionConfiguration.asset";
            var existing = AssetDatabase.LoadAssetAtPath<InteractionConfiguration>(path);
            if (existing != null)
            {
                MigrateWheelGeometry(existing, path);
                return existing;
            }
            EditorFolders.Ensure("Assets/_Game/Settings/HUD");
            var asset = ScriptableObject.CreateInstance<InteractionConfiguration>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// The tap-first systems wheel (docs/TAP_DISCLOSURE_DESIGN.md, milestone 2) prefers a larger band
        /// and a narrower end tolerance than the M13 arc; it is fitted to the screen from them at runtime.
        /// An asset still holding the untouched M13 defaults (band 170-290, end tolerance 10) is moved to
        /// the new preferred values once; any value the owner tuned is left alone.
        /// </summary>
        static void MigrateWheelGeometry(InteractionConfiguration configuration, string path)
        {
            var arc = configuration.arc;
            if (arc == null) return;
            bool m13Defaults = Mathf.Approximately(arc.innerRadius, 170f) && Mathf.Approximately(arc.outerRadius, 290f)
                               && Mathf.Approximately(arc.endTolerance, 10f);
            if (!m13Defaults) return;
            var preferred = new SystemsArcSettings();
            arc.innerRadius = preferred.innerRadius;
            arc.outerRadius = preferred.outerRadius;
            arc.endTolerance = preferred.endTolerance;
            EditorUtility.SetDirty(configuration);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OpenSkies] {path}: systems wheel geometry moved from the M13 defaults to the milestone 2 preferred values");
        }

        static MissionConfiguration LoadOrCreateMissionConfiguration()
        {
            var configuration = AssetDatabase.LoadAssetAtPath<MissionConfiguration>(MissionConfigurationPath);
            if (configuration != null) return configuration;

            EditorFolders.Ensure(Path.GetDirectoryName(MissionConfigurationPath));
            configuration = ScriptableObject.CreateInstance<MissionConfiguration>();
            AssetDatabase.CreateAsset(configuration, MissionConfigurationPath);
            Debug.Log($"[OpenSkies] Created {MissionConfigurationPath}");
            return configuration;
        }

        /// <summary>Assistance tuning and first-launch defaults: created once, then left alone like the other configurations.</summary>
        static FlightAssistConfiguration LoadOrCreateAssistConfiguration()
        {
            var configuration = AssetDatabase.LoadAssetAtPath<FlightAssistConfiguration>(AssistConfigurationPath);
            if (configuration != null) return configuration;

            EditorFolders.Ensure(Path.GetDirectoryName(AssistConfigurationPath));
            configuration = ScriptableObject.CreateInstance<FlightAssistConfiguration>();
            AssetDatabase.CreateAsset(configuration, AssistConfigurationPath);
            Debug.Log($"[OpenSkies] Created {AssistConfigurationPath}");
            return configuration;
        }

        /// <summary>
        /// The fictional four-seat high-wing trainer, modelled by <see cref="AircraftMeshBuilder"/>
        /// and flown by Rigidbody physics. The root pivot sits at wheel-contact level with the
        /// gear fully extended; +Z is the nose. Wheel positions come from the configuration so
        /// visuals and physics agree.
        /// </summary>
        static AircraftController CreateAircraft(AircraftConfiguration configuration, PrototypeMaterials materials, out Transform cockpitAnchor,
            out CockpitRig cockpit, out AircraftLightsView lights, out AirframeView airframe)
        {
            var rootGo = new GameObject("Aircraft");
            var root = rootGo.transform;
            root.SetPositionAndRotation(AircraftStart, Quaternion.identity);

            var body = new GameObject("Visual").transform;
            body.SetParent(root, false);

            // The modelled trainer (fuselage, wings with flaps and ailerons, tail, propeller, gear) and the view that moves its parts.
            // If the model cannot be built, the aircraft keeps flying as a few boxes rather than not at all.
            AirframeView builtAirframe = null;
            if (!Step("aircraft model", () => builtAirframe = AircraftMeshBuilder.Build(body, configuration, materials.Aircraft)))
            {
                foreach (Transform child in body) UnityEngine.Object.DestroyImmediate(child.gameObject);
                Primitive(PrimitiveType.Cube, "Fuselage", body, new Vector3(0f, 1.35f, 0.2f), new Vector3(1.2f, 1.3f, 4.2f), materials.Aircraft.Paint);
                Primitive(PrimitiveType.Cube, "TailBoom", body, new Vector3(0f, 1.45f, -2.9f), new Vector3(0.7f, 0.8f, 2.8f), materials.Aircraft.Paint);
                Primitive(PrimitiveType.Cube, "Wing", body, new Vector3(0f, 2.1f, 0.4f), new Vector3(11f, 0.15f, 1.6f), materials.Aircraft.Paint);
                Primitive(PrimitiveType.Cube, "Stabiliser", body, new Vector3(0f, 1.55f, -4.1f), new Vector3(3.6f, 0.1f, 0.9f), materials.Aircraft.Paint);
                Primitive(PrimitiveType.Cube, "Fin", body, new Vector3(0f, 2.3f, -4.1f), new Vector3(0.1f, 1.4f, 1.1f), materials.Aircraft.Paint);
                foreach (var wheel in configuration.wheels)
                    Primitive(PrimitiveType.Cylinder, wheel.name, body, AircraftMeshBuilder.WheelCentre(wheel), new Vector3(wheel.wheelRadius * 2f, 0.08f, wheel.wheelRadius * 2f),
                        materials.Tyre).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            airframe = builtAirframe;

            // Collision shapes on the root (one compound collider for the Rigidbody).
            // They sit clear of the ground while on the wheels, so they only matter in a crash or tail strike.
            AddBox(rootGo, new Vector3(0f, 1.35f, 0.5f), new Vector3(1.2f, 1.3f, 4.8f));   // cabin and cowling
            AddBox(rootGo, new Vector3(0f, 1.45f, -2.9f), new Vector3(0.7f, 0.8f, 2.8f));  // tail boom
            AddBox(rootGo, new Vector3(0f, 2.2f, 0.4f), new Vector3(11f, 0.2f, 1.65f));    // wing
            AddBox(rootGo, new Vector3(0f, 1.9f, -4.1f), new Vector3(3.6f, 1.4f, 1.0f));   // tail surfaces

            // Left seat eye point.
            cockpitAnchor = new GameObject("CockpitCameraAnchor").transform;
            cockpitAnchor.SetParent(root, false);
            // Eye height chosen so the view over the cowling (top at 1.75 m) reaches below the horizon.
            cockpitAnchor.localPosition = new Vector3(-0.3f, 1.9f, 0.9f);
            // Resting view slightly down, so the instrument panel is in view (look offsets are relative to this).
            cockpitAnchor.localRotation = Quaternion.Euler(CockpitLayout.RestingPitchDegrees, 0f, 0f);

            // Placeholder cockpit interior (instruments, switches, flap selector) and exterior lights.
            var font = Resources.GetBuiltinResource<Font>(BuiltinFont);
            cockpit = CockpitFactory.BuildInterior(cockpitAnchor, font, materials.Cockpit);
            lights = CockpitFactory.BuildExteriorLights(body, materials.Cockpit, AircraftMeshBuilder.ExteriorLights);

            SetLayerRecursively(rootGo, AircraftLayer);

            // AddComponent adds the required Rigidbody; the controller configures it in Awake.
            var controller = rootGo.AddComponent<AircraftController>();
            controller.configuration = configuration;
            controller.groundLayers = Physics.DefaultRaycastLayers; // excludes the aircraft's own layer
            rootGo.AddComponent<AircraftTelemetry>();
            return controller;
        }

        static void AddBox(GameObject go, Vector3 centre, Vector3 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        static CameraRig CreateCamera(Transform aircraft, Transform cockpitAnchor)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.08f; // cockpit eye point is 0.1 m below the cabin roof
            camera.farClipPlane = 6000f;
            camera.fieldOfView = 60f;
            camera.allowHDR = true;
            go.AddComponent<AudioListener>();
            // Post-processing and FXAA on the view (the graphics presets switch them at runtime).
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            cameraData.volumeLayerMask = 1; // Default: the global volume's layer

            var rig = go.AddComponent<CameraRig>();
            rig.target = aircraft;
            rig.cockpitAnchor = cockpitAnchor;
            rig.mode = CameraMode.Chase;
            go.transform.position = ChaseCameraMath.DesiredPosition(aircraft.position, aircraft.rotation, rig.chaseDistance, rig.chaseHeight);
            go.transform.LookAt(aircraft.position + Vector3.up * rig.lookAheadHeight);
            return rig;
        }

        /// <summary>Mission UI parts, wired into the game modes.</summary>
        sealed class MissionUi
        {
            public ModeSelectScreen ModeSelect;
            public MissionCard Card;
            public CoachingPanel Coach;
            public ControlHighlights Highlights;
            public CheckpointMarker Marker;
            public SettingsScreen Settings;
            public PauseSheet PauseSheet;
            public EntryStationsScreen Entry;
            public CanvasScaler Scaler;
            public Text PerformanceText;
        }

        static HudPresenter CreateHud(Camera viewCamera, out TouchFlightControls touchControls, out ContextualHud contextualHud,
            out CockpitInteractionManager cockpitInteraction, out CockpitFocusPanel focusPanel, out CommandCluster commandCluster,
            out FlightSettingsPanel flightSettings, out GuidanceLine guidanceLine, out MissionUi missionUi,
            out InteractionSurface interactionSurface, out GestureHud gestureHud, out FlightInfoCluster flightInfo)
        {
            missionUi = new MissionUi();
            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f); // the player's interface scale changes this at runtime
            scaler.matchWidthOrHeight = 1f; // landscape: scale by height
            missionUi.Scaler = scaler;

            var safeArea = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter)).GetComponent<RectTransform>();
            safeArea.SetParent(canvasGo.transform, false);
            Stretch(safeArea);

            var font = Resources.GetBuiltinResource<Font>(BuiltinFont);

            // Interaction surface: first child, so it is behind every control and receives only the
            // touches nothing else took; it routes them to gestures or, in cockpit view, to the cockpit
            // interaction (which lives on its own object without a graphic). It draws nothing.
            var surfaceGo = new GameObject("InteractionSurface", typeof(RectTransform), typeof(RaycastOnlyGraphic));
            var surfaceRect = (RectTransform)surfaceGo.transform;
            surfaceRect.SetParent(safeArea, false);
            Stretch(surfaceRect);
            var surfaceGraphic = surfaceGo.GetComponent<RaycastOnlyGraphic>();
            surfaceGraphic.raycastTarget = true;
            interactionSurface = surfaceGo.AddComponent<InteractionSurface>();
            interactionSurface.surface = surfaceGraphic;

            var cockpitGo = new GameObject("CockpitInteraction", typeof(RectTransform));
            ((RectTransform)cockpitGo.transform).SetParent(safeArea, false);
            cockpitInteraction = cockpitGo.AddComponent<CockpitInteractionManager>();
            cockpitInteraction.surface = null; // routed by the interaction surface, not by its own raycast target

            // Developer telemetry line, top-left below the flight information cluster's open panel. Temporary (see HudPresenter).
            var readout = NewText("DeveloperReadout", safeArea, font, 26, TextAnchor.UpperLeft);
            var readoutRect = readout.rectTransform;
            readoutRect.anchorMin = new Vector2(0f, 1f);
            readoutRect.anchorMax = new Vector2(0.6f, 1f);
            readoutRect.pivot = new Vector2(0f, 1f);
            readoutRect.anchoredPosition = new Vector2(24f, -220f);
            readoutRect.sizeDelta = new Vector2(-48f, 40f);
            readout.color = new Color(1f, 1f, 1f, 0.8f);
            readout.raycastTarget = false;

            // Development performance line under the telemetry line (PerformanceMonitor; never in release builds).
            var perf = NewText("PerformanceReadout", safeArea, font, 22, TextAnchor.UpperLeft);
            var perfRect = perf.rectTransform;
            perfRect.anchorMin = new Vector2(0f, 1f);
            perfRect.anchorMax = new Vector2(0.6f, 1f);
            perfRect.pivot = new Vector2(0f, 1f);
            perfRect.anchoredPosition = new Vector2(24f, -330f);
            perfRect.sizeDelta = new Vector2(-48f, 32f);
            perf.color = new Color(1f, 1f, 1f, 0.75f);
            perf.raycastTarget = false;
            missionUi.PerformanceText = perf;

            // Touch flight controls: joystick lower left, throttle and brakes lower right.
            // Built by runtime code so tests use the identical layout.
            touchControls = TouchControlsFactory.Build(safeArea, font);

            // Cockpit focus panel (enlarged instruments, system selectors) in the free column between
            // the joystick region and the action rail; before the contextual HUD so warnings draw over it.
            focusPanel = CockpitFocusPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset, interactionSurface);

            // Flight assistance settings (same free column, never open with the focus panel) and the flying-tip
            // line along the bottom of that column; also before the contextual HUD, so warnings draw over them.
            flightSettings = FlightSettingsPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            guidanceLine = FlightSettingsPanelFactory.BuildGuidanceLine(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);

            // Mission coaching in the same place as the flying tips (never both), and the checkpoint
            // marker, both under the contextual HUD so warnings draw over them.
            missionUi.Coach = MissionUiFactory.BuildCoachingPanel(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            missionUi.Marker = MissionUiFactory.BuildCheckpointMarker(safeArea, font, viewCamera);

            // Contextual HUD (warnings, the legacy action rail, inspector),
            // built after the touch controls so it draws on top; it overlaps none of them.
            contextualHud = ContextualHudFactory.Build(safeArea, font);

            // Gesture-first HUD: the systems handle (where SYSTEMS was), the HUD band, the arc, the flap
            // lever, the density ladder and the explanation plate, over the contextual HUD and under
            // the mission UI (a modal card still dims and blocks everything).
            gestureHud = GestureHudFactory.Build(safeArea, font, interactionSurface);

            // The top-left flight information cluster (the capsule, the panel of readings and guides, the
            // focused reading; docs/TAP_DISCLOSURE_DESIGN.md, milestone 4), over the gesture HUD so it takes
            // its own taps over the HUD band, and under the command cluster and the mission UI.
            flightInfo = FlightInfoClusterFactory.Build(safeArea, font);

            // The top-right command cluster (camera, options, pause; LOOK FWD under the camera; the options panel
            // with the HUD profile and the session actions), over the HUDs; then the control highlights over the
            // controls (no touches), the mission card (a modal pause card dims and blocks everything else), and
            // the entry screen on top.
            commandCluster = CommandClusterFactory.Build(safeArea, font);
            missionUi.Highlights = MissionUiFactory.BuildHighlights(safeArea);
            missionUi.Card = MissionUiFactory.BuildMissionCard(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset,
                CockpitFocusPanelFactory.ColumnBottom, CockpitFocusPanelFactory.ColumnTop);
            // The pull-up pause sheet (gesture-first) over the HUD and the card; then the two entry
            // screens: the legacy mode select and the spatial stations (the session shows one).
            missionUi.PauseSheet = PauseSheetFactory.Build(safeArea, font, interactionSurface);
            missionUi.ModeSelect = MissionUiFactory.BuildModeSelect(safeArea, font);
            missionUi.Entry = EntryStationsFactory.Build(safeArea, font, interactionSurface);
            // Settings on top of everything: opened over the pause card or the entry screen.
            missionUi.Settings = SettingsScreenFactory.Build(safeArea, font);

            var hud = canvasGo.AddComponent<HudPresenter>();
            hud.developerReadout = readout;
            return hud;
        }

        static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<InputSystemUIInputModule>();
        }

        static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Creates a primitive. Only ground surfaces keep their collider; aircraft parts use the root's compound collider.</summary>
        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material,
            bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Materials are assets so the scene can reference them. Created once, then updated in place.</summary>
        sealed class PrototypeMaterials
        {
            public readonly Material Land = Lit("Land", Color.white, 0.08f);
            public readonly Material Meadow = Lit("Meadow", new Color(0.36f, 0.48f, 0.24f), 0.08f);
            public readonly Material Asphalt = Lit("Asphalt", Color.white, 0.3f);
            public readonly Material Concrete = Lit("Concrete", Color.white, 0.25f);
            public readonly Material Taxiway = Lit("Taxiway", Color.white, 0.25f);
            public readonly Material Marking = Lit("RunwayMarking", new Color(0.92f, 0.92f, 0.9f), 0.2f);
            public readonly Material Accent = Lit("AirframeAccent", new Color(0.12f, 0.32f, 0.62f), 0.55f);
            public readonly Material Tyre = Lit("Tyre", new Color(0.06f, 0.06f, 0.06f), 0.15f);
            public readonly Material Sea = Lit("Sea", new Color(0.03f, 0.19f, 0.30f), 0.93f);
            public readonly Material Rock = Lit("Rock", new Color(0.47f, 0.44f, 0.4f), 0.1f);
            public readonly Material LighthouseRed = Lit("LighthouseRed", new Color(0.72f, 0.12f, 0.1f), 0.3f);
            public readonly Material Beacon = Unlit("Beacon", new Color(3.2f, 2.9f, 2.1f));
            public readonly Material Sand = Lit("Sand", new Color(0.8f, 0.74f, 0.6f), 0.12f);
            public readonly Material HangarWalls = Lit("HangarWalls", new Color(0.82f, 0.82f, 0.8f), 0.3f);
            public readonly Material HangarRoof = Lit("HangarRoof", new Color(0.42f, 0.47f, 0.52f), 0.35f);
            public readonly Material Cloud = Unlit("CloudDeck", Color.white);
            public readonly SceneryBuilder.Materials Scenery;
            // Display aircraft: flat colours in place of the textures that were not supplied.
            public readonly Material DisplayPaintF16 = Lit("DisplayPaintF16", new Color(0.56f, 0.59f, 0.62f), 0.55f);       // light air-superiority grey
            public readonly Material DisplayPaintSpitfire = Lit("DisplayPaintSpitfire", new Color(0.26f, 0.31f, 0.21f), 0.45f); // dark green
            public readonly Material DisplayPaintBeech = Lit("DisplayPaintBeech", new Color(0.62f, 0.08f, 0.07f), 0.75f);     // classic red
            public readonly Material[] DisplayRoles;
            public readonly AircraftMaterials Aircraft;

            public PrototypeMaterials()
            {
                // Airfield surfaces: asphalt tiled across the runway's width, concrete panels on the apron and taxiway.
                var asphalt = GeneratedAssets.LoadOrCreateTexture(SceneryBuilder.AsphaltPath, 512, 512, () => SceneryBuilder.AsphaltPixels(512), GeneratedAssets.TextureKind.Albedo, 512);
                var concrete = GeneratedAssets.LoadOrCreateTexture(SceneryBuilder.ConcretePath, 512, 512, () => SceneryBuilder.ConcretePixels(512), GeneratedAssets.TextureKind.Albedo, 512);
                MaterialSetup.SetBaseMap(Asphalt, asphalt, new Vector2(1f, PrototypeWorld.RunwayLengthMetres / PrototypeWorld.RunwayWidthMetres));
                MaterialSetup.SetBaseMap(Concrete, concrete, new Vector2(130f / 20f, 180f / 20f));
                MaterialSetup.SetBaseMap(Taxiway, concrete, new Vector2(30f / 20f, 18f / 20f));

                // The scenery palette (one material for trees, town, harbour, tower, lighthouse), the runway lights and the clouds.
                var palette = Lit("ScenePalette", Color.white, 0.2f);
                MaterialSetup.SetBaseMap(palette, ScenePalette.LoadOrCreate(), Vector2.one);
                var cloudTexture = GeneratedAssets.LoadOrCreateTexture(SceneryBuilder.CloudTexturePath, 1024, 1024, () => SceneryBuilder.CloudPixels(1024),
                    GeneratedAssets.TextureKind.AlbedoAlpha, 1024);
                MaterialSetup.SetBaseMap(Cloud, cloudTexture, Vector2.one * (SceneryBuilder.CloudDeckSizeMetres / 10000f));
                MaterialSetup.MakeTransparent(Cloud, twoSided: true);
                Scenery = new SceneryBuilder.Materials
                {
                    Palette = palette,
                    Marking = Marking,
                    LightWhite = Unlit("RunwayLightWhite", new Color(2.6f, 2.5f, 2.2f)),
                    LightGreen = Unlit("RunwayLightGreen", new Color(0.5f, 2.6f, 0.9f)),
                    LightRed = Unlit("RunwayLightRed", new Color(2.6f, 0.4f, 0.3f)),
                    LightBlue = Unlit("RunwayLightBlue", new Color(0.5f, 0.9f, 2.6f)),
                    Cloud = Cloud,
                };
                MaterialSetup.SetSurface(DisplayPaintF16, 0.55f, 0.2f);
                MaterialSetup.SetSurface(DisplayPaintBeech, 0.75f, 0.1f);

                // The flown aircraft: the generated livery on a glossy paint, tinted glass, a dark cabin, a polished spinner.
                var livery = GeneratedAssets.LoadOrCreateTexture(AircraftMeshBuilder.LiveryPath, 1024, 1024, AircraftMeshBuilder.LiveryPixels,
                    GeneratedAssets.TextureKind.AlbedoClamped, 1024);
                var paint = Lit("AirframePaint", Color.white, 0.78f);
                MaterialSetup.SetBaseMap(paint, livery, Vector2.one);
                MaterialSetup.SetSurface(paint, 0.78f, 0.05f);
                var glass = Lit("AircraftGlass", new Color(0.3f, 0.42f, 0.52f, 0.42f), 0.97f);
                MaterialSetup.SetSurface(glass, 0.97f, 0.1f);
                MaterialSetup.MakeTransparent(glass);
                var spinner = Lit("AircraftSpinner", new Color(0.82f, 0.83f, 0.85f), 0.85f);
                MaterialSetup.SetSurface(spinner, 0.85f, 0.9f);
                var propellerDisc = Unlit("PropellerDisc", Color.white);
                var discTexture = GeneratedAssets.LoadOrCreateTexture(AircraftMeshBuilder.PropellerDiscPath, 256, 256,
                    () => AircraftMeshBuilder.PropellerDiscPixels(256), GeneratedAssets.TextureKind.AlbedoAlphaClamped, 256);
                MaterialSetup.SetBaseMap(propellerDisc, discTexture, Vector2.one);
                MaterialSetup.MakeTransparent(propellerDisc, twoSided: true);
                Aircraft = new AircraftMaterials
                {
                    Paint = paint,
                    Accent = Lit("AircraftFittings", new Color(0.2f, 0.21f, 0.23f), 0.5f),
                    Glass = glass,
                    Interior = Lit("AircraftInterior", new Color(0.16f, 0.15f, 0.14f), 0.15f),
                    Metal = spinner,
                    Tyre = Tyre,
                    Dark = Lit("PropellerBlade", new Color(0.05f, 0.05f, 0.05f), 0.4f),
                    PropellerDisc = propellerDisc,
                };

                // Index = DisplayAircraft.Role; Paint is replaced per aircraft.
                DisplayRoles = new[]
                {
                    DisplayPaintF16,
                    Lit("DisplaySecondary", new Color(0.46f, 0.48f, 0.5f), 0.35f),
                    Metal("DisplayMetal", new Color(0.72f, 0.73f, 0.75f), 0.75f),
                    Lit("DisplayGlass", new Color(0.12f, 0.15f, 0.19f), 0.92f), // opaque: no transparent overdraw
                    Tyre,
                    Lit("DisplayDark", new Color(0.09f, 0.09f, 0.1f), 0.25f),
                    Cockpit.LensRed,
                    Cockpit.LensGreen,
                };
            }
            public readonly Material Sky = Skybox();
            public readonly CockpitMaterials Cockpit = new CockpitMaterials
            {
                Panel = Lit("CockpitPanel", new Color(0.12f, 0.12f, 0.13f), 0.3f),
                Frame = Lit("CockpitFrame", new Color(0.07f, 0.07f, 0.08f), 0.2f),
                Switch = Lit("CockpitSwitch", new Color(0.8f, 0.8f, 0.8f), 0.6f),
                Indicator = Unlit("CockpitIndicator", new Color(0.15f, 0.15f, 0.15f)),
                LensWhite = Unlit("LightWhite", new Color(1f, 1f, 0.95f)),
                LensRed = Unlit("LightRed", new Color(1f, 0.1f, 0.1f)),
                LensGreen = Unlit("LightGreen", new Color(0.1f, 1f, 0.3f)),
            };

            static Material Lit(string name, Color color, float smoothness)
            {
                var material = LoadOrCreate(name, LitShader);
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", smoothness);
                EditorUtility.SetDirty(material);
                return material;
            }

            static Material Metal(string name, Color color, float smoothness)
            {
                var material = Lit(name, color, smoothness);
                MaterialSetup.SetSurface(material, smoothness, 0.9f);
                return material;
            }

            static Material Unlit(string name, Color color)
            {
                var material = LoadOrCreate(name, UnlitShader);
                material.SetColor("_BaseColor", color);
                EditorUtility.SetDirty(material);
                return material;
            }

            static Material Skybox()
            {
                var material = LoadOrCreate("Sky", SkyShader);
                EditorUtility.SetDirty(material);
                return material;
            }

            static Material LoadOrCreate(string name, string shaderName)
            {
                string path = $"{MaterialsFolder}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) return material;

                var shader = Shader.Find(shaderName);
                if (shader == null) throw new InvalidOperationException($"Shader '{shaderName}' not found. Is URP installed?");
                EditorFolders.Ensure(MaterialsFolder);
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
        }
    }
}
