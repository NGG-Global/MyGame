using System;
using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Tuning for one aircraft type.
    ///
    /// PROTOTYPE TUNING VALUES. They are in a plausible range for a light
    /// four-seat, high-wing, single-engine trainer so the prototype flies
    /// believably. They are not validated specifications of any real aircraft.
    ///
    /// Axes are Unity's, relative to the aircraft root: +X right, +Y up, +Z nose.
    /// The root pivot sits at wheel-contact level when the gear is fully extended.
    /// Units: metres, kilograms, seconds, newtons, watts; angles in degrees here
    /// for readability, converted to radians in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Open Skies/Aircraft Configuration", fileName = "AircraftConfiguration")]
    public sealed class AircraftConfiguration : ScriptableObject
    {
        [Header("Mass")]
        [Tooltip("Flying mass, kg.")]
        public float mass = 1100f;
        [Tooltip("Centre of mass relative to the aircraft root, m.")]
        public Vector3 centerOfMass = new Vector3(0f, 1.25f, 0.7f);
        [Tooltip("Principal moments of inertia about X (pitch), Y (yaw), Z (roll), kg·m².")]
        public Vector3 inertia = new Vector3(1825f, 2667f, 1285f);

        [Header("Wing geometry")]
        [Tooltip("Reference wing area, m².")]
        public float wingArea = 16.2f;
        [Tooltip("Wing span, m.")]
        public float wingSpan = 11f;
        [Tooltip("Mean aerodynamic chord, m.")]
        public float meanChord = 1.5f;

        [Header("Lift")]
        [Tooltip("Lift coefficient at zero angle of attack (wing camber and incidence).")]
        public float liftCoefficientAtZeroAlpha = 0.3f;
        [Tooltip("Lift curve slope, per radian.")]
        public float liftCurveSlope = 4.9f;

        [Header("Stall")]
        [Tooltip("Critical angle of attack, positive side, degrees. Lift peaks here.")]
        public float stallAngle = 15f;
        [Tooltip("Critical angle of attack, negative side, degrees.")]
        public float negativeStallAngle = -12f;
        [Tooltip("Angle range over which lift falls from its peak towards flat-plate lift, degrees.")]
        public float stallTransition = 6f;
        [Tooltip("Scale of post-stall flat-plate lift, CL = k·sin(2α).")]
        public float flatPlateLift = 1f;
        [Tooltip("Stall warning sounds this many degrees before the critical angle.")]
        public float stallWarningMargin = 3f;

        [Header("Drag")]
        [Tooltip("Parasitic (zero-lift) drag coefficient.")]
        public float parasiticDrag = 0.027f;
        [Tooltip("Oswald span efficiency for induced drag.")]
        public float oswaldEfficiency = 0.75f;
        [Tooltip("Extra drag in stalled flow, CD += k·sin²(α) scaled by stall depth.")]
        public float flatPlateDrag = 1.2f;
        [Tooltip("Side force per radian of sideslip (negative: opposes the slip).")]
        public float sideForcePerSideslip = -0.6f;

        [Header("Pitch moment")]
        public float pitchMomentAtZeroAlpha = 0.04f;
        [Tooltip("Static pitch stability per radian (negative is stable).")]
        public float pitchStability = -1.2f;
        [Tooltip("Pitch damping derivative Cmq (negative).")]
        public float pitchDamping = -12f;
        [Tooltip("Nose-down pitch moment added when the wing is fully stalled.")]
        public float stallPitchBreak = -0.12f;

        [Header("Roll and yaw moments")]
        [Tooltip("Dihedral effect Clβ per radian (negative: rolls away from a sideslip).")]
        public float dihedralEffect = -0.09f;
        [Tooltip("Roll damping derivative Clp (negative).")]
        public float rollDamping = -0.47f;
        [Tooltip("Directional (weathercock) stability Cnβ per radian (positive is stable).")]
        public float directionalStability = 0.065f;
        [Tooltip("Yaw damping derivative Cnr (negative).")]
        public float yawDamping = -0.1f;
        [Tooltip("Extra angular damping applied by the Rigidbody, 1/s. Keep small; aerodynamic damping does the real work.")]
        public float angularDamping = 0.05f;

        [Header("Control surface authority (moment coefficient at full deflection)")]
        public float elevatorAuthority = 0.4f;
        public float aileronAuthority = 0.05f;
        public float rudderAuthority = 0.03f;

        [Header("Control response")]
        [Tooltip("How fast each surface follows the stick, full travel per second. Large values are near-instant.")]
        public float controlSurfaceRate = 4f;

        [Header("Engine and propeller")]
        [Tooltip("Maximum shaft power, W.")]
        public float maxPower = 120000f;
        [Tooltip("Maximum static thrust at full power, N.")]
        public float maxStaticThrust = 2400f;
        [Tooltip("Propeller efficiency at speed (thrust = η·P/V).")]
        public float propellerEfficiency = 0.8f;
        [Tooltip("Engine power response time constant, s.")]
        public float engineResponseTime = 0.6f;

        [Header("Landing gear")]
        public WheelDefinition[] wheels =
        {
            new WheelDefinition
            {
                name = "Main left", attachPoint = new Vector3(-1.2f, 0.6f, 0.35f), suspensionLength = 0.35f, wheelRadius = 0.25f,
                springRate = 44000f, damperRate = 4500f, steerable = false, braked = true,
            },
            new WheelDefinition
            {
                name = "Main right", attachPoint = new Vector3(1.2f, 0.6f, 0.35f), suspensionLength = 0.35f, wheelRadius = 0.25f,
                springRate = 44000f, damperRate = 4500f, steerable = false, braked = true,
            },
            new WheelDefinition
            {
                name = "Nose", attachPoint = new Vector3(0f, 0.6f, 2.5f), suspensionLength = 0.35f, wheelRadius = 0.25f,
                springRate = 30000f, damperRate = 3000f, steerable = true, braked = false,
            },
        };
        [Tooltip("Nosewheel deflection at full rudder pedal, degrees.")]
        public float maxNoseWheelSteering = 20f;
        [Tooltip("Rolling resistance coefficient.")]
        public float rollingResistance = 0.02f;
        [Tooltip("Braking friction coefficient at full brake.")]
        public float brakeFriction = 0.6f;
        [Tooltip("Lateral tyre friction coefficient.")]
        public float lateralFriction = 0.8f;
        [Tooltip("Lateral slip speed at which tyre side force saturates, m/s.")]
        public float lateralSlipSpeed = 0.5f;
        [Tooltip("Speed below which rolling and braking friction fade in smoothly, m/s. Prevents jitter at rest.")]
        public float frictionSmoothingSpeed = 0.3f;

        public float AspectRatio => wingSpan * wingSpan / Mathf.Max(wingArea, 0.01f);
    }

    [Serializable]
    public struct WheelDefinition
    {
        public string name;
        [Tooltip("Top of the suspension strut, relative to the aircraft root, m.")]
        public Vector3 attachPoint;
        [Tooltip("Suspension travel when fully extended, m.")]
        public float suspensionLength;
        public float wheelRadius;
        [Tooltip("N/m.")]
        public float springRate;
        [Tooltip("N·s/m.")]
        public float damperRate;
        public bool steerable;
        public bool braked;

        public float MaxReach => suspensionLength + wheelRadius;
    }
}
