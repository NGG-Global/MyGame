using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Flies one aircraft with Rigidbody physics. Each physics step it applies
    /// gravity (by the Rigidbody), aerodynamic forces and moments, engine thrust
    /// and landing-gear contact forces. It never moves the transform directly,
    /// except when resetting.
    ///
    /// Commands arrive through <see cref="SetControls"/> from whatever input
    /// source the game uses; this class knows nothing about devices, cameras or UI.
    /// </summary>
    [DefaultExecutionOrder(0)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class AircraftController : MonoBehaviour
    {
        public AircraftConfiguration configuration;

        [Tooltip("Layers the wheels can stand on. The aircraft's own colliders must not be on these layers.")]
        public LayerMask groundLayers = Physics.DefaultRaycastLayers;

        [Header("Debug")]
        [Tooltip("Draw aerodynamic, thrust and gear forces in the Scene view (Gizmos on in the Game view too).")]
        public bool drawForceGizmos = true;
        [Tooltip("Gizmo length per newton.")]
        public float gizmoMetresPerNewton = 0.001f;

        Rigidbody _body;
        EngineModel _engine;
        FlightControls _controls = FlightControls.Neutral;
        FlightControls _surfaces = FlightControls.Neutral;  // control surfaces and flaps follow _controls at limited rates
        AerodynamicForces _aero;
        Vector3 _startPosition;
        Quaternion _startRotation = Quaternion.identity;
        Vector3[] _gearForces = new Vector3[0];
        Vector3[] _gearPoints = new Vector3[0];

        public Rigidbody Body => _body;
        public FlightControls Controls => _controls;
        public AerodynamicForces LastAerodynamics => _aero;
        public float EnginePower => _engine != null ? _engine.PowerFraction : 0f;
        public EngineState EngineState => _engine != null ? _engine.State : EngineState.Running;
        /// <summary>Actual flap position, 0-1 (follows the selected position at the flap travel rate).</summary>
        public float FlapPosition => _surfaces.Flaps;
        public float Thrust { get; private set; }
        public bool WeightOnWheels { get; private set; }
        public float AirDensity { get; private set; } = Atmosphere.SeaLevelDensity;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            if (configuration == null) configuration = ScriptableObject.CreateInstance<AircraftConfiguration>();
            _startPosition = transform.position;
            _startRotation = transform.rotation;
            ApplyConfiguration();
        }

        /// <summary>Copies mass properties from the configuration to the Rigidbody. Call after changing them at runtime.</summary>
        public void ApplyConfiguration()
        {
            var c = configuration;
            _body.mass = c.mass;
            _body.automaticCenterOfMass = false;
            _body.centerOfMass = c.centerOfMass;
            _body.automaticInertiaTensor = false;
            _body.inertiaTensor = c.inertia;
            _body.inertiaTensorRotation = Quaternion.identity;
            _body.linearDamping = 0f;              // drag is aerodynamic, not a damping hack
            _body.angularDamping = c.angularDamping;
            _body.useGravity = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            _engine = new EngineModel(c);
            int wheelCount = c.wheels != null ? c.wheels.Length : 0;
            _gearForces = new Vector3[wheelCount];
            _gearPoints = new Vector3[wheelCount];
        }

        public void SetControls(in FlightControls controls) => _controls = controls;

        /// <summary>Sets where <see cref="ResetToStart"/> returns to. Defaults to the pose at Awake.</summary>
        public void SetStartPose(Vector3 position, Quaternion rotation)
        {
            _startPosition = position;
            _startRotation = rotation;
        }

        /// <summary>Back to the start pose, at rest, engine idle, controls neutral.</summary>
        public void ResetToStart()
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.position = _startPosition;
            _body.rotation = _startRotation;
            transform.SetPositionAndRotation(_startPosition, _startRotation); // no interpolation smear from the old pose
            _engine.Reset();
            _controls = FlightControls.Neutral;
            _surfaces = FlightControls.Neutral;
            _aero = default;
            Thrust = 0f;
            WeightOnWheels = false;
            for (int i = 0; i < _gearForces.Length; i++) _gearForces[i] = Vector3.zero;
        }

        /// <summary>
        /// Starts a scenario: the given pose, velocity and attitude, controls neutral with the
        /// throttle and flaps already where the state says (the flaps do not travel from up,
        /// the engine does not spool up from idle). Like a reset, this is the only other time
        /// the aircraft is placed rather than flown.
        /// </summary>
        public void ResetTo(in AircraftStartState state)
        {
            _body.linearVelocity = state.Velocity;
            _body.angularVelocity = Vector3.zero;
            _body.position = state.Position;
            _body.rotation = state.Rotation;
            transform.SetPositionAndRotation(state.Position, state.Rotation);
            _engine.SetRunning(state.Throttle);
            _controls = new FlightControls(0f, 0f, 0f, state.Throttle, 0f, state.Flaps, true);
            _surfaces = _controls;
            _aero = default;
            Thrust = 0f;
            WeightOnWheels = false;
            for (int i = 0; i < _gearForces.Length; i++) _gearForces[i] = Vector3.zero;
        }

        /// <summary>The pose <see cref="ResetToStart"/> returns to (the runway start in the prototype).</summary>
        public Vector3 StartPosition => _startPosition;
        public Quaternion StartRotation => _startRotation;

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var c = configuration;

            float flapStep = c.flapTravelSeconds > 0f ? dt / c.flapTravelSeconds : 1f;
            _surfaces = MoveSurfaces(_surfaces, _controls, c.controlSurfaceRate * dt, flapStep);
            _engine.Update(_controls.Throttle, _controls.Ignition, dt);

            Quaternion toBody = Quaternion.Inverse(_body.rotation);
            Vector3 bodyVelocity = toBody * _body.linearVelocity;          // no wind yet: airflow = −velocity
            Vector3 bodyAngularVelocity = toBody * _body.angularVelocity;
            AirDensity = Atmosphere.Density(_body.worldCenterOfMass.y);

            _aero = Aerodynamics.Compute(bodyVelocity, bodyAngularVelocity, _surfaces, AirDensity, c);
            _body.AddRelativeForce(_aero.Force, ForceMode.Force);
            _body.AddRelativeTorque(_aero.Torque, ForceMode.Force);

            // Thrust line through the centre of mass, along the nose.
            Thrust = _engine.Thrust(_aero.Airspeed);
            _body.AddRelativeForce(Vector3.forward * Thrust, ForceMode.Force);

            ApplyLandingGear(c);
        }

        void ApplyLandingGear(AircraftConfiguration c)
        {
            // Use the physics pose, not the transform: with interpolation the transform
            // holds the smoothed render pose.
            bool anyContact = false;
            Vector3 position = _body.position;
            Quaternion rotation = _body.rotation;
            Vector3 up = rotation * Vector3.up;
            Vector3 down = -up;

            for (int i = 0; i < _gearForces.Length; i++)
            {
                _gearForces[i] = Vector3.zero;
                WheelDefinition wheel = c.wheels[i];
                Vector3 attach = position + rotation * wheel.attachPoint;
                _gearPoints[i] = attach + down * wheel.MaxReach;

                if (!Physics.Raycast(attach, down, out RaycastHit hit, wheel.MaxReach, groundLayers, QueryTriggerInteraction.Ignore))
                    continue;

                float compression = wheel.MaxReach - hit.distance;
                Vector3 wheelForward = rotation * Vector3.forward;
                if (wheel.steerable)
                    wheelForward = Quaternion.AngleAxis(_controls.Yaw * c.maxNoseWheelSteering, up) * wheelForward;

                Vector3 force = LandingGear.ContactForce(compression, _body.GetPointVelocity(hit.point), hit.normal,
                    wheelForward, _controls.Brake, wheel, c);
                if (force == Vector3.zero) continue;

                _body.AddForceAtPosition(force, hit.point, ForceMode.Force);
                _gearForces[i] = force;
                _gearPoints[i] = hit.point;
                anyContact = true;
            }
            WeightOnWheels = anyContact;
        }

        static FlightControls MoveSurfaces(in FlightControls current, in FlightControls target, float maxStep, float flapStep)
        {
            return new FlightControls(
                Mathf.MoveTowards(current.Pitch, target.Pitch, maxStep),
                Mathf.MoveTowards(current.Roll, target.Roll, maxStep),
                Mathf.MoveTowards(current.Yaw, target.Yaw, maxStep),
                target.Throttle,
                target.Brake,
                Mathf.MoveTowards(current.Flaps, target.Flaps, flapStep),
                target.Ignition);
        }

        void OnDrawGizmos()
        {
            if (!drawForceGizmos || _body == null || !Application.isPlaying) return;
            Vector3 origin = _body.worldCenterOfMass;
            Quaternion toWorld = _body.rotation;
            float scale = gizmoMetresPerNewton;

            Gizmos.color = Color.green;   // lift
            Gizmos.DrawLine(origin, origin + toWorld * _aero.Lift * scale);
            Gizmos.color = Color.red;     // drag
            Gizmos.DrawLine(origin, origin + toWorld * _aero.Drag * scale);
            Gizmos.color = Color.magenta; // side force
            Gizmos.DrawLine(origin, origin + toWorld * _aero.SideForce * scale);
            Gizmos.color = Color.blue;    // thrust
            Gizmos.DrawLine(origin, origin + transform.forward * Thrust * scale);
            Gizmos.color = Color.white;   // weight
            Gizmos.DrawLine(origin, origin + Physics.gravity * _body.mass * scale);
            Gizmos.DrawWireSphere(origin, 0.15f);

            Gizmos.color = Color.yellow;  // landing gear
            for (int i = 0; i < _gearForces.Length; i++)
                Gizmos.DrawLine(_gearPoints[i], _gearPoints[i] + _gearForces[i] * scale);
        }
    }
}
