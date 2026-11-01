namespace OpenSkies.Missions
{
    /// <summary>The First Flight steps, in order. Values are stable (indices).</summary>
    public enum TutorialStepId
    {
        Preparation = 0,
        Takeoff = 1,
        Rotation = 2,
        InitialClimb = 3,
        Turn = 4,
        Cruise = 5,
        Return = 6,
        ApproachAndLanding = 7,
    }

    /// <summary>Where a RESET puts the aircraft back during a step. Values are stable.</summary>
    public enum TutorialCheckpoint
    {
        /// <summary>The start of the runway, back to the take-off.</summary>
        Runway = 0,
        /// <summary>In the air where the last checkpoint was reached, heading on.</summary>
        Airborne = 1,
        /// <summary>On final approach, configured for landing.</summary>
        FinalApproach = 2,
    }

    /// <summary>A condition on the tutorial's current situation (telemetry, phase, navigation).</summary>
    public delegate bool TutorialCondition(TutorialContext context);

    /// <summary>
    /// What to tell the player in a situation. A step lists its callouts in
    /// priority order; the first whose condition holds is the instruction (the
    /// last one usually has no condition: the default). Texts are built once.
    /// </summary>
    public sealed class TutorialCallout
    {
        public string Text;
        /// <summary>Used with the Assisted profile, where the aircraft holds bank and trim when the stick is released.</summary>
        public string AssistedText;
        /// <summary>Used without touch controls (keyboard or gamepad).</summary>
        public string KeyboardText;
        public CoachHighlight Highlights;
        /// <summary>Null: always applies.</summary>
        public TutorialCondition When;
        /// <summary>The situation must last this long before the callout replaces the current one, s (corrections).</summary>
        public float SustainSeconds;

        public string TextFor(TutorialContext context)
        {
            if (!context.Input.TouchControls && KeyboardText != null) return KeyboardText;
            if (context.Input.Profile == OpenSkies.FlightState.FlightAssistProfile.Assisted && AssistedText != null) return AssistedText;
            return Text;
        }
    }

    /// <summary>
    /// Extra help for a player who seems to struggle. A hint shows only after its
    /// condition has held for <see cref="SustainSeconds"/>, never twice in a step,
    /// and never within the cooldown of another hint.
    /// </summary>
    public sealed class TutorialHint
    {
        public string Text;
        public string KeyboardText;
        public CoachHighlight Highlights;
        public TutorialCondition When;
        /// <summary>Not before this long in the step, s.</summary>
        public float AfterSeconds;
        public float SustainSeconds = 1f;

        public string TextFor(TutorialContext context) =>
            !context.Input.TouchControls && KeyboardText != null ? KeyboardText : Text;
    }

    /// <summary>
    /// One tutorial step as data: what it teaches, how it is recognised as done
    /// (from telemetry, the phase of flight and the world, never from a timer
    /// alone), what it says, and where a RESET returns to.
    /// </summary>
    public sealed class TutorialStep
    {
        public TutorialStepId Id;
        public string Title;
        /// <summary>"3/8  ROTATION", built once.</summary>
        public string Label;
        /// <summary>The short explanation shown when the player taps the message.</summary>
        public string Detail;
        /// <summary>Shown briefly when the step is done.</summary>
        public string Success;
        public TutorialCallout[] Callouts = new TutorialCallout[0];
        public TutorialHint[] Hints = new TutorialHint[0];
        /// <summary>Null: the step is done when its last waypoint is reached.</summary>
        public TutorialCondition Complete;
        /// <summary>The completion condition must hold this long, s.</summary>
        public float CompleteSustainSeconds;
        /// <summary>The player is doing fine: the instruction may fade after a while.</summary>
        public TutorialCondition OnTrack;
        /// <summary>How long the instruction stays once the player is on track, s (negative: the settings' default).</summary>
        public float QuietAfterSeconds = -1f;
        /// <summary>Reaching the last waypoint completes the step. Otherwise the waypoints start over until <see cref="Complete"/> holds.</summary>
        public bool CompleteOnLastWaypoint = true;
        /// <summary>Landmark ids (or <see cref="TutorialCourse.FinalEntryId"/>) to fly to, in order.</summary>
        public string[] Waypoints = new string[0];
        /// <summary>Messages when a waypoint (other than the last) is reached, same order as <see cref="Waypoints"/>.</summary>
        public string[] WaypointReached = new string[0];
        /// <summary>HUD guides the step needs, one bit per HudElement.</summary>
        public int Guides;
        /// <summary>Action rail entries the step points at, one bit per ContextAction.</summary>
        public int Actions;
        public TutorialCheckpoint Checkpoint;
    }
}
