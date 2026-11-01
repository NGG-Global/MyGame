using System;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// A deterministic flight laboratory for EditMode scenarios: one aircraft in the
    /// air, flown with the game's own pure flight model (<see cref="Aerodynamics"/>,
    /// <see cref="EngineModel"/>, control-surface and flap rates) and the same
    /// telemetry (<see cref="TelemetryMath"/>), integrated like the physics engine
    /// at the 50 Hz physics rate (forces, then velocities, then positions). Each step
    /// follows the game's order: the flight assist reads the previous step's
    /// telemetry, then the aircraft moves.
    ///
    /// Not the Rigidbody: no landing gear, no collisions, no gyroscopic term, rigid
    /// rotation integrated in double-checked single precision. Optional wind (the
    /// game has none yet) moves the air mass for disturbance scenarios. PlayMode
    /// tests confirm the important cases with the real Rigidbody.
    /// </summary>
    sealed class FlightLab
    {
        public const float Dt = 0.02f;
        const float Gravity = 9.81f;

        public readonly AircraftConfiguration Config;
        readonly EngineModel _engine;
        FlightControls _surfaces = FlightControls.Neutral;

        /// <summary>Centre of mass, world, m. y is the altitude.</summary>
        public Vector3 Position;
        public Vector3 Velocity;
        /// <summary>World axes, rad/s.</summary>
        public Vector3 AngularVelocity;
        public Quaternion Rotation = Quaternion.identity;
        /// <summary>Air-mass velocity (wind blowing towards), world, m/s.</summary>
        public Vector3 Wind;

        public IFlightAssist Assist = NoFlightAssist.Instance;
        public FlightTelemetry Telemetry { get; private set; }
        public FlightControls LastPilot { get; private set; }
        public FlightControls LastCommands { get; private set; }
        public AerodynamicForces LastAerodynamics { get; private set; }
        public float Time { get; private set; }

        public FlightLab(AircraftConfiguration config)
        {
            Config = config;
            _engine = new EngineModel(config);
        }

        public float AltitudeFeet => FlightUnits.MetresToFeet(Position.y);

        /// <summary>
        /// Level, unaccelerated flight at the speed where <paramref name="elevator"/> balances
        /// the pitch moment (0 = stick released at neutral trim), wings level, heading north,
        /// with the throttle set for level flight. Returns the lab and that throttle.
        /// </summary>
        public static FlightLab Level(AircraftConfiguration c, float altitudeMetres, out float throttle, float elevator = 0f, float flaps = 0f)
        {
            var lab = new FlightLab(c);
            float density = Atmosphere.Density(altitudeMetres);
            // Pitch moment balance (pilot convention): Cm0 + Cmα·α + flap + elevator = 0.
            float alpha = -(c.pitchMomentAtZeroAlpha + c.flapPitchMoment * flaps + c.elevatorAuthority * elevator) / c.pitchStability;
            float cl = Aerodynamics.LiftCoefficient(alpha, flaps, c, out _);
            float speed = Mathf.Sqrt(2f * c.mass * Gravity / (density * c.wingArea * Mathf.Max(cl, 0.1f)));
            float cd = Aerodynamics.DragCoefficient(cl, alpha, 0f, flaps, c);
            float drag = 0.5f * density * speed * speed * c.wingArea * cd;
            float perUnitPower = EngineModel.ThrustAt(1f, speed, c);
            throttle = Mathf.Clamp01(drag / Mathf.Max(perUnitPower, 1f));

            lab.Position = new Vector3(0f, altitudeMetres, 0f);
            lab.Rotation = AxisAngle(Vector3.right, -alpha * Mathf.Rad2Deg); // nose up by α (Unity: +X pitches down)
            lab.Velocity = new Vector3(0f, 0f, speed);
            lab._surfaces = new FlightControls(elevator, 0f, 0f, throttle, 0f, flaps, true);
            for (int i = 0; i < 400; i++) lab._engine.Update(throttle, true, Dt); // engine settled
            lab.Telemetry = lab.Sample(lab._surfaces, lab.Evaluate(out _));
            return lab;
        }

        /// <summary>One physics step with the pilot's commands (the assist applies to them first).</summary>
        public void Step(in FlightControls pilot)
        {
            LastPilot = pilot;
            var commands = Assist.Apply(pilot, Telemetry, Dt);
            LastCommands = commands;
            var c = Config;

            float flapStep = c.flapTravelSeconds > 0f ? Dt / c.flapTravelSeconds : 1f;
            float maxStep = c.controlSurfaceRate * Dt;
            _surfaces = new FlightControls(
                Mathf.MoveTowards(_surfaces.Pitch, commands.Pitch, maxStep),
                Mathf.MoveTowards(_surfaces.Roll, commands.Roll, maxStep),
                Mathf.MoveTowards(_surfaces.Yaw, commands.Yaw, maxStep),
                commands.Throttle, commands.Brake, Mathf.MoveTowards(_surfaces.Flaps, commands.Flaps, flapStep), commands.Ignition);
            _engine.Update(commands.Throttle, commands.Ignition, Dt);

            var aero = Evaluate(out float density);
            LastAerodynamics = aero;
            float thrust = _engine.Thrust(aero.Airspeed);

            Vector3 forceWorld = Rotation * (aero.Force + Vector3.forward * thrust) + Vector3.down * (c.mass * Gravity);
            Velocity += forceWorld / c.mass * Dt;

            Vector3 bodyRates = Conjugate(Rotation) * AngularVelocity;
            Vector3 torque = aero.Torque;
            bodyRates += new Vector3(torque.x / c.inertia.x, torque.y / c.inertia.y, torque.z / c.inertia.z) * Dt;
            bodyRates *= Mathf.Max(0f, 1f - c.angularDamping * Dt);
            AngularVelocity = Rotation * bodyRates;

            Position += Velocity * Dt;
            Rotation = Integrate(Rotation, AngularVelocity, Dt);
            Time += Dt;
            Telemetry = Sample(commands, Evaluate(out _));
        }

        /// <summary>Flies for <paramref name="seconds"/>; <paramref name="pilot"/> gives the commands each step.</summary>
        public void Run(float seconds, Func<FlightLab, FlightControls> pilot, Action<FlightLab> afterStep = null)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                Step(pilot(this));
                afterStep?.Invoke(this);
            }
        }

        AerodynamicForces Evaluate(out float density)
        {
            var toBody = Conjugate(Rotation);
            density = Atmosphere.Density(Position.y);
            return Aerodynamics.Compute(toBody * (Velocity - Wind), toBody * AngularVelocity, _surfaces, density, Config);
        }

        FlightTelemetry Sample(in FlightControls commands, in AerodynamicForces aero)
        {
            return TelemetryMath.Compose(new TelemetrySource
            {
                Velocity = Velocity,
                TrueAirspeed = (Velocity - Wind).magnitude,
                Rotation = Rotation,
                BodyAngularVelocity = Conjugate(Rotation) * AngularVelocity,
                Aerodynamics = aero,
                Controls = commands,
                EnginePower = _engine.PowerFraction,
                EngineState = _engine.State,
                FlapPosition = _surfaces.Flaps,
                WeightOnWheels = false,
                AirDensity = Atmosphere.Density(Position.y),
                Altitude = Position.y,
                HeightAboveGround = Mathf.Max(0f, Position.y),
            }, Config);
        }

        // ---- Rotation helpers (managed, so the lab also runs outside the engine) -------------

        public static Quaternion AxisAngle(Vector3 axis, float degrees)
        {
            float half = 0.5f * degrees * Mathf.Deg2Rad;
            Vector3 n = axis.normalized * Mathf.Sin(half);
            return new Quaternion(n.x, n.y, n.z, Mathf.Cos(half));
        }

        static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        static Quaternion Integrate(Quaternion q, Vector3 omegaWorld, float dt)
        {
            float angle = omegaWorld.magnitude * dt;
            if (angle < 1e-9f) return q;
            var delta = AxisAngle(omegaWorld, angle * Mathf.Rad2Deg);
            return Normalize(delta * q);
        }

        static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        /// <summary>Rolls the aircraft about its nose by <paramref name="degrees"/> (right wing down positive), keeping the velocity.</summary>
        public void Roll(float degrees) => Rotation = Rotation * AxisAngle(Vector3.forward, -degrees);
    }
}
