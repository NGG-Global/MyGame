namespace OpenSkies.Awareness
{
    /// <summary>
    /// Phase of flight inferred from telemetry by <see cref="FlightPhaseEstimator"/>.
    /// Phases do not have to follow each other in this order (go-arounds,
    /// touch-and-goes and rejected take-offs skip or repeat phases).
    /// Values are stable: they are used as array indices.
    /// </summary>
    public enum FlightPhase
    {
        Parked = 0,
        Taxi = 1,
        TakeoffRoll = 2,
        InitialClimb = 3,
        Climb = 4,
        Cruise = 5,
        Descent = 6,
        Approach = 7,
        /// <summary>Short final below the landing height, the touchdown and the first seconds on the wheels.</summary>
        Landing = 8,
        /// <summary>High-speed deceleration on the runway, after a landing or a rejected take-off.</summary>
        Rollout = 9,
    }

    /// <summary>The main cue behind a phase change, for the inspector and tests.</summary>
    public enum PhaseTransitionReason
    {
        None = 0,
        InitialEstimate,
        Stopped,
        Taxiing,
        SlowedToTaxi,
        TakeoffPower,
        TakeoffRejected,
        TouchAndGo,
        LiftOff,
        GoAround,
        Climbing,
        LevelFlight,
        Descending,
        ApproachGeometry,
        ShortFinal,
        Touchdown,
        DeceleratingOnRunway,
    }

    /// <summary>
    /// The estimator's current belief. Kept apart from <see cref="OpenSkies.FlightState.FlightTelemetry"/>:
    /// telemetry is measured, the phase is inferred.
    /// </summary>
    public struct FlightPhaseEstimate
    {
        public FlightPhase Phase;
        /// <summary>0-1: how well the current telemetry fits the phase (smoothed).</summary>
        public float Confidence;
        /// <summary>Seconds since the phase was entered.</summary>
        public float TimeInPhase;
        public FlightPhase Previous;
        /// <summary>Why the current phase was entered.</summary>
        public PhaseTransitionReason Reason;
        /// <summary>The phase currently challenging <see cref="Phase"/>; equal to it when there is none.</summary>
        public FlightPhase Candidate;
        /// <summary>0-1: how far the candidate is through its confirmation time.</summary>
        public float CandidateProgress;
        /// <summary>Weight on wheels after debouncing.</summary>
        public bool OnGround;

        public bool HasCandidate => Candidate != Phase;
    }

    /// <summary>One committed phase change.</summary>
    public struct PhaseTransition
    {
        public FlightPhase From;
        public FlightPhase To;
        public PhaseTransitionReason Reason;
        /// <summary>Estimator clock, seconds since the last reset.</summary>
        public float Time;
        /// <summary>Score of the new phase when it was committed.</summary>
        public float Score;
    }

    public static class FlightPhases
    {
        public const int Count = 10;

        /// <summary>Phases that only exist with weight on wheels.</summary>
        public static bool IsGroundPhase(FlightPhase phase) =>
            phase == FlightPhase.Parked || phase == FlightPhase.Taxi || phase == FlightPhase.TakeoffRoll || phase == FlightPhase.Rollout;

        /// <summary>Phases that only exist in the air. Landing is neither: it spans the touchdown.</summary>
        public static bool IsAirbornePhase(FlightPhase phase) =>
            phase == FlightPhase.InitialClimb || phase == FlightPhase.Climb || phase == FlightPhase.Cruise ||
            phase == FlightPhase.Descent || phase == FlightPhase.Approach;

        public static string Label(FlightPhase phase)
        {
            switch (phase)
            {
                case FlightPhase.Parked: return "PARKED";
                case FlightPhase.Taxi: return "TAXI";
                case FlightPhase.TakeoffRoll: return "TAKEOFF ROLL";
                case FlightPhase.InitialClimb: return "INITIAL CLIMB";
                case FlightPhase.Climb: return "CLIMB";
                case FlightPhase.Cruise: return "CRUISE";
                case FlightPhase.Descent: return "DESCENT";
                case FlightPhase.Approach: return "APPROACH";
                case FlightPhase.Landing: return "LANDING";
                case FlightPhase.Rollout: return "ROLLOUT";
                default: return "?";
            }
        }

        public static string Describe(PhaseTransitionReason reason)
        {
            switch (reason)
            {
                case PhaseTransitionReason.InitialEstimate: return "initial estimate";
                case PhaseTransitionReason.Stopped: return "stopped";
                case PhaseTransitionReason.Taxiing: return "moving on the ground";
                case PhaseTransitionReason.SlowedToTaxi: return "slowed to taxi speed";
                case PhaseTransitionReason.TakeoffPower: return "take-off power, accelerating";
                case PhaseTransitionReason.TakeoffRejected: return "take-off rejected";
                case PhaseTransitionReason.TouchAndGo: return "touch-and-go";
                case PhaseTransitionReason.LiftOff: return "lift-off";
                case PhaseTransitionReason.GoAround: return "go-around";
                case PhaseTransitionReason.Climbing: return "climbing";
                case PhaseTransitionReason.LevelFlight: return "level flight";
                case PhaseTransitionReason.Descending: return "descending";
                case PhaseTransitionReason.ApproachGeometry: return "in the approach sector";
                case PhaseTransitionReason.ShortFinal: return "short final";
                case PhaseTransitionReason.Touchdown: return "touchdown";
                case PhaseTransitionReason.DeceleratingOnRunway: return "decelerating on the runway";
                default: return "-";
            }
        }
    }
}
