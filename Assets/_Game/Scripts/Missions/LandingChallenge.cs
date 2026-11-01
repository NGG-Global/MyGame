using System;
using OpenSkies.Awareness;
using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>
    /// A replayable landing: the aircraft starts established on final approach
    /// (<see cref="StartState"/>), the player lands, and the landing is scored
    /// once the aircraft has slowed. A go-around is allowed: the guide then points
    /// back to the start of the final approach until the player is lined up again.
    /// Plain C#; it never changes the flight.
    /// </summary>
    public sealed class LandingChallenge
    {
        public enum Status
        {
            Idle = 0,
            Flying = 1,
            Result = 2,
        }

        readonly LandingChallengeSettings _settings;
        readonly LandingEvaluator _evaluator;
        readonly Coach _coach = new Coach();
        readonly NavigationTarget _finalEntry;
        readonly string _label, _intro, _introDetail, _goAroundText;
        const string StopText = "Idle power and hold BRAKE to stop";
        const string StopKeyboardText = "Left Ctrl to idle, hold Space to stop";
        Status _status;
        float _time;
        bool _circling; // after a go-around, until lined up again
        LandingAssessment _result;

        public LandingChallenge(LandingChallengeSettings settings, LandingScoringSettings scoring, in TrainingSpeeds speeds, in RunwayInfo runway)
        {
            _settings = settings ?? new LandingChallengeSettings();
            _evaluator = new LandingEvaluator(scoring, speeds);
            int designator = RunwayGeometry.Designator(_settings.landingHeadingDegrees);
            _finalEntry = new NavigationTarget("FINAL " + designator.ToString("00"),
                NavigationMath.PointOnFinal(runway, _settings.landingHeadingDegrees, _settings.startDistanceMetres), 800f, false);
            _label = "LANDING CHALLENGE  RWY " + designator.ToString("00");
            _intro = "Follow the lights to the runway. About " + MissionText.Number(speeds.ApproachFlapsKnots) + " KT";
            _introDetail = "Two white and two red lights: on the path. Over the runway, idle the power and raise the nose gently. "
                           + "Scored on touchdown rate, alignment, centreline, speed and control.";
            _goAroundText = "Going around. Follow the arrow back to the final approach";
        }

        public Status State => _status;
        public bool IsActive => _status == Status.Flying;
        public LandingEvaluator Evaluator => _evaluator;
        public LandingAssessment Result => _result;
        public CoachingCue Cue => _coach.Cue;
        public int Attempts { get; private set; }
        public int GoArounds { get; private set; }
        public bool HasNavigationTarget => _status == Status.Flying && _circling;
        public NavigationTarget NavigationTarget => _finalEntry;
        public int RequestedGuides => _status != Status.Flying ? 0
            : HudElements.Bit(HudElement.ApproachGuide) | (_circling ? HudElements.Bit(HudElement.NavigationGuide) : 0);

        public event Action Landed;

        /// <summary>Where the challenge starts (and every retry).</summary>
        public MissionStart StartState => new MissionStart
        {
            Kind = MissionStartKind.FinalApproach,
            DistanceBeforeThresholdMetres = _settings.startDistanceMetres,
            FlapDetent = _settings.flapDetent,
            GlidepathDegrees = _settings.glidepathDegrees,
            LandingHeadingDegrees = _settings.landingHeadingDegrees,
        };

        /// <summary>A new attempt (also Retry): the session puts the aircraft at <see cref="StartState"/>.</summary>
        public void Start()
        {
            _status = Status.Flying;
            _time = 0f;
            _circling = false;
            _result = default;
            Attempts++;
            _evaluator.Arm();
            _coach.SetEnabled(true);
            _coach.BeginStep(_label, _introDetail);
            _coach.SetInstruction(_intro, CoachHighlight.None, false);
            _coach.Update(0f);
        }

        /// <summary>Leaves the challenge (Free Flight from here, or another mode).</summary>
        public void Stop()
        {
            _status = Status.Idle;
            _evaluator.Disarm();
            _coach.SetEnabled(false);
        }

        public void ToggleDetail()
        {
            if (_status == Status.Flying) _coach.ToggleDetail();
        }

        public void Update(in MissionInput input, float deltaTime)
        {
            if (_status != Status.Flying || !(deltaTime > 0f)) return;
            _time += deltaTime;
            var state = _evaluator.Update(input, deltaTime);

            bool goAround = state == LandingEvaluator.Status.Cancelled
                            || (!_evaluator.TouchedDown && input.Airborne && input.Phase.Reason == PhaseTransitionReason.GoAround
                                && (input.Phase.Phase == FlightPhase.InitialClimb || input.Phase.Phase == FlightPhase.Climb));
            if (goAround && !_circling)
            {
                GoArounds++;
                _circling = true;
                _evaluator.Arm();
                _coach.ShowHint(_goAroundText, CoachHighlight.None, 5f);
            }
            if (_circling && input.Airborne && (input.Phase.Phase == FlightPhase.Approach || input.Phase.Phase == FlightPhase.Landing))
                _circling = false;

            var safety = FlightGuidanceAdvisor.SafetyHintFor(input.WarningMask);
            _coach.SetSafety(safety == GuidanceHint.None ? null : FlightGuidanceAdvisor.SafetyText(safety));
            if (_evaluator.TouchedDown)
                _coach.SetInstruction(input.TouchControls ? StopText : StopKeyboardText, CoachHighlight.Throttle | CoachHighlight.Brake, false);
            else
                _coach.SetInstruction(_intro, CoachHighlight.None, _time > _settings.introSeconds);
            _coach.Update(deltaTime);

            if (state == LandingEvaluator.Status.Complete)
            {
                _result = _evaluator.Result;
                _status = Status.Result;
                _coach.SetEnabled(false);
                Landed?.Invoke();
            }
        }
    }
}
