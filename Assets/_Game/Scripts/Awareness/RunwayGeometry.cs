using System;
using UnityEngine;

namespace OpenSkies.Awareness
{
    /// <summary>
    /// A runway strip in world space. Either end can be used for landing; the
    /// geometry picks the end that matches the aircraft's direction of travel.
    /// </summary>
    [Serializable]
    public struct RunwayInfo
    {
        [Tooltip("Centre of the runway surface, world space, m.")]
        public Vector3 centre;
        [Tooltip("Landing direction of the primary end, degrees true (0 = +Z, 90 = +X).")]
        public float headingDegrees;
        [Tooltip("m")]
        public float lengthMetres;
        [Tooltip("m")]
        public float widthMetres;

        public RunwayInfo(Vector3 centre, float headingDegrees, float lengthMetres, float widthMetres)
        {
            this.centre = centre;
            this.headingDegrees = headingDegrees;
            this.lengthMetres = lengthMetres;
            this.widthMetres = widthMetres;
        }

        public bool IsDefined => lengthMetres > 0f && widthMetres > 0f;
    }

    /// <summary>
    /// The aircraft relative to the runway end it is most likely to use, all
    /// horizontal distances in metres. "Along track" is measured on the extended
    /// centreline from the landing threshold: positive before the threshold (on
    /// the approach side), negative beyond it (over the runway or past its end).
    /// </summary>
    public struct RunwayRelation
    {
        /// <summary>False when no runway is defined; every other field is then zero.</summary>
        public bool Valid;
        /// <summary>Runway number of the selected landing direction, 1-36.</summary>
        public int Designator;
        public float LandingHeadingDegrees;
        public float RunwayLengthMetres;
        public float RunwayWidthMetres;
        public float AlongTrackMetres;
        /// <summary>Positive right of the extended centreline, looking in the landing direction.</summary>
        public float CrossTrackMetres;
        /// <summary>Horizontal straight-line distance to the landing threshold.</summary>
        public float DistanceToThresholdMetres;
        public float HeightAboveThresholdMetres;
        /// <summary>Aircraft track minus landing heading, −180..180 (positive: track is right of the runway heading).</summary>
        public float TrackErrorDegrees;
        /// <summary>Over the paved surface (horizontally).</summary>
        public bool OnRunway;
        /// <summary>True when the aircraft is far enough before the aim point for a glide path angle to mean something.</summary>
        public bool GlidepathValid;
        /// <summary>Elevation of the aircraft seen from the touchdown aim point, degrees.</summary>
        public float GlidepathAngleDegrees;
        /// <summary>Distance to the runway centre (the airfield), for navigation.</summary>
        public float DistanceToRunwayMetres;
        /// <summary>Bearing from the aircraft to the runway centre, degrees true, 0-360.</summary>
        public float BearingToRunwayDegrees;
    }

    /// <summary>Pure geometry between an aircraft position and a runway.</summary>
    public static class RunwayGeometry
    {
        /// <summary>A glide path angle is only reported this far (m, horizontal) or more before the aim point.</summary>
        public const float MinGlidepathDistanceMetres = 30f;

        /// <param name="position">Aircraft reference point, world space, m.</param>
        /// <param name="trackDegrees">Direction of travel over the ground (heading when nearly stationary).</param>
        /// <param name="aimPointMetres">Touchdown aim point, measured from the threshold along the runway.</param>
        public static RunwayRelation Evaluate(in RunwayInfo runway, Vector3 position, float trackDegrees, float aimPointMetres)
        {
            var r = new RunwayRelation();
            if (!runway.IsDefined) return r;

            // Use the end whose landing direction is closest to the aircraft's track.
            float primary = Normalize360(runway.headingDegrees);
            float landing = Mathf.Abs(Mathf.DeltaAngle(primary, trackDegrees)) <= 90f ? primary : Normalize360(primary + 180f);
            Vector3 direction = HorizontalDirection(landing);
            Vector3 right = new Vector3(direction.z, 0f, -direction.x);
            Vector3 threshold = runway.centre - direction * (runway.lengthMetres * 0.5f);

            Vector3 offset = position - threshold;
            offset.y = 0f;
            float pastThreshold = Vector3.Dot(offset, direction);
            float cross = Vector3.Dot(offset, right);
            float height = position.y - runway.centre.y;

            r.Valid = true;
            r.Designator = Designator(landing);
            r.LandingHeadingDegrees = landing;
            r.RunwayLengthMetres = runway.lengthMetres;
            r.RunwayWidthMetres = runway.widthMetres;
            r.AlongTrackMetres = -pastThreshold;
            r.CrossTrackMetres = cross;
            r.DistanceToThresholdMetres = offset.magnitude;
            r.HeightAboveThresholdMetres = height;
            r.TrackErrorDegrees = Mathf.DeltaAngle(landing, trackDegrees);
            r.OnRunway = pastThreshold >= 0f && pastThreshold <= runway.lengthMetres && Mathf.Abs(cross) <= runway.widthMetres * 0.5f;

            float toAimPoint = aimPointMetres - pastThreshold;
            r.GlidepathValid = toAimPoint >= MinGlidepathDistanceMetres;
            r.GlidepathAngleDegrees = r.GlidepathValid ? Mathf.Atan2(height, toAimPoint) * Mathf.Rad2Deg : 0f;

            Vector3 toCentre = runway.centre - position;
            toCentre.y = 0f;
            r.DistanceToRunwayMetres = toCentre.magnitude;
            r.BearingToRunwayDegrees = r.DistanceToRunwayMetres > 1e-3f ? HeadingOf(toCentre) : 0f;
            return r;
        }

        /// <summary>Runway number for a landing heading: 360° is 36, 5° rounds to 36, 184° is 18.</summary>
        public static int Designator(float headingDegrees)
        {
            int number = Mathf.RoundToInt(Normalize360(headingDegrees) / 10f) % 36;
            return number == 0 ? 36 : number;
        }

        /// <summary>Compass direction of a horizontal vector: 0 = +Z, 90 = +X.</summary>
        public static float HeadingOf(Vector3 horizontal) => Normalize360(Mathf.Atan2(horizontal.x, horizontal.z) * Mathf.Rad2Deg);

        public static Vector3 HorizontalDirection(float headingDegrees)
        {
            float radians = headingDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        public static float Normalize360(float degrees)
        {
            float d = degrees % 360f;
            return d < 0f ? d + 360f : d;
        }
    }
}
