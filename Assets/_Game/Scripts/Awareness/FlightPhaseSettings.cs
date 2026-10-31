using System;
using UnityEngine;

namespace OpenSkies.Awareness
{
    /// <summary>
    /// Every threshold the flight phase estimator uses, in pilot units (knots,
    /// feet, feet per minute, nautical miles). Most conditions are soft: a score
    /// ramps from 0 to 1 between two values, so there is no single edge for the
    /// phase to flicker on. PROTOTYPE TUNING VALUES for the light trainer; tune
    /// them against recorded flights.
    /// </summary>
    [Serializable]
    public sealed class FlightPhaseSettings
    {
        [Header("Ground and air (debounce of weight on wheels)")]
        [Tooltip("Wheels must be clear of the ground this long before the aircraft counts as airborne, s.")]
        public float liftOffConfirmSeconds = 0.5f;
        [Tooltip("Wheels must be on the ground this long before the aircraft counts as landed, s.")]
        public float touchdownConfirmSeconds = 0.2f;

        [Header("Ground speeds, kt")]
        [Tooltip("At or below this groundspeed the aircraft is stopped.")]
        public float stoppedGroundspeed = 1f;
        [Tooltip("At or above this groundspeed the aircraft is moving.")]
        public float movingGroundspeed = 3f;
        [Tooltip("Highest normal taxi speed.")]
        public float taxiMaxGroundspeed = 25f;
        [Tooltip("Taxi no longer applies this far above the highest taxi speed.")]
        public float taxiFadeGroundspeed = 10f;
        [Tooltip("Rollout starts to apply above this groundspeed...")]
        public float rolloutMinGroundspeed = 10f;
        [Tooltip("...and fully applies above this one.")]
        public float rolloutFullGroundspeed = 25f;
        [Tooltip("With take-off power, a ground run counts as a take-off roll from this groundspeed...")]
        public float takeoffRunMinGroundspeed = 15f;
        [Tooltip("...fully from this one, even without measurable acceleration.")]
        public float takeoffRunFullGroundspeed = 30f;

        [Header("Power and acceleration")]
        [Tooltip("Throttle (0-1) at which take-off power fully applies.")]
        public float takeoffThrottle = 0.7f;
        [Tooltip("Take-off power fades out this far below the take-off throttle.")]
        public float throttleFade = 0.2f;
        [Tooltip("Throttle (0-1) at which climb power fully applies (initial climb).")]
        public float climbThrottle = 0.6f;
        [Tooltip("Throttle (0-1) at which a go-around fully applies on approach.")]
        public float goAroundThrottle = 0.8f;
        [Tooltip("Groundspeed change at which the aircraft starts to count as accelerating, kt/s.")]
        public float accelerationMin = 0.3f;
        [Tooltip("Groundspeed change at which it fully counts as accelerating, kt/s.")]
        public float accelerationFull = 1f;
        [Tooltip("Take-off rolls away from the runway (grass, taxiway) score this much of a runway roll.")]
        [Range(0f, 1f)] public float offRunwayTakeoffFactor = 0.75f;
        [Tooltip("A rollout counts fully within this time after a touchdown (or right after a take-off roll), s.")]
        public float rolloutContextSeconds = 60f;
        [Tooltip("A fast idle ground run without a recent landing or take-off roll scores this much of a rollout.")]
        [Range(0f, 1f)] public float rolloutWithoutContextFactor = 0.6f;

        [Header("Climb, cruise, descent")]
        [Tooltip("Initial climb ends at this height above ground, ft.")]
        public float initialClimbTopFeet = 500f;
        [Tooltip("Height band over which initial climb hands over to climb, ft.")]
        public float initialClimbBandFeet = 200f;
        [Tooltip("Initial climb can last at most this long after lift-off or a go-around, s.")]
        public float initialClimbMaxSeconds = 120f;
        [Tooltip("Vertical speed at which the aircraft fully counts as climbing, fpm (half of it is the threshold).")]
        public float climbVerticalSpeed = 300f;
        [Tooltip("Descent rate at which the aircraft fully counts as descending, fpm (half of it is the threshold).")]
        public float descentVerticalSpeed = 300f;
        [Tooltip("Within ± this vertical speed the aircraft is level, fpm (twice it is clearly not level).")]
        public float levelVerticalSpeed = 250f;
        [Tooltip("Level flight below this height above ground is not cruise, ft (fades in from a third of it).")]
        public float cruiseMinHeightFeet = 300f;
        [Tooltip("Vertical speed smoothing time constant, s. Filters turbulence and pitch oscillations.")]
        public float verticalSpeedSmoothingSeconds = 2f;
        [Tooltip("Groundspeed-change smoothing time constant, s.")]
        public float accelerationSmoothingSeconds = 1f;

        [Header("Approach and landing (relative to the runway)")]
        [Tooltip("Approach applies within this distance of the landing threshold, NM.")]
        public float approachMaxDistanceNm = 5f;
        [Tooltip("Approach applies below this height above the runway, ft.")]
        public float approachMaxHeightFeet = 1500f;
        [Tooltip("Half-angle of the approach sector around the extended centreline, degrees.")]
        public float approachSectorHalfAngleDegrees = 30f;
        [Tooltip("The approach sector is at least this wide on each side of the centreline, m.")]
        public float approachSectorMinHalfWidthMetres = 300f;
        [Tooltip("Approach applies when the track is within this angle of the runway heading, degrees.")]
        public float approachMaxTrackErrorDegrees = 45f;
        [Tooltip("Above this climb rate the aircraft is no longer approaching, fpm (fades out up to twice it).")]
        public float approachMaxClimbRate = 300f;
        [Tooltip("Within this much height above the runway, close to the threshold, the approach becomes a landing, ft.")]
        public float landingHeightFeet = 100f;
        [Tooltip("Landing applies within this distance before the threshold, NM.")]
        public float landingMaxDistanceNm = 1f;
        [Tooltip("Landing applies within this distance of the centreline, m.")]
        public float landingMaxCrossTrackMetres = 150f;
        [Tooltip("Landing applies when the track is within this angle of the runway heading, degrees.")]
        public float landingMaxTrackErrorDegrees = 30f;
        [Tooltip("After touchdown, the landing phase lasts this long before rollout takes over, s.")]
        public float landingHoldSeconds = 3f;
        [Tooltip("Climb rate at which a go-around fully applies on approach, fpm.")]
        public float goAroundClimbRate = 300f;
        [Tooltip("Approach limits (distance, height, sector, track) fade out over this fraction beyond the limit.")]
        public float approachFadeFraction = 0.2f;
        [Tooltip("Landing limits (height, distance, centreline, track) fade out over this fraction beyond the limit.")]
        public float landingFadeFraction = 0.5f;
        [Tooltip("How strongly approach geometry is preferred over plain cruise or descent near the runway (0-1).")]
        [Range(0f, 1f)] public float approachPreference = 0.6f;

        [Header("Hysteresis and timing")]
        [Tooltip("A challenger must score this much higher than the current phase before it can take over.")]
        [Range(0f, 0.5f)] public float switchMargin = 0.15f;
        [Tooltip("A phase is held at least this long, unless the aircraft changes between ground and air, s.")]
        public float minimumPhaseSeconds = 2f;
        [Tooltip("Confidence smoothing time constant, s.")]
        public float confidenceSmoothingSeconds = 1f;

        [Header("Confirmation: a challenger must lead this long, s")]
        public float parkedConfirmSeconds = 2f;
        public float taxiConfirmSeconds = 1f;
        public float takeoffRollConfirmSeconds = 1f;
        public float initialClimbConfirmSeconds = 0.5f;
        public float climbConfirmSeconds = 3f;
        public float cruiseConfirmSeconds = 5f;
        public float descentConfirmSeconds = 3f;
        public float approachConfirmSeconds = 3f;
        public float landingConfirmSeconds = 0.5f;
        public float rolloutConfirmSeconds = 1f;

        public float ConfirmSeconds(FlightPhase phase)
        {
            switch (phase)
            {
                case FlightPhase.Parked: return parkedConfirmSeconds;
                case FlightPhase.Taxi: return taxiConfirmSeconds;
                case FlightPhase.TakeoffRoll: return takeoffRollConfirmSeconds;
                case FlightPhase.InitialClimb: return initialClimbConfirmSeconds;
                case FlightPhase.Climb: return climbConfirmSeconds;
                case FlightPhase.Cruise: return cruiseConfirmSeconds;
                case FlightPhase.Descent: return descentConfirmSeconds;
                case FlightPhase.Approach: return approachConfirmSeconds;
                case FlightPhase.Landing: return landingConfirmSeconds;
                case FlightPhase.Rollout: return rolloutConfirmSeconds;
                default: return 1f;
            }
        }
    }
}
