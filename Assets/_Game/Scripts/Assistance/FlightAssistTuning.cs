using System;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Assistance
{
    /// <summary>
    /// What one assistance profile does: gains and on/off weights (0 = off,
    /// 1 = on). The controller blends between two of these over a few seconds
    /// when the profile changes, so a weight can be part-way during a change.
    /// </summary>
    [Serializable]
    public sealed class FlightAssistGains
    {
        [Tooltip("Pitch rate damping: stick per degree per second of pitch rate.")]
        public float pitchDamping;
        [Tooltip("Phugoid damping: stick per knot per second of airspeed change (nose up while gaining speed).")]
        public float speedDamping;
        [Tooltip("Roll rate damping: stick per degree per second of roll rate.")]
        public float rollDamping;
        [Tooltip("Yaw damping (about the turn rate a coordinated turn needs): rudder per degree per second.")]
        public float yawDamping;
        [Tooltip("Automatic rudder coordination (holds the slip ball centred), 0-1.")]
        [Range(0f, 1f)] public float coordination;
        [Tooltip("Bank stabilisation with the stick released: levels small banks, holds moderate ones, eases steep ones back, 0-1.")]
        [Range(0f, 1f)] public float bankHold;
        [Tooltip("Automatic pitch trim with the stick released (holds the angle of attack, so the airspeed), 0-1.")]
        [Range(0f, 1f)] public float autoTrim;
        [Tooltip("Adds the extra angle of attack a level turn needs while the bank is held, 0-1.")]
        [Range(0f, 1f)] public float turnCompensation;
        [Tooltip("Gentle stall prevention: limits nose-up elevator near the critical angle of attack, 0-1.")]
        [Range(0f, 1f)] public float stallPrevention;

        public FlightAssistGains Copy() => (FlightAssistGains)MemberwiseClone();

        /// <summary>Writes the blend of <paramref name="a"/> and <paramref name="b"/> into <paramref name="result"/> (no allocation).</summary>
        public static void Lerp(FlightAssistGains a, FlightAssistGains b, float t, FlightAssistGains result)
        {
            t = Mathf.Clamp01(t);
            result.pitchDamping = Mathf.Lerp(a.pitchDamping, b.pitchDamping, t);
            result.speedDamping = Mathf.Lerp(a.speedDamping, b.speedDamping, t);
            result.rollDamping = Mathf.Lerp(a.rollDamping, b.rollDamping, t);
            result.yawDamping = Mathf.Lerp(a.yawDamping, b.yawDamping, t);
            result.coordination = Mathf.Lerp(a.coordination, b.coordination, t);
            result.bankHold = Mathf.Lerp(a.bankHold, b.bankHold, t);
            result.autoTrim = Mathf.Lerp(a.autoTrim, b.autoTrim, t);
            result.turnCompensation = Mathf.Lerp(a.turnCompensation, b.turnCompensation, t);
            result.stallPrevention = Mathf.Lerp(a.stallPrevention, b.stallPrevention, t);
        }

        public void CopyFrom(FlightAssistGains other)
        {
            pitchDamping = other.pitchDamping;
            speedDamping = other.speedDamping;
            rollDamping = other.rollDamping;
            yawDamping = other.yawDamping;
            coordination = other.coordination;
            bankHold = other.bankHold;
            autoTrim = other.autoTrim;
            turnCompensation = other.turnCompensation;
            stallPrevention = other.stallPrevention;
        }
    }

    /// <summary>
    /// Tuning for the assistance layer: the three profiles and the limits every
    /// profile shares. PROTOTYPE TUNING VALUES for the fictional trainer, tuned in
    /// the EditMode flight scenarios (docs/FLIGHT_ASSISTANCE.md); check them in a
    /// flight test. Stick and rudder values are fractions of full deflection.
    /// </summary>
    [Serializable]
    public sealed class FlightAssistTuning
    {
        [Header("Profiles")]
        public FlightAssistGains assisted = new FlightAssistGains
        {
            pitchDamping = 0.006f, speedDamping = 0.015f, rollDamping = 0.01f, yawDamping = 0.02f, coordination = 1f, bankHold = 1f, autoTrim = 1f,
            turnCompensation = 1f, stallPrevention = 1f,
        };
        public FlightAssistGains standard = new FlightAssistGains
        {
            pitchDamping = 0.002f, speedDamping = 0.012f, rollDamping = 0.003f, yawDamping = 0.008f, coordination = 1f, bankHold = 0f, autoTrim = 1f,
            turnCompensation = 0f, stallPrevention = 0f,
        };
        public FlightAssistGains manual = new FlightAssistGains();

        [Header("Engagement and blending")]
        [Tooltip("Assistance works only in the air at or above this indicated airspeed, kt.")]
        public float engageAirspeedKnots = 35f;
        [Tooltip("Once engaged, assistance stays on in the air down to this indicated airspeed, kt (stall prevention must work when slow).")]
        public float disengageAirspeedKnots = 15f;
        [Tooltip("Assistance fades in over this time after lift-off, s.")]
        public float engageSeconds = 1.5f;
        [Tooltip("Assistance fades out over this time after touchdown (so the rudder never steers the nosewheel), s.")]
        public float disengageSeconds = 0.3f;
        [Tooltip("A profile change blends the old and new behaviour over this time, s.")]
        public float profileBlendSeconds = 2f;

        [Header("Pilot input")]
        [Tooltip("Stick below this (fraction of full deflection) counts as released.")]
        public float releaseThreshold = 0.04f;
        [Tooltip("Stabilisation on an axis starts to fade at this stick deflection...")]
        public float overrideStart = 0.15f;
        [Tooltip("...and is gone at this deflection: strong deliberate input is never fought.")]
        public float overrideEnd = 0.7f;
        [Tooltip("A released axis is captured (bank or trim reference) after this long...")]
        public float captureDelaySeconds = 0.4f;
        [Tooltip("...once its rotation rate is below this, deg/s...")]
        public float captureRateDegreesPerSecond = 4f;
        [Tooltip("...or after this long in any case, s.")]
        public float captureTimeoutSeconds = 2f;

        [Header("Smoothness")]
        [Tooltip("Smoothing of the airspeed trend used by the phugoid damper, s.")]
        public float accelerationSmoothingSeconds = 0.5f;
        [Tooltip("Fastest change of each assistance correction, full deflection per second.")]
        public float correctionRate = 1.5f;
        public float maxPitchCorrection = 0.3f;
        public float maxRollCorrection = 0.3f;
        public float maxRudderCorrection = 0.6f;

        [Header("Bank stabilisation")]
        [Tooltip("Released below this bank, the wings are levelled, degrees.")]
        public float levelBelowDegrees = 6f;
        [Tooltip("Released up to this bank, the bank is held; steeper banks ease back to it, degrees.")]
        public float maxHeldBankDegrees = 30f;
        [Tooltip("Stick per degree of bank error.")]
        public float bankGain = 0.01f;
        [Tooltip("Largest stick the bank hold uses: keeps its roll gentle (about 6°/s at cruise).")]
        public float maxBankCorrection = 0.12f;

        [Header("Coordination")]
        [Tooltip("Rudder per degree of sideslip.")]
        public float sideslipGain = 0.08f;

        [Header("Pitch trim (shared by automatic and manual trim)")]
        [Tooltip("Largest trim, fraction of elevator travel. Full stick still reaches full elevator.")]
        public float trimAuthority = 0.4f;
        [Tooltip("Fastest trim movement, fraction per second (like an electric trim motor).")]
        public float trimRate = 0.12f;
        [Tooltip("Automatic trim rate per degree of angle-of-attack error, per second.")]
        public float trimGain = 0.02f;
        [Tooltip("Share of a steadily held stick that moves into the trim when the stick is released.")]
        [Range(0f, 1f)] public float heldStickTransfer = 0.85f;
        [Tooltip("Time to learn a steadily held stick, s.")]
        public float heldStickSeconds = 0.8f;
        [Tooltip("Manual trim speed with the trim keys held, fraction per second.")]
        public float manualTrimRate = 0.1f;

        [Header("Stall prevention")]
        [Tooltip("Starts limiting nose-up elevator this far below the critical angle of attack, degrees.")]
        public float stallProtectionStartMargin = 6f;
        [Tooltip("The limit is tightest this far below the critical angle, degrees.")]
        public float stallProtectionFullMargin = 2f;
        [Tooltip("Tightest nose-up elevator limit (gentle: a slight pull is still allowed).")]
        public float stallProtectionMinimumElevator = 0.15f;
        [Tooltip("Nose-up attitude where stall prevention starts easing the nose down (energy runs out in a steep climb), degrees.")]
        public float pitchProtectionStartDegrees = 15f;
        [Tooltip("Nose-up attitude where that limit is tightest, degrees.")]
        public float pitchProtectionFullDegrees = 30f;
        [Tooltip("Tightest elevator limit from the attitude protection (slightly nose down).")]
        public float pitchProtectionMinimumElevator = -0.1f;
        [Tooltip("Pitch rate lead: a fast pull is limited earlier, s.")]
        public float stallAnticipationSeconds = 0.5f;

        public FlightAssistGains Gains(FlightAssistProfile profile)
        {
            switch (profile)
            {
                case FlightAssistProfile.Assisted: return assisted;
                case FlightAssistProfile.Standard: return standard;
                default: return manual;
            }
        }
    }
}
