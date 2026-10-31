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
        const float GroundProbeStartAbove = 2f;   // m; the probe starts above the reference point, which sits slightly below the surface when the gear is compressed

        [Tooltip("Longest height the radio altimeter measures, m. Above it, height above ground falls back to height above sea level.")]
        public float radioAltimeterRange = 750f;

        AircraftController _aircraft;

        public FlightTelemetry Current { get; private set; }

        void Awake() => _aircraft = GetComponent<AircraftController>();

        void FixedUpdate() => Current = Sample();

        /// <summary>Resample now, e.g. right after a reset, so readers never see the pre-reset state.</summary>
        public void Refresh() => Current = Sample();

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
                HeightAboveGroundFeet = FlightUnits.MetresToFeet(HeightAboveGround(body.position)),
                VerticalSpeedFeetPerMinute = FlightUnits.MetresPerSecondToFeetPerMinute(velocity.y),
                HeadingDegrees = AttitudeMath.HeadingDegrees(rotation),
                PitchDegrees = AttitudeMath.PitchDegrees(rotation),
                BankDegrees = AttitudeMath.BankDegrees(rotation),
                AngleOfAttackDegrees = alphaDegrees,
                SideslipDegrees = aero.Sideslip * Mathf.Rad2Deg,
                Throttle = controls.Throttle,
                EnginePower = _aircraft.EnginePower,
                Brake = controls.Brake,
                FlapPosition = _aircraft.FlapPosition,
                FlapDegrees = _aircraft.FlapPosition * c.MaxFlapDegrees,
                EngineState = _aircraft.EngineState,
                WeightOnWheels = _aircraft.WeightOnWheels,
                StallWarning = stallWarning,
            };
        }

        /// <summary>Radio-altimeter style: straight down to the first ground collider within range.</summary>
        float HeightAboveGround(Vector3 position)
        {
            Vector3 origin = position + Vector3.up * GroundProbeStartAbove;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, radioAltimeterRange + GroundProbeStartAbove,
                    _aircraft.groundLayers, QueryTriggerInteraction.Ignore))
                return Mathf.Max(0f, hit.distance - GroundProbeStartAbove);
            return Mathf.Max(0f, position.y); // out of range or no ground below: height above sea level
        }
    }
}
