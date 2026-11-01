using UnityEngine;

namespace OpenSkies.World
{
    /// <summary>
    /// Layout of the prototype world: one airfield with runway 36/18 on a flat
    /// coastal plain, the sea to the east, a lighthouse on a low headland, a
    /// harbour with a breakwater and an islet offshore. PLACEHOLDER LAYOUT until
    /// the regional airport and coastline milestone (docs/ROADMAP.md, M5): the
    /// scene builder draws primitives from these numbers, and the landmark
    /// registry gives the same positions to missions and navigation.
    ///
    /// Axes: +Z north, +X east, metres; the airfield is at sea level (y = 0).
    /// </summary>
    public static class PrototypeWorld
    {
        // Runway 36/18, centred on the origin, landing direction north for 36.
        public const float RunwayLengthMetres = 1500f;
        public const float RunwayWidthMetres = 30f;
        public const float RunwayHeadingDegrees = 0f;
        public static readonly Vector3 RunwayCentre = Vector3.zero;

        // Land and sea. The land plane ends where the sea plane starts (no overlap, so no depth fighting).
        public const float ShorelineX = 1600f;
        public const float LandWestX = -6400f;
        public const float SeaEastX = 13600f;
        public const float WorldSouthZ = -8000f;
        public const float WorldNorthZ = 8000f;
        public const float LandHeight = -0.1f;   // 10 cm under the runway surface, as before
        public const float SeaHeight = -0.6f;

        // Lighthouse on a low rocky headland, north-east of the airfield.
        public static readonly Vector3 HeadlandCentre = new Vector3(1850f, 0f, 2600f);
        public static readonly Vector3 HeadlandSize = new Vector3(700f, 3f, 480f); // top 3 m above sea level
        public static readonly Vector3 LighthousePosition = new Vector3(1980f, 3f, 2600f);
        public const float LighthouseHeightMetres = 22f;

        // Harbour south-east of the airfield: a breakwater running out to sea with a light at its head.
        public static readonly Vector3 BreakwaterStart = new Vector3(1500f, 0f, -1800f);
        public static readonly Vector3 HarbourLightPosition = new Vector3(2000f, 2f, -1800f);

        // Islet offshore, east of the airfield.
        public static readonly Vector3 IsletCentre = new Vector3(3300f, 0f, 300f);
        public const float IsletRadiusMetres = 130f;
        public const float IsletHeightMetres = 10f;

        public const string AirfieldId = "airfield";
        public const string LighthouseId = "lighthouse";
        public const string HarbourId = "harbour";
        public const string IsletId = "islet";

        /// <summary>The prototype world's landmarks: airfield, lighthouse, harbour, islet.</summary>
        public static Landmark[] CreateLandmarks() => new[]
        {
            new Landmark(AirfieldId, "AIRFIELD", LandmarkKind.Airfield, RunwayCentre, 1200f),
            new Landmark(LighthouseId, "LIGHTHOUSE", LandmarkKind.Lighthouse, Ground(LighthousePosition), 550f),
            new Landmark(HarbourId, "HARBOUR", LandmarkKind.Harbour, Ground(HarbourLightPosition), 550f),
            new Landmark(IsletId, "ISLET", LandmarkKind.Island, IsletCentre, 450f),
        };

        public static LandmarkRegistry CreateRegistry() => new LandmarkRegistry(CreateLandmarks());

        static Vector3 Ground(Vector3 p) => new Vector3(p.x, 0f, p.z);
    }
}
