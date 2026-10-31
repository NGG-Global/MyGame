using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Piston engine and fixed-pitch propeller. Power follows the throttle with
    /// a first-order lag; thrust is limited by static thrust at low speed and by
    /// propeller power (η·P/V) at higher speed.
    /// </summary>
    public sealed class EngineModel
    {
        readonly AircraftConfiguration _c;

        /// <summary>Current engine output, 0-1.</summary>
        public float PowerFraction { get; private set; }

        public EngineModel(AircraftConfiguration configuration)
        {
            _c = configuration;
        }

        public void Update(float throttle, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            float target = Mathf.Clamp01(throttle);
            float response = _c.engineResponseTime > 0f ? 1f - Mathf.Exp(-deltaTime / _c.engineResponseTime) : 1f;
            PowerFraction = Mathf.Clamp01(PowerFraction + (target - PowerFraction) * response);
        }

        public void Reset() => PowerFraction = 0f;

        /// <summary>Thrust in newtons at the current power and the given airspeed (m/s).</summary>
        public float Thrust(float airspeed) => ThrustAt(PowerFraction, airspeed, _c);

        public static float ThrustAt(float powerFraction, float airspeed, AircraftConfiguration c)
        {
            float power = Mathf.Clamp01(powerFraction) * c.maxPower;
            float staticLimit = c.maxStaticThrust * Mathf.Clamp01(powerFraction);
            float propellerLimit = c.propellerEfficiency * power / Mathf.Max(airspeed, 1f);
            return Mathf.Min(staticLimit, propellerLimit);
        }
    }
}
