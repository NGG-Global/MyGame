using OpenSkies.World;
using UnityEngine;

namespace OpenSkies.Editor
{
    /// <summary>
    /// The shape and land use of the coastal plain, as pure functions of the world
    /// position (metres, +X east, +Z north): height, the flat airfield zone, the
    /// western hills, woodland, the town, the roads and the resulting ground colour.
    /// The terrain mesh, its textures, the tree planting and the town all read the
    /// same functions, so they agree with each other. Deterministic (fixed seeds).
    /// Within <see cref="PrototypeWorld"/>'s layout: nothing here moves the runway,
    /// the shoreline or the landmarks.
    /// </summary>
    public static class TerrainShape
    {
        public const int Seed = 7;

        /// <summary>Land extent (the sea plane starts at the shoreline; the beach covers the join).</summary>
        public static float WestX => PrototypeWorld.LandWestX;
        public static float EastX => PrototypeEnvironment.GrassEastX;
        public static float SouthZ => PrototypeWorld.WorldSouthZ;
        public static float NorthZ => PrototypeWorld.WorldNorthZ;
        public static float Width => EastX - WestX;
        public static float Length => NorthZ - SouthZ;

        /// <summary>The town by the harbour: a rectangle on the plain, south-east of the airfield, behind the beach.</summary>
        public static readonly Rect Town = new Rect(950f, -2250f, 450f, 720f); // x, z, width, depth
        /// <summary>Road centre lines (polylines): airfield gate to the town, and the coast road.</summary>
        public static readonly Vector2[][] Roads =
        {
            new[] { new Vector2(-150f, -230f), new Vector2(-80f, -600f), new Vector2(500f, -1250f), new Vector2(950f, -1850f) },
            new[] { new Vector2(1280f, -2250f), new Vector2(1300f, -1500f), new Vector2(1250f, 0f), new Vector2(1380f, 1800f), new Vector2(1500f, 2600f) },
        };
        public const float RoadHalfWidth = 4.5f;

        // ---- Height ---------------------------------------------------------------------------------------

        /// <summary>
        /// Terrain height, m. Exactly <see cref="PrototypeWorld.LandHeight"/> on the airfield and
        /// its surroundings, a gently rolling plain beyond, rising into hills in the west
        /// (outside every mission route), falling to the beach in the east.
        /// </summary>
        public static float Height(float x, float z)
        {
            float u = (x - WestX) / Width, v = (z - SouthZ) / Length;
            float flat = AirfieldFlatness(x, z);
            float coast = ProceduralNoise.SmoothStep(EastX - 600f, EastX - 80f, x); // 0 inland .. 1 at the beach

            // Rolling plain: a few metres, longer waves along the coast.
            float plain = (ProceduralNoise.Fractal(u * 1.0f, v * 2.0f, 6, 3, Seed) - 0.5f) * 14f;
            plain = Mathf.Max(plain, -1f);

            // Western hills: a ridge system that grows from 3.4 km west of the runway to the edge of the land.
            // The fractal spans about 0.15 to 0.56 here, so the ridges are mapped from that range (not from 0.38
            // to 1, which left them under 110 m): summits reach about 350 m, slopes mostly under 30 degrees.
            float west = ProceduralNoise.SmoothStep(-3300f, -5400f, x);
            float ridge = ProceduralNoise.Fractal(u * 3f, v * 6f, 2, 5, Seed + 3, 0.4f);
            ridge = Mathf.Clamp01((ridge - 0.3f) / 0.3f);
            float hills = west * (50f + 450f * ridge);
            // Two foothills nearer the plain, low and broad, south-west and north-west of the field.
            hills += 70f * Bump(x, z, -4300f, -3900f, 900f) + 90f * Bump(x, z, -4200f, 4300f, 1000f);

            float height = PrototypeWorld.LandHeight + (plain + hills) * (1f - flat) * (1f - coast);
            return height;
        }

        static float Bump(float x, float z, float cx, float cz, float radius)
        {
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) / radius;
            return d >= 1f ? 0f : (1f - d * d) * (1f - d * d);
        }

        /// <summary>1 where the ground must stay exactly flat (runway, apron, approaches), 0 well away from the airfield.</summary>
        public static float AirfieldFlatness(float x, float z)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(x) - 900f), dz = Mathf.Max(0f, Mathf.Abs(z) - 1500f);
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            return 1f - ProceduralNoise.SmoothStep(0f, 900f, d);
        }

        /// <summary>Ground slope (rise over run) from finite differences of <see cref="Height"/>.</summary>
        public static float Slope(float x, float z, float step = 40f)
        {
            float dx = (Height(x + step, z) - Height(x - step, z)) / (2f * step);
            float dz = (Height(x, z + step) - Height(x, z - step)) / (2f * step);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The surface normal from the same differences.</summary>
        public static Vector3 Normal(float x, float z, float step = 40f)
        {
            float dx = (Height(x + step, z) - Height(x - step, z)) / (2f * step);
            float dz = (Height(x, z + step) - Height(x, z - step)) / (2f * step);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        // ---- Land use -------------------------------------------------------------------------------------

        /// <summary>Woodland density 0..1 (broad patches, denser towards the hills, none on the airfield, the beach, the town or the roads).</summary>
        public static float Woodland(float x, float z) => Woodland(x, z, Height(x, z) - PrototypeWorld.LandHeight);

        /// <param name="heightAboveLand">The terrain height above the plain at (x, z), already known.</param>
        public static float Woodland(float x, float z, float heightAboveLand)
        {
            float u = (x - WestX) / Width, v = (z - SouthZ) / Length;
            float n = ProceduralNoise.Fractal(u * 2f, v * 4f, 3, 4, Seed + 11, 0.5f);
            float towardsHills = 0.08f * ProceduralNoise.SmoothStep(-2500f, -5000f, x);
            float w = ProceduralNoise.SmoothStep(0.56f - towardsHills, 0.66f - towardsHills, n);
            w *= 1f - AirfieldFlatness(x, z) * 0.9f;
            w *= 1f - ProceduralNoise.SmoothStep(EastX - 400f, EastX - 150f, x);
            w *= 1f - ProceduralNoise.SmoothStep(260f, 420f, heightAboveLand); // treeline
            if (Town.Contains(new Vector2(x, z))) w = 0f;
            if (RoadDistance(x, z) < 25f) w = 0f;
            return w;
        }

        /// <summary>Distance to the nearest road centre line, m.</summary>
        public static float RoadDistance(float x, float z)
        {
            float best = float.MaxValue;
            var p = new Vector2(x, z);
            foreach (var road in Roads)
                for (int i = 1; i < road.Length; i++)
                    best = Mathf.Min(best, SegmentDistance(p, road[i - 1], road[i]));
            return best;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Inside the town's street grid (a street every 90 m along z, every 110 m along x).</summary>
        public static bool OnTownStreet(float x, float z)
        {
            if (!Town.Contains(new Vector2(x, z))) return false;
            float lx = Mathf.Repeat(x - Town.xMin, 110f), lz = Mathf.Repeat(z - Town.yMin, 90f);
            return lx < 7f || lz < 7f;
        }

        // ---- Colour ------------------------------------------------------------------------------------------

        static readonly Color[] Crops =
        {
            new Color(0.34f, 0.49f, 0.21f), // pasture
            new Color(0.49f, 0.54f, 0.24f), // hay meadow
            new Color(0.70f, 0.60f, 0.33f), // ripe cereal
            new Color(0.40f, 0.31f, 0.22f), // ploughed
            new Color(0.27f, 0.42f, 0.19f), // dark crop
            new Color(0.56f, 0.52f, 0.28f), // stubble
        };
        static readonly Color AirfieldGrass = new Color(0.38f, 0.5f, 0.23f);
        static readonly Color Hedge = new Color(0.17f, 0.27f, 0.12f);
        static readonly Color Wood = new Color(0.15f, 0.28f, 0.12f);
        static readonly Color Sand = new Color(0.79f, 0.73f, 0.57f);
        static readonly Color Rock = new Color(0.47f, 0.45f, 0.41f);
        static readonly Color Moor = new Color(0.46f, 0.44f, 0.26f);
        static readonly Color Road = new Color(0.33f, 0.33f, 0.34f);
        static readonly Color TownGround = new Color(0.52f, 0.49f, 0.44f);

        /// <summary>
        /// Ground colour at a position (albedo, sRGB): a patchwork of fields with hedgerows,
        /// woods, the mown airfield, the beach, rock on the steep hills, roads and the town's
        /// streets. Pure; the texture generator samples it per texel.
        /// </summary>
        public static Color Colour(float x, float z) => Colour(x, z, Height(x, z) - PrototypeWorld.LandHeight, Slope(x, z));

        /// <param name="height">Terrain height above the plain, m (already known).</param>
        /// <param name="slope">Ground slope, rise over run (already known).</param>
        public static Color Colour(float x, float z, float height, float slope)
        {
            float u = (x - WestX) / Width, v = (z - SouthZ) / Length;

            // Fields: cellular cells about 330 m across, each its own crop and brightness.
            ProceduralNoise.Cellular(u * 1f, v * 2f, 24, Seed + 5, out int id, out float d1, out float d2);
            // The cellular field is periodic in 24 cells; v * 2 repeats it twice along the coast, which 16 km hides well enough.
            var crop = Crops[Mathf.Abs(id * 7 + Seed) % Crops.Length];
            float shade = 0.85f + 0.3f * ProceduralNoise.Random01(id, 3);
            var colour = crop * shade;
            float mottle = ProceduralNoise.Fractal(u * 8f, v * 16f, 8, 3, Seed + 21) - 0.5f;
            colour *= 1f + mottle * 0.35f;
            if (d2 - d1 < 0.05f) colour = Color.Lerp(colour, Hedge, 0.75f); // hedgerows along the field boundaries

            // Woods.
            float wood = Woodland(x, z, height);
            colour = Color.Lerp(colour, Wood * (1f + mottle * 0.5f), wood);

            // The airfield: one mown meadow.
            float flat = AirfieldFlatness(x, z);
            colour = Color.Lerp(colour, AirfieldGrass * (1f + mottle * 0.25f), ProceduralNoise.SmoothStep(0.35f, 0.9f, flat));

            // Hills: moorland above 150 m, rock where it is steep.
            colour = Color.Lerp(colour, Moor * (1f + mottle * 0.3f), ProceduralNoise.SmoothStep(150f, 320f, height));
            colour = Color.Lerp(colour, Rock * (1f + mottle * 0.4f), ProceduralNoise.SmoothStep(0.28f, 0.5f, slope));

            // Roads and the town.
            float road = RoadDistance(x, z);
            if (Town.Contains(new Vector2(x, z))) colour = Color.Lerp(TownGround, Road, OnTownStreet(x, z) ? 1f : 0f) * (1f + mottle * 0.2f);
            colour = Color.Lerp(colour, Road, 1f - ProceduralNoise.SmoothStep(RoadHalfWidth, RoadHalfWidth + 3f, road));

            // The beach.
            colour = Color.Lerp(colour, Sand * (1f + mottle * 0.15f), ProceduralNoise.SmoothStep(EastX - 160f, EastX - 40f, x));
            colour.a = 1f;
            return colour;
        }
    }
}
