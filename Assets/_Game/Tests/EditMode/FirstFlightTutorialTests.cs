using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.Missions;
using OpenSkies.World;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The First Flight driven by scripted telemetry through the real phase estimator
    /// and warning monitor: every transition comes from what the aircraft does, never
    /// from a timer alone.
    /// </summary>
    public class FirstFlightTutorialTests
    {
        static MissionScript Started()
        {
            var m = new MissionScript();
            m.Tutorial.Start();
            m.StepOnce();
            return m;
        }

        /// <summary>Stick moved and released, full power, rotation and lift-off: the tutorial is in the initial climb.</summary>
        static void TakeOff(MissionScript m)
        {
            m.StickPitch = 0.8f;
            m.Run(0.5f);
            m.StickPitch = 0f;
            m.RunUntil(s => s.Step == TutorialStepId.Takeoff, 3f);
            m.Flight.Throttle = 1f;
            m.RunUntil(s => s.Step == TutorialStepId.Rotation, 15f, s => s.Flight.ChangeSpeed(5f, 80f));
            m.RunUntil(s => s.Flight.Groundspeed >= 58f, 20f, s => s.Flight.ChangeSpeed(5f, 80f));
            m.StickPitch = 0.4f;
            m.Flight.WeightOnWheels = false;
            m.Flight.VerticalSpeed = 700f;
            m.RunUntil(s => s.Step == TutorialStepId.InitialClimb, 10f, s => s.Flight.ChangeSpeed(2f, 68f));
            m.StickPitch = 0f;
        }

        // ---- Successful first flight ---------------------------------------------------------------

        [Test]
        public void SuccessfulFirstFlight_EveryStepFromTheAircraftsBehaviour_ToCompletion()
        {
            var m = Started();
            Assert.AreEqual(TutorialStepId.Preparation, m.Step);
            Assert.IsTrue(m.Cue.Visible, "the first instruction shows");
            Assert.AreEqual(CoachHighlight.Stick, m.Cue.Highlights, "the stick is pointed out");
            Assert.IsTrue(m.Cue.HasDetail, "a short explanation is available on request");
            m.Run(5f);
            Assert.AreEqual(TutorialStepId.Preparation, m.Step, "time alone completes nothing");

            TakeOff(m);
            m.RunUntil(s => s.Step == TutorialStepId.Turn, 90f, s => { s.Flight.ChangeSpeed(1f, 70f); });
            Assert.That(m.Flight.AltitudeFeet, Is.GreaterThanOrEqualTo(MissionScript.Settings.climbTargetFeet - 1f));
            Assert.IsTrue(m.Tutorial.HasNavigationTarget, "the turn points at the lighthouse");
            Assert.AreEqual("LIGHTHOUSE", m.Tutorial.NavigationTarget.Name);
            Assert.AreEqual(PrototypeWorld.LighthouseId, m.Tutorial.ActiveLandmarkId, "its light flashes");
            Assert.AreEqual(HudElements.Bit(HudElement.NavigationGuide), m.Tutorial.RequestedGuides);
            Assert.AreEqual(MissionStartKind.Airborne, m.Tutorial.Checkpoint.Kind, "RESET now restarts in the air");

            m.RunUntil(s => s.Step == TutorialStepId.Cruise, 90f, s => s.FlyToTarget(85f, 1000f));
            m.RunUntil(s => s.Step == TutorialStepId.Return, 400f, s => s.FlyToTarget(90f, 1000f));
            Assert.IsTrue(m.Saw("Lighthouse reached"), "the lighthouse checkpoint is confirmed");
            Assert.AreEqual(0, m.Tutorial.MissedWaypoints);

            m.RunUntil(s => s.Step == TutorialStepId.ApproachAndLanding, 400f, s => s.FlyToTarget(80f, 700f));
            Assert.AreEqual(MissionStartKind.FinalApproach, m.Tutorial.Checkpoint.Kind, "RESET now restarts on final");
            Assert.AreEqual(HudElements.Bit(HudElement.ApproachGuide), m.Tutorial.RequestedGuides, "the approach lights are asked for");

            m.RunUntil(s => s.Tutorial.State == FirstFlightTutorial.Status.Completed, 300f, s => s.FlyApproachAndLanding());
            var landing = m.Tutorial.LastLanding;
            Assert.IsTrue(landing.Valid, "the landing is assessed");
            Assert.IsFalse(landing.Crashed);
            Assert.IsTrue(landing.Touchdown.OnRunway, "touched down on the runway");
            Assert.That(landing.Total, Is.GreaterThan(50), "a stabilised approach scores well");
            Assert.IsFalse(m.Tutorial.Cue.Visible, "the coach hands over to the result card");

            CollectionAssert.AreEqual(new[]
            {
                TutorialStepId.Preparation, TutorialStepId.Takeoff, TutorialStepId.Rotation, TutorialStepId.InitialClimb,
                TutorialStepId.Turn, TutorialStepId.Cruise, TutorialStepId.Return, TutorialStepId.ApproachAndLanding,
            }, m.Steps);
            Assert.AreEqual(0, m.Tutorial.FailedTakeoffs);
            Assert.AreEqual(0, m.Tutorial.GoArounds);
            Assert.That(m.Tutorial.HintsShown, Is.LessThanOrEqualTo(2), "a player who flies well gets no nagging");
        }

        [Test]
        public void Instructions_AreQuietWhenThePlayerIsDoingWell_AndNeverRepeatedBackToBack()
        {
            var m = Started();
            TakeOff(m);
            m.RunUntil(s => s.Step == TutorialStepId.Turn, 90f, s => s.Flight.ChangeSpeed(1f, 70f));
            m.RunUntil(s => s.Step == TutorialStepId.Cruise, 90f, s => s.FlyToTarget(85f, 1000f));
            m.Run(18f, s => s.FlyToTarget(90f, 1000f));
            Assert.IsFalse(m.Cue.Visible, "on course in cruise the instruction has faded");
            foreach (var message in m.Messages)
                Assert.That(m.TimesShown(message), Is.LessThanOrEqualTo(1), "never repeated: " + message);
        }

        [Test]
        public void KeyboardPlayers_GetKeyboardTexts()
        {
            var m = new MissionScript { Touch = false };
            m.Tutorial.Start();
            m.StepOnce();
            StringAssert.Contains("W and S", m.Cue.Text);
        }

        [Test]
        public void ExperiencedPilot_TakingOffStraightAway_SkipsTheGroundSteps()
        {
            var m = Started();
            m.Flight.Throttle = 1f;
            m.RunUntil(s => s.Flight.Groundspeed >= 58f, 30f, s => s.Flight.ChangeSpeed(5f, 80f));
            m.Flight.WeightOnWheels = false;
            m.Flight.VerticalSpeed = 800f;
            m.RunUntil(s => s.Step == TutorialStepId.InitialClimb, 10f, s => s.Flight.ChangeSpeed(2f, 68f));
            Assert.Pass("airborne without the preparation step: the tutorial follows the pilot");
        }

        // ---- Repeated take-off attempts -----------------------------------------------------------

        [Test]
        public void AbandonedTakeoff_GoesBackToTheTakeoff_WithAShortExplanation_ThenSucceeds()
        {
            var m = Started();
            m.StickPitch = 0.8f;
            m.Run(0.5f);
            m.StickPitch = 0f;
            m.RunUntil(s => s.Step == TutorialStepId.Takeoff, 3f);
            m.Flight.Throttle = 1f;
            m.RunUntil(s => s.Flight.Groundspeed >= 35f, 20f, s => s.Flight.ChangeSpeed(5f, 80f));
            Assert.AreEqual(TutorialStepId.Rotation, m.Step);

            // The player loses heart: idle and brake to a stop with plenty of runway left.
            m.Flight.Throttle = 0f;
            m.RunUntil(s => s.Step == TutorialStepId.Takeoff, 20f, s => s.Flight.ChangeSpeed(-6f, 0f));
            Assert.AreEqual(1, m.Tutorial.FailedTakeoffs);
            Assert.AreEqual(CoachTone.Hint, m.Cue.Tone);
            StringAssert.Contains("ease the stick back", m.Cue.Text, "throttle and pitch explained");
            StringAssert.Contains("Full power", m.Cue.Text);
            Assert.AreEqual(TutorialStepId.Takeoff, m.Step, "not failed: back to the take-off");

            // Second attempt.
            m.Flight.Throttle = 1f;
            m.RunUntil(s => s.Flight.Groundspeed >= 58f, 30f, s => s.Flight.ChangeSpeed(5f, 80f));
            m.Flight.WeightOnWheels = false;
            m.Flight.VerticalSpeed = 700f;
            m.RunUntil(s => s.Step == TutorialStepId.InitialClimb, 10f, s => s.Flight.ChangeSpeed(2f, 68f));
            Assert.AreEqual(1, m.Tutorial.FailedTakeoffs);
        }

        [Test]
        public void AbandonedTakeoff_WithoutEnoughRunwayLeft_SuggestsReset()
        {
            var m = Started();
            m.Tutorial.StartAt(TutorialStepId.Takeoff);
            // Most of the runway behind: 1200 m past the threshold.
            m.Flight.Position = new Vector3(0f, 0f, FlightScript.Threshold36Z + 1200f);
            m.Flight.Throttle = 1f;
            m.RunUntil(s => s.Flight.Groundspeed >= 30f, 20f, s => s.Flight.ChangeSpeed(8f, 80f));
            m.Flight.Throttle = 0f;
            m.RunUntil(s => s.Tutorial.FailedTakeoffs == 1, 20f, s => s.Flight.ChangeSpeed(-8f, 0f));
            StringAssert.Contains("RESET", m.Cue.Text);
            Assert.AreEqual(CoachHighlight.Reset, m.Cue.Highlights, "RESET is pointed out");
        }

        // ---- Stall during the tutorial ------------------------------------------------------------

        [Test]
        public void Stall_TheRealWarningComesFirst_ThenTheLessonContinues()
        {
            var m = Started();
            TakeOff(m);
            m.Run(3f, s => s.Flight.ChangeSpeed(1f, 70f));
            m.Flight.StallWarning = true;
            m.Flight.Groundspeed = 50f;
            m.Run(1f);
            Assert.AreEqual(CoachTone.Safety, m.Cue.Tone, "the stall takes over the message");
            Assert.AreEqual(FlightGuidanceAdvisor.SafetyText(GuidanceHint.StallRecovery), m.Cue.Text);
            Assert.AreEqual(TutorialStepId.InitialClimb, m.Step);

            m.Flight.StallWarning = false;
            m.RunUntil(s => s.Cue.Tone != CoachTone.Safety, 5f, s => s.Flight.ChangeSpeed(3f, 70f));
            Assert.AreEqual(TutorialStepId.InitialClimb, m.Step, "the step is kept");
            m.RunUntil(s => s.Step == TutorialStepId.Turn, 90f, s => s.Flight.ChangeSpeed(1f, 70f));
        }

        // ---- Missed waypoints ---------------------------------------------------------------------

        static MissionScript InCruise(Vector3 position, float heading)
        {
            var m = new MissionScript(onGround: false);
            m.Flight.Position = position;
            m.Flight.Heading = heading;
            m.Flight.Groundspeed = 90f;
            m.Flight.Throttle = 0.6f;
            m.Flight.VerticalSpeed = 0f;
            m.Flight.Run(3f);
            m.Tutorial.StartAt(TutorialStepId.Cruise);
            m.StepOnce();
            return m;
        }

        [Test]
        public void MissedWaypoint_IsReported_AndCanBeFlownBackTo()
        {
            // North, 2 km west of the lighthouse: it passes well outside the checkpoint.
            var m = InCruise(new Vector3(0f, 1000f / FlightUnits.FeetPerMetre, 0f), 0f);
            Assert.AreEqual(TutorialStepId.Cruise, m.Step);
            Assert.AreEqual("LIGHTHOUSE", m.Tutorial.NavigationTarget.Name);
            m.RunUntil(s => s.Tutorial.MissedWaypoints == 1, 200f, s => { s.SteerTo(0f); s.HoldAltitude(1000f); });
            Assert.IsTrue(m.Saw("You passed the lighthouse"));
            Assert.AreEqual(TutorialStepId.Cruise, m.Step, "a miss never fails the mission");
            Assert.AreEqual("LIGHTHOUSE", m.Tutorial.NavigationTarget.Name, "the arrow still leads back to it");

            m.RunUntil(s => s.Tutorial.NavigationTarget.Name == "HARBOUR", 200f, s => s.FlyToTarget(90f, 1000f));
            Assert.IsTrue(m.Saw("Lighthouse reached"), "flown back to and reached");
        }

        [Test]
        public void MissedWaypoint_CarryingOn_TheNextCheckpointTakesOver()
        {
            var m = InCruise(new Vector3(0f, 1000f / FlightUnits.FeetPerMetre, 0f), 0f);
            m.RunUntil(s => s.Tutorial.MissedWaypoints == 1, 200f, s => { s.SteerTo(0f); s.HoldAltitude(1000f); });
            // The player turns south along the coast towards the harbour instead.
            m.RunUntil(s => s.Tutorial.NavigationTarget.Name == "HARBOUR", 200f, s =>
            {
                s.SteerTowards(PrototypeWorld.HarbourLightPosition);
                s.HoldAltitude(1000f);
            });
            Assert.AreEqual(TutorialStepId.Cruise, m.Step);
            m.RunUntil(s => s.Step == TutorialStepId.Return, 300f, s => s.FlyToTarget(90f, 1000f));
        }

        // ---- Go-around ---------------------------------------------------------------------------

        [Test]
        public void GoAround_OnTheApproach_IsPraised_AndLeadsBackToTheFinalApproach()
        {
            var m = new MissionScript(onGround: false);
            m.Flight.Position = new Vector3(0f, 600f / FlightUnits.FeetPerMetre, FlightScript.Threshold36Z - 2.2f * FlightScript.MetresPerNm);
            m.Flight.Heading = 0f;
            m.Flight.Groundspeed = 70f;
            m.Flight.Throttle = 0.3f;
            m.Flight.VerticalSpeed = m.Flight.GlidepathVerticalSpeed(3f);
            m.Flight.RunUntil(s => s.Phase == FlightPhase.Approach, 20f);
            m.Tutorial.StartAt(TutorialStepId.ApproachAndLanding);
            m.Run(5f, s => s.FlyApproachAndLanding(70f));
            Assert.AreEqual(TutorialStepId.ApproachAndLanding, m.Step);

            // Too high and fast: full power and climb away.
            m.Flight.Throttle = 1f;
            m.RunUntil(s => s.Step == TutorialStepId.Return, 30f, s => s.Flight.ApproachVerticalSpeed(800f, 600f));
            Assert.AreEqual(1, m.Tutorial.GoArounds);
            Assert.IsTrue(m.Saw("Go-around"), "the decision is acknowledged");
            Assert.AreEqual(CoachTone.Success, m.Tones[m.Messages.Count - 1]);
            Assert.IsTrue(m.Tutorial.HasNavigationTarget);
            StringAssert.StartsWith("FINAL", m.Tutorial.NavigationTarget.Name, "the arrow leads round to the final approach");
        }

        // ---- Crash and reset ------------------------------------------------------------------------

        [Test]
        public void DownInAField_SuggestsReset_AndResetReturnsToTheLastCheckpoint()
        {
            var m = InCruise(new Vector3(1200f, 1000f / FlightUnits.FeetPerMetre, 2000f), 45f);
            m.RunUntil(s => s.Tutorial.NavigationTarget.Name == "HARBOUR", 120f, s => s.FlyToTarget(90f, 1000f));
            Assert.AreEqual(MissionStartKind.Airborne, m.Tutorial.Checkpoint.Kind);
            Assert.AreEqual(TutorialStepId.Cruise, m.Tutorial.CheckpointStep);
            Vector3 checkpoint = m.Tutorial.Checkpoint.Position;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(m.Tutorial.Checkpoint.HeadingDegrees, 180f)), Is.LessThan(20f),
                "the checkpoint restart faces the harbour");

            // The flight ends in a field (the project has no crash model: the aircraft simply stops).
            m.Flight.Position = new Vector3(1000f, 0f, 1500f);
            m.Flight.WeightOnWheels = true;
            m.Flight.Groundspeed = 0f;
            m.Flight.Throttle = 0f;
            m.Run(5f);
            Assert.IsTrue(m.Saw("Tap RESET"), "the way on is offered, not forced");
            Assert.AreEqual(TutorialStepId.Cruise, m.Step);

            // The session puts the aircraft at the checkpoint, then tells the tutorial.
            m.Flight.Position = checkpoint;
            m.Flight.WeightOnWheels = false;
            m.Flight.Groundspeed = 90f;
            m.Tutorial.OnFlightReset();
            m.StepOnce();
            Assert.AreEqual(TutorialStepId.Cruise, m.Step);
            Assert.AreEqual("HARBOUR", m.Tutorial.NavigationTarget.Name, "the lighthouse stays done");
            Assert.AreEqual(1, m.Tutorial.Resets);
            StringAssert.Contains("checkpoint", m.Cue.Text);
        }

        [Test]
        public void ResetBeforeLiftOff_ReturnsToTheTakeoff_NotThePreparation()
        {
            var m = Started();
            m.StickPitch = 0.8f;
            m.Run(0.5f);
            m.StickPitch = 0f;
            m.RunUntil(s => s.Step == TutorialStepId.Takeoff, 3f);
            m.Flight.Throttle = 1f;
            m.Run(4f, s => s.Flight.ChangeSpeed(5f, 80f));
            Assert.AreEqual(MissionStartKind.Runway, m.Tutorial.Checkpoint.Kind);
            m.Flight.Position = FlightScript.RunwayStart;
            m.Flight.Groundspeed = 0f;
            m.Flight.Throttle = 0f;
            m.Tutorial.OnFlightReset();
            m.StepOnce();
            Assert.AreEqual(TutorialStepId.Takeoff, m.Step);
        }

        // ---- Camera and assistance changes ----------------------------------------------------------

        [Test]
        public void CameraSwitching_KeepsTheTutorialProgress()
        {
            var m = Started();
            TakeOff(m);
            m.Run(5f, s => s.Flight.ChangeSpeed(1f, 70f));
            float timeInStep = m.Tutorial.Context.TimeInStep;
            m.Flight.View = ViewMode.Cockpit;
            m.Run(1f, s => s.Flight.ChangeSpeed(1f, 70f));
            Assert.AreEqual(TutorialStepId.InitialClimb, m.Step);
            Assert.AreEqual(timeInStep + 1f, m.Tutorial.Context.TimeInStep, 0.05f, "the step carries on");
            m.Flight.View = ViewMode.External;
            m.RunUntil(s => s.Step == TutorialStepId.Turn, 90f, s => s.Flight.ChangeSpeed(1f, 70f));
        }

        [Test]
        public void AssistanceProfileChange_KeepsTheStep_AndTheTextFollowsTheProfile()
        {
            var m = Started();
            TakeOff(m);
            m.RunUntil(s => s.Step == TutorialStepId.Turn, 90f, s => s.Flight.ChangeSpeed(1f, 70f));
            m.Run(2.5f, s => { s.HoldAltitude(1000f); }); // past the climb's success message
            StringAssert.Contains("let go", m.Cue.Text, "Assisted: the bank holds when the stick is released");
            m.Profile = FlightAssistProfile.Manual;
            m.Run(0.2f, s => s.HoldAltitude(1000f));
            Assert.AreEqual(TutorialStepId.Turn, m.Step);
            StringAssert.Contains("tilt the stick", m.Cue.Text, "Manual: the player levels the wings");
        }

        // ---- Pause, resume, skip, restart -----------------------------------------------------------

        [Test]
        public void PauseAndResume_FreezeProgress_ThenContinue()
        {
            var m = Started();
            float before = m.Tutorial.Context.TimeInStep;
            m.Tutorial.Pause();
            Assert.AreEqual(FirstFlightTutorial.Status.Paused, m.Tutorial.State);
            m.StickPitch = 0.8f;
            m.Run(0.5f);
            m.StickPitch = 0f;
            m.Run(2f);
            Assert.AreEqual(TutorialStepId.Preparation, m.Step, "nothing advances while paused");
            Assert.AreEqual(before, m.Tutorial.Context.TimeInStep, 1e-4f, "the step's clock is frozen too");
            m.Tutorial.Resume();
            m.StickPitch = 0.8f;
            m.Run(0.5f);
            m.StickPitch = 0f;
            m.RunUntil(s => s.Step == TutorialStepId.Takeoff, 3f);
        }

        [Test]
        public void Skip_StopsCoachingAtOnce_AndAsksTheHudForNothing()
        {
            var m = Started();
            Assert.IsTrue(m.Cue.Visible);
            m.Tutorial.Skip();
            m.StepOnce();
            Assert.AreEqual(FirstFlightTutorial.Status.Skipped, m.Tutorial.State);
            Assert.IsFalse(m.Cue.Visible);
            Assert.IsFalse(m.Tutorial.HasNavigationTarget);
            Assert.AreEqual(0, m.Tutorial.RequestedGuides);
            Assert.AreEqual(0, m.Tutorial.RequestedActions);
        }

        [Test]
        public void Restart_BeginsAgainFromThePreparation()
        {
            var m = Started();
            TakeOff(m);
            Assert.AreEqual(TutorialStepId.InitialClimb, m.Step);
            m.Tutorial.Restart(); // the session puts the aircraft back on the runway with it
            Assert.AreEqual(TutorialStepId.Preparation, m.Step);
            Assert.AreEqual(MissionStartKind.Runway, m.Tutorial.Checkpoint.Kind);
            Assert.AreEqual(FirstFlightTutorial.Status.Running, m.Tutorial.State);
        }

        [Test]
        public void TapForDetail_ShowsTheExplanation_UntilTappedAgain()
        {
            var m = Started();
            string instruction = m.Cue.Text;
            m.Tutorial.ToggleDetail();
            m.StepOnce();
            Assert.IsTrue(m.Cue.ShowingDetail);
            Assert.AreNotEqual(instruction, m.Cue.Text);
            m.Tutorial.ToggleDetail();
            m.StepOnce();
            Assert.IsFalse(m.Cue.ShowingDetail);
            Assert.AreEqual(instruction, m.Cue.Text);
        }

        // ---- Course data ----------------------------------------------------------------------------

        [Test]
        public void Course_HasEightSteps_WithSpeedsFromTheAircraft()
        {
            var steps = TutorialCourse.Build(new TutorialSettings(), MissionScript.Speeds);
            Assert.AreEqual(8, steps.Length);
            for (int i = 0; i < steps.Length; i++)
            {
                Assert.AreEqual((TutorialStepId)i, steps[i].Id);
                Assert.IsNotEmpty(steps[i].Callouts, steps[i].Title);
                Assert.IsFalse(string.IsNullOrEmpty(steps[i].Detail), steps[i].Title);
                StringAssert.StartsWith((i + 1) + "/8", steps[i].Label);
                foreach (var callout in steps[i].Callouts)
                    Assert.That(callout.Text.Length, Is.LessThanOrEqualTo(64), "short messages: " + callout.Text);
            }
            string rotate = MissionText.Number(MissionScript.Speeds.RotateKnots);
            StringAssert.Contains(rotate + " KT", steps[(int)TutorialStepId.Rotation].Callouts[1].Text);
        }
    }
}
