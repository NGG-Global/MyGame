namespace OpenSkies.FlightState
{
    /// <summary>
    /// Unit conversions. The simulation works in SI units (metres, seconds,
    /// radians); pilots read knots, feet and feet per minute.
    /// </summary>
    public static class FlightUnits
    {
        // 1 knot = 1852 m per hour; 1 foot = 0.3048 m (both exact by definition).
        public const float KnotsPerMetrePerSecond = 3600f / 1852f;
        public const float FeetPerMetre = 1f / 0.3048f;
        public const float FeetPerMinutePerMetrePerSecond = 60f / 0.3048f;

        public static float MetresPerSecondToKnots(float mps) => mps * KnotsPerMetrePerSecond;
        public static float KnotsToMetresPerSecond(float knots) => knots / KnotsPerMetrePerSecond;
        public static float MetresToFeet(float metres) => metres * FeetPerMetre;
        public static float MetresPerSecondToFeetPerMinute(float mps) => mps * FeetPerMinutePerMetrePerSecond;
    }
}
