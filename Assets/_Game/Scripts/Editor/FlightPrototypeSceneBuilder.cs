using System;
using System.IO;
using System.Linq;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.Gameplay;
using OpenSkies.Simulation;
using OpenSkies.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
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
        const string SkyShader = "Skybox/Procedural";
        const string BuiltinFont = "LegacyRuntime.ttf";
        const string AircraftConfigurationPath = "Assets/_Game/Settings/Aircraft/TrainerConfiguration.asset";
        const int AircraftLayer = 2; // built-in "Ignore Raycast": wheel raycasts must not hit the aircraft itself

        // Layout, in metres. The runway runs south to north along +Z.
        const float GroundSize = 4000f;
        const float RunwayLength = 1500f;  // take-off needs ~350 m, landing roll ~250 m with brakes
        const float RunwayWidth = 30f;
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

            CreateLighting(materials.Sky);
            CreateGround(materials.Grass);
            CreateRunway(materials.Asphalt, materials.Marking);
            var configuration = LoadOrCreateAircraftConfiguration();
            var aircraft = CreateAircraft(configuration, materials, out var cockpitAnchor);
            var cameraRig = CreateCamera(aircraft.transform, cockpitAnchor);
            var hud = CreateHud(out var touchControls);
            CreateEventSystem();

            var session = new GameObject("FlightSession").AddComponent<FlightSession>();
            session.aircraft = aircraft;
            session.telemetry = aircraft.GetComponent<AircraftTelemetry>();
            session.cameraRig = cameraRig;
            session.hud = hud;
            session.touchControls = touchControls;

            EditorFolders.Ensure(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save {ScenePath}");
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[OpenSkies] Built {ScenePath}");
        }

        /// <summary>Puts the prototype scene first in the build, keeping any other scenes.</summary>
        public static void RegisterInBuildSettings()
        {
            var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
        }

        static void CreateLighting(Material sky)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Hard; // soft shadow filtering is costly on mobile GPUs
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
        }

        static void CreateGround(Material grass)
        {
            // Unity's plane primitive is 10 m x 10 m at scale 1. It sits 10 cm below the
            // runway so the two surfaces never z-fight at a distance.
            var ground = Primitive(PrimitiveType.Plane, "Ground", null, new Vector3(0f, -0.1f, 0f), Vector3.one * (GroundSize / 10f), grass,
                keepCollider: true);
            ground.isStatic = true;
        }

        static void CreateRunway(Material asphalt, Material marking)
        {
            var runway = new GameObject("Runway").transform;
            // Top surface at y = 0, where the flight model puts the wheels.
            Primitive(PrimitiveType.Cube, "Surface", runway, new Vector3(0f, -RunwayThickness / 2f, 0f),
                new Vector3(RunwayWidth, RunwayThickness, RunwayLength), asphalt, keepCollider: true);

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

            foreach (Transform child in runway) child.gameObject.isStatic = true;
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

        /// <summary>
        /// Placeholder four-seat high-wing trainer built from primitives, flown by
        /// Rigidbody physics. The root pivot sits at wheel-contact level with the
        /// gear fully extended; +Z is the nose. Wheel positions come from the
        /// configuration so visuals and physics agree.
        /// </summary>
        static AircraftController CreateAircraft(AircraftConfiguration configuration, PrototypeMaterials materials, out Transform cockpitAnchor)
        {
            var rootGo = new GameObject("Aircraft");
            var root = rootGo.transform;
            root.SetPositionAndRotation(AircraftStart, Quaternion.identity);

            var body = new GameObject("Visual").transform;
            body.SetParent(root, false);

            Primitive(PrimitiveType.Cube, "Fuselage", body, new Vector3(0f, 1.35f, 0.2f), new Vector3(1.2f, 1.3f, 4.2f), materials.Airframe);
            Primitive(PrimitiveType.Cube, "TailBoom", body, new Vector3(0f, 1.45f, -2.9f), new Vector3(0.7f, 0.8f, 2.8f), materials.Airframe);
            Primitive(PrimitiveType.Cube, "Cowling", body, new Vector3(0f, 1.25f, 2.6f), new Vector3(1.0f, 1.0f, 0.8f), materials.Accent);
            Primitive(PrimitiveType.Cube, "Wing", body, new Vector3(0f, 2.1f, 0.4f), new Vector3(11f, 0.15f, 1.6f), materials.Airframe);
            Primitive(PrimitiveType.Cube, "WingStrutL", body, new Vector3(-1.6f, 1.45f, 0.4f), new Vector3(0.08f, 1.3f, 0.08f), materials.Accent)
                .transform.localRotation = Quaternion.Euler(0f, 0f, -55f);
            Primitive(PrimitiveType.Cube, "WingStrutR", body, new Vector3(1.6f, 1.45f, 0.4f), new Vector3(0.08f, 1.3f, 0.08f), materials.Accent)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 55f);
            Primitive(PrimitiveType.Cube, "Stabiliser", body, new Vector3(0f, 1.55f, -4.1f), new Vector3(3.6f, 0.1f, 0.9f), materials.Airframe);
            Primitive(PrimitiveType.Cube, "Fin", body, new Vector3(0f, 2.3f, -4.1f), new Vector3(0.1f, 1.4f, 1.1f), materials.Accent);
            Primitive(PrimitiveType.Cube, "Propeller", body, new Vector3(0f, 1.25f, 3.05f), new Vector3(1.8f, 0.12f, 0.05f), materials.Tyre);

            // Fixed tricycle gear, placed from the configuration with the strut fully extended.
            // A cylinder primitive is 1 m wide and 2 m tall at scale 1.
            foreach (var wheel in configuration.wheels)
            {
                Vector3 centre = wheel.attachPoint + Vector3.down * wheel.suspensionLength;
                float diameter = wheel.wheelRadius * 2f;
                Primitive(PrimitiveType.Cylinder, wheel.name, body, centre, new Vector3(diameter, 0.08f, diameter), materials.Tyre)
                    .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Primitive(PrimitiveType.Cube, wheel.name + " strut", body, wheel.attachPoint + Vector3.down * (wheel.suspensionLength / 2f),
                    new Vector3(0.06f, wheel.suspensionLength, 0.06f), materials.Accent);
            }

            // Collision shapes on the root (one compound collider for the Rigidbody).
            // They sit clear of the ground while on the wheels, so they only matter in a crash or tail strike.
            AddBox(rootGo, new Vector3(0f, 1.35f, 0.5f), new Vector3(1.2f, 1.3f, 4.8f));   // cabin and cowling
            AddBox(rootGo, new Vector3(0f, 1.45f, -2.9f), new Vector3(0.7f, 0.8f, 2.8f));  // tail boom
            AddBox(rootGo, new Vector3(0f, 2.1f, 0.4f), new Vector3(11f, 0.15f, 1.6f));    // wing
            AddBox(rootGo, new Vector3(0f, 1.9f, -4.1f), new Vector3(3.6f, 1.4f, 1.0f));   // tail surfaces

            // Left seat eye point.
            cockpitAnchor = new GameObject("CockpitCameraAnchor").transform;
            cockpitAnchor.SetParent(root, false);
            // Eye height chosen so the view over the cowling (top at 1.75 m) reaches below the horizon.
            cockpitAnchor.localPosition = new Vector3(-0.3f, 1.9f, 0.9f);

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
            go.AddComponent<AudioListener>();

            var rig = go.AddComponent<CameraRig>();
            rig.target = aircraft;
            rig.cockpitAnchor = cockpitAnchor;
            rig.mode = CameraMode.Chase;
            go.transform.position = ChaseCameraMath.DesiredPosition(aircraft.position, aircraft.rotation, rig.chaseDistance, rig.chaseHeight);
            go.transform.LookAt(aircraft.position + Vector3.up * rig.lookAheadHeight);
            return rig;
        }

        static HudPresenter CreateHud(out TouchFlightControls touchControls)
        {
            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f; // landscape: scale by height

            var safeArea = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter)).GetComponent<RectTransform>();
            safeArea.SetParent(canvasGo.transform, false);
            Stretch(safeArea);

            var font = Resources.GetBuiltinResource<Font>(BuiltinFont);

            // Developer telemetry line, top-left, small. Temporary (see HudPresenter).
            var readout = NewText("DeveloperReadout", safeArea, font, 26, TextAnchor.UpperLeft);
            var readoutRect = readout.rectTransform;
            readoutRect.anchorMin = new Vector2(0f, 1f);
            readoutRect.anchorMax = new Vector2(1f, 1f);
            readoutRect.pivot = new Vector2(0f, 1f);
            readoutRect.anchoredPosition = new Vector2(24f, -16f);
            readoutRect.sizeDelta = new Vector2(-48f, 40f);
            readout.color = new Color(1f, 1f, 1f, 0.8f);
            readout.raycastTarget = false;

            // Session buttons, top-right, away from the flight controls; at least 96 px tall at 1080p.
            var cameraButton = NewHudButton("CameraButton", "CAM", safeArea, font, new Vector2(-24f, -16f));
            var resetButton = NewHudButton("ResetButton", "RESET", safeArea, font, new Vector2(-168f, -16f));

            // Touch flight controls: joystick lower left, throttle and brakes lower right.
            // Built by runtime code so tests use the identical layout.
            touchControls = TouchControlsFactory.Build(safeArea, font);

            var hud = canvasGo.AddComponent<HudPresenter>();
            hud.developerReadout = readout;
            hud.cameraButton = cameraButton;
            hud.resetButton = resetButton;
            return hud;
        }

        static Button NewHudButton(string name, string caption, Transform parent, Font font, Vector2 topRightOffset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = topRightOffset;
            rect.sizeDelta = new Vector2(128f, 96f);
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            var label = NewText("Label", rect, font, 28, TextAnchor.MiddleCenter);
            label.text = caption;
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.raycastTarget = false;
            Stretch(label.rectTransform);
            return go.GetComponent<Button>();
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
            public readonly Material Grass = Lit("Grass", new Color(0.32f, 0.45f, 0.22f), 0.1f);
            public readonly Material Asphalt = Lit("Asphalt", new Color(0.17f, 0.17f, 0.18f), 0.25f);
            public readonly Material Marking = Lit("RunwayMarking", new Color(0.92f, 0.92f, 0.9f), 0.2f);
            public readonly Material Airframe = Lit("Airframe", new Color(0.93f, 0.93f, 0.95f), 0.55f);
            public readonly Material Accent = Lit("AirframeAccent", new Color(0.12f, 0.32f, 0.62f), 0.55f);
            public readonly Material Tyre = Lit("Tyre", new Color(0.06f, 0.06f, 0.06f), 0.15f);
            public readonly Material Sky = Skybox();

            static Material Lit(string name, Color color, float smoothness)
            {
                var material = LoadOrCreate(name, LitShader);
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", smoothness);
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
