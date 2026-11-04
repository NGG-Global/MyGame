using OpenSkies.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Light, air and the airfield's surroundings, within the geographic layout of
    /// <see cref="PrototypeWorld"/>: a warm late-morning sun with soft shadows,
    /// daylight ambient, haze matched to the sky, the procedural sky's tuning, a
    /// sloping beach, the grain detail textures, runway markings and the apron with
    /// its hangars. The land itself is <see cref="TerrainBuilder"/>, the props are
    /// <see cref="SceneryBuilder"/>. Everything is generated and deterministic.
    /// </summary>
    static class PrototypeEnvironment
    {
        const string TexturesFolder = "Assets/_Game/Art/Textures/Prototype";
        const int TextureSize = 512;

        /// <summary>Haze colour: close to the sky's horizon, so distant terrain and sea fade into it.</summary>
        public static readonly Color FogColour = new Color(0.74f, 0.80f, 0.88f);
        /// <summary>Exponential-squared density: about 30 % haze at 2 km, about 95 % at 6 km (the default view distance).</summary>
        public const float FogDensity = 0.00027f;

        /// <summary>Beach: from the end of the grass to below the sea surface.</summary>
        public const float BeachWidth = 100f;
        public static float GrassEastX => PrototypeWorld.ShorelineX - 60f;

        // ---- Light and air ----------------------------------------------------------------------------

        public static void ApplyAtmosphere(Light sun, Material sky)
        {
            // A late-morning sun from the south-east over the sea, warm; soft shadows (the presets switch them).
            sun.transform.rotation = Quaternion.Euler(44f, -32f, 0f);
            sun.color = new Color(1f, 0.95f, 0.87f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.6f;

            // Daylight ambient: blue sky from above, neutral at the horizon, warm earth bounce.
            // Explicit (trilight) so it needs no lighting bake; reflections come from the skybox at start-up.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.58f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.24f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 128;
            RenderSettings.reflectionIntensity = 0.85f;

            // Atmospheric perspective: distant land and sea fade into the horizon haze.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.fogColor = FogColour;

            // Procedural sky: a clear coastal day with a visible sun; the ground colour below the horizon
            // matches the haze, so the view-distance edge disappears into it.
            sky.SetFloat("_SunSize", 0.04f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", 0.95f);
            sky.SetColor("_SkyTint", new Color(0.47f, 0.56f, 0.68f));
            sky.SetColor("_GroundColor", FogColour);
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
        }

        // ---- Surfaces ---------------------------------------------------------------------------------

        /// <summary>
        /// Two-scale detail on a URP Lit material: a large patchwork in the base map and fine
        /// grain in the detail map (multiply x2), tiled at different sizes so neither repeats visibly.
        /// </summary>
        public static void ApplyDetail(Material material, string name, float surfaceWidth, float surfaceLength, float patchTileMetres,
            float grainTileMetres, Color patchTint, float patchContrast, float grainContrast, int seed)
        {
            var patches = PatchTexture(name + "Patches", seed, patchContrast, patchTint);
            var grain = GrainTexture(name + "Grain", seed + 17, grainContrast);
            MaterialSetup.SetBaseMap(material, patches, new Vector2(surfaceWidth / patchTileMetres, surfaceLength / patchTileMetres));
            MaterialSetup.SetDetail(material, grain, null, new Vector2(surfaceWidth / grainTileMetres, surfaceLength / grainTileMetres));
        }

        /// <summary>Tileable grain centred on mid grey (a detail map's neutral value).</summary>
        public static Texture2D GrainTexture(string name, int seed, float contrast) =>
            GeneratedAssets.LoadOrCreateTexture($"{TexturesFolder}/{name}.png", TextureSize, TextureSize, () => NoisePixels(seed, 16, contrast, Color.white, true),
                GeneratedAssets.TextureKind.Albedo, TextureSize);

        /// <summary>Tileable patches: bright, darker and tinted in places (multiplies a base colour).</summary>
        public static Texture2D PatchTexture(string name, int seed, float contrast, Color tint) =>
            GeneratedAssets.LoadOrCreateTexture($"{TexturesFolder}/{name}.png", TextureSize, TextureSize, () => NoisePixels(seed, 4, contrast, tint, false),
                GeneratedAssets.TextureKind.Albedo, TextureSize);

        static Color32[] NoisePixels(int seed, int baseCells, float contrast, Color tint, bool centredOnGrey)
        {
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float n = ProceduralNoise.Fractal((x + 0.5f) / TextureSize, (y + 0.5f) / TextureSize, baseCells, 4, seed);
                Color c;
                if (centredOnGrey)
                {
                    float g = 0.5f + (n - 0.5f) * contrast;
                    c = new Color(g, g, g, 1f);
                }
                else
                {
                    float shade = 1f - contrast * ProceduralNoise.SmoothStep(0.35f, 0.75f, n);
                    c = Color.Lerp(Color.white, tint, ProceduralNoise.SmoothStep(0.45f, 0.8f, n)) * shade;
                    c.a = 1f;
                }
                pixels[y * TextureSize + x] = c;
            }
            return pixels;
        }

        // ---- Coast ---------------------------------------------------------------------------------------

        /// <summary>
        /// A sand strip that slopes from the grass level under the sea surface: the waterline is a
        /// crisp intersection, not two coplanar surfaces fighting for depth. It has a collider.
        /// </summary>
        public static void CreateBeach(Transform coast, Material sand)
        {
            float length = PrototypeWorld.WorldNorthZ - PrototypeWorld.WorldSouthZ;
            float top = PrototypeWorld.LandHeight, bottom = PrototypeWorld.SeaHeight - 0.5f;
            float slope = Mathf.Atan2(top - bottom, BeachWidth) * Mathf.Rad2Deg;
            const float thickness = 0.2f;
            var beach = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beach.name = "Beach";
            beach.transform.SetParent(coast, false);
            float centreX = GrassEastX + BeachWidth * 0.5f;
            beach.transform.localPosition = new Vector3(centreX, (top + bottom) * 0.5f - thickness * 0.5f, (PrototypeWorld.WorldNorthZ + PrototypeWorld.WorldSouthZ) * 0.5f);
            beach.transform.localRotation = Quaternion.Euler(0f, 0f, -slope); // falls towards the sea (+X)
            beach.transform.localScale = new Vector3(BeachWidth / Mathf.Cos(slope * Mathf.Deg2Rad), thickness, length);
            beach.GetComponent<MeshRenderer>().sharedMaterial = sand;
            beach.isStatic = true;
        }

        // ---- Airfield ----------------------------------------------------------------------------------------

        /// <summary>Runway edge lines and aiming-point markings (no colliders), at the height of the existing markings.</summary>
        public static void CreateRunwayMarkings(Transform runway, Material marking, float length, float width)
        {
            foreach (float side in new[] { -1f, 1f })
                Box("EdgeLine", runway, new Vector3(side * (width * 0.5f - 1.2f), 0.01f, 0f), new Vector3(0.9f, 0.02f, length - 20f), marking, false);
            // Aiming points: two broad bars 300 m in from each threshold, where a 3° approach meets the runway.
            foreach (float end in new[] { -1f, 1f })
            foreach (float side in new[] { -1f, 1f })
                Box("AimingPoint", runway, new Vector3(side * 6.5f, 0.01f, end * (length * 0.5f - 300f)), new Vector3(4f, 0.02f, 45f), marking, false);
            // Touchdown zone bars: three pairs beyond each aiming point.
            foreach (float end in new[] { -1f, 1f })
            for (int i = 1; i <= 3; i++)
            foreach (float side in new[] { -1f, 1f })
                Box("TouchdownZone", runway, new Vector3(side * 8f, 0.01f, end * (length * 0.5f - 300f - i * 150f)), new Vector3(1.8f, 0.02f, 22f), marking, false);
        }

        /// <summary>A small apron west of the runway with a taxiway and two hangars (colliders: you can taxi onto it).</summary>
        public static void CreateApron(Material taxiway, Material apron, Material walls, Material roofs, float runwayWidth)
        {
            var root = new GameObject("Apron").transform;
            const float thickness = 0.1f;
            float runwayEdge = -runwayWidth * 0.5f;
            Box("Taxiway", root, new Vector3(runwayEdge - 15f, -thickness * 0.5f, 0f), new Vector3(30f, thickness, 18f), taxiway, true);
            Box("ApronSurface", root, new Vector3(runwayEdge - 30f - 65f, -thickness * 0.5f, 0f), new Vector3(130f, thickness, 180f), apron, true);
            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? -48f : 42f;
                var position = new Vector3(runwayEdge - 30f - 105f, 0f, z);
                Box("Hangar" + i, root, position + new Vector3(0f, 4.5f, 0f), new Vector3(28f, 9f, 26f), walls, true);
                Box("HangarRoof" + i, root, position + new Vector3(0f, 9.2f, 0f), new Vector3(29f, 0.4f, 27f), roofs, false);
            }
            foreach (Transform child in root) child.gameObject.isStatic = true;
        }

        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.isStatic = true;
            return go;
        }
    }
}
