using System;
using System.Collections.Generic;
using System.IO;
using OpenSkies.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Environment polish for the placeholder coast (M12), within the same
    /// geographic layout (<see cref="PrototypeWorld"/>): atmospheric haze matched to
    /// the sky, explicit daylight ambient, a sloping beach where the land meets the
    /// sea, ground and sea detail textures at two scales (less visible repetition),
    /// low hills on the western horizon, tree clusters that the graphics presets
    /// cull by distance (LOD bias), runway edge and aiming-point markings, and a
    /// small apron with two hangars. Everything is generated and deterministic
    /// (fixed seeds). All of it is PLACEHOLDER until the M5 airport and coastline.
    ///
    /// Mobile budget: no real-time extras (no post-processing, no extra lights,
    /// no transparent layers); fog is per-pixel in the URP Lit shader; trees are a
    /// few combined meshes per cluster with distance culling; textures are small
    /// (512 px) and mipmapped. Not measured on a device yet.
    /// </summary>
    static class PrototypeEnvironment
    {
        const string TexturesFolder = "Assets/_Game/Art/Textures/Prototype";
        const string MeshesFolder = "Assets/_Game/Art/Meshes/Prototype";
        const int TextureSize = 512;

        /// <summary>Haze colour: close to the procedural sky's horizon, so distant terrain fades into the sky.</summary>
        public static readonly Color FogColour = new Color(0.72f, 0.79f, 0.86f);
        /// <summary>Exponential-squared density: about 30 % haze at 2 km, about 95 % at 6 km (the default view distance).</summary>
        public const float FogDensity = 0.00028f;

        /// <summary>Beach: from the end of the grass to below the sea surface.</summary>
        public const float BeachWidth = 100f;
        public static float GrassEastX => PrototypeWorld.ShorelineX - 60f;

        // ---- Light and air ----------------------------------------------------------------------------

        public static void ApplyAtmosphere(Light sun, Material sky)
        {
            // A late-morning sun, slightly warm; hard shadows (the graphics presets switch them).
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.25f;
            sun.shadowStrength = 0.8f;

            // Explicit daylight ambient: sky blue from above, neutral at the horizon, warm ground bounce.
            // Deterministic without baked lighting data (a skybox ambient needs a lighting bake).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.57f, 0.61f, 0.63f);
            RenderSettings.ambientGroundColor = new Color(0.33f, 0.32f, 0.28f);

            // Atmospheric perspective: distant land and sea fade into the horizon haze.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.fogColor = FogColour;

            // Procedural sky tuned towards a clear coastal day; the ground colour below the horizon
            // matches the haze, so the view-distance edge disappears into it.
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.58f, 0.66f));
            sky.SetColor("_GroundColor", FogColour);
            sky.SetFloat("_Exposure", 1.15f);
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
            var patches = LoadOrCreateTexture(name + "Patches", seed, 4, patchContrast, patchTint, centredOnGrey: false);
            var grain = LoadOrCreateTexture(name + "Grain", seed + 17, 16, grainContrast, Color.white, centredOnGrey: true);
            if (patches != null)
            {
                material.SetTexture("_BaseMap", patches);
                material.SetTextureScale("_BaseMap", new Vector2(surfaceWidth / patchTileMetres, surfaceLength / patchTileMetres));
            }
            if (grain != null)
            {
                material.SetTexture("_DetailAlbedoMap", grain);
                material.SetTextureScale("_DetailAlbedoMap", new Vector2(surfaceWidth / grainTileMetres, surfaceLength / grainTileMetres));
                material.SetFloat("_DetailAlbedoMapScale", 1f);
                material.EnableKeyword("_DETAIL_MULX2");
            }
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// A tileable noise texture written once as a PNG (deterministic). Patches: white with
        /// darker, tinted areas (multiplies the base colour). Grain: centred on mid grey (the
        /// detail map's neutral value).
        /// </summary>
        static Texture2D LoadOrCreateTexture(string name, int seed, int baseCells, float contrast, Color tint, bool centredOnGrey)
        {
            string path = $"{TexturesFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            EditorFolders.Ensure(TexturesFolder);
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float u = (float)x / TextureSize, v = (float)y / TextureSize;
                float n = 0f, amplitude = 1f, total = 0f;
                for (int octave = 0, cells = baseCells; octave < 4; octave++, cells *= 2, amplitude *= 0.5f)
                {
                    n += amplitude * ValueNoise(u, v, cells, seed + octave * 101);
                    total += amplitude;
                }
                n /= total; // 0..1, around 0.5
                Color c;
                if (centredOnGrey)
                {
                    float g = 0.5f + (n - 0.5f) * contrast;
                    c = new Color(g, g, g, 1f);
                }
                else
                {
                    // Bright everywhere, darker and tinted in patches: multiplies the material's base colour.
                    float shade = 1f - contrast * Mathf.SmoothStep(0.35f, 0.75f, n);
                    c = Color.Lerp(Color.white, tint, Mathf.SmoothStep(0.45f, 0.8f, n)) * shade;
                    c.a = 1f;
                }
                pixels[y * TextureSize + x] = c;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 2;
                importer.maxTextureSize = TextureSize;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            Debug.Log($"[OpenSkies] Created {path}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Periodic value noise (tiles across the texture edges), 0..1.</summary>
        static float ValueNoise(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, cells, seed), b = Hash(x0 + 1, y0, cells, seed);
            float c = Hash(x0, y0 + 1, cells, seed), d = Hash(x0 + 1, y0 + 1, cells, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
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

        /// <summary>
        /// Low hills on the western horizon (beyond the flying area of the missions), so the
        /// land does not end in a flat line. Mesh colliders: they are real terrain.
        /// </summary>
        public static void CreateHills(Material hills)
        {
            var root = new GameObject("Hills").transform;
            // x, z, footprint radius, height (m). West of every mission route; inside the land area.
            var shapes = new[]
            {
                new Vector4(-5300f, -5200f, 1300f, 180f), new Vector4(-5700f, -2600f, 1500f, 260f), new Vector4(-5100f, 200f, 1100f, 150f),
                new Vector4(-5800f, 2900f, 1400f, 230f), new Vector4(-5200f, 5600f, 1200f, 170f), new Vector4(-4300f, -3900f, 800f, 90f),
                new Vector4(-4200f, 4300f, 900f, 110f),
            };
            for (int i = 0; i < shapes.Length; i++)
            {
                var s = shapes[i];
                var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Hill" + i;
                hill.transform.SetParent(root, false);
                // An ellipsoid twice as tall as the hill, centred one hill-height below the ground:
                // the visible part meets the ground at a gentle angle.
                float h = s.w, r = s.z / 0.866f;
                hill.transform.localPosition = new Vector3(s.x, PrototypeWorld.LandHeight - h, s.y);
                hill.transform.localScale = new Vector3(r * 2f, h * 4f, r * 2f);
                hill.transform.localRotation = Quaternion.Euler(0f, i * 37f, 0f);
                UnityEngine.Object.DestroyImmediate(hill.GetComponent<Collider>());
                hill.AddComponent<MeshCollider>().sharedMesh = hill.GetComponent<MeshFilter>().sharedMesh;
                hill.GetComponent<MeshRenderer>().sharedMaterial = hills;
                hill.isStatic = true;
            }
        }

        // ---- Trees -----------------------------------------------------------------------------------------

        /// <summary>
        /// Clusters of low-poly trees around the airfield, away from the runway and its approach
        /// paths. Three cluster shapes (combined meshes, two materials each) are reused with
        /// different rotations and sizes. Each cluster culls by screen size (LODGroup), so the
        /// graphics preset's LOD bias sets the vegetation distance. Trees have no colliders.
        /// </summary>
        public static void CreateTrees(Material trunk, Material foliage)
        {
            var meshes = new Mesh[3];
            for (int v = 0; v < meshes.Length; v++) meshes[v] = LoadOrCreateClusterMesh(v);

            var root = new GameObject("Trees").transform;
            var rng = new Random(4242);
            var placed = new List<Vector2>();
            int attempts = 0;
            while (placed.Count < 34 && attempts++ < 2000)
            {
                bool west = rng.NextDouble() < 0.7;
                float x = west ? Lerp(-3600f, -320f, rng.NextDouble()) : Lerp(320f, GrassEastX - 80f, rng.NextDouble());
                float z = Lerp(-4200f, 4200f, rng.NextDouble());
                var p = new Vector2(x, z);
                if (!ClearOfAirfield(p)) continue;
                bool crowded = false;
                foreach (var q in placed)
                    if ((q - p).sqrMagnitude < 160f * 160f) { crowded = true; break; }
                if (crowded) continue;
                placed.Add(p);

                var cluster = new GameObject("TreeCluster" + placed.Count);
                cluster.transform.SetParent(root, false);
                cluster.transform.localPosition = new Vector3(x, PrototypeWorld.LandHeight, z);
                cluster.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float scale = Lerp(0.8f, 1.25f, rng.NextDouble());
                cluster.transform.localScale = new Vector3(scale, Lerp(0.85f, 1.2f, rng.NextDouble()), scale);
                cluster.AddComponent<MeshFilter>().sharedMesh = meshes[placed.Count % meshes.Length];
                var renderer = cluster.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { trunk, foliage };
                renderer.shadowCastingMode = ShadowCastingMode.On;
                cluster.isStatic = true;

                var lod = cluster.AddComponent<LODGroup>();
                lod.fadeMode = LODFadeMode.None;
                // Culled below 1.2 % of the screen height: about 2.9 km at LOD bias 1 (Balanced),
                // 1.7 km at 0.6 (Battery Saver), 4.3 km at 1.5 (High Quality).
                lod.SetLODs(new[] { new LOD(0.012f, new Renderer[] { renderer }) });
                lod.RecalculateBounds();
            }
        }

        /// <summary>Away from the runway corridor (and its approaches), the apron and the shore.</summary>
        static bool ClearOfAirfield(Vector2 p)
        {
            if (Mathf.Abs(p.x) < 300f) return false;                                                   // runway and approach corridor
            if (p.x > -320f && p.x < 0f && Mathf.Abs(p.y) < 300f) return false;                       // apron
            if (p.x > GrassEastX - 60f) return false;                                                  // beach
            if ((p - new Vector2(PrototypeWorld.HeadlandCentre.x, PrototypeWorld.HeadlandCentre.z)).magnitude < 600f) return false;
            if ((p - new Vector2(PrototypeWorld.BreakwaterStart.x, PrototypeWorld.BreakwaterStart.z)).magnitude < 400f) return false;
            return true;
        }

        static float Lerp(float a, float b, double t) => a + (b - a) * (float)t;

        /// <summary>One cluster shape: 6-9 trees (round and conical) in a 50 m patch; submesh 0 trunks, 1 foliage.</summary>
        static Mesh LoadOrCreateClusterMesh(int variant)
        {
            string path = $"{MeshesFolder}/TreeCluster{(char)('A' + variant)}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            var rng = new Random(900 + variant);
            var trunks = new List<CombineInstance>();
            var crowns = new List<CombineInstance>();
            var trunkMesh = Cone(6, 0.35f, 0.25f, 1f);   // a tapered trunk, 1 m tall before scaling
            var roundCrown = Ellipsoid(8, 5);
            var conifer = Cone(8, 1f, 0f, 1f);
            int count = 6 + rng.Next(4);
            for (int i = 0; i < count; i++)
            {
                var offset = new Vector3(Lerp(-25f, 25f, rng.NextDouble()), 0f, Lerp(-25f, 25f, rng.NextDouble()));
                float height = Lerp(7f, 14f, rng.NextDouble());
                bool cone = rng.NextDouble() < 0.4;
                float trunkHeight = height * (cone ? 0.18f : 0.35f);
                trunks.Add(new CombineInstance
                {
                    mesh = trunkMesh,
                    transform = Matrix4x4.TRS(offset, Quaternion.identity, new Vector3(1f, trunkHeight, 1f)),
                });
                float crownHeight = height - trunkHeight * 0.8f;
                float width = cone ? height * 0.32f : height * Lerp(0.38f, 0.5f, rng.NextDouble());
                var rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                crowns.Add(new CombineInstance
                {
                    mesh = cone ? conifer : roundCrown,
                    transform = Matrix4x4.TRS(offset + Vector3.up * (cone ? trunkHeight * 0.8f : trunkHeight * 0.8f + crownHeight * 0.5f), rotation,
                        cone ? new Vector3(width, crownHeight, width) : new Vector3(width, crownHeight * 0.5f, width)),
                });
            }

            var trunkPart = new Mesh();
            trunkPart.CombineMeshes(trunks.ToArray(), true, true);
            var crownPart = new Mesh();
            crownPart.CombineMeshes(crowns.ToArray(), true, true);
            var mesh = new Mesh { name = "TreeCluster" + (char)('A' + variant) };
            mesh.CombineMeshes(new[]
            {
                new CombineInstance { mesh = trunkPart, transform = Matrix4x4.identity },
                new CombineInstance { mesh = crownPart, transform = Matrix4x4.identity },
            }, false, true); // two submeshes: trunks, foliage
            mesh.RecalculateBounds();

            EditorFolders.Ensure(MeshesFolder);
            AssetDatabase.CreateAsset(mesh, path);
            UnityEngine.Object.DestroyImmediate(trunkPart);
            UnityEngine.Object.DestroyImmediate(crownPart);
            UnityEngine.Object.DestroyImmediate(trunkMesh);
            UnityEngine.Object.DestroyImmediate(roundCrown);
            UnityEngine.Object.DestroyImmediate(conifer);
            return mesh;
        }

        /// <summary>A cone (or truncated cone) standing on y = 0, 1 unit tall, flat-shaded sides.</summary>
        static Mesh Cone(int sides, float bottomRadius, float topRadius, float height)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var b0 = new Vector3(Mathf.Cos(a0) * bottomRadius, 0f, Mathf.Sin(a0) * bottomRadius);
                var b1 = new Vector3(Mathf.Cos(a1) * bottomRadius, 0f, Mathf.Sin(a1) * bottomRadius);
                var t0 = new Vector3(Mathf.Cos(a0) * topRadius, height, Mathf.Sin(a0) * topRadius);
                var t1 = new Vector3(Mathf.Cos(a1) * topRadius, height, Mathf.Sin(a1) * topRadius);
                int n = vertices.Count;
                vertices.Add(b0); vertices.Add(b1); vertices.Add(t1); vertices.Add(t0);
                triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 1);
                triangles.Add(n); triangles.Add(n + 3); triangles.Add(n + 2);
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>A low-poly unit sphere (radius 1), smooth normals.</summary>
        static Mesh Ellipsoid(int segments, int rings)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float theta = Mathf.PI * 2f * s / segments;
                    vertices.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)));
                }
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + segments + 1;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
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
        }

        /// <summary>A small apron west of the runway with a taxiway and two hangars (colliders: you can taxi onto it).</summary>
        public static void CreateApron(Material asphalt, Material walls, Material roofs, float runwayWidth)
        {
            var apron = new GameObject("Apron").transform;
            const float thickness = 0.1f;
            float runwayEdge = -runwayWidth * 0.5f;
            Box("Taxiway", apron, new Vector3(runwayEdge - 15f, -thickness * 0.5f, 0f), new Vector3(30f, thickness, 18f), asphalt, true);
            Box("ApronSurface", apron, new Vector3(runwayEdge - 30f - 65f, -thickness * 0.5f, 0f), new Vector3(130f, thickness, 180f), asphalt, true);
            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? -48f : 42f;
                var position = new Vector3(runwayEdge - 30f - 105f, 0f, z);
                Box("Hangar" + i, apron, position + new Vector3(0f, 4.5f, 0f), new Vector3(28f, 9f, 26f), walls, true);
                Box("HangarRoof" + i, apron, position + new Vector3(0f, 9.2f, 0f), new Vector3(29f, 0.4f, 27f), roofs, false);
            }
            foreach (Transform child in apron) child.gameObject.isStatic = true;
        }

        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.isStatic = true;
            return go;
        }
    }
}
