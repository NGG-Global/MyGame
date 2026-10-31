using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Samples the aircraft once per physics step, after the controller, and
    /// publishes a <see cref="FlightTelemetry"/> snapshot in pilot units.
    /// Read-only: nothing here changes the flight.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(AircraftController))]
    public sealed class AircraftTelemetry : MonoBehaviour
    {
        const float StallWarningMinAirspeed = 5f; // m/s; no meaningful angle of attack below this

        AircraftController _aircraft;

        public FlightTelemetry Current { get; private set; }

        void Awake() => _aircraft = GetComponent<AircraftController>();

        void FixedUpdate() => Current = Sample();

        public FlightTelemetry Sample()
        {
            var body = _aircraft.Body;
            var aero = _aircraft.LastAerodynamics;
            var controls = _aircraft.Controls;
            var c = _aircraft.configuration;
            Vector3 velocity = body.linearVelocity;
            Quaternion rotation = body.rotation;
            float trueAirspeed = velocity.magnitude; // no wind yet

            float alphaDegrees = aero.AngleOfAttack * Mathf.Rad2Deg;
            bool stallWarning = !_aircraft.WeightOnWheels
                                && trueAirspeed > StallWarningMinAirspeed
                                && alphaDegrees >= c.stallAngle - c.stallWarningMargin;

            return new FlightTelemetry
            {
                IndicatedAirspeedKnots = FlightUnits.MetresPerSecondToKnots(Atmosphere.IndicatedFromTrueAirspeed(trueAirspeed, _aircraft.AirDensity)),
                TrueAirspeedKnots = FlightUnits.MetresPerSecondToKnots(trueAirspeed),
                GroundspeedKnots = FlightUnits.MetresPerSecondToKnots(new Vector2(velocity.x, velocity.z).magnitude),
                AltitudeFeet = FlightUnits.MetresToFeet(body.position.y),
                VerticalSpeedFeetPerMinute = FlightUnits.MetresPerSecondToFeetPerMinute(velocity.y),
                HeadingDegrees = AttitudeMath.HeadingDegrees(rotation),
                PitchDegrees = AttitudeMath.PitchDegrees(rotation),
                BankDegrees = AttitudeMath.BankDegrees(rotation),
                AngleOfAttackDegrees = alphaDegrees,
                SideslipDegrees = aero.Sideslip * Mathf.Rad2Deg,
                Throttle = controls.Throttle,
                EnginePower = _aircraft.EnginePower,
                Brake = controls.Brake,
                WeightOnWheels = _aircraft.WeightOnWheels,
                StallWarning = stallWarning,
            };
        }
    }
}
