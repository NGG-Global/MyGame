using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Samples the aircraft once per physics step, after the controller, and
    /// publishes a <see cref="FlightTelemetry"/> snapshot in pilot units.
    /// Read-only: nothing here changes the flight. The values are built by
    /// <see cref="TelemetryMath"/>, which test harnesses share.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(AircraftController))]
    public sealed class AircraftTelemetry : MonoBehaviour
    {
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
            Quaternion rotation = body.rotation;
            Vector3 velocity = body.linearVelocity;
            return TelemetryMath.Compose(new TelemetrySource
            {
                Velocity = velocity,
                TrueAirspeed = velocity.magnitude, // no wind yet
                Rotation = rotation,
                BodyAngularVelocity = Quaternion.Inverse(rotation) * body.angularVelocity,
                Aerodynamics = _aircraft.LastAerodynamics,
                Controls = _aircraft.Controls,
                EnginePower = _aircraft.EnginePower,
                EngineState = _aircraft.EngineState,
                FlapPosition = _aircraft.FlapPosition,
                WeightOnWheels = _aircraft.WeightOnWheels,
                AirDensity = _aircraft.AirDensity,
                Altitude = body.position.y,
                HeightAboveGround = HeightAboveGround(body.position),
            }, _aircraft.configuration);
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
