using OpenSkies.Awareness;
using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>
    /// The tutorial's view of the current situation, refreshed every step and
    /// read by the step conditions. Measured values come from telemetry, the
    /// phase from the awareness layer: the tutorial tracks what the player is
    /// trying to do, never re-derives what the aircraft is doing.
    /// </summary>
    public sealed class TutorialContext
    {
        public MissionInput Input;
        public TrainingSpeeds Speeds;
        public TutorialSettings Settings;
        /// <summary>Towards the current waypoint; invalid when the step has none.</summary>
        public NavigationRelation Navigation;
        public int WaypointIndex;
        public float TimeInStep;
        /// <summary>Largest stick deflection (pitch or roll) since the step began, 0-1.</summary>
        public float StickActivity;
        /// <summary>A take-off roll has started in this attempt.</summary>
        public bool RollStarted;
        /// <summary>The landing evaluator has seen the touchdown.</summary>
        public bool TouchedDown;
        /// <summary>The touchdown or the roll was out of control.</summary>
        public bool LandingCrashed;
        /// <summary>Seconds the distance to the waypoint has been growing.</summary>
        public float MovingAwaySeconds;

        public float Ias => Input.Telemetry.IndicatedAirspeedKnots;
        public float Groundspeed => Input.Telemetry.GroundspeedKnots;
        public float VerticalSpeed => Input.Telemetry.VerticalSpeedFeetPerMinute;
        public float HeightFeet => Input.Telemetry.HeightAboveGroundFeet;
        public float Bank => Mathf.Abs(Input.Telemetry.BankDegrees);
        public float Throttle => Input.Telemetry.Throttle;
        public bool Airborne => Input.Airborne;
        public bool OnGround => Input.Phase.OnGround;
        public FlightPhase Phase => Input.Phase.Phase;
        public float StallKnots => Speeds.StallKnots(Input.Telemetry.FlapPosition);
        public float SlowKnots => StallKnots * Settings.slowFactor;
        public float ApproachKnots => Speeds.ApproachKnots(Input.FlapSelection);
        public bool HasTarget => Navigation.Valid;
        /// <summary>Target bearing relative to the nose, −180..180 (positive: to the right).</summary>
        public float RelativeBearing => Navigation.Valid ? Navigation.RelativeBearingDegrees : 0f;
        public float TargetDistance => Navigation.Valid ? Navigation.DistanceMetres : float.MaxValue;
        public bool OnCourse => Navigation.Valid && Mathf.Abs(Navigation.RelativeBearingDegrees) <= Settings.onCourseDegrees;
        public bool OffCourse => Navigation.Valid && Mathf.Abs(Navigation.RelativeBearingDegrees) > Settings.offCourseDegrees;
        public bool WingsLevel => Bank <= Settings.levelBankDegrees;

        /// <summary>Paved runway left ahead while on the runway, m (0 elsewhere).</summary>
        public float RunwayRemainingMetres
        {
            get
            {
                var r = Input.Runway;
                if (!r.Valid || !r.OnRunway) return 0f;
                return Mathf.Max(0f, r.RunwayLengthMetres + r.AlongTrackMetres); // along-track is negative past the threshold
            }
        }

        /// <summary>Climbing steadily at a safe speed with the wings nearly level.</summary>
        public bool SteadyClimb => Airborne && VerticalSpeed >= Settings.steadyClimbFpm && Ias >= SlowKnots && Bank <= Settings.levelBankDegrees;

        /// <summary>Glide path deviation, degrees (positive: high); 0 when it cannot be measured.</summary>
        public float GlidepathError => Input.Runway.Valid && Input.Runway.GlidepathValid ? Input.Runway.GlidepathAngleDegrees - Settings.glidepathDegrees : 0f;
        public bool High => GlidepathError > Settings.glidepathToleranceDegrees;
        public bool Low => GlidepathError < -Settings.glidepathToleranceDegrees;
        public bool OnApproachPhase => Phase == FlightPhase.Approach || Phase == FlightPhase.Landing;

        /// <summary>
        /// Before a landing threshold, near its extended centreline and tracking along it. The
        /// phase of flight alone is not enough: level flight near either runway end can look
        /// like an approach.
        /// </summary>
        public bool LinedUp
        {
            get
            {
                var r = Input.Runway;
                return r.Valid && r.AlongTrackMetres > 0f && Mathf.Abs(r.TrackErrorDegrees) <= Settings.lineUpTrackDegrees
                       && Mathf.Abs(r.CrossTrackMetres) <= Settings.lineUpCrossTrackMetres;
            }
        }
    }
}
