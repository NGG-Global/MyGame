using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>Overall landing grade. Values are stable.</summary>
    public enum LandingGrade
    {
        NeedsWork = 0,
        Fair = 1,
        Good = 2,
        Excellent = 3,
        /// <summary>Not a landing: the aircraft was out of control.</summary>
        Crash = 4,
    }

    /// <summary>The moment the wheels first touched.</summary>
    public struct LandingTouchdown
    {
        /// <summary>Descent rate just before contact, fpm (positive = descending).</summary>
        public float DescentFpm;
        public float AirspeedKnots;
        /// <summary>1 g stall speed for the flaps at touchdown, kt.</summary>
        public float StallSpeedKnots;
        /// <summary>The larger of the heading and track errors from the runway direction, degrees.</summary>
        public float AlignmentErrorDegrees;
        /// <summary>Positive right of the centreline, m.</summary>
        public float CrossTrackMetres;
        /// <summary>Distance past the landing threshold, m (negative = before the runway).</summary>
        public float PastThresholdMetres;
        public bool OnRunway;
        public float RunwayHalfWidthMetres;
        public int RunwayDesignator;
    }

    /// <summary>A scored landing. Scores are 0-100 game points.</summary>
    public struct LandingAssessment
    {
        public bool Valid;
        public bool Crashed;
        public LandingTouchdown Touchdown;
        public int Bounces;
        public bool LeftRunway;
        public float MaxGroundBankDegrees;
        public float DescentScore;
        public float AlignmentScore;
        public float LateralScore;
        public float SpeedScore;
        public float ControlScore;
        public int Total;
        public LandingGrade Grade;
    }

    /// <summary>Pure scoring functions with the ranges from <see cref="LandingScoringSettings"/>.</summary>
    public static class LandingScoring
    {
        /// <summary>100 at or below <paramref name="full"/>, 0 at or above <paramref name="zero"/>, linear between.</summary>
        public static float Linear(float value, float full, float zero)
        {
            if (float.IsNaN(value)) return 0f;
            if (zero <= full) return value <= full ? 100f : 0f;
            return 100f * Mathf.Clamp01((zero - value) / (zero - full));
        }

        public static float Descent(float descentFpm, LandingScoringSettings s) =>
            Linear(Mathf.Max(0f, descentFpm), s.descentFullFpm, s.descentZeroFpm);

        public static float Alignment(float errorDegrees, LandingScoringSettings s) =>
            Linear(Mathf.Abs(errorDegrees), s.alignmentFullDegrees, s.alignmentZeroDegrees);

        /// <summary>Off the paved surface (beside it or before the threshold) scores 0.</summary>
        public static float Lateral(in LandingTouchdown t, LandingScoringSettings s) =>
            t.OnRunway ? Linear(Mathf.Abs(t.CrossTrackMetres), s.lateralFullMetres, Mathf.Max(t.RunwayHalfWidthMetres, s.lateralFullMetres + 1f)) : 0f;

        public static float Speed(float airspeedKnots, float stallSpeedKnots, LandingScoringSettings s)
        {
            if (!(stallSpeedKnots > 1f)) return 0f;
            float ratio = airspeedKnots / stallSpeedKnots;
            if (ratio > s.speedIdealMaxFactor) return Linear(ratio, s.speedIdealMaxFactor, s.speedZeroHighFactor);
            if (ratio < s.speedIdealMinFactor) return Linear(-ratio, -s.speedIdealMinFactor, -s.speedZeroLowFactor);
            return 100f;
        }

        public static float Control(int bounces, bool leftRunway, float maxGroundBankDegrees, LandingScoringSettings s)
        {
            float score = 100f - bounces * s.bouncePenalty - (leftRunway ? s.leftRunwayPenalty : 0f);
            float bankLoss = 100f - Linear(maxGroundBankDegrees, s.groundBankFullDegrees, s.groundBankZeroDegrees);
            score -= bankLoss * 0.4f;
            return Mathf.Clamp(score, 0f, 100f);
        }

        public static LandingGrade Grade(int total, bool crashed, LandingScoringSettings s)
        {
            if (crashed) return LandingGrade.Crash;
            if (total >= s.excellentScore) return LandingGrade.Excellent;
            if (total >= s.goodScore) return LandingGrade.Good;
            if (total >= s.fairScore) return LandingGrade.Fair;
            return LandingGrade.NeedsWork;
        }

        /// <summary>Fills the scores, total and grade of an assessment from its measurements.</summary>
        public static void Score(ref LandingAssessment a, LandingScoringSettings s)
        {
            var t = a.Touchdown;
            a.DescentScore = Descent(t.DescentFpm, s);
            a.AlignmentScore = Alignment(t.AlignmentErrorDegrees, s);
            a.LateralScore = Lateral(t, s);
            a.SpeedScore = Speed(t.AirspeedKnots, t.StallSpeedKnots, s);
            a.ControlScore = a.Crashed ? 0f : Control(a.Bounces, a.LeftRunway, a.MaxGroundBankDegrees, s);
            float weights = s.descentWeight + s.alignmentWeight + s.lateralWeight + s.speedWeight + s.controlWeight;
            float total = weights > 0f
                ? (a.DescentScore * s.descentWeight + a.AlignmentScore * s.alignmentWeight + a.LateralScore * s.lateralWeight
                   + a.SpeedScore * s.speedWeight + a.ControlScore * s.controlWeight) / weights
                : 0f;
            a.Total = a.Crashed ? 0 : Mathf.Clamp(Mathf.RoundToInt(total), 0, 100);
            a.Grade = Grade(a.Total, a.Crashed, s);
        }

        public static string GradeLabel(LandingGrade grade)
        {
            switch (grade)
            {
                case LandingGrade.Excellent: return "Excellent";
                case LandingGrade.Good: return "Good";
                case LandingGrade.Fair: return "Fair";
                case LandingGrade.Crash: return "Out of control";
                default: return "Keep practising";
            }
        }

        /// <summary>
        /// Up to four short lines for the result card, built once per landing (not per
        /// frame): the weakest areas first would read as a scolding, so the order is fixed.
        /// </summary>
        public static string Summary(in LandingAssessment a, LandingScoringSettings s)
        {
            var t = a.Touchdown;
            if (a.Crashed)
                return "Touchdown " + MissionText.Number(t.DescentFpm) + " FPM, " + MissionText.Number(t.AirspeedKnots) + " KT\n"
                       + "The aircraft did not stay under control.";

            string descent = "Touchdown " + MissionText.Number(t.DescentFpm) + " FPM: "
                             + (t.DescentFpm <= s.descentFullFpm ? "smooth" : t.DescentFpm < s.descentZeroFpm ? "firm" : "hard");

            string position;
            if (!t.OnRunway) position = t.PastThresholdMetres < 0f ? "Touched down before the runway" : "Touched down beside the runway";
            else if (Mathf.Abs(t.CrossTrackMetres) <= s.lateralFullMetres) position = "On the centreline";
            else position = "Centreline: " + MissionText.Number(Mathf.Abs(t.CrossTrackMetres)) + " M " + (t.CrossTrackMetres > 0f ? "right" : "left");
            position += ", " + (t.AlignmentErrorDegrees <= s.alignmentFullDegrees ? "straight" : MissionText.Number(t.AlignmentErrorDegrees) + "° off");

            float ratio = t.StallSpeedKnots > 1f ? t.AirspeedKnots / t.StallSpeedKnots : 0f;
            string speed = "Speed " + MissionText.Number(t.AirspeedKnots) + " KT: "
                           + (ratio > s.speedIdealMaxFactor ? "fast" : ratio < s.speedIdealMinFactor ? "slow" : "good")
                           + " (stall " + MissionText.Number(t.StallSpeedKnots) + ")";

            string control = a.LeftRunway ? "Left the runway on the roll"
                : a.Bounces == 1 ? "1 bounce"
                : a.Bounces > 1 ? a.Bounces + " bounces"
                : "No bounce, kept straight";
            return descent + "\n" + position + "\n" + speed + "\n" + control;
        }
    }

    /// <summary>
    /// Watches one landing and scores it: the touchdown (descent rate, alignment,
    /// centreline, airspeed against the stall speed for the flaps in use), then the
    /// roll (bounces, staying on the runway, wings level) until the aircraft has
    /// slowed. A climb-away after the touchdown cancels it (touch-and-go or
    /// go-around). It never changes the flight and adds no failure behaviour:
    /// an uncontrolled arrival is only reported. Plain C#, allocation-free per step.
    /// </summary>
    public sealed class LandingEvaluator
    {
        public enum Status
        {
            Idle = 0,
            /// <summary>Armed, waiting for the touchdown.</summary>
            Flying = 1,
            /// <summary>On the wheels (or bouncing), slowing down.</summary>
            Rolling = 2,
            Complete = 3,
            /// <summary>Climbed away after touching down.</summary>
            Cancelled = 4,
        }

        const int History = 8;

        readonly LandingScoringSettings _settings;
        TrainingSpeeds _speeds;
        readonly float[] _descent = new float[History];
        int _historyNext;
        Status _status;
        float _airborneTime, _offGroundTime, _slowTime;
        bool _bouncing;
        LandingAssessment _assessment;

        public LandingEvaluator(LandingScoringSettings settings, in TrainingSpeeds speeds)
        {
            _settings = settings ?? new LandingScoringSettings();
            _speeds = speeds;
        }

        public LandingScoringSettings Settings => _settings;
        public Status State => _status;
        public bool TouchedDown => _status == Status.Rolling || _status == Status.Complete;
        public bool HasResult => _status == Status.Complete;
        /// <summary>The assessment so far (touchdown values are set from the first contact).</summary>
        public LandingAssessment Result => _assessment;
        public bool Crashed => _assessment.Crashed;

        public void SetSpeeds(in TrainingSpeeds speeds) => _speeds = speeds;

        /// <summary>Ready for the next landing.</summary>
        public void Arm()
        {
            _status = Status.Flying;
            _airborneTime = _offGroundTime = _slowTime = 0f;
            _bouncing = false;
            _assessment = default;
            for (int i = 0; i < History; i++) _descent[i] = 0f;
            _historyNext = 0;
        }

        public void Disarm() => _status = Status.Idle;

        public Status Update(in MissionInput input, float deltaTime)
        {
            if (_status == Status.Idle || _status == Status.Complete || _status == Status.Cancelled || !(deltaTime > 0f)) return _status;
            var t = input.Telemetry;
            bool wheels = t.WeightOnWheels;

            if (_status == Status.Flying)
            {
                if (!wheels)
                {
                    _airborneTime += deltaTime;
                    _descent[_historyNext] = -t.VerticalSpeedFeetPerMinute;
                    _historyNext = (_historyNext + 1) % History;
                    return _status;
                }
                if (_airborneTime < _settings.minimumAirborneSeconds) return _status; // still on the take-off run
                Touchdown(input);
                return _status;
            }

            // Rolling: bounces, control, and when the aircraft has slowed enough to show the result.
            if (!wheels)
            {
                _offGroundTime += deltaTime;
                if (!_bouncing && _offGroundTime >= _settings.bounceSeconds)
                {
                    _bouncing = true;
                    _assessment.Bounces++;
                }
                bool climbingAway = t.VerticalSpeedFeetPerMinute > 300f && t.Throttle > 0.6f && _offGroundTime > 1.5f;
                if (_offGroundTime >= _settings.goAroundSeconds || climbingAway) _status = Status.Cancelled;
                return _status;
            }
            _offGroundTime = 0f;
            _bouncing = false;

            float bank = Mathf.Abs(t.BankDegrees);
            if (bank > _assessment.MaxGroundBankDegrees) _assessment.MaxGroundBankDegrees = bank;
            if (bank >= _settings.crashBankDegrees || Mathf.Abs(t.PitchDegrees) >= _settings.crashPitchDegrees) _assessment.Crashed = true;
            var r = input.Runway;
            if (r.Valid && !r.OnRunway && t.GroundspeedKnots > 10f && _assessment.Touchdown.OnRunway) _assessment.LeftRunway = true;

            _slowTime = t.GroundspeedKnots <= _settings.resultKnots ? _slowTime + deltaTime : 0f;
            if (_slowTime >= _settings.resultSeconds || t.GroundspeedKnots < 2f) Finish();
            return _status;
        }

        void Touchdown(in MissionInput input)
        {
            var t = input.Telemetry;
            var r = input.Runway;
            float descent = -t.VerticalSpeedFeetPerMinute;
            for (int i = 0; i < History; i++) descent = Mathf.Max(descent, _descent[i]);

            float alignment = 0f;
            if (r.Valid)
            {
                float headingError = Mathf.Abs(Mathf.DeltaAngle(r.LandingHeadingDegrees, t.HeadingDegrees));
                alignment = Mathf.Max(headingError, Mathf.Abs(r.TrackErrorDegrees));
            }
            _assessment = new LandingAssessment
            {
                Valid = true,
                Touchdown = new LandingTouchdown
                {
                    DescentFpm = Mathf.Max(0f, descent),
                    AirspeedKnots = t.IndicatedAirspeedKnots,
                    StallSpeedKnots = _speeds.StallKnots(t.FlapPosition),
                    AlignmentErrorDegrees = alignment,
                    CrossTrackMetres = r.Valid ? r.CrossTrackMetres : 0f,
                    PastThresholdMetres = r.Valid ? -r.AlongTrackMetres : 0f,
                    OnRunway = r.Valid && r.OnRunway,
                    RunwayHalfWidthMetres = r.Valid ? r.RunwayWidthMetres * 0.5f : 15f,
                    RunwayDesignator = r.Valid ? r.Designator : 0,
                },
                Crashed = descent >= _settings.crashDescentFpm || Mathf.Abs(t.BankDegrees) >= _settings.crashBankDegrees,
            };
            _status = Status.Rolling;
            _offGroundTime = _slowTime = 0f;
        }

        void Finish()
        {
            LandingScoring.Score(ref _assessment, _settings);
            _status = Status.Complete;
        }
    }
}
