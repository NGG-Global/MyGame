using System;
using System.Collections.Generic;
using OpenSkies.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace OpenSkies.Editor
{
    /// <summary>
    /// The land: a height-field mesh in tiles (each with its collider, culled on
    /// its own) over <see cref="TerrainShape"/>, a painted land-use albedo and a
    /// relief normal map sampled from the same functions, and tree clusters
    /// planted where the woodland is. Generated and deterministic; the textures are
    /// written once as PNGs.
    /// </summary>
    public static class TerrainBuilder
    {
        const string MeshesFolder = "Assets/_Game/Art/Meshes/Terrain";
        const string TexturesFolder = "Assets/_Game/Art/Textures/Terrain";
        public const string AlbedoPath = TexturesFolder + "/LandAlbedo.png";
        public const string NormalPath = TexturesFolder + "/LandNormal.png";
        public const int AlbedoSize = 2048;
        public const int NormalSize = 1024;
        /// <summary>Tiles across (x) and along (z) the land, and cells per tile: 80 m cells.</summary>
        public const int TilesX = 4, TilesZ = 4, CellsX = 25, CellsZ = 50;

        // ---- Mesh ---------------------------------------------------------------------------------------------

        /// <summary>The terrain tiles under a "Terrain" object, static, each with a mesh collider.</summary>
        public static Transform Create(Material land)
        {
            var root = new GameObject("Terrain").transform;
            for (int tz = 0; tz < TilesZ; tz++)
            for (int tx = 0; tx < TilesX; tx++)
            {
                var data = BuildTile(tx, tz);
                string name = $"Land_{tx}_{tz}";
                var mesh = GeneratedAssets.SaveMesh(data, name, $"{MeshesFolder}/{name}.asset", compress: false);
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = land;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.isStatic = true;
            }
            return root;
        }

        /// <summary>One tile in world space: positions from <see cref="TerrainShape.Height"/>, analytic normals, world-wide texture coordinates.</summary>
        public static MeshData BuildTile(int tx, int tz)
        {
            var mesh = new MeshData();
            float tileWidth = TerrainShape.Width / TilesX, tileLength = TerrainShape.Length / TilesZ;
            float x0 = TerrainShape.WestX + tx * tileWidth, z0 = TerrainShape.SouthZ + tz * tileLength;
            int stride = CellsX + 1;
            for (int r = 0; r <= CellsZ; r++)
            for (int c = 0; c <= CellsX; c++)
            {
                float x = x0 + tileWidth * c / CellsX, z = z0 + tileLength * r / CellsZ;
                mesh.AddVertex(new Vector3(x, TerrainShape.Height(x, z), z), TerrainShape.Normal(x, z),
                    new Vector2((x - TerrainShape.WestX) / TerrainShape.Width, (z - TerrainShape.SouthZ) / TerrainShape.Length));
            }
            for (int r = 0; r < CellsZ; r++)
            for (int c = 0; c < CellsX; c++)
            {
                int a = r * stride + c, b = a + 1, d = a + stride, e = d + 1;
                if ((r + c) % 2 == 0) { mesh.AddTriangle(0, a, d, e); mesh.AddTriangle(0, a, e, b); }
                else { mesh.AddTriangle(0, a, d, b); mesh.AddTriangle(0, b, d, e); }
            }
            return mesh;
        }

        // ---- Textures ------------------------------------------------------------------------------------------

        public static Texture2D LoadOrCreateAlbedo() =>
            GeneratedAssets.LoadOrCreateTexture(AlbedoPath, AlbedoSize, AlbedoSize, AlbedoPixels, GeneratedAssets.TextureKind.AlbedoClamped, AlbedoSize);

        public static Texture2D LoadOrCreateNormal() =>
            GeneratedAssets.LoadOrCreateTexture(NormalPath, NormalSize, NormalSize, NormalPixels, GeneratedAssets.TextureKind.Normal, NormalSize);

        /// <summary>Heights of the land sampled on a texture grid (texel centres), in metres above the plain.</summary>
        public static float[] HeightGrid(int size)
        {
            var heights = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                float z = TerrainShape.SouthZ + TerrainShape.Length * (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float wx = TerrainShape.WestX + TerrainShape.Width * (x + 0.5f) / size;
                    heights[y * size + x] = TerrainShape.Height(wx, z) - PrototypeWorld.LandHeight;
                }
            }
            return heights;
        }

        static Color32[] AlbedoPixels()
        {
            int size = AlbedoSize;
            var heights = HeightGrid(size);
            float texelX = TerrainShape.Width / size, texelZ = TerrainShape.Length / size;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float z = TerrainShape.SouthZ + TerrainShape.Length * (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float wx = TerrainShape.WestX + TerrainShape.Width * (x + 0.5f) / size;
                    int i = y * size + x;
                    float dx = (heights[Math.Min(size - 1, x + 1) + y * size] - heights[Math.Max(0, x - 1) + y * size]) / (2f * texelX);
                    float dz = (heights[x + Math.Min(size - 1, y + 1) * size] - heights[x + Math.Max(0, y - 1) * size]) / (2f * texelZ);
                    float slope = Mathf.Sqrt(dx * dx + dz * dz);
                    pixels[i] = TerrainShape.Colour(wx, z, heights[i], slope);
                }
            }
            return pixels;
        }

        /// <summary>
        /// Relief for the normal map: the terrain itself plus what the albedo paints on top of it,
        /// so hedgerows stand up, woods have a rough canopy and the fields a faint furrow grain.
        /// </summary>
        public static float ReliefHeight(float x, float z, float terrainHeight)
        {
            float u = (x - TerrainShape.WestX) / TerrainShape.Width, v = (z - TerrainShape.SouthZ) / TerrainShape.Length;
            float relief = terrainHeight;
            ProceduralNoise.Cellular(u, v * 2f, 24, TerrainShape.Seed + 5, out _, out float d1, out float d2);
            float hedge = 1f - ProceduralNoise.SmoothStep(0.02f, 0.05f, d2 - d1);
            float wood = TerrainShape.Woodland(x, z, terrainHeight);
            float canopy = ProceduralNoise.Fractal(u * 40f, v * 80f, 32, 2, TerrainShape.Seed + 31);
            relief += 3f * hedge * (1f - wood);
            relief += wood * (6f + 10f * canopy);
            relief += 0.8f * (ProceduralNoise.Fractal(u * 16f, v * 32f, 16, 2, TerrainShape.Seed + 41) - 0.5f);
            return relief;
        }

        static Color32[] NormalPixels()
        {
            int size = NormalSize;
            var heights = HeightGrid(size);
            for (int y = 0; y < size; y++)
            {
                float z = TerrainShape.SouthZ + TerrainShape.Length * (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float wx = TerrainShape.WestX + TerrainShape.Width * (x + 0.5f) / size;
                    heights[y * size + x] = ReliefHeight(wx, z, heights[y * size + x]);
                }
            }
            return GeneratedAssets.NormalMapFromHeights(heights, size, size, 1f, TerrainShape.Width / size, TerrainShape.Length / size, tile: false);
        }

        // ---- Trees ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Tree clusters where the woodland is (the same function that paints the albedo), on the
        /// terrain, away from the runway corridor, the town, the roads and the beach. Four cluster
        /// shapes (one mesh, one palette material each) are reused with random turns and sizes.
        /// Each cluster culls by screen size, so the graphics preset's LOD bias sets its distance.
        /// </summary>
        public static Transform CreateTrees(Material palette, int clusters = 110)
        {
            var meshes = new Mesh[4];
            for (int v = 0; v < meshes.Length; v++)
                meshes[v] = GeneratedAssets.SaveMesh(BuildCluster(v), "TreeCluster" + (char)('A' + v), $"{MeshesFolder}/TreeCluster{(char)('A' + v)}.asset");

            var root = new GameObject("Trees").transform;
            var rng = new Random(4242);
            var placed = new List<Vector2>();
            int attempts = 0;
            while (placed.Count < clusters && attempts++ < 20000)
            {
                float x = Lerp(TerrainShape.WestX + 200f, TerrainShape.EastX - 200f, rng.NextDouble());
                float z = Lerp(TerrainShape.SouthZ + 200f, TerrainShape.NorthZ - 200f, rng.NextDouble());
                if (TerrainShape.Woodland(x, z) < 0.5f) continue;
                if (!ClearOfAirfield(x, z)) continue;
                var p = new Vector2(x, z);
                bool crowded = false;
                foreach (var q in placed)
                    if ((q - p).sqrMagnitude < 130f * 130f) { crowded = true; break; }
                if (crowded) continue;
                placed.Add(p);

                var cluster = new GameObject("TreeCluster" + placed.Count);
                cluster.transform.SetParent(root, false);
                cluster.transform.localPosition = new Vector3(x, TerrainShape.Height(x, z) - 0.3f, z);
                cluster.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float scale = Lerp(0.85f, 1.3f, rng.NextDouble());
                cluster.transform.localScale = new Vector3(scale, Lerp(0.9f, 1.2f, rng.NextDouble()), scale);
                cluster.AddComponent<MeshFilter>().sharedMesh = meshes[placed.Count % meshes.Length];
                var renderer = cluster.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = palette;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                cluster.isStatic = true;

                var lod = cluster.AddComponent<LODGroup>();
                lod.fadeMode = LODFadeMode.None;
                // Culled below 1.2 % of the screen height: about 3 km at LOD bias 1 (Balanced).
                lod.SetLODs(new[] { new LOD(0.012f, new Renderer[] { renderer }) });
                lod.RecalculateBounds();
            }
            return root;
        }

        /// <summary>Away from the runway corridor and its approaches, the apron, the town and the roads.</summary>
        public static bool ClearOfAirfield(float x, float z)
        {
            if (Mathf.Abs(x) < 320f) return false;                                          // runway and approach corridor
            if (x > -420f && x < 0f && Mathf.Abs(z) < 320f) return false;                   // apron, tower, hangars
            if (x > TerrainShape.EastX - 120f) return false;                                // beach
            if (TerrainShape.Town.Contains(new Vector2(x, z))) return false;
            if (TerrainShape.RoadDistance(x, z) < 40f) return false;
            if (Vector2.Distance(new Vector2(x, z), new Vector2(PrototypeWorld.HeadlandCentre.x, PrototypeWorld.HeadlandCentre.z)) < 600f) return false;
            if (Vector2.Distance(new Vector2(x, z), new Vector2(PrototypeWorld.BreakwaterStart.x, PrototypeWorld.BreakwaterStart.z)) < 400f) return false;
            return true;
        }

        static float Lerp(float a, float b, double t) => a + (b - a) * (float)t;

        /// <summary>One cluster: 10-16 trees in a 60 m patch; broadleaves from two or three crown lobes, conifers from stacked tiers.</summary>
        public static MeshData BuildCluster(int variant)
        {
            var rng = new Random(900 + variant);
            var mesh = new MeshData();
            var trunk = new MeshData();
            MeshKit.Revolve(trunk, new[] { new Vector2(0.26f, 0f), new Vector2(0.17f, 0.55f), new Vector2(0.09f, 1f) }, 6, 0, capBottom: false, capTop: true);
            trunk.MapUv(ScenePalette.Uv(ScenePalette.Cell.Trunk));
            var lobe = Ellipsoid(8, 5);
            var conifer = new MeshData();
            MeshKit.Revolve(conifer, new[] { new Vector2(0.5f, 0f), new Vector2(0.42f, 0.3f), new Vector2(0.3f, 0.45f), new Vector2(0.3f, 0.6f), new Vector2(0.14f, 0.8f), new Vector2(0f, 1f) },
                8, 0, capBottom: true, capTop: false);

            int count = 10 + rng.Next(7);
            for (int i = 0; i < count; i++)
            {
                var offset = new Vector3(Lerp(-30f, 30f, rng.NextDouble()), 0f, Lerp(-30f, 30f, rng.NextDouble()));
                float height = Lerp(8f, 16f, rng.NextDouble());
                bool cone = rng.NextDouble() < 0.35;
                var lean = Quaternion.Euler(Lerp(-4f, 4f, rng.NextDouble()), (float)rng.NextDouble() * 360f, Lerp(-4f, 4f, rng.NextDouble()));
                float trunkHeight = height * (cone ? 0.2f : 0.38f);
                mesh.Append(trunk, Matrix4x4.TRS(offset, lean, new Vector3(1f, trunkHeight * 1.15f, 1f)));
                if (cone)
                {
                    var tiers = new MeshData();
                    tiers.Append(conifer, Matrix4x4.identity);
                    tiers.MapUv(ScenePalette.Uv(ScenePalette.Cell.Conifer));
                    float width = height * 0.3f;
                    mesh.Append(tiers, Matrix4x4.TRS(offset + lean * Vector3.up * (trunkHeight * 0.8f), lean, new Vector3(width, height - trunkHeight * 0.8f, width)));
                }
                else
                {
                    var cell = rng.NextDouble() < 0.12 ? ScenePalette.Cell.FoliageAutumn
                        : rng.NextDouble() < 0.5 ? ScenePalette.Cell.FoliageMid : rng.NextDouble() < 0.5 ? ScenePalette.Cell.FoliageLight : ScenePalette.Cell.FoliageDark;
                    int lobes = 2 + rng.Next(2);
                    float crown = height - trunkHeight * 0.8f;
                    for (int l = 0; l < lobes; l++)
                    {
                        var part = new MeshData();
                        part.Append(lobe, Matrix4x4.identity);
                        part.MapUv(ScenePalette.Uv(cell));
                        float r = crown * Lerp(0.3f, 0.42f, rng.NextDouble());
                        var centre = offset + Vector3.up * (trunkHeight * 0.8f + crown * Lerp(0.35f, 0.62f, rng.NextDouble()))
                                     + new Vector3(Lerp(-0.25f, 0.25f, rng.NextDouble()) * crown, 0f, Lerp(-0.25f, 0.25f, rng.NextDouble()) * crown);
                        mesh.Append(part, Matrix4x4.TRS(centre, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), new Vector3(r, r * Lerp(0.75f, 1.05f, rng.NextDouble()), r)));
                    }
                }
            }
            return mesh;
        }

        /// <summary>A unit sphere by revolution, smooth, facing out.</summary>
        public static MeshData Ellipsoid(int segments, int rings)
        {
            var profile = new List<Vector2>(rings + 1);
            for (int r = 0; r <= rings; r++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * r / rings;
                profile.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
            var mesh = new MeshData();
            MeshKit.Revolve(mesh, profile, segments, 0, capBottom: false, capTop: false);
            return mesh;
        }
    }
}
