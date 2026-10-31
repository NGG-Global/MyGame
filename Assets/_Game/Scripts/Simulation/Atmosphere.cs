using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// International Standard Atmosphere, troposphere only (0-11 km), no weather.
    /// T = 288.15 K − 0.0065 K/m · h;  ρ = 1.225 kg/m³ · (T / 288.15 K)^4.2559.
    /// </summary>
    public static class Atmosphere
    {
        public const float SeaLevelDensity = 1.225f;          // kg/m³
        const float SeaLevelTemperature = 288.15f;            // K
        const float LapseRate = 0.0065f;                      // K/m
        const float DensityExponent = 4.2559f;                // g/(L·R) − 1
        const float TroposphereTop = 11000f;                  // m

        public static float Density(float altitudeMetres)
        {
            float h = Mathf.Clamp(altitudeMetres, 0f, TroposphereTop);
            float temperatureRatio = (SeaLevelTemperature - LapseRate * h) / SeaLevelTemperature;
            return SeaLevelDensity * Mathf.Pow(temperatureRatio, DensityExponent);
        }

        /// <summary>
        /// Approximate indicated airspeed: equivalent airspeed, IAS ≈ TAS·√(ρ/ρ₀).
        /// Ignores compressibility and instrument/position errors (fine below ~200 kt).
        /// </summary>
        public static float IndicatedFromTrueAirspeed(float trueAirspeed, float density) =>
            trueAirspeed * Mathf.Sqrt(Mathf.Max(density, 0f) / SeaLevelDensity);
    }
}
