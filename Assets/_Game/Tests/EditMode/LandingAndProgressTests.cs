using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.Missions;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>Landing scoring, the Landing Challenge, and local progress.</summary>
    public class LandingAndProgressTests
    {
        static readonly LandingScoringSettings Scoring = new LandingScoringSettings();

        // ---- Scoring ranges -------------------------------------------------------------------------

        [Test]
        public void Scoring_UsesTheConfiguredRanges()
        {
            Assert.AreEqual(100f, LandingScoring.Descent(150f, Scoring));
            Assert.AreEqual(50f, LandingScoring.Descent((Scoring.descentFullFpm + Scoring.descentZeroFpm) * 0.5f, Scoring), 0.01f);
            Assert.AreEqual(0f, LandingScoring.Descent(900f, Scoring));
            Assert.AreEqual(100f, LandingScoring.Alignment(-2f, Scoring));
            Assert.AreEqual(0f, LandingScoring.Alignment(20f, Scoring));
            Assert.AreEqual(100f, LandingScoring.Speed(55f, 50f, Scoring), "1.1 × stall: ideal");
            Assert.AreEqual(0f, LandingScoring.Speed(80f, 50f, Scoring), "1.6 × stall: far too fast");
            Assert.That(LandingScoring.Speed(66f, 50f, Scoring), Is.InRange(1f, 99f), "1.32 × stall: partly");

            var onCentre = new LandingTouchdown { OnRunway = true, CrossTrackMetres = 1f, RunwayHalfWidthMetres = 15f };
            var nearEdge = new LandingTouchdown { OnRunway = true, CrossTrackMetres = -14f, RunwayHalfWidthMetres = 15f };
            var beside = new LandingTouchdown { OnRunway = false, CrossTrackMetres = 20f, RunwayHalfWidthMetres = 15f };
            Assert.AreEqual(100f, LandingScoring.Lateral(onCentre, Scoring));
            Assert.That(LandingScoring.Lateral(nearEdge, Scoring), Is.LessThan(15f));
            Assert.AreEqual(0f, LandingScoring.Lateral(beside, Scoring));

            Assert.AreEqual(100f, LandingScoring.Control(0, false, 2f, Scoring));
            Assert.AreEqual(100f - Scoring.bouncePenalty, LandingScoring.Control(1, false, 2f, Scoring), 0.01f);
            Assert.That(LandingScoring.Control(0, true, 2f, Scoring), Is.LessThanOrEqualTo(100f - Scoring.leftRunwayPenalty));
        }

        [Test]
        public void Scoring_RangesAreConfigurable()
        {
            var strict = new LandingScoringSettings { descentFullFpm = 100f, descentZeroFpm = 300f };
            Assert.AreEqual(100f, LandingScoring.Descent(200f, Scoring));
            Assert.AreEqual(50f, LandingScoring.Descent(200f, strict), 0.01f);
        }

        [Test]
        public void Total_AndGrade_FromTheWeightedCategories()
        {
            var a = new LandingAssessment
            {
                Valid = true,
                Touchdown = new LandingTouchdown
                {
                    DescentFpm = 150f, AirspeedKnots = 52f, StallSpeedKnots = 46f, AlignmentErrorDegrees = 1f,
                    CrossTrackMetres = 1f, OnRunway = true, RunwayHalfWidthMetres = 15f, PastThresholdMetres = 200f,
                },
            };
            LandingScoring.Score(ref a, Scoring);
            Assert.AreEqual(100, a.Total);
            Assert.AreEqual(LandingGrade.Excellent, a.Grade);

            var crash = a;
            crash.Crashed = true;
            LandingScoring.Score(ref crash, Scoring);
            Assert.AreEqual(0, crash.Total);
            Assert.AreEqual(LandingGrade.Crash, crash.Grade);
            StringAssert.Contains("did not stay under control", LandingScoring.Summary(crash, Scoring));

            string summary = LandingScoring.Summary(a, Scoring);
            Assert.That(summary.Split('\n').Length, Is.LessThanOrEqualTo(4), "concise feedback");
            StringAssert.Contains("smooth", summary);
        }

        // ---- Evaluator with scripted telemetry ----------------------------------------------------

        static MissionInput Input(FlightScript s) => new MissionInput
        {
            Telemetry = s.LastTelemetry, Phase = s.Estimator.Current, WarningMask = s.Warnings.ActiveMask, Runway = s.LastRunway,
            Position = s.Position, TouchControls = true,
        };

        /// <summary>A 3° final from 1.5 NM at 65 kt, flare at 30 ft, touchdown at the given descent rate and a braked roll.</summary>
        static void Land(FlightScript s, LandingEvaluator e, float touchdownFpm, float crossTrack = 0f, bool rollOff = false)
        {
            s.Position = new Vector3(crossTrack, 0f, FlightScript.Threshold36Z - 1.5f * FlightScript.MetresPerNm);
            s.AltitudeFeet = (1.5f * FlightScript.MetresPerNm + 150f) * Mathf.Tan(3f * Mathf.Deg2Rad) * FlightUnits.FeetPerMetre;
            s.Groundspeed = 65f;
            s.Heading = 0f;
            s.Throttle = 0.3f;
            s.VerticalSpeed = s.GlidepathVerticalSpeed(3f);
            e.Arm();
            for (int i = 0; i < 200 * 50 && e.State != LandingEvaluator.Status.Complete; i++)
            {
                if (!s.WeightOnWheels)
                {
                    if (s.AltitudeFeet < 30f)
                    {
                        s.Throttle = 0f;
                        s.VerticalSpeed = -touchdownFpm;
                        s.ChangeSpeed(-2f, 55f);
                    }
                    if (s.Position.y <= 0.02f) s.WeightOnWheels = true;
                }
                else
                {
                    s.ChangeSpeed(-6f, 0f);
                    if (rollOff) s.Heading = 30f;
                }
                s.Step();
                e.Update(Input(s), FlightScript.Dt);
            }
        }

        [Test]
        public void Evaluator_SmoothCentredLanding_ScoresHigh()
        {
            var s = FlightScript.Airborne(1.5f, 500f, 65f, -350f, 0.3f);
            var e = new LandingEvaluator(Scoring, MissionScript.Speeds);
            Land(s, e, 150f);
            Assert.IsTrue(e.HasResult);
            var r = e.Result;
            Assert.IsFalse(r.Crashed);
            Assert.IsTrue(r.Touchdown.OnRunway);
            Assert.That(r.Touchdown.DescentFpm, Is.InRange(140f, 400f), "the descent just before contact is used");
            Assert.AreEqual(0, r.Bounces);
            Assert.That(r.Total, Is.GreaterThanOrEqualTo(Scoring.goodScore));
        }

        [Test]
        public void Evaluator_HardOffCentreLanding_ScoresLower()
        {
            var good = new LandingEvaluator(Scoring, MissionScript.Speeds);
            Land(FlightScript.Airborne(1.5f, 500f, 65f, -350f, 0.3f), good, 150f);
            var hard = new LandingEvaluator(Scoring, MissionScript.Speeds);
            Land(FlightScript.Airborne(1.5f, 500f, 65f, -350f, 0.3f), hard, 650f, crossTrack: 11f);
            Assert.That(hard.Result.Total, Is.LessThan(good.Result.Total - 20));
            StringAssert.Contains("firm", LandingScoring.Summary(hard.Result, Scoring));
        }

        [Test]
        public void Evaluator_VeryHardArrival_IsOutOfControl_NotALanding()
        {
            var e = new LandingEvaluator(Scoring, MissionScript.Speeds);
            Land(FlightScript.Airborne(1.5f, 500f, 65f, -350f, 0.3f), e, 1500f);
            Assert.IsTrue(e.Result.Crashed);
            Assert.AreEqual(LandingGrade.Crash, e.Result.Grade);
        }

        [Test]
        public void Evaluator_ClimbingAwayAfterTouchdown_IsAGoAround()
        {
            var s = FlightScript.Airborne(1.5f, 500f, 65f, -350f, 0.3f);
            var e = new LandingEvaluator(Scoring, MissionScript.Speeds);
            e.Arm();
            for (int i = 0; i < 200 * 50 && s.Position.y > 0.02f; i++)
            {
                if (s.AltitudeFeet < 30f) s.VerticalSpeed = -150f;
                s.Step();
                e.Update(Input(s), FlightScript.Dt);
            }
            s.WeightOnWheels = true;
            for (int i = 0; i < 10; i++) { s.Step(); e.Update(Input(s), FlightScript.Dt); }
            Assert.IsTrue(e.TouchedDown);
            s.WeightOnWheels = false;
            s.Throttle = 1f;
            s.VerticalSpeed = 700f;
            for (int i = 0; i < 150 && e.State != LandingEvaluator.Status.Cancelled; i++) { s.Step(); e.Update(Input(s), FlightScript.Dt); }
            Assert.AreEqual(LandingEvaluator.Status.Cancelled, e.State);
        }

        // ---- Landing Challenge ----------------------------------------------------------------------

        [Test]
        public void LandingChallenge_StartsOnFinal_ScoresTheLanding_AndRestarts()
        {
            var challenge = new LandingChallenge(new LandingChallengeSettings(), Scoring, MissionScript.Speeds, FlightScript.Runway);
            var start = challenge.StartState;
            Assert.AreEqual(MissionStartKind.FinalApproach, start.Kind);
            Assert.That(start.DistanceBeforeThresholdMetres, Is.GreaterThan(1000f));
            Assert.AreEqual(-1, start.FlapDetent, "configured for landing (full flap)");

            challenge.Start();
            Assert.AreEqual(LandingChallenge.Status.Flying, challenge.State);
            Assert.AreEqual(1, challenge.Attempts);
            Assert.AreEqual(HudElements.Bit(HudElement.ApproachGuide), challenge.RequestedGuides);
            Assert.IsTrue(challenge.Cue.Visible, "a short opening instruction");

            var s = FlightScript.Airborne(1.5f, 500f, 65f, -350f, 0.3f);
            bool landed = false;
            challenge.Landed += () => landed = true;
            for (int i = 0; i < 200 * 50 && challenge.State == LandingChallenge.Status.Flying; i++)
            {
                if (!s.WeightOnWheels)
                {
                    if (s.AltitudeFeet < 30f) { s.Throttle = 0f; s.VerticalSpeed = -180f; }
                    if (s.Position.y <= 0.02f) s.WeightOnWheels = true;
                }
                else s.ChangeSpeed(-6f, 0f);
                s.Step();
                challenge.Update(Input(s), FlightScript.Dt);
            }
            Assert.IsTrue(landed);
            Assert.AreEqual(LandingChallenge.Status.Result, challenge.State);
            Assert.IsTrue(challenge.Result.Valid);
            Assert.IsFalse(challenge.Cue.Visible, "the result card takes over");

            challenge.Start(); // Retry
            Assert.AreEqual(LandingChallenge.Status.Flying, challenge.State);
            Assert.AreEqual(2, challenge.Attempts);
            Assert.AreEqual(LandingEvaluator.Status.Flying, challenge.Evaluator.State, "armed again");
            Assert.IsFalse(challenge.Result.Valid, "the previous result is cleared");
        }

        // ---- Progress ------------------------------------------------------------------------------

        [Test]
        public void Progress_TutorialCompletion_IsSavedAndLoaded()
        {
            var store = new MemoryProgressStore();
            var progress = new PlayerProgress(store);
            Assert.AreEqual(TutorialStatus.NotStarted, progress.Tutorial);
            Assert.IsTrue(progress.RecommendFirstFlight);

            progress.MarkTutorialCompleted();
            Assert.That(store.Saves, Is.GreaterThanOrEqualTo(1), "written at once (mobile apps can be killed)");
            var reloaded = new PlayerProgress(store);
            Assert.AreEqual(TutorialStatus.Completed, reloaded.Tutorial);
            Assert.IsFalse(reloaded.RecommendFirstFlight);

            reloaded.MarkTutorialSkipped();
            Assert.AreEqual(TutorialStatus.Completed, new PlayerProgress(store).Tutorial, "skipping later never undoes a completion");
        }

        [Test]
        public void Progress_Skip_StopsRecommendingTheFirstFlight()
        {
            var store = new MemoryProgressStore();
            new PlayerProgress(store).MarkTutorialSkipped();
            var reloaded = new PlayerProgress(store);
            Assert.AreEqual(TutorialStatus.Skipped, reloaded.Tutorial);
            Assert.IsFalse(reloaded.RecommendFirstFlight);
        }

        [Test]
        public void Progress_BestLanding_KeepsTheBest_AndIgnoresBadData()
        {
            var store = new MemoryProgressStore();
            var progress = new PlayerProgress(store);
            Assert.IsFalse(progress.HasBestLanding);
            Assert.IsTrue(progress.RecordLanding(60));
            Assert.IsFalse(progress.RecordLanding(40));
            Assert.IsTrue(progress.RecordLanding(85));
            var reloaded = new PlayerProgress(store);
            Assert.AreEqual(85, reloaded.BestLandingScore);
            Assert.AreEqual(3, reloaded.Landings);

            store.SetInt(PlayerProgress.TutorialKey, 42);
            store.SetInt(PlayerProgress.BestLandingKey, 400);
            var corrupt = new PlayerProgress(store);
            Assert.AreEqual(TutorialStatus.NotStarted, corrupt.Tutorial);
            Assert.IsFalse(corrupt.HasBestLanding);
        }
    }
}
