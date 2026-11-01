using UnityEngine;

namespace OpenSkies.Awareness
{
    /// <summary>
    /// A place the navigation guide points to: the airfield by default, or a
    /// mission checkpoint. The name is a cached string (HUD text never allocates).
    /// </summary>
    public struct NavigationTarget
    {
        /// <summary>Short upper-case name shown on the guide, e.g. "LIGHTHOUSE".</summary>
        public string Name;
        /// <summary>World position, m (only the horizontal position is used).</summary>
        public Vector3 Position;
        /// <summary>Within this horizontal distance the target counts as reached, m.</summary>
        public float ArrivalRadiusMetres;
        /// <summary>A landmark that can carry a checkpoint marker (not the runway or an approach point).</summary>
        public bool IsLandmark;

        public NavigationTarget(string name, Vector3 position, float arrivalRadiusMetres, bool isLandmark)
        {
            Name = name;
            Position = position;
            ArrivalRadiusMetres = arrivalRadiusMetres;
            IsLandmark = isLandmark;
        }

        public bool IsDefined => !string.IsNullOrEmpty(Name);
    }

    /// <summary>The aircraft relative to its navigation target.</summary>
    public struct NavigationRelation
    {
        /// <summary>False when there is no target; every other field is then default.</summary>
        public bool Valid;
        public NavigationTarget Target;
        /// <summary>Horizontal distance to the target, m.</summary>
        public float DistanceMetres;
        /// <summary>Course to steer to the target, degrees true, 0-360.</summary>
        public float BearingDegrees;
        /// <summary>Bearing minus the aircraft heading, −180..180 (positive: target to the right).</summary>
        public float RelativeBearingDegrees;
        public bool WithinArrivalRadius;
    }

    /// <summary>Pure navigation geometry.</summary>
    public static class NavigationMath
    {
        public const float MetresPerNauticalMile = 1852f;

        public static NavigationRelation Evaluate(in NavigationTarget target, Vector3 position, float headingDegrees)
        {
            if (!target.IsDefined) return default;
            Vector3 offset = target.Position - position;
            offset.y = 0f;
            float distance = offset.magnitude;
            float bearing = distance > 1e-3f ? RunwayGeometry.HeadingOf(offset) : 0f;
            return new NavigationRelation
            {
                Valid = true,
                Target = target,
                DistanceMetres = distance,
                BearingDegrees = bearing,
                RelativeBearingDegrees = Mathf.DeltaAngle(SoftLogic.Finite(headingDegrees, 0f), bearing),
                WithinArrivalRadius = distance <= Mathf.Max(target.ArrivalRadiusMetres, 0f),
            };
        }

        /// <summary>The airfield as a navigation target: the runway centre.</summary>
        public static NavigationTarget Airfield(in RunwayInfo runway, string name = "AIRFIELD") =>
            runway.IsDefined ? new NavigationTarget(name, runway.centre, runway.lengthMetres * 0.5f, false) : default;

        /// <summary>
        /// The same guide data the HUD used before missions existed: the runway relation's
        /// distance and bearing to the airfield.
        /// </summary>
        public static NavigationRelation FromRunway(in RunwayRelation runway, float headingDegrees, string name = "AIRFIELD")
        {
            if (!runway.Valid) return default;
            return new NavigationRelation
            {
                Valid = true,
                Target = new NavigationTarget(name, Vector3.zero, 0f, false),
                DistanceMetres = runway.DistanceToRunwayMetres,
                BearingDegrees = runway.BearingToRunwayDegrees,
                RelativeBearingDegrees = Mathf.DeltaAngle(SoftLogic.Finite(headingDegrees, 0f), runway.BearingToRunwayDegrees),
            };
        }

        /// <summary>
        /// A point on the extended centreline of a runway end, <paramref name="distanceMetres"/>
        /// before its landing threshold (horizontal), for example the start of a final approach.
        /// </summary>
        public static Vector3 PointOnFinal(in RunwayInfo runway, float landingHeadingDegrees, float distanceMetres)
        {
            Vector3 direction = RunwayGeometry.HorizontalDirection(landingHeadingDegrees);
            Vector3 threshold = runway.centre - direction * (runway.lengthMetres * 0.5f);
            return threshold - direction * distanceMetres;
        }
    }
}
