using System;
using OpenSkies.Awareness;
using OpenSkies.World;
using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>
    /// Runs the First Flight: which step the player is on, what to tell them,
    /// where they are flying to, and where a RESET takes them back to. It tracks
    /// what the player is trying to accomplish; what the aircraft is actually
    /// doing comes from telemetry and the phase of flight (the awareness layer),
    /// which it reads but never changes. It never moves a control or the aircraft.
    ///
    /// Lifecycle: Start, Pause / Resume, Skip, Restart, and recovery from
    /// mistakes: an abandoned take-off goes back to the take-off with a short
    /// explanation; a missed checkpoint can be flown back to or left behind; a
    /// go-around returns to the approach set-up; a RESET returns to the last
    /// checkpoint. Camera and assistance changes keep the progress (texts follow
    /// the profile). Plain C#; no allocations per step after construction.
    /// </summary>
    public sealed class FirstFlightTutorial
    {
        public enum Status
        {
            Idle = 0,
            Running = 1,
            Paused = 2,
            Completed = 3,
            Skipped = 4,
        }

        const int MaxHints = 8;

        readonly TutorialSettings _s;
        readonly LandingChallengeSettings _final;
        readonly TutorialStep[] _steps;
        readonly LandmarkRegistry _world;
        readonly RunwayInfo _runway;
        readonly Coach _coach = new Coach();
        readonly WaypointTracker _tracker = new WaypointTracker();
        readonly TutorialContext _ctx = new TutorialContext();
        readonly LandingEvaluator _landing;
        readonly NavigationTarget _finalEntry, _threshold;
        readonly bool[] _hintUsed = new bool[MaxHints];
        readonly float[] _hintHeld = new float[MaxHints];
        readonly bool[] _reached;           // per landmark in the registry
        readonly string[] _missedText;      // per landmark in the registry
        readonly string _abandonedText, _noRunwayText, _resetText, _downText, _crashText, _goAroundText, _offApproachText, _retryLineUpText;

        Status _status;
        int _step;
        ProgressFlags _flags;
        float _completeHeld;
        int _callout = -1, _candidate = -1;
        float _calloutShown, _candidateHeld, _onTrackTime;
        float _sinceHint;
        int _hintsShown;
        bool _waypointMissHinted;
        float _lastDistance = float.MaxValue;
        float _downTime, _offApproachTime;
        bool _downHinted;
        MissionStart _checkpoint = MissionStart.Runway;
        TutorialStepId _checkpointStep = TutorialStepId.Takeoff;
        int _checkpointWaypoint;
        LandingAssessment _lastLanding;

        [Flags]
        enum ProgressFlags
        {
            None = 0,
            PreparationDone = 1,
            CrashHinted = 2,
        }

        public FirstFlightTutorial(TutorialSettings settings, in TrainingSpeeds speeds, LandmarkRegistry world, in RunwayInfo runway,
            LandingChallengeSettings finalApproach = null, LandingScoringSettings scoring = null)
        {
            _s = settings ?? new TutorialSettings();
            _final = finalApproach ?? new LandingChallengeSettings();
            _world = world ?? PrototypeWorld.CreateRegistry();
            _runway = runway;
            _steps = TutorialCourse.Build(_s, speeds);
            _ctx.Settings = _s;
            _ctx.Speeds = speeds;
            _landing = new LandingEvaluator(scoring, speeds);

            float landing = _final.landingHeadingDegrees;
            _finalEntry = new NavigationTarget("FINAL " + RunwayGeometry.Designator(landing).ToString("00"),
                NavigationMath.PointOnFinal(runway, landing, _s.finalEntryDistanceMetres), 700f, false);
            _threshold = new NavigationTarget("RUNWAY " + RunwayGeometry.Designator(landing).ToString("00"),
                NavigationMath.PointOnFinal(runway, landing, 0f), 350f, false);

            _reached = new bool[_world.Count];
            _missedText = new string[_world.Count];
            for (int i = 0; i < _world.Count; i++)
                _missedText[i] = "You passed the " + _world[i].displayName.ToLowerInvariant() + ": turn back to the arrow, or carry on";

            string rotate = MissionText.Number(speeds.RotateKnots);
            _abandonedText = "Full power, then at " + rotate + " KT ease the stick back to lift off";
            _noRunwayText = "Not enough runway left: tap RESET to line up again";
            _resetText = "Back at the last checkpoint";
            _downText = "Tap RESET to carry on from the last checkpoint";
            _crashText = "That was rough. Tap RESET to fly the approach again";
            _goAroundText = "Go-around: good call. Follow the arrow to try again";
            _offApproachText = "Off the approach: follow the arrow to line up again";
            _retryLineUpText = "Not lined up yet: follow the arrow round again";
        }

        // ---- State for the session and the HUD ----------------------------------------------------

        public Status State => _status;
        public bool IsActive => _status == Status.Running || _status == Status.Paused;
        public TutorialStepId CurrentStep => _steps[_step].Id;
        public TutorialStep Step => _steps[_step];
        public int StepIndex => _step;
        public int StepCount => _steps.Length;
        public CoachingCue Cue => _coach.Cue;
        public Coach Coach => _coach;
        public TutorialContext Context => _ctx;
        public LandingEvaluator Landing => _landing;
        /// <summary>The landing that completed the flight.</summary>
        public LandingAssessment LastLanding => _lastLanding;
        public TrainingSpeeds Speeds => _ctx.Speeds;

        /// <summary>Where the navigation guide should point, if anywhere.</summary>
        public bool HasNavigationTarget => IsActive && _tracker.Active;
        public NavigationTarget NavigationTarget => _tracker.Target;
        /// <summary>The active checkpoint is a landmark (its light flashes and the marker shows).</summary>
        public string ActiveLandmarkId => HasNavigationTarget && _tracker.Target.IsLandmark ? CurrentWaypointId : null;
        /// <summary>HUD guides the current step needs (HudElement bits).</summary>
        public int RequestedGuides => IsActive ? _steps[_step].Guides : 0;
        /// <summary>Rail actions the current step points at (ContextAction bits).</summary>
        public int RequestedActions => IsActive ? _steps[_step].Actions : 0;

        /// <summary>Where a RESET puts the aircraft now. Changes with <see cref="CheckpointVersion"/>.</summary>
        public MissionStart Checkpoint => _checkpoint;
        public int CheckpointVersion { get; private set; }
        public TutorialStepId CheckpointStep => _checkpointStep;

        public int FailedTakeoffs { get; private set; }
        public int GoArounds { get; private set; }
        public int MissedWaypoints { get; private set; }
        public int Resets { get; private set; }
        public int HintsShown { get; private set; }

        public event Action<TutorialStepId> StepChanged;
        public event Action Completed;

        string CurrentWaypointId
        {
            get
            {
                var wps = _steps[_step].Waypoints;
                return _ctx.WaypointIndex < wps.Length ? wps[_ctx.WaypointIndex] : null;
            }
        }

        // ---- Lifecycle ---------------------------------------------------------------------------

        /// <summary>Begins the First Flight at the preparation step, on the runway.</summary>
        public void Start()
        {
            _status = Status.Running;
            _flags = ProgressFlags.None;
            FailedTakeoffs = GoArounds = MissedWaypoints = Resets = HintsShown = 0;
            _lastLanding = default;
            for (int i = 0; i < _reached.Length; i++) _reached[i] = false;
            _coach.SetEnabled(true);
            SetCheckpoint(MissionStart.Runway, TutorialStepId.Preparation, 0);
            EnterStep((int)TutorialStepId.Preparation);
        }

        public void Restart() => Start();

        /// <summary>
        /// Begins at a later step (development tools and tests; the game always starts at
        /// the beginning). Waypoints before the step count as reached, and the checkpoint
        /// is the step's own: the caller places the aircraft to match.
        /// </summary>
        public void StartAt(TutorialStepId step)
        {
            Start();
            if (step == TutorialStepId.Preparation) return;
            _flags |= ProgressFlags.PreparationDone;
            if (step >= TutorialStepId.Cruise) MarkReached(PrototypeWorld.LighthouseId, step > TutorialStepId.Cruise);
            if (step >= TutorialStepId.Return) MarkReached(PrototypeWorld.HarbourId, true);
            if (step >= TutorialStepId.Turn && step <= TutorialStepId.Return)
                SetCheckpoint(MissionStart.Airborne(_ctx.Input.Position, 0f), step, 0);
            EnterStep((int)step, step == TutorialStepId.Cruise || step == TutorialStepId.Return ? 0 : -1);
        }

        void MarkReached(string landmarkId, bool reached)
        {
            int index = _world.IndexOf(landmarkId);
            if (index >= 0) _reached[index] = reached;
        }

        /// <summary>The player already knows how to fly: stop coaching at once (the flight carries on).</summary>
        public void Skip()
        {
            if (!IsActive) return;
            _status = Status.Skipped;
            _coach.SetEnabled(false);
            _tracker.Stop();
            _landing.Disarm();
        }

        /// <summary>Freezes progress and timers (pause menu). The message stays as it was.</summary>
        public void Pause()
        {
            if (_status == Status.Running) _status = Status.Paused;
        }

        public void Resume()
        {
            if (_status == Status.Paused) _status = Status.Running;
        }

        /// <summary>The player tapped the message.</summary>
        public void ToggleDetail()
        {
            if (IsActive) _coach.ToggleDetail();
        }

        /// <summary>
        /// The aircraft was put back at <see cref="Checkpoint"/> (the player pressed RESET):
        /// continue from the step that checkpoint belongs to.
        /// </summary>
        public void OnFlightReset()
        {
            if (!IsActive) return;
            Resets++;
            _flags &= ~ProgressFlags.CrashHinted;
            var step = _checkpointStep;
            if (step <= TutorialStepId.Takeoff) step = (_flags & ProgressFlags.PreparationDone) != 0 ? TutorialStepId.Takeoff : TutorialStepId.Preparation;
            EnterStep((int)step, _checkpointWaypoint);
            _coach.ShowSuccess(_resetText, _s.successSeconds);
        }

        // ---- Per physics step -------------------------------------------------------------------

        public void Update(in MissionInput input, float deltaTime)
        {
            if (_status != Status.Running || !(deltaTime > 0f) || float.IsInfinity(deltaTime)) return;
            _ctx.Input = input;
            _ctx.TimeInStep += deltaTime;
            _ctx.StickActivity = Mathf.Max(_ctx.StickActivity, Mathf.Max(Mathf.Abs(input.Pilot.Pitch), Mathf.Abs(input.Pilot.Roll)));
            _sinceHint += deltaTime;

            UpdateNavigation(deltaTime);
            if (CurrentStep == TutorialStepId.ApproachAndLanding)
            {
                _landing.Update(input, deltaTime);
                _ctx.TouchedDown = _landing.TouchedDown;
                _ctx.LandingCrashed = _landing.Crashed;
            }

            int stepBefore = _step;
            Recover(deltaTime);
            if (_status != Status.Running) return;
            if (_step == stepBefore) EvaluateCompletion(deltaTime);
            if (_status != Status.Running) return;
            UpdateCoaching(deltaTime);
            _coach.Update(deltaTime);
        }

        // ---- Steps ------------------------------------------------------------------------------

        void EnterStep(int index, int waypoint = -1)
        {
            _step = Mathf.Clamp(index, 0, _steps.Length - 1);
            var step = _steps[_step];
            _ctx.TimeInStep = 0f;
            _ctx.StickActivity = 0f;
            _ctx.RollStarted = false;
            _ctx.TouchedDown = false;
            _ctx.LandingCrashed = false;
            _ctx.MovingAwaySeconds = 0f;
            _completeHeld = 0f;
            _callout = _candidate = -1;
            _calloutShown = _candidateHeld = _onTrackTime = 0f;
            _sinceHint = _s.hintCooldownSeconds;
            _hintsShown = 0;
            for (int i = 0; i < MaxHints; i++)
            {
                _hintUsed[i] = false;
                _hintHeld[i] = 0f;
            }
            _downTime = _offApproachTime = 0f;
            _downHinted = false;

            _coach.BeginStep(step.Label, step.Detail);
            _ctx.WaypointIndex = waypoint >= 0 ? waypoint : FirstUnreachedWaypoint(step);
            BeginWaypoint();

            if (step.Checkpoint == TutorialCheckpoint.Runway && _checkpoint.Kind != MissionStartKind.Runway)
                SetCheckpoint(MissionStart.Runway, TutorialStepId.Takeoff, 0);
            if (step.Checkpoint == TutorialCheckpoint.FinalApproach)
                SetCheckpoint(FinalApproachStart(), TutorialStepId.ApproachAndLanding, 0);

            if (step.Id == TutorialStepId.ApproachAndLanding) _landing.Arm();
            else _landing.Disarm();
            StepChanged?.Invoke(step.Id);
        }

        void EvaluateCompletion(float dt)
        {
            var step = _steps[_step];
            bool done = step.Complete != null && step.Complete(_ctx);
            if (!done && step.CompleteOnLastWaypoint) done = step.Waypoints.Length > 0 && _ctx.WaypointIndex >= step.Waypoints.Length;
            _completeHeld = done ? _completeHeld + dt : 0f;
            if (done && _completeHeld + 1e-4f >= step.CompleteSustainSeconds) CompleteStep();
        }

        void CompleteStep()
        {
            var step = _steps[_step];
            if (step.Id == TutorialStepId.Preparation) _flags |= ProgressFlags.PreparationDone;
            _coach.ShowSuccess(step.Success, _s.successSeconds);

            switch (step.Id)
            {
                case TutorialStepId.InitialClimb:
                    SetCheckpoint(AirborneCheckpoint(PrototypeWorld.LighthouseId), TutorialStepId.Turn, 0);
                    break;
                case TutorialStepId.ApproachAndLanding:
                    Finish();
                    return;
            }
            EnterStep(_step + 1);
        }

        void Finish()
        {
            var assessment = _landing.Result;
            if (_landing.State != LandingEvaluator.Status.Complete) LandingScoring.Score(ref assessment, _landing.Settings);
            _lastLanding = assessment;
            _status = Status.Completed;
            _tracker.Stop();
            _landing.Disarm();
            _coach.SetEnabled(false); // the result card takes over
            Completed?.Invoke();
        }

        // ---- Recovery ---------------------------------------------------------------------------

        void Recover(float dt)
        {
            var id = CurrentStep;
            var c = _ctx;

            // Flying already (a pilot who knows how): skip the ground steps.
            if (id <= TutorialStepId.Takeoff && c.Airborne && c.HeightFeet >= _s.liftOffHeightFeet)
            {
                _flags |= ProgressFlags.PreparationDone;
                _coach.ShowSuccess(_steps[(int)TutorialStepId.Rotation].Success, _s.successSeconds);
                EnterStep((int)TutorialStepId.InitialClimb);
                return;
            }

            // Take-off abandoned (slowed down on the ground after a take-off roll): back to the take-off, explained.
            if (id == TutorialStepId.Takeoff || id == TutorialStepId.Rotation || id == TutorialStepId.InitialClimb)
            {
                if (c.OnGround && c.Groundspeed >= _s.rollStartedKnots) c.RollStarted = true;
                bool abandoned = c.OnGround && c.Groundspeed < _s.abandonedKnots
                                 && (c.RollStarted || (id == TutorialStepId.InitialClimb && c.TimeInStep > 2f));
                if (abandoned)
                {
                    FailedTakeoffs++;
                    EnterStep((int)TutorialStepId.Takeoff);
                    bool enoughRunway = c.Input.Runway.Valid && c.Input.Runway.OnRunway
                                        && c.RunwayRemainingMetres >= c.Speeds.TakeoffRunMetres * _s.runwayMarginFactor;
                    if (enoughRunway) ShowHint(_abandonedText, CoachHighlight.Throttle | CoachHighlight.Stick);
                    else ShowHint(_noRunwayText, CoachHighlight.Reset);
                    return;
                }
            }

            // Already lined up for the runway on the way round: go straight to the landing.
            if ((id == TutorialStepId.Turn || id == TutorialStepId.Cruise) && c.Airborne && c.OnApproachPhase && c.LinedUp
                && c.VerticalSpeed < 0f && c.Input.Runway.DistanceToThresholdMetres < _s.finalEntryDistanceMetres * 1.5f)
            {
                EnterStep((int)TutorialStepId.ApproachAndLanding);
                return;
            }

            if (id == TutorialStepId.ApproachAndLanding)
            {
                // Go-around before or after the touchdown: a good decision, not a failure.
                bool goAround = !_landing.TouchedDown && c.Airborne
                                && (c.Phase == FlightPhase.InitialClimb || c.Phase == FlightPhase.Climb)
                                && c.Input.Phase.Reason == PhaseTransitionReason.GoAround;
                if (goAround || _landing.State == LandingEvaluator.Status.Cancelled)
                {
                    GoArounds++;
                    EnterStep((int)TutorialStepId.Return, 0);
                    _coach.ShowSuccess(_goAroundText, _s.hintSeconds);
                    return;
                }

                // Drifted away from the approach without landing.
                bool offApproach = c.Airborne && !_landing.TouchedDown && !c.OnApproachPhase && c.HeightFeet > 100f;
                _offApproachTime = offApproach ? _offApproachTime + dt : 0f;
                if (_offApproachTime > 8f)
                {
                    EnterStep((int)TutorialStepId.Return, 0);
                    ShowHint(_offApproachText, CoachHighlight.None);
                    return;
                }

                if (_landing.Crashed && (_flags & ProgressFlags.CrashHinted) == 0)
                {
                    _flags |= ProgressFlags.CrashHinted;
                    ShowHint(_crashText, CoachHighlight.Reset, force: true);
                }
                return;
            }

            // Down in a field during the flight: nothing is broken, but RESET is the way on.
            if (id >= TutorialStepId.Turn && id <= TutorialStepId.Return)
            {
                bool down = c.OnGround && c.Groundspeed < 30f;
                _downTime = down ? _downTime + dt : 0f;
                if (_downTime >= _s.downSeconds && !_downHinted)
                {
                    _downHinted = true;
                    ShowHint(_downText, CoachHighlight.Reset, force: true);
                }
            }
        }

        // ---- Navigation -------------------------------------------------------------------------

        int FirstUnreachedWaypoint(TutorialStep step)
        {
            for (int i = 0; i < step.Waypoints.Length; i++)
            {
                int landmark = _world.IndexOf(step.Waypoints[i]);
                if (landmark < 0 || !_reached[landmark]) return i;
            }
            return step.Waypoints.Length;
        }

        void BeginWaypoint()
        {
            _waypointMissHinted = false;
            _lastDistance = float.MaxValue;
            var wps = _steps[_step].Waypoints;
            if (_ctx.WaypointIndex < wps.Length && TryTarget(wps[_ctx.WaypointIndex], out var target)) _tracker.Begin(target);
            else _tracker.Stop();
            _ctx.Navigation = default;
        }

        bool TryTarget(string id, out NavigationTarget target)
        {
            if (id == TutorialCourse.FinalEntryId) { target = _finalEntry; return true; }
            if (id == TutorialCourse.ThresholdId) { target = _threshold; return true; }
            if (_world.TryGet(id, out var landmark))
            {
                target = new NavigationTarget(landmark.displayName, landmark.position, landmark.arrivalRadiusMetres, true);
                return true;
            }
            target = default;
            return false;
        }

        void UpdateNavigation(float dt)
        {
            if (!_tracker.Active)
            {
                _ctx.Navigation = default;
                return;
            }
            var input = _ctx.Input;
            _ctx.Navigation = NavigationMath.Evaluate(_tracker.Target, input.Position, input.Telemetry.HeadingDegrees);
            float distance = _ctx.Navigation.DistanceMetres;
            _ctx.MovingAwaySeconds = distance > _lastDistance + 0.01f ? _ctx.MovingAwaySeconds + dt : 0f;
            _lastDistance = distance;
            if (!_ctx.Airborne) return; // checkpoints are flown to

            var state = _tracker.Update(input.Position, _s);
            var step = _steps[_step];
            string id = CurrentWaypointId;
            if (state == WaypointState.Reached)
            {
                int landmark = _world.IndexOf(id);
                if (landmark >= 0) _reached[landmark] = true;
                AdvanceWaypoint(reached: true);
                return;
            }
            if (state != WaypointState.Missed) return;

            if (!_waypointMissHinted)
            {
                _waypointMissHinted = true;
                MissedWaypoints++;
                int landmark = _world.IndexOf(id);
                if (landmark >= 0) ShowHint(_missedText[landmark], CoachHighlight.None, force: true);
            }
            // Carry on: closer to the next checkpoint than to the missed one.
            int next = _ctx.WaypointIndex + 1;
            if (next < step.Waypoints.Length && TryTarget(step.Waypoints[next], out var nextTarget))
            {
                Vector3 toNext = nextTarget.Position - input.Position;
                toNext.y = 0f;
                if (toNext.magnitude < _tracker.DistanceMetres) AdvanceWaypoint(reached: false);
            }
        }

        void AdvanceWaypoint(bool reached)
        {
            var step = _steps[_step];
            int index = _ctx.WaypointIndex;
            _ctx.WaypointIndex = index + 1;
            bool last = _ctx.WaypointIndex >= step.Waypoints.Length;

            if (reached && !last && index < step.WaypointReached.Length) _coach.ShowSuccess(step.WaypointReached[index], _s.successSeconds);
            if (!last)
            {
                // Restart in the air here, heading for the next checkpoint.
                if (reached && step.Checkpoint == TutorialCheckpoint.Airborne)
                    SetCheckpoint(AirborneCheckpoint(step.Waypoints[_ctx.WaypointIndex]), step.Id, _ctx.WaypointIndex);
                BeginWaypoint();
                return;
            }

            if (!step.CompleteOnLastWaypoint)
            {
                // The last checkpoint (the runway) was passed without settling on the approach: go round again.
                _ctx.WaypointIndex = 0;
                BeginWaypoint();
                ShowHint(_retryLineUpText, CoachHighlight.None, force: true);
                return;
            }

            _tracker.Stop();
            if (reached && step.Id == TutorialStepId.Cruise)
                SetCheckpoint(AirborneCheckpoint(TutorialCourse.FinalEntryId), TutorialStepId.Return, 0);
            // Completion (EvaluateCompletion) sees the exhausted list on this or the next step.
        }

        // ---- Checkpoints ------------------------------------------------------------------------

        void SetCheckpoint(in MissionStart start, TutorialStepId step, int waypoint)
        {
            _checkpoint = start;
            _checkpointStep = step;
            _checkpointWaypoint = waypoint;
            CheckpointVersion++;
        }

        MissionStart AirborneCheckpoint(string nextWaypointId)
        {
            Vector3 position = _ctx.Input.Position;
            float minimum = _s.checkpointMinFeet / FeetPerMetre;
            float maximum = _s.checkpointMaxFeet / FeetPerMetre;
            position.y = Mathf.Clamp(position.y, _runway.centre.y + minimum, _runway.centre.y + maximum);
            float heading = _ctx.Input.Telemetry.HeadingDegrees;
            if (TryTarget(nextWaypointId, out var target))
            {
                Vector3 offset = target.Position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude > 1f) heading = RunwayGeometry.HeadingOf(offset);
            }
            return MissionStart.Airborne(position, heading);
        }

        MissionStart FinalApproachStart() => new MissionStart
        {
            Kind = MissionStartKind.FinalApproach,
            DistanceBeforeThresholdMetres = _final.startDistanceMetres,
            FlapDetent = _final.flapDetent,
            GlidepathDegrees = _final.glidepathDegrees,
            LandingHeadingDegrees = _final.landingHeadingDegrees,
        };

        const float FeetPerMetre = 3.28084f;

        // ---- Coaching ---------------------------------------------------------------------------

        void ShowHint(string text, CoachHighlight highlights, bool force = false)
        {
            if (!force && _hintsShown >= _s.maxHintsPerStep) return;
            _coach.ShowHint(text, highlights, _s.hintSeconds);
            _hintsShown++;
            HintsShown++;
            _sinceHint = 0f;
        }

        void UpdateCoaching(float dt)
        {
            var step = _steps[_step];
            var c = _ctx;

            // A real warning comes first; tutorial instructions wait.
            var safety = FlightGuidanceAdvisor.SafetyHintFor(c.Input.WarningMask);
            _coach.SetSafety(safety == GuidanceHint.None ? null : FlightGuidanceAdvisor.SafetyText(safety));
            bool warning = safety != GuidanceHint.None;

            // Instruction: the first callout whose situation holds (and has lasted), without flicker.
            int wanted = -1;
            for (int i = 0; i < step.Callouts.Length; i++)
            {
                var callout = step.Callouts[i];
                if (callout.When != null && !callout.When(c)) continue;
                wanted = i;
                break;
            }
            _calloutShown += dt;
            if (wanted != _callout)
            {
                if (wanted != _candidate)
                {
                    _candidate = wanted;
                    _candidateHeld = 0f;
                }
                _candidateHeld += dt;
                float sustain = wanted >= 0 ? Mathf.Max(_s.calloutSettleSeconds, step.Callouts[wanted].SustainSeconds) : _s.calloutSettleSeconds;
                bool first = _callout < 0;
                if (first || (_candidateHeld >= sustain && _calloutShown >= _s.calloutMinimumSeconds))
                {
                    _callout = wanted;
                    _calloutShown = 0f;
                    _onTrackTime = 0f;
                }
            }
            else
            {
                _candidate = _callout;
                _candidateHeld = 0f;
            }

            // Doing well: let the instruction fade instead of repeating it.
            bool onTrack = step.OnTrack != null && step.OnTrack(c);
            _onTrackTime = onTrack ? _onTrackTime + dt : 0f;
            float quietAfter = step.QuietAfterSeconds >= 0f ? step.QuietAfterSeconds : _s.quietAfterSeconds;
            bool quiet = onTrack && _onTrackTime + 1e-4f >= quietAfter && _calloutShown + 1e-4f >= Mathf.Min(quietAfter, _s.calloutMinimumSeconds);
            if (_callout >= 0)
            {
                var callout = step.Callouts[_callout];
                _coach.SetInstruction(callout.TextFor(c), callout.Highlights, quiet);
            }
            else _coach.SetInstruction(null, CoachHighlight.None, true);

            // Hints: only for a sustained struggle, at most once each, with a cooldown, never during a warning.
            if (warning || _coach.ShowingHint) return;
            for (int i = 0; i < step.Hints.Length && i < MaxHints; i++)
            {
                if (_hintUsed[i]) continue;
                var hint = step.Hints[i];
                bool applies = c.TimeInStep >= hint.AfterSeconds && hint.When != null && hint.When(c);
                _hintHeld[i] = applies ? _hintHeld[i] + dt : 0f;
                if (!applies || _hintHeld[i] < hint.SustainSeconds) continue;
                if (_sinceHint < _s.hintCooldownSeconds || _hintsShown >= _s.maxHintsPerStep) continue;
                _hintUsed[i] = true;
                ShowHint(hint.TextFor(c), hint.Highlights);
                break;
            }
        }
    }
}
