using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Piston engine and fixed-pitch propeller. Power follows the throttle with
    /// a first-order lag; thrust is limited by static thrust at low speed and by
    /// propeller power (η·P/V) at higher speed.
    ///
    /// Simplified ignition: switching the ignition off stops the engine (power
    /// decays to zero); switching it on from off cranks for
    /// <see cref="AircraftConfiguration.engineStartSeconds"/> and then runs.
    /// No fuel, mixture or carburettor icing yet.
    /// </summary>
    public sealed class EngineModel
    {
        readonly AircraftConfiguration _c;
        float _startTimer;

        /// <summary>Current engine output, 0-1.</summary>
        public float PowerFraction { get; private set; }
        public EngineState State { get; private set; } = EngineState.Running;

        public EngineModel(AircraftConfiguration configuration)
        {
            _c = configuration;
        }

        public void Update(float throttle, float deltaTime) => Update(throttle, true, deltaTime);

        public void Update(float throttle, bool ignition, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            if (!ignition)
            {
                State = EngineState.Off;
            }
            else if (State == EngineState.Off)
            {
                State = EngineState.Starting;
                _startTimer = 0f;
            }
            else if (State == EngineState.Starting)
            {
                _startTimer += deltaTime;
                if (_startTimer >= _c.engineStartSeconds) State = EngineState.Running;
            }

            float target = State == EngineState.Running ? Mathf.Clamp01(throttle) : 0f;
            float response = _c.engineResponseTime > 0f ? 1f - Mathf.Exp(-deltaTime / _c.engineResponseTime) : 1f;
            PowerFraction = Mathf.Clamp01(PowerFraction + (target - PowerFraction) * response);
        }

        /// <summary>Back to the start state: running at idle.</summary>
        public void Reset()
        {
            PowerFraction = 0f;
            State = EngineState.Running;
            _startTimer = 0f;
        }

        /// <summary>Running and settled at a power (a scenario that starts in the air, with power set).</summary>
        public void SetRunning(float powerFraction)
        {
            PowerFraction = Mathf.Clamp01(powerFraction);
            State = EngineState.Running;
            _startTimer = 0f;
        }

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
