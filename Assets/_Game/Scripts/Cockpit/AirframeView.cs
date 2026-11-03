using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Cockpit
{
    /// <summary>
    /// The airframe's moving parts, driven by what the aircraft actually received
    /// and what it reports: the propeller turns with the engine (a translucent disc
    /// replaces the blades at speed), ailerons, elevator and rudder follow the
    /// applied commands, the flaps follow their actual travel and the nosewheel the
    /// rudder. Visual only: it reads telemetry and commands and moves nothing but
    /// child transforms of the visual model. Allocation-free.
    /// </summary>
    public sealed class AirframeView : MonoBehaviour
    {
        [Header("Propeller")]
        [Tooltip("Turns about its local Z (the aircraft's nose axis).")]
        public Transform propeller;
        public Renderer propellerBlades;
        [Tooltip("Translucent disc shown instead of the blades above the disc fraction of maximum speed.")]
        public Renderer propellerDisc;
        public float idleRpm = 650f;
        public float maxRpm = 2500f;
        public float starterRpm = 220f;
        [Range(0f, 1f)] public float discFraction = 0.45f;
        [Tooltip("How quickly the propeller speed follows the engine, per second (1/s).")]
        public float spinSharpness = 3f;

        [Header("Control surfaces (hinge = local X; trailing edge rises with a positive angle)")]
        public Transform aileronLeft;
        public Transform aileronRight;
        public Transform elevator;
        [Tooltip("Hinge = local Y; the trailing edge moves left with a positive angle.")]
        public Transform rudder;
        public Transform flapLeft;
        public Transform flapRight;
        [Tooltip("Turns about local Y with the rudder command.")]
        public Transform noseWheel;
        public float aileronDegrees = 18f;
        public float elevatorDegrees = 22f;
        public float rudderDegrees = 24f;
        public float noseWheelDegrees = 20f;
        [Tooltip("How quickly a surface follows its command, per second (1/s).")]
        public float surfaceSharpness = 14f;

        float _spinDegrees;
        float _rpm;
        float _aileron, _elevator, _rudder, _flapDegrees, _steer;
        bool _discShown;

        public float PropellerRpm => _rpm;
        public bool DiscShown => _discShown;
        public float AileronDegrees => _aileron;
        public float ElevatorDegrees => _elevator;
        public float RudderDegrees => _rudder;
        public float FlapDegrees => _flapDegrees;

        /// <summary>Propeller speed for an engine state and output: idle to maximum while running, the starter speed while cranking, still when off.</summary>
        public static float TargetRpm(EngineState state, float enginePower, float idleRpm, float maxRpm, float starterRpm)
        {
            switch (state)
            {
                case EngineState.Running: return Mathf.Lerp(idleRpm, maxRpm, Mathf.Clamp01(enginePower));
                case EngineState.Starting: return starterRpm;
                default: return 0f;
            }
        }

        /// <summary>Frame-rate independent approach factor for an exponential follower.</summary>
        public static float Approach(float sharpness, float deltaTime) => 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * Mathf.Max(0f, deltaTime));

        void Awake()
        {
            if (propellerDisc != null) propellerDisc.enabled = false;
        }

        /// <summary>Moves every part for this frame. Call once per rendered frame with the applied commands.</summary>
        public void Present(in FlightTelemetry telemetry, in FlightControls commands, float deltaTime)
        {
            float k = Approach(surfaceSharpness, deltaTime);
            _aileron = Mathf.Lerp(_aileron, commands.Roll * aileronDegrees, k);
            _elevator = Mathf.Lerp(_elevator, commands.Pitch * elevatorDegrees, k);
            _rudder = Mathf.Lerp(_rudder, commands.Yaw * rudderDegrees, k);
            _steer = Mathf.Lerp(_steer, commands.Yaw * noseWheelDegrees, k);
            _flapDegrees = telemetry.FlapDegrees;

            // Stick right: right aileron up, left aileron down. Stick back: elevator trailing edge up.
            if (aileronRight != null) aileronRight.localRotation = Quaternion.Euler(_aileron, 0f, 0f);
            if (aileronLeft != null) aileronLeft.localRotation = Quaternion.Euler(-_aileron, 0f, 0f);
            if (elevator != null) elevator.localRotation = Quaternion.Euler(_elevator, 0f, 0f);
            // Right pedal: rudder trailing edge right (a negative turn about +Y moves the trailing edge right).
            if (rudder != null) rudder.localRotation = Quaternion.Euler(0f, -_rudder, 0f);
            if (noseWheel != null) noseWheel.localRotation = Quaternion.Euler(0f, _steer, 0f);
            if (flapLeft != null) flapLeft.localRotation = Quaternion.Euler(-_flapDegrees, 0f, 0f);
            if (flapRight != null) flapRight.localRotation = Quaternion.Euler(-_flapDegrees, 0f, 0f);

            float target = TargetRpm(telemetry.EngineState, telemetry.EnginePower, idleRpm, maxRpm, starterRpm);
            _rpm = Mathf.Lerp(_rpm, target, Approach(spinSharpness, deltaTime));
            if (propeller != null)
            {
                // Clockwise seen from the cockpit: a negative turn about the nose axis.
                _spinDegrees = Mathf.Repeat(_spinDegrees - _rpm * 6f * deltaTime, 360f);
                propeller.localRotation = Quaternion.Euler(0f, 0f, _spinDegrees);
            }
            bool disc = maxRpm > 0f && _rpm >= discFraction * maxRpm;
            if (disc != _discShown || deltaTime <= 0f)
            {
                _discShown = disc;
                if (propellerDisc != null) propellerDisc.enabled = disc;
                if (propellerBlades != null) propellerBlades.enabled = !disc;
            }
        }
    }
}
