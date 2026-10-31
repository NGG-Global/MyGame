using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>Flight instruments. Values are stable and match the cockpit's instrument items.</summary>
    public enum InstrumentKind
    {
        Airspeed = 0,
        Attitude = 1,
        Altimeter = 2,
        Heading = 3,
        VerticalSpeed = 4,
        Navigation = 5,
    }

    /// <summary>
    /// What every instrument shows, taken once per frame from the shared
    /// telemetry and runway geometry. Instruments only display it; there is no
    /// simulation (no gyro drift, lag or errors) inside the instrument UI.
    /// </summary>
    public struct InstrumentReadings
    {
        public float AirspeedKnots;
        public float AltitudeFeet;
        public float VerticalSpeedFeetPerMinute;
        public float HeadingDegrees;
        public float PitchDegrees;
        public float BankDegrees;
        public float GroundspeedKnots;
        public float TrackDegrees;
        public bool HasRunway;
        public int RunwayDesignator;
        public float RunwayBearingDegrees;
        public float RunwayDistanceMetres;
        public float RunwayHeadingDegrees;
        public float RunwayLengthMetres;

        public static InstrumentReadings From(in FlightTelemetry t, in RunwayRelation runway)
        {
            float heading = SoftLogic.Finite(t.HeadingDegrees, 0f);
            return new InstrumentReadings
            {
                AirspeedKnots = SoftLogic.Finite(t.IndicatedAirspeedKnots, 0f),
                AltitudeFeet = SoftLogic.Finite(t.AltitudeFeet, 0f),
                VerticalSpeedFeetPerMinute = SoftLogic.Finite(t.VerticalSpeedFeetPerMinute, 0f),
                HeadingDegrees = heading,
                PitchDegrees = SoftLogic.Finite(t.PitchDegrees, 0f),
                BankDegrees = SoftLogic.Finite(t.BankDegrees, 0f),
                GroundspeedKnots = SoftLogic.Finite(t.GroundspeedKnots, 0f),
                TrackDegrees = runway.Valid ? RunwayGeometry.Normalize360(runway.LandingHeadingDegrees + runway.TrackErrorDegrees) : heading,
                HasRunway = runway.Valid,
                RunwayDesignator = runway.Designator,
                RunwayBearingDegrees = runway.BearingToRunwayDegrees,
                RunwayDistanceMetres = runway.DistanceToRunwayMetres,
                RunwayHeadingDegrees = runway.LandingHeadingDegrees,
                RunwayLengthMetres = runway.RunwayLengthMetres,
            };
        }
    }

    /// <summary>
    /// Airspeed indicator arcs and red line, indicated knots. Filled from the
    /// aircraft data (stall speeds from the flight model, limits from the warning
    /// settings) so the dial always agrees with the simulation and the warnings.
    /// </summary>
    public struct AirspeedMarkings
    {
        /// <summary>Bottom of the white arc (stall speed, full flap).</summary>
        public float FlapStall;
        /// <summary>Bottom of the green arc (stall speed, flaps up).</summary>
        public float CleanStall;
        /// <summary>Top of the white arc (maximum speed with flaps extended).</summary>
        public float FlapExtended;
        /// <summary>Top of the green arc, start of the yellow (caution) arc.</summary>
        public float NormalOperating;
        /// <summary>Red line.</summary>
        public float NeverExceed;

        public static AirspeedMarkings Default => new AirspeedMarkings
        {
            FlapStall = 45f, CleanStall = 51f, FlapExtended = 85f, NormalOperating = 125f, NeverExceed = 160f,
        };
    }

    /// <summary>
    /// Needle and card positions, pure functions. Angles are degrees clockwise
    /// from 12 o'clock, as on a dial; UI rotation is counter-clockwise, so a
    /// needle is rotated by the negative angle.
    /// </summary>
    public static class InstrumentMath
    {
        /// <summary>Airspeed dial: 0-40 kt compressed into the first 30°, then 40-200 kt over 30°-330°.</summary>
        public static float AirspeedAngle(float knots)
        {
            float k = Mathf.Clamp(SoftLogic.Finite(knots, 0f), 0f, 200f);
            return k < 40f ? k / 40f * 30f : 30f + (k - 40f) / 160f * 300f;
        }

        /// <summary>Long altimeter needle: one turn per 1000 ft.</summary>
        public static float AltimeterHundredsAngle(float feet) => Mathf.Repeat(SoftLogic.Finite(feet, 0f), 1000f) * 0.36f;

        /// <summary>Short altimeter needle: one turn per 10 000 ft.</summary>
        public static float AltimeterThousandsAngle(float feet) => Mathf.Repeat(SoftLogic.Finite(feet, 0f), 10000f) * 0.036f;

        /// <summary>Vertical speed: 0 at 9 o'clock, ±2000 fpm at ±170° (climb clockwise, upwards).</summary>
        public static float VerticalSpeedAngle(float feetPerMinute) =>
            270f + Mathf.Clamp(SoftLogic.Finite(feetPerMinute, 0f), -2000f, 2000f) * (170f / 2000f);

        /// <summary>Heading card rotation (UI, counter-clockwise): the current heading sits under the lubber line at the top.</summary>
        public static float HeadingCardRotation(float headingDegrees) => RunwayGeometry.Normalize360(SoftLogic.Finite(headingDegrees, 0f));

        /// <summary>Attitude card roll (UI, counter-clockwise): banking right turns the horizon counter-clockwise.</summary>
        public static float AttitudeRoll(float bankDegrees) => SoftLogic.Finite(bankDegrees, 0f);

        /// <summary>Attitude card offset: nose up moves the horizon down.</summary>
        public static float AttitudePitchOffset(float pitchDegrees, float pixelsPerDegree, float limitDegrees) =>
            -Mathf.Clamp(SoftLogic.Finite(pitchDegrees, 0f), -limitDegrees, limitDegrees) * pixelsPerDegree;

        /// <summary>A point on a dial at an angle (clockwise from 12 o'clock) and radius.</summary>
        public static Vector2 DialPoint(float angleDegrees, float radius)
        {
            float a = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius);
        }

        /// <summary>
        /// Heading-up map position of a target at a bearing and distance, relative to
        /// the aircraft symbol. Targets beyond the range are pinned to the range ring.
        /// </summary>
        public static Vector2 MapPosition(float bearingDegrees, float headingDegrees, float distanceMetres, float rangeMetres,
            float radius, out bool inRange)
        {
            float relative = Mathf.DeltaAngle(headingDegrees, bearingDegrees);
            float fraction = rangeMetres > 0f ? distanceMetres / rangeMetres : 0f;
            inRange = fraction <= 1f;
            return DialPoint(relative, Mathf.Min(fraction, 1f) * radius);
        }

        static readonly float[] MapRanges = { 2f, 5f, 10f, 20f, 50f };

        /// <summary>
        /// Map range (NM) for a distance, with hysteresis so the scale does not jump
        /// back and forth: zoom out when the target passes 90 % of the range, zoom in
        /// when it comes within 40 % of the next smaller range.
        /// </summary>
        public static float MapRangeNm(float distanceNm, float currentRangeNm)
        {
            float d = Mathf.Max(0f, SoftLogic.Finite(distanceNm, 0f));
            int index = 0;
            for (int i = 0; i < MapRanges.Length; i++)
                if (Mathf.Approximately(MapRanges[i], currentRangeNm)) index = i;
            if (currentRangeNm <= 0f) index = 0;
            while (index < MapRanges.Length - 1 && d > 0.9f * MapRanges[index]) index++;
            while (index > 0 && d < 0.4f * MapRanges[index - 1]) index--;
            return MapRanges[index];
        }
    }
}
