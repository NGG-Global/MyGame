using System;
using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>
    /// First Flight tutorial tuning. Speeds are multiples of the aircraft's stall
    /// speeds (from the flight model), so they follow the aircraft data.
    /// PROTOTYPE TUNING VALUES: adjust after playtests.
    /// </summary>
    [Serializable]
    public sealed class TutorialSettings
    {
        [Header("Reference speeds (multiples of the stall speed)")]
        [Tooltip("Rotation speed, multiple of the clean stall speed (same as the Assisted flying tip).")]
        public float rotateFactor = 1.1f;
        [Tooltip("Climb speed, multiple of the clean stall speed.")]
        public float climbFactor = 1.35f;
        [Tooltip("Approach speed, multiple of the stall speed for the flaps in use.")]
        public float approachFactor = 1.4f;
        [Tooltip("Below this multiple of the stall speed (current flaps) the coach asks for more speed.")]
        public float slowFactor = 1.2f;
        [Tooltip("Lift-off speed for the take-off run estimate, multiple of the clean stall speed (the offline validation lifted off at about 66 kt, 1.3 × 51).")]
        public float liftOffFactor = 1.3f;
        [Tooltip("Used only if the take-off run cannot be estimated from the aircraft data, m.")]
        public float fallbackTakeoffRunMetres = 400f;

        [Header("Take-off")]
        [Tooltip("Throttle (0-1) that counts as take-off power.")]
        public float takeoffThrottle = 0.9f;
        [Tooltip("A take-off roll has started from this groundspeed, kt.")]
        public float rollStartedKnots = 20f;
        [Tooltip("After a take-off roll, slowing below this on the ground counts as an abandoned take-off, kt.")]
        public float abandonedKnots = 8f;
        [Tooltip("Lift-off needs this much runway left, as a multiple of the estimated ground run.")]
        public float runwayMarginFactor = 1.15f;
        [Tooltip("Height above ground that confirms the lift-off, ft.")]
        public float liftOffHeightFeet = 20f;

        [Header("Climb and cruise")]
        [Tooltip("The initial climb is complete at this height above ground, ft.")]
        public float climbTargetFeet = 500f;
        [Tooltip("A steady climb has at least this vertical speed, fpm.")]
        public float steadyClimbFpm = 200f;
        [Tooltip("Wings count as level within this bank, degrees.")]
        public float levelBankDegrees = 12f;
        [Tooltip("Above this bank the coach asks for gentler turns, degrees.")]
        public float steepBankDegrees = 35f;
        [Tooltip("On course: target within this angle of the nose, degrees.")]
        public float onCourseDegrees = 15f;
        [Tooltip("Off course: target further than this from the nose, degrees.")]
        public float offCourseDegrees = 45f;
        [Tooltip("Below this height above ground in cruise, the coach asks for a climb, ft.")]
        public float minimumCruiseFeet = 400f;
        [Tooltip("Above this height above ground near the airfield, the coach asks for a descent, ft.")]
        public float returnDescentFeet = 1300f;
        [Tooltip("Lined up: track within this angle of the runway direction, degrees.")]
        public float lineUpTrackDegrees = 30f;
        [Tooltip("Lined up: within this distance of the extended centreline, m.")]
        public float lineUpCrossTrackMetres = 500f;

        [Header("Waypoints")]
        [Tooltip("Passing within this multiple of a checkpoint's radius also counts.")]
        public float passRadiusFactor = 2f;
        [Tooltip("A checkpoint is missed once the aircraft is this much further away than its closest point, m.")]
        public float missedMarginMetres = 600f;
        [Tooltip("Start of the final approach for the return leg, before the threshold, m.")]
        public float finalEntryDistanceMetres = 3000f;
        [Tooltip("Airborne checkpoints restart at least this high above the ground, ft.")]
        public float checkpointMinFeet = 800f;
        [Tooltip("Airborne checkpoints restart at most this high, ft.")]
        public float checkpointMaxFeet = 3000f;

        [Header("Landing")]
        [Tooltip("Height for the flare cue, ft.")]
        public float flareHeightFeet = 30f;
        [Tooltip("The flight is complete once the aircraft has slowed below this on the ground, kt.")]
        public float stoppedKnots = 5f;
        [Tooltip("Glide path of the approach lights, degrees (the approach guide's).")]
        public float glidepathDegrees = 3f;
        [Tooltip("Above or below the glide path by more than this (all lights white or all red), the coach suggests a correction, degrees.")]
        public float glidepathToleranceDegrees = 0.6f;
        [Tooltip("Faster than the approach speed by more than this, the coach suggests less power, kt.")]
        public float approachFastMarginKnots = 15f;
        [Tooltip("Off the centreline beyond this, the coach suggests steering to the marker, m.")]
        public float centrelineCueMetres = 40f;

        [Header("Coaching rhythm")]
        [Tooltip("Success messages stay this long, s.")]
        public float successSeconds = 2f;
        [Tooltip("A hint stays at least this long, s.")]
        public float hintSeconds = 5f;
        [Tooltip("At least this long between two hints, s.")]
        public float hintCooldownSeconds = 20f;
        [Tooltip("At most this many hints per step (the same hint never repeats in a step).")]
        public int maxHintsPerStep = 3;
        [Tooltip("A situation must last this long before its instruction replaces the current one, s.")]
        public float calloutSettleSeconds = 0.6f;
        [Tooltip("An instruction stays at least this long before another replaces it, s.")]
        public float calloutMinimumSeconds = 2.5f;
        [Tooltip("When the player is doing well, the instruction fades after this long, s.")]
        public float quietAfterSeconds = 8f;
        [Tooltip("A correction (too fast, too high...) must last this long before the coach mentions it, s.")]
        public float correctionSustainSeconds = 1.5f;
        [Tooltip("Not flying and off the runway this long suggests RESET, s.")]
        public float downSeconds = 3f;
    }

    /// <summary>Landing Challenge start. PROTOTYPE TUNING VALUES.</summary>
    [Serializable]
    public sealed class LandingChallengeSettings
    {
        [Tooltip("Start distance before the landing threshold, m.")]
        public float startDistanceMetres = 3000f;
        [Tooltip("Glide path angle of the start, degrees (the approach guide's is 3).")]
        public float glidepathDegrees = 3f;
        [Tooltip("Runway landing direction, degrees true (0 = runway 36).")]
        public float landingHeadingDegrees = 0f;
        [Tooltip("Flap detent at the start (-1 = full flap).")]
        public int flapDetent = -1;
        [Tooltip("The start speed is the stick-free trimmed speed, kept within these multiples of the stall speed.")]
        public float minSpeedFactor = 1.3f;
        public float maxSpeedFactor = 1.6f;
        [Tooltip("The opening instruction stays this long, s.")]
        public float introSeconds = 7f;
    }

    /// <summary>Free Flight's in-the-air start. PROTOTYPE LAYOUT.</summary>
    [Serializable]
    public sealed class FreeFlightSettings
    {
        [Tooltip("Airborne start position (y = altitude), m: south-west of the airfield.")]
        public Vector3 airbornePosition = new Vector3(-1500f, 457f, -2500f);
        [Tooltip("Airborne start heading, degrees true: towards the coast.")]
        public float airborneHeadingDegrees = 45f;
    }

    /// <summary>
    /// Landing score ranges. GAME SCORING for feedback and replay value, loosely
    /// inspired by what makes a landing smooth. These are not certified
    /// flight-training or check-ride standards, and the UI says so.
    /// Each category scores 100 up to its "full" value and falls linearly to 0 at
    /// its "zero" value.
    /// </summary>
    [Serializable]
    public sealed class LandingScoringSettings
    {
        [Header("Touchdown descent rate, fpm")]
        public float descentFullFpm = 240f;
        public float descentZeroFpm = 720f;
        [Tooltip("At or above this descent rate the touchdown is a crash, fpm.")]
        public float crashDescentFpm = 1200f;

        [Header("Alignment with the runway (heading and track), degrees")]
        public float alignmentFullDegrees = 3f;
        public float alignmentZeroDegrees = 12f;

        [Header("Distance from the centreline, m (0 points off the paved surface)")]
        public float lateralFullMetres = 3f;

        [Header("Touchdown airspeed, multiple of the stall speed for the flaps in use")]
        public float speedIdealMinFactor = 1.0f;
        public float speedIdealMaxFactor = 1.2f;
        public float speedZeroHighFactor = 1.5f;
        public float speedZeroLowFactor = 0.85f;

        [Header("Control")]
        public float bouncePenalty = 35f;
        public float leftRunwayPenalty = 60f;
        [Tooltip("Bank on the ground above this costs points, degrees.")]
        public float groundBankFullDegrees = 5f;
        public float groundBankZeroDegrees = 15f;
        [Tooltip("At or above this bank on the ground, or this pitch, the aircraft is out of control, degrees.")]
        public float crashBankDegrees = 30f;
        public float crashPitchDegrees = 25f;

        [Header("Weights of the total")]
        public float descentWeight = 30f;
        public float alignmentWeight = 15f;
        public float lateralWeight = 20f;
        public float speedWeight = 15f;
        public float controlWeight = 20f;

        [Header("Grades (total at or above)")]
        public int excellentScore = 85;
        public int goodScore = 70;
        public int fairScore = 50;

        [Header("Detection")]
        [Tooltip("Airborne this long after a touchdown counts as a bounce, s.")]
        public float bounceSeconds = 0.25f;
        [Tooltip("Airborne this long after a touchdown, or climbing away with power, cancels the landing (go-around), s.")]
        public float goAroundSeconds = 4f;
        [Tooltip("The result is ready once the aircraft has slowed below this on the ground, kt.")]
        public float resultKnots = 15f;
        [Tooltip("...and stayed below it this long, s.")]
        public float resultSeconds = 1f;
        [Tooltip("The aircraft must have flown this long before a touchdown is scored, s.")]
        public float minimumAirborneSeconds = 2f;
    }

    /// <summary>
    /// Mission tuning in one asset, so it survives scene rebuilds (the scene
    /// builder creates it once and never overwrites it). Without an asset, the
    /// defaults in code are used.
    /// </summary>
    [CreateAssetMenu(menuName = "Open Skies/Mission Configuration", fileName = "MissionConfiguration")]
    public sealed class MissionConfiguration : ScriptableObject
    {
        public TutorialSettings tutorial = new TutorialSettings();
        public LandingChallengeSettings landingChallenge = new LandingChallengeSettings();
        public LandingScoringSettings landingScoring = new LandingScoringSettings();
        public FreeFlightSettings freeFlight = new FreeFlightSettings();
    }
}
