using System.Collections.Generic;
using OpenSkies.World;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace OpenSkies.Editor
{
    /// <summary>
    /// The scenery that dresses the world: the airfield (control tower, windsock,
    /// fuel station, runway numbers and edge lights, hangar doors), the coastal
    /// town, the harbour's quay and boats, the lighthouse, and the cloud deck.
    /// Everything is generated from <see cref="MeshKit"/> parts coloured through
    /// <see cref="ScenePalette"/>, combined into few meshes, and placed on
    /// <see cref="TerrainShape"/>. PLACEHOLDER until the real airport and coastline.
    /// </summary>
    public static class SceneryBuilder
    {
        const string MeshesFolder = "Assets/_Game/Art/Meshes/Scenery";
        public const string CloudTexturePath = "Assets/_Game/Art/Textures/Prototype/CloudDeck.png";
        public const string WaterNormalPath = "Assets/_Game/Art/Textures/Prototype/WaterNormal.png";
        public const string AsphaltPath = "Assets/_Game/Art/Textures/Prototype/Asphalt.png";
        public const string ConcretePath = "Assets/_Game/Art/Textures/Prototype/Concrete.png";
        public const float CloudBaseMetres = 2300f;
        public const float CloudDeckSizeMetres = 60000f;

        /// <summary>Materials the scenery uses.</summary>
        public sealed class Materials
        {
            /// <summary>Lit, base map = the palette.</summary>
            public Material Palette;
            public Material Marking;
            public Material LightWhite, LightGreen, LightRed, LightBlue;
            public Material Cloud;
        }

        // ---- Airfield -------------------------------------------------------------------------------------

        /// <summary>Tower, windsock, fuel station, hangar doors and apron markings, west of the runway.</summary>
        public static void CreateAirfieldDressing(Materials m, float runwayWidth)
        {
            var root = new GameObject("AirfieldDressing").transform;
            float runwayEdge = -runwayWidth * 0.5f;

            // Control tower: a tapered shaft, a glazed octagonal cab with a wide roof, a railing ring.
            var tower = new MeshData();
            Revolved(tower, new[] { new Vector2(2.6f, 0f), new Vector2(2.1f, 9f), new Vector2(2.0f, 12.5f) }, 12, ScenePalette.Cell.Concrete, capTop: true);
            Revolved(tower, new[] { new Vector2(3.6f, 12.3f), new Vector2(3.6f, 12.9f) }, 8, ScenePalette.Cell.DarkMetal, capTop: true, capBottom: true);
            Revolved(tower, new[] { new Vector2(3.3f, 12.9f), new Vector2(3.5f, 15.6f) }, 8, ScenePalette.Cell.CabGlass, capTop: false);
            Revolved(tower, new[] { new Vector2(4.4f, 15.6f), new Vector2(4.4f, 15.9f), new Vector2(0f, 16.6f) }, 8, ScenePalette.Cell.DarkMetal, capBottom: true);
            Revolved(tower, new[] { new Vector2(0.08f, 16.6f), new Vector2(0.08f, 19.5f) }, 6, ScenePalette.Cell.Steel, capTop: true);
            Place("ControlTower", root, tower, m.Palette, new Vector3(runwayEdge - 175f, 0f, 112f), 20f, collider: true);

            // Windsock on a pole, pointing down the prevailing wind (from the sea).
            var windsock = new MeshData();
            Revolved(windsock, new[] { new Vector2(0.07f, 0f), new Vector2(0.07f, 7f) }, 6, ScenePalette.Cell.Steel, capTop: true);
            var sock = new MeshData();
            MeshKit.Revolve(sock, new[] { new Vector2(0.45f, 0f), new Vector2(0.28f, 2.6f) }, 8, 0, capBottom: false, capTop: false);
            sock.MapUv(ScenePalette.Uv(ScenePalette.Cell.Windsock));
            windsock.Append(sock, Matrix4x4.TRS(new Vector3(0f, 7f, 0f), Quaternion.Euler(0f, 0f, -75f), Vector3.one));
            Place("Windsock", root, windsock, m.Palette, new Vector3(runwayEdge - 25f, 0f, -140f), 0f);

            // Fuel station: a canopy on posts and two pumps, by the apron's north end.
            var fuel = new MeshData();
            Boxed(fuel, new Vector3(0f, 4.4f, 0f), new Vector3(9f, 0.3f, 6f), ScenePalette.Cell.White);
            foreach (float px in new[] { -3.8f, 3.8f })
            foreach (float pz in new[] { -2.5f, 2.5f })
                Boxed(fuel, new Vector3(px, 2.2f, pz), new Vector3(0.3f, 4.4f, 0.3f), ScenePalette.Cell.Steel);
            foreach (float px in new[] { -1.6f, 1.6f })
                Boxed(fuel, new Vector3(px, 0.8f, 0f), new Vector3(0.9f, 1.6f, 0.6f), ScenePalette.Cell.FuelRed);
            Place("FuelStation", root, fuel, m.Palette, new Vector3(runwayEdge - 60f, 0f, 100f), 0f, collider: true);

            // Hangar doors and a roof overhang on the two hangars (the hangars themselves are in PrototypeEnvironment).
            var doors = new MeshData();
            foreach (float z in new[] { -48f, 42f })
            {
                Boxed(doors, new Vector3(runwayEdge - 135f + 14.2f, 3.6f, z), new Vector3(0.3f, 7.2f, 20f), ScenePalette.Cell.HangarDoor);
                for (int i = -2; i <= 2; i++) Boxed(doors, new Vector3(runwayEdge - 135f + 14.35f, 3.6f, z + i * 4f), new Vector3(0.1f, 7.4f, 0.25f), ScenePalette.Cell.DarkMetal);
                Boxed(doors, new Vector3(runwayEdge - 135f, 9.45f, z), new Vector3(30.5f, 0.3f, 28.5f), ScenePalette.Cell.RoofSlate);
            }
            Place("HangarDoors", root, doors, m.Palette, Vector3.zero, 0f);

            // Apron markings: stand lines for the three parked aircraft and a taxiway centreline.
            var marks = new MeshData();
            foreach (float z in new[] { -62f, -34f, 40f })
            {
                MeshKit.Box(marks, new Vector3(runwayEdge - 90f, 0.012f, z), new Vector3(24f, 0.02f, 0.3f), 0);
                MeshKit.Box(marks, new Vector3(runwayEdge - 78f, 0.012f, z), new Vector3(0.3f, 0.02f, 12f), 0);
            }
            MeshKit.Box(marks, new Vector3(runwayEdge - 15f, 0.012f, 0f), new Vector3(30f, 0.02f, 0.3f), 0);
            Place("ApronMarkings", root, marks, m.Marking, Vector3.zero, 0f);

            foreach (Transform child in root) child.gameObject.isStatic = true;
        }

        /// <summary>Runway numbers at both thresholds (36 south, 18 north), 5x7 blocks like painted markings.</summary>
        public static void CreateRunwayNumbers(Transform runway, Material marking, float runwayLength)
        {
            var mesh = new MeshData();
            float z = runwayLength * 0.5f - 95f;
            Number(mesh, "36", new Vector3(0f, 0.012f, -z), facingNorth: true);
            Number(mesh, "18", new Vector3(0f, 0.012f, z), facingNorth: false);
            Place("Numbers", runway, mesh, marking, Vector3.zero, 0f);
        }

        /// <summary>Digit glyphs, 5 columns by 7 rows, top row first.</summary>
        public static readonly string[][] Digits =
        {
            new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
            new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            new[] { "01110", "10001", "00001", "00110", "01000", "10000", "11111" },
            new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" },
            new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
            new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
            new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
            new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
            new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
        };

        /// <summary>Paints a number of 9 m wide, 15 m tall digits centred at <paramref name="centre"/>, readable by an aircraft facing north or south.</summary>
        public static void Number(MeshData mesh, string text, Vector3 centre, bool facingNorth)
        {
            const float cellW = 1.8f, cellH = 2.15f, digitGap = 3.6f;
            float width = text.Length * 5f * cellW + (text.Length - 1) * digitGap;
            float dir = facingNorth ? 1f : -1f;
            for (int d = 0; d < text.Length; d++)
            {
                var glyph = Digits[text[d] - '0'];
                float left = -width * 0.5f + d * (5f * cellW + digitGap);
                for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                {
                    if (glyph[row][col] != '1') continue;
                    float x = left + (col + 0.5f) * cellW;           // left to right as the pilot sees it
                    float zz = (3f - row) * cellH;                    // top row furthest ahead
                    MeshKit.Box(mesh, centre + new Vector3(dir * x, 0f, dir * zz), new Vector3(cellW + 0.05f, 0.02f, cellH + 0.05f), 0);
                }
            }
        }

        /// <summary>Edge lights (white), threshold lights (green facing the approach, red at the far end) and taxiway lights (blue): one mesh per colour.</summary>
        public static void CreateRunwayLights(Transform runway, Materials m, float runwayLength, float runwayWidth)
        {
            var white = new MeshData();
            var green = new MeshData();
            var red = new MeshData();
            var blue = new MeshData();
            var bulb = TerrainBuilder.Ellipsoid(8, 4);
            float half = runwayLength * 0.5f, edge = runwayWidth * 0.5f + 1.5f;
            for (float z = -half + 60f; z <= half - 60f; z += 60f)
            foreach (float side in new[] { -1f, 1f })
                white.Append(bulb, Matrix4x4.TRS(new Vector3(side * edge, 0.3f, z), Quaternion.identity, Vector3.one * 0.3f));
            for (int i = 0; i < 8; i++)
            {
                float x = (i - 3.5f) * runwayWidth / 8f;
                // Each threshold: green bar seen on approach, red seen from the runway (both are modelled; the colours mark the ends).
                green.Append(bulb, Matrix4x4.TRS(new Vector3(x, 0.3f, -half - 2f), Quaternion.identity, Vector3.one * 0.32f));
                green.Append(bulb, Matrix4x4.TRS(new Vector3(x, 0.3f, half + 2f), Quaternion.identity, Vector3.one * 0.32f));
                red.Append(bulb, Matrix4x4.TRS(new Vector3(x, 0.3f, -half - 2.8f), Quaternion.identity, Vector3.one * 0.26f));
                red.Append(bulb, Matrix4x4.TRS(new Vector3(x, 0.3f, half + 2.8f), Quaternion.identity, Vector3.one * 0.26f));
            }
            float runwayEdge = -runwayWidth * 0.5f;
            for (float x = runwayEdge - 10f; x >= runwayEdge - 30f; x -= 10f)
            foreach (float side in new[] { -1f, 1f })
                blue.Append(bulb, Matrix4x4.TRS(new Vector3(x, 0.3f, side * 10.5f), Quaternion.identity, Vector3.one * 0.28f));
            for (float z = -90f; z <= 90f; z += 15f)
                blue.Append(bulb, Matrix4x4.TRS(new Vector3(runwayEdge - 31f, 0.3f, z), Quaternion.identity, Vector3.one * 0.28f));
            Place("EdgeLights", runway, white, m.LightWhite, Vector3.zero, 0f, castShadows: false);
            Place("ThresholdLightsGreen", runway, green, m.LightGreen, Vector3.zero, 0f, castShadows: false);
            Place("ThresholdLightsRed", runway, red, m.LightRed, Vector3.zero, 0f, castShadows: false);
            Place("TaxiwayLights", runway, blue, m.LightBlue, Vector3.zero, 0f, castShadows: false);
        }

        // ---- Town and harbour ----------------------------------------------------------------------------------------

        /// <summary>The coastal town: houses along a street grid, a church, warehouses by the quay; one mesh.</summary>
        public static void CreateTown(Materials m)
        {
            var rng = new Random(1234);
            var mesh = new MeshData();
            var town = TerrainShape.Town;
            var wallCells = new[] { ScenePalette.Cell.HouseCream, ScenePalette.Cell.HouseOchre, ScenePalette.Cell.HouseWhite, ScenePalette.Cell.HouseGrey, ScenePalette.Cell.HouseRose };
            var roofCells = new[] { ScenePalette.Cell.RoofTerracotta, ScenePalette.Cell.RoofDark, ScenePalette.Cell.RoofSlate };
            int houses = 0;
            for (float bx = town.xMin + 7f; bx < town.xMax - 20f; bx += 110f)
            for (float bz = town.yMin + 7f; bz < town.yMax - 20f; bz += 90f)
            {
                // A block between streets: houses along its edges, gardens inside.
                float blockW = Mathf.Min(103f, town.xMax - bx), blockD = Mathf.Min(83f, town.yMax - bz);
                int perBlock = 5 + rng.Next(4);
                for (int i = 0; i < perBlock; i++)
                {
                    float w = Lerp(8f, 14f, rng.NextDouble()), d = Lerp(7f, 12f, rng.NextDouble());
                    bool alongX = rng.NextDouble() < 0.5;
                    float x, z;
                    if (alongX)
                    {
                        x = bx + 6f + (float)rng.NextDouble() * (blockW - 12f);
                        z = rng.NextDouble() < 0.5 ? bz + 7f : bz + blockD - 7f;
                    }
                    else
                    {
                        x = rng.NextDouble() < 0.5 ? bx + 8f : bx + blockW - 8f;
                        z = bz + 6f + (float)rng.NextDouble() * (blockD - 12f);
                    }
                    if (x + w * 0.5f > town.xMax || z + d * 0.5f > town.yMax) continue;
                    if (TerrainShape.RoadDistance(x, z) < 14f) continue; // the coast road crosses the town
                    float wall = Lerp(3f, 6.5f, rng.NextDouble()), roof = Lerp(1.8f, 3.4f, rng.NextDouble());
                    var house = new MeshData();
                    MeshKit.House(house, w, d, wall, roof, 0, 1);
                    ColourSubmeshes(house, wallCells[rng.Next(wallCells.Length)], roofCells[rng.Next(roofCells.Length)]);
                    float y = TerrainShape.Height(x, z) - 0.2f;
                    mesh.Append(house, Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0f, alongX ? 0f : 90f, 0f), Vector3.one));
                    houses++;
                }
            }
            // Church with a tower near the middle of the town, and warehouses along the quay side.
            {
                float x = town.center.x - 20f, z = town.center.y + 12f, y = TerrainShape.Height(x, z) - 0.2f;
                var church = new MeshData();
                MeshKit.House(church, 12f, 26f, 8f, 5f, 0, 1);
                ColourSubmeshes(church, ScenePalette.Cell.Stone, ScenePalette.Cell.RoofSlate);
                MeshKit.Box(church, new Vector3(0f, 11f, 16f), new Vector3(6f, 22f, 6f), 0);
                var spire = new MeshData();
                MeshKit.Revolve(spire, new[] { new Vector2(4.2f, 0f), new Vector2(0f, 9f) }, 4, 0, capBottom: true, capTop: false);
                spire.MapUv(ScenePalette.Uv(ScenePalette.Cell.RoofSlate));
                church.Append(spire, Matrix4x4.TRS(new Vector3(0f, 22f, 16f), Quaternion.Euler(0f, 45f, 0f), Vector3.one));
                mesh.Append(church, Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, Vector3.one));
            }
            for (int i = 0; i < 3; i++)
            {
                float x = town.xMax - 25f, z = town.yMin + 60f + i * 40f, y = TerrainShape.Height(x, z) - 0.2f;
                var shed = new MeshData();
                MeshKit.House(shed, 30f, 16f, 6f, 2.5f, 0, 1);
                ColourSubmeshes(shed, ScenePalette.Cell.HouseGrey, ScenePalette.Cell.RoofDark);
                mesh.Append(shed, Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0f, 90f, 0f), Vector3.one));
            }
            var go = Place("Town", null, mesh, m.Palette, Vector3.zero, 0f, collider: false);
            go.isStatic = true;
        }

        /// <summary>The harbour: a quay along the breakwater root, bollards, five moored boats with cabins and masts.</summary>
        public static void CreateHarbour(Transform harbour, Materials m)
        {
            var rng = new Random(77);
            var mesh = new MeshData();
            Vector3 start = PrototypeWorld.BreakwaterStart;
            float sea = PrototypeWorld.SeaHeight;
            // Quay: a stone platform on the shore side of the breakwater root, and a slipway.
            Boxed(mesh, new Vector3(start.x - 20f, sea + 1.2f, start.z + 60f), new Vector3(60f, 2.6f, 140f), ScenePalette.Cell.Stone);
            for (float z = start.z + 0f; z <= start.z + 120f; z += 20f)
                Boxed(mesh, new Vector3(start.x + 8f, sea + 2.9f, z), new Vector3(0.6f, 0.8f, 0.6f), ScenePalette.Cell.DarkMetal);
            // Boats moored in the lee of the breakwater.
            var hullCells = new[] { ScenePalette.Cell.HullWhite, ScenePalette.Cell.HullBlue, ScenePalette.Cell.HullRed, ScenePalette.Cell.Navy, ScenePalette.Cell.HullWhite };
            for (int i = 0; i < hullCells.Length; i++)
            {
                float length = Lerp(9f, 16f, rng.NextDouble());
                var boat = Boat(length, hullCells[i]);
                var position = new Vector3(start.x + 60f + i * 45f + Lerp(-8f, 8f, rng.NextDouble()), sea, start.z + 40f + Lerp(-10f, 10f, rng.NextDouble()));
                mesh.Append(boat, Matrix4x4.TRS(position, Quaternion.Euler(0f, 90f + Lerp(-15f, 15f, rng.NextDouble()), 0f), Vector3.one));
            }
            var go = Place("Quay", harbour, mesh, m.Palette, Vector3.zero, 0f, collider: true);
            go.isStatic = true;
        }

        /// <summary>A small boat: lofted hull with a transom, a wheelhouse and a mast; bow along +Z, waterline at y = 0.</summary>
        public static MeshData Boat(float length, ScenePalette.Cell hull)
        {
            var mesh = new MeshData();
            var rings = new List<MeshKit.Ring>();
            float beam = length * 0.3f;
            // stern (flat transom) to bow: z, half width, depth below, freeboard above
            foreach (var s in new[] { new Vector4(-0.5f, 0.42f, 0.5f, 0.5f), new Vector4(-0.2f, 0.5f, 0.6f, 0.5f), new Vector4(0.15f, 0.48f, 0.55f, 0.55f), new Vector4(0.4f, 0.3f, 0.35f, 0.65f), new Vector4(0.5f, 0.02f, 0.05f, 0.75f) })
            {
                var section = MeshKit.RoundedSection(12, s.y * beam, 0.5f * (s.z + s.w), 0.5f * (s.w - s.z), 2.6f);
                rings.Add(MeshKit.Place(section, s.x * length, s.x + 0.5f));
            }
            var hullMesh = new MeshData();
            MeshKit.Loft(hullMesh, rings, 0, (segment, point, u, v) => v > 0.35f && v < 0.65f && segment >= 0, capStart: true, capEnd: true); // open deck
            hullMesh.MapUv(ScenePalette.Uv(hull));
            mesh.Append(hullMesh, Matrix4x4.identity);
            Boxed(mesh, new Vector3(0f, 0.5f, 0f), new Vector3(beam * 0.95f, 0.1f, length * 0.9f), ScenePalette.Cell.Wood);                 // deck
            Boxed(mesh, new Vector3(0f, 1.3f, -length * 0.12f), new Vector3(beam * 0.6f, 1.6f, length * 0.22f), ScenePalette.Cell.White);   // wheelhouse
            Boxed(mesh, new Vector3(0f, 1.35f, -length * 0.12f + length * 0.111f), new Vector3(beam * 0.5f, 0.8f, 0.05f), ScenePalette.Cell.CabGlass);
            var mast = new MeshData();
            MeshKit.Revolve(mast, new[] { new Vector2(0.06f, 0f), new Vector2(0.04f, length * 0.6f) }, 6, 0, capBottom: false, capTop: true);
            mast.MapUv(ScenePalette.Uv(ScenePalette.Cell.Steel));
            mesh.Append(mast, Matrix4x4.Translate(new Vector3(0f, 0.5f, length * 0.12f)));
            return mesh;
        }

        /// <summary>The lighthouse: a tapered white tower with two red bands, a gallery, a glazed lantern and a red cap. The lens is the caller's.</summary>
        public static MeshData Lighthouse(float height)
        {
            var mesh = new MeshData();
            float r0 = 3.6f, r1 = 2.5f;
            float Radius(float y) => Mathf.Lerp(r0, r1, y / height);
            var bands = new[] { 0f, 0.3f, 0.42f, 0.62f, 0.74f, 1f };
            for (int i = 0; i < bands.Length - 1; i++)
            {
                float y0 = bands[i] * height, y1 = bands[i + 1] * height;
                Revolved(mesh, new[] { new Vector2(Radius(y0), y0), new Vector2(Radius(y1), y1) }, 16, i % 2 == 1 ? ScenePalette.Cell.Red : ScenePalette.Cell.White, capTop: i == bands.Length - 2);
            }
            Revolved(mesh, new[] { new Vector2(r1 + 1.3f, height + 0.1f), new Vector2(r1 + 1.3f, height + 0.5f) }, 16, ScenePalette.Cell.Red, capTop: true, capBottom: true);
            Revolved(mesh, new[] { new Vector2(r1 - 0.3f, height + 0.5f), new Vector2(r1 - 0.3f, height + 3.4f) }, 12, ScenePalette.Cell.CabGlass, capTop: false);
            Revolved(mesh, new[] { new Vector2(r1 + 0.2f, height + 3.4f), new Vector2(r1 - 0.4f, height + 4.2f), new Vector2(0f, height + 5.4f) }, 12, ScenePalette.Cell.Red, capBottom: true);
            return mesh;
        }

        // ---- Sky ----------------------------------------------------------------------------------------------------

        /// <summary>The cloud deck: a wide, two-sided translucent plane at the cloud base, drifting with the wind.</summary>
        public static GameObject CreateClouds(Material cloud)
        {
            var mesh = new MeshData();
            MeshKit.Grid(mesh, 12, 12, CloudDeckSizeMetres, CloudDeckSizeMetres, (c, r) => 0f, 0);
            var go = Place("CloudDeck", null, mesh, cloud, new Vector3(0f, CloudBaseMetres, 0f), 0f, castShadows: false);
            var scroll = go.AddComponent<ScrollingSurface>();
            scroll.baseSpeed = new Vector2(0.0009f, 0.0004f); // about 9 m/s of wind across 10 km tiles
            scroll.detailSpeed = Vector2.zero;
            return go;
        }

        /// <summary>Tileable cumulus deck: clear between the clouds, white tops with slightly grey, flat bases.</summary>
        public static Color32[] CloudPixels(int size)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float n = ProceduralNoise.Fractal(u, v, 3, 6, 505, 0.55f);
                float shape = ProceduralNoise.SmoothStep(0.5f, 0.6f, n);
                float thick = ProceduralNoise.SmoothStep(0.55f, 0.78f, n);
                float alpha = shape * (0.86f + 0.14f * thick);
                var colour = Color.Lerp(new Color(0.98f, 0.98f, 1f), new Color(0.80f, 0.82f, 0.87f), thick);
                pixels[y * size + x] = new Color32((byte)(colour.r * 255f), (byte)(colour.g * 255f), (byte)(colour.b * 255f), (byte)Mathf.RoundToInt(alpha * 255f));
            }
            return pixels;
        }

        /// <summary>Tileable wave normals: a swell with ripples across it.</summary>
        public static Color32[] WaterNormalPixels(int size)
        {
            var heights = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float swell = Mathf.Sin((u * 3f + v * 1.2f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                float chop = ProceduralNoise.Fractal(u, v, 6, 5, 909, 0.55f);
                heights[y * size + x] = swell * 0.35f + chop * 0.65f;
            }
            return GeneratedAssets.NormalMapFromHeights(heights, size, size, 1.6f, 1f, tile: true);
        }

        /// <summary>Tileable asphalt: dark, fine aggregate, faint patches.</summary>
        public static Color32[] AsphaltPixels(int size)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float grain = ProceduralNoise.Fractal(u, v, 64, 3, 303) - 0.5f;
                float patch = ProceduralNoise.Fractal(u, v, 3, 3, 304) - 0.5f;
                float g = Mathf.Clamp01(0.2f + grain * 0.22f + patch * 0.08f);
                pixels[y * size + x] = new Color32((byte)(g * 255f), (byte)(g * 255f), (byte)((g + 0.01f) * 255f), 255);
            }
            return pixels;
        }

        /// <summary>Tileable concrete apron: light grey panels with joint lines, light staining.</summary>
        public static Color32[] ConcretePixels(int size)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float grain = ProceduralNoise.Fractal(u, v, 48, 3, 404) - 0.5f;
                float stain = ProceduralNoise.Fractal(u, v, 4, 3, 405) - 0.5f;
                float joint = Mathf.Min(Mathf.Repeat(u * 4f, 1f), Mathf.Repeat(v * 4f, 1f)) < 0.012f ? -0.18f : 0f;
                float g = Mathf.Clamp01(0.58f + grain * 0.12f + stain * 0.1f + joint);
                pixels[y * size + x] = new Color32((byte)(g * 255f), (byte)((g - 0.01f) * 255f), (byte)((g - 0.03f) * 255f), 255);
            }
            return pixels;
        }

        // ---- Helpers -------------------------------------------------------------------------------------------------

        static void Revolved(MeshData mesh, Vector2[] profile, int segments, ScenePalette.Cell cell, bool capTop = false, bool capBottom = false)
        {
            var part = new MeshData();
            MeshKit.Revolve(part, profile, segments, 0, capBottom: capBottom, capTop: capTop);
            part.MapUv(ScenePalette.Uv(cell));
            mesh.Append(part, Matrix4x4.identity);
        }

        static void Boxed(MeshData mesh, Vector3 centre, Vector3 size, ScenePalette.Cell cell)
        {
            var part = new MeshData();
            MeshKit.Box(part, centre, size, 0);
            part.MapUv(ScenePalette.Uv(cell));
            mesh.Append(part, Matrix4x4.identity);
        }

        /// <summary>Maps submesh 0 (walls) and 1 (roof) of a house to palette cells and merges them into one submesh.</summary>
        static void ColourSubmeshes(MeshData house, ScenePalette.Cell walls, ScenePalette.Cell roof)
        {
            // Vertices of the roof come after the walls (House adds the box first): find the split by triangle lists.
            var wallsUv = ScenePalette.Uv(walls);
            var roofUv = ScenePalette.Uv(roof);
            var roofVertices = new HashSet<int>();
            if (house.SubmeshCount > 1)
                foreach (int i in house.Triangles(1)) roofVertices.Add(i);
            var merged = new MeshData();
            for (int i = 0; i < house.VertexCount; i++)
            {
                var uv = roofVertices.Contains(i) ? roofUv : wallsUv;
                merged.AddVertex(house.Vertices[i], house.Normals[i], new Vector2(uv.x, uv.y));
            }
            for (int s = 0; s < house.SubmeshCount; s++)
            {
                var t = house.Triangles(s);
                for (int i = 0; i < t.Count; i += 3) merged.AddTriangle(0, t[i], t[i + 1], t[i + 2]);
            }
            house.Replace(merged);
        }

        /// <summary>Saves the geometry as a mesh asset and places it as a static renderer (optionally with a mesh collider).</summary>
        public static GameObject Place(string name, Transform parent, MeshData data, Material material, Vector3 position, float yaw, bool collider = false, bool castShadows = true)
        {
            var mesh = GeneratedAssets.SaveMesh(data, name, $"{MeshesFolder}/{name}.asset");
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
            return go;
        }

        static float Lerp(float a, float b, double t) => a + (b - a) * (float)t;
    }
}
