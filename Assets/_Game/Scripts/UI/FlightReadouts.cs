using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// How the flight information shows a number: the quantisation step of each reading and
    /// its cached text, so the capsule, the expanded panel and the focused reading show the
    /// same value from the same telemetry and steady flight allocates nothing. The values
    /// come from <see cref="FlightTelemetry"/>, <see cref="RunwayRelation"/> and
    /// <see cref="NavigationRelation"/> as the session computed them; nothing is recalculated
    /// here, only rounded for display. Pure and EditMode-tested.
    /// </summary>
    public static class FlightReadouts
    {
        /// <summary>The five flight readings are the first <see cref="HudElement"/> values, Airspeed to Groundspeed.</summary>
        public const int ReadingCount = 5;
        /// <summary>Returned for a value that has no reading (a guide).</summary>
        public const int NoValue = int.MinValue;
        const float MetresPerTenthNauticalMile = NavigationMath.MetresPerNauticalMile / 10f;

        static readonly NumberStrings Integers = new NumberStrings(0, 999, v => v.ToString());
        static readonly NumberStrings Altitudes = new NumberStrings(-100, 6000, v => (v * 10).ToString());     // tens of feet
        static readonly NumberStrings VerticalSpeeds = new NumberStrings(-200, 200, v => NumberStrings.Signed(v * 50)); // 50 fpm steps
        static readonly NumberStrings Headings = new NumberStrings(1, 360, NumberStrings.Heading);
        static readonly NumberStrings NauticalMilesText = new NumberStrings(0, 999, v => NumberStrings.Tenths(v) + " NM");
        static readonly NumberStrings HeightsFeet = new NumberStrings(0, 500, v => (v * 10).ToString() + " FT");  // tens of feet
        static readonly NumberStrings Runways = new NumberStrings(1, 36, v => "RWY " + v.ToString("00"));
        static readonly NumberStrings Bearings = new NumberStrings(1, 360, v => NumberStrings.Heading(v) + "°");
        static readonly NumberStrings Courses = new NumberStrings(1, 360, v => "COURSE " + NumberStrings.Heading(v) + "°");

        static readonly string[] Labels = { "IAS KT", "ALT FT", "VS FPM", "HDG", "GS KT" };

        public static bool IsReading(HudElement element) => (int)element >= 0 && (int)element < ReadingCount;

        /// <summary>The reading's label with its unit ("IAS KT"); constant strings.</summary>
        public static string Label(HudElement element) => IsReading(element) ? Labels[(int)element] : HudElements.ShortLabel(element);

        /// <summary>
        /// The displayed step of a reading: whole knots, tens of feet, 50 fpm, whole degrees
        /// (north as 360). Bad samples (NaN, infinity) show as 0. <see cref="NoValue"/> for a guide.
        /// </summary>
        public static int Quantise(HudElement element, in FlightTelemetry t)
        {
            switch (element)
            {
                case HudElement.Airspeed:
                    return Mathf.Clamp(Mathf.RoundToInt(SoftLogic.Finite(t.IndicatedAirspeedKnots, 0f)), 0, 999);
                case HudElement.Altitude:
                    return Mathf.RoundToInt(Mathf.Clamp(SoftLogic.Finite(t.AltitudeFeet, 0f), -99999f, 99999f) / 10f);
                case HudElement.VerticalSpeed:
                    return Mathf.RoundToInt(Mathf.Clamp(SoftLogic.Finite(t.VerticalSpeedFeetPerMinute, 0f), -99999f, 99999f) / 50f);
                case HudElement.Heading:
                    return CompassDegrees(t.HeadingDegrees);
                case HudElement.Groundspeed:
                    return Mathf.Clamp(Mathf.RoundToInt(SoftLogic.Finite(t.GroundspeedKnots, 0f)), 0, 999);
                default:
                    return NoValue;
            }
        }

        /// <summary>The text of a quantised reading ("112", "2400", "+500", "005"). Cached within the usual range.</summary>
        public static string Text(HudElement element, int quantised)
        {
            switch (element)
            {
                case HudElement.Airspeed:
                case HudElement.Groundspeed: return Integers.Get(quantised);
                case HudElement.Altitude: return Altitudes.Get(quantised);
                case HudElement.VerticalSpeed: return VerticalSpeeds.Get(quantised);
                case HudElement.Heading: return Headings.Get(quantised);
                default: return string.Empty;
            }
        }

        /// <summary>Whole degrees on a compass card: 1 to 360, north shown as 360.</summary>
        public static int CompassDegrees(float degrees)
        {
            int d = Mathf.RoundToInt(SoftLogic.Finite(degrees, 0f)) % 360;
            return d <= 0 ? d + 360 : d;
        }

        // ---- Guides --------------------------------------------------------------------------------

        /// <summary>A distance in tenths of a nautical mile (0 to 9999).</summary>
        public static int Tenths(float metres) => Mathf.Clamp(Mathf.RoundToInt(SoftLogic.Finite(metres, 0f) / MetresPerTenthNauticalMile), 0, 9999);

        /// <summary>"4.2 NM" from 42 tenths.</summary>
        public static string NauticalMiles(int tenths) => NauticalMilesText.Get(tenths);

        /// <summary>A height in tens of feet, never negative.</summary>
        public static int HeightTens(float metres) => Mathf.Max(0, Mathf.RoundToInt(SoftLogic.Finite(metres, 0f) * FlightUnits.FeetPerMetre / 10f));

        /// <summary>"650 FT" from 65 tens.</summary>
        public static string Height(int tens) => HeightsFeet.Get(tens);

        /// <summary>"RWY 36".</summary>
        public static string Runway(int designator) => Runways.Get(designator);

        /// <summary>"184°".</summary>
        public static string Bearing(int compassDegrees) => Bearings.Get(compassDegrees);

        /// <summary>"COURSE 184°".</summary>
        public static string Course(int compassDegrees) => Courses.Get(compassDegrees);

        /// <summary>The navigation arrow's turn in whole degrees (the transform changes only when the arrow visibly turns).</summary>
        public static int RelativeBearing(in NavigationRelation nav) => Mathf.RoundToInt(SoftLogic.Finite(nav.RelativeBearingDegrees, 0f));

        /// <summary>White lights of the four PAPI-style lights for the runway relation, or -1 when no glide path can be shown.</summary>
        public static int PapiWhite(in RunwayRelation runway, DisclosureSettings settings) =>
            runway.Valid && runway.GlidepathValid && settings != null ? ApproachGuidance.PapiWhiteLights(runway.GlidepathAngleDegrees, settings) : -1;
    }
}
