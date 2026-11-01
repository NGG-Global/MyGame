using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.Missions;
using OpenSkies.World;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>Coach, checkpoints, navigation, landmark registry, reference speeds and mission guides in the HUD rules.</summary>
    public class MissionSupportTests
    {
        // ---- Coach -------------------------------------------------------------------------------

        [Test]
        public void Coach_SafetyFirst_ThenSuccess_ThenHint_ThenInstruction()
        {
            var coach = new Coach();
            coach.SetEnabled(true);
            coach.BeginStep("1/8  TEST", "why");
            coach.SetInstruction("do this", CoachHighlight.Stick, false);
            coach.Update(0.02f);
            Assert.AreEqual("do this", coach.Cue.Text);
            Assert.AreEqual(CoachHighlight.Stick, coach.Cue.Highlights);

            coach.ShowHint("try that", CoachHighlight.Throttle, 5f);
            coach.ShowSuccess("well done", 2f);
            coach.SetSafety("STALL: lower the nose");
            coach.Update(0.02f);
            Assert.AreEqual(CoachTone.Safety, coach.Cue.Tone);
            coach.SetSafety(null);
            coach.Update(0.02f);
            Assert.AreEqual(CoachTone.Success, coach.Cue.Tone);
            coach.Update(2f);
            Assert.AreEqual(CoachTone.Hint, coach.Cue.Tone);
            Assert.AreEqual(CoachHighlight.Throttle, coach.Cue.Highlights);
            coach.Update(5f);
            Assert.AreEqual("do this", coach.Cue.Text);

            coach.SetInstruction("do this", CoachHighlight.Stick, true);
            coach.Update(0.02f);
            Assert.IsFalse(coach.Cue.Visible, "quiet when the player is doing well");
        }

        [Test]
        public void Coach_VersionChangesOnlyWithTheContent()
        {
            var coach = new Coach();
            coach.SetEnabled(true);
            coach.BeginStep("L", null);
            coach.SetInstruction("same", CoachHighlight.None, false);
            coach.Update(0.02f);
            int version = coach.Cue.Version;
            for (int i = 0; i < 100; i++)
            {
                coach.SetInstruction("same", CoachHighlight.None, false);
                coach.Update(0.02f);
            }
            Assert.AreEqual(version, coach.Cue.Version, "no HUD update while nothing changes");
            Assert.IsFalse(coach.Cue.HasDetail);
        }

        // ---- Checkpoints --------------------------------------------------------------------------

        static readonly TutorialSettings Settings = new TutorialSettings();

        [Test]
        public void Waypoint_ReachedWithinTheRadius_OrPassingClose()
        {
            var target = new NavigationTarget("T", new Vector3(0f, 0f, 1000f), 500f, true);
            var tracker = new WaypointTracker();
            tracker.Begin(target);
            Assert.AreEqual(WaypointState.Approaching, tracker.Update(new Vector3(0f, 300f, 0f), Settings));
            Assert.AreEqual(WaypointState.Reached, tracker.Update(new Vector3(0f, 300f, 600f), Settings));

            tracker.Begin(target);
            // Passes 800 m abeam (inside twice the radius), then flies on.
            for (float z = 0f; z <= 1600f; z += 20f) tracker.Update(new Vector3(800f, 300f, z), Settings);
            Assert.AreEqual(WaypointState.Reached, tracker.State, "close enough counts");
        }

        [Test]
        public void Waypoint_WideMiss_IsMissed_UntilTheAircraftTurnsBack()
        {
            var tracker = new WaypointTracker();
            tracker.Begin(new NavigationTarget("T", new Vector3(0f, 0f, 1000f), 500f, true));
            for (float z = 0f; z <= 3000f; z += 20f) tracker.Update(new Vector3(2000f, 300f, z), Settings);
            Assert.AreEqual(WaypointState.Missed, tracker.State);
            for (float z = 3000f; z >= 1000f; z -= 20f) tracker.Update(new Vector3(2000f - (3000f - z) * 0.9f, 300f, z), Settings);
            Assert.AreNotEqual(WaypointState.Missed, tracker.State, "turning back clears the miss");
        }

        // ---- Navigation and the world --------------------------------------------------------------

        [Test]
        public void Navigation_DistanceBearingAndRelativeBearing()
        {
            var target = new NavigationTarget("EAST", new Vector3(1000f, 0f, 0f), 100f, true);
            var r = NavigationMath.Evaluate(target, new Vector3(0f, 300f, 0f), 0f);
            Assert.IsTrue(r.Valid);
            Assert.AreEqual(1000f, r.DistanceMetres, 0.01f, "horizontal distance only");
            Assert.AreEqual(90f, r.BearingDegrees, 0.01f);
            Assert.AreEqual(90f, r.RelativeBearingDegrees, 0.01f, "to the right of the nose");
            Assert.AreEqual(-90f, NavigationMath.Evaluate(target, Vector3.zero, 180f).RelativeBearingDegrees, 0.01f);
            Assert.IsFalse(NavigationMath.Evaluate(default, Vector3.zero, 0f).Valid);
        }

        [Test]
        public void Navigation_PointOnFinal_IsOnTheExtendedCentreline()
        {
            var p = NavigationMath.PointOnFinal(FlightScript.Runway, 0f, 3000f);
            Assert.AreEqual(0f, p.x, 0.01f);
            Assert.AreEqual(FlightScript.Threshold36Z - 3000f, p.z, 0.01f);
            var q = NavigationMath.PointOnFinal(FlightScript.Runway, 180f, 1000f);
            Assert.AreEqual(-FlightScript.Threshold36Z + 1000f, q.z, 0.01f, "runway 18 is approached from the north");
        }

        [Test]
        public void World_LandmarkRegistry_MatchesTheLayout()
        {
            var world = PrototypeWorld.CreateRegistry();
            Assert.AreEqual(4, world.Count);
            Assert.IsTrue(world.TryGet(PrototypeWorld.LighthouseId, out var lighthouse));
            Assert.AreEqual(LandmarkKind.Lighthouse, lighthouse.kind);
            Assert.That(lighthouse.position.x, Is.GreaterThan(PrototypeWorld.ShorelineX), "on the coast, east of the shoreline");
            Assert.IsTrue(world.TryGetFirst(LandmarkKind.Airfield, out var airfield));
            Assert.AreEqual(PrototypeWorld.RunwayCentre, airfield.position);
            Assert.AreEqual(-1, world.IndexOf("nowhere"));
            Assert.Throws<System.ArgumentException>(() => new LandmarkRegistry(new[]
            {
                new Landmark("a", "A", LandmarkKind.Island, Vector3.zero, 1f), new Landmark("a", "B", LandmarkKind.Island, Vector3.one, 1f),
            }));
        }

        [Test]
        public void Beacon_FlashesSlowly()
        {
            Assert.IsTrue(LandmarkBeacon.FlashOn(0.1f, 2f, 0.35f));
            Assert.IsFalse(LandmarkBeacon.FlashOn(1.0f, 2f, 0.35f));
            Assert.IsTrue(LandmarkBeacon.FlashOn(2.1f, 2f, 0.35f));
        }

        // ---- Reference speeds ----------------------------------------------------------------------

        [Test]
        public void TrainingSpeeds_ComeFromTheAircraftEnvelope()
        {
            var envelope = AircraftEnvelope.PrototypeTrainer;
            var speeds = TrainingSpeeds.From(envelope, 340f, Settings);
            Assert.AreEqual(TrainingSpeeds.RoundToFive(envelope.StallSpeedCleanKnots * Settings.rotateFactor), speeds.RotateKnots);
            Assert.That(speeds.RotateKnots, Is.GreaterThan(envelope.StallSpeedCleanKnots));
            Assert.That(speeds.ClimbKnots, Is.GreaterThan(speeds.RotateKnots));
            Assert.That(speeds.ApproachFlapsKnots, Is.LessThan(speeds.ApproachCleanKnots), "flaps let the aircraft fly slower");
            Assert.AreEqual(0f, speeds.RotateKnots % 5f, 1e-4f, "rounded for display");

            var heavier = envelope;
            heavier.StallSpeedCleanKnots = 60f;
            Assert.That(TrainingSpeeds.From(heavier, 340f, Settings).RotateKnots, Is.GreaterThan(speeds.RotateKnots), "a different aircraft, different speeds");
            Assert.AreEqual(Settings.fallbackTakeoffRunMetres, TrainingSpeeds.From(envelope, float.NaN, Settings).TakeoffRunMetres);
        }

        // ---- Mission guides through the disclosure rules -------------------------------------------

        static DisclosureInput Cruise(HudProfile profile, ViewMode view, int guides, int actions = 0) => new DisclosureInput
        {
            Profile = profile,
            View = view,
            Phase = new FlightPhaseEstimate { Phase = FlightPhase.Cruise, Confidence = 0.9f, TimeInPhase = 30f },
            HasRunway = true,
            HasNavigationTarget = true,
            DistanceToRunwayNm = 3f,
            MissionGuides = guides,
            MissionActions = actions,
            Systems = new AircraftSystemsState { HasFlaps = true, HasParkingBrake = true },
        };

        [Test]
        public void MissionGuides_ShowInEveryProfileAndView_AndThePlayerCanStillHideThem()
        {
            int nav = HudElements.Bit(HudElement.NavigationGuide);
            foreach (HudProfile profile in System.Enum.GetValues(typeof(HudProfile)))
            foreach (ViewMode view in System.Enum.GetValues(typeof(ViewMode)))
            {
                var d = new DisclosureController();
                d.Update(Cruise(profile, view, nav), 0.02f);
                Assert.IsTrue(d.IsVisible(HudElement.NavigationGuide), $"{profile} {view}");
                Assert.AreEqual(DisclosureReason.Mission, d.GetReason(HudElement.NavigationGuide));
            }

            var controller = new DisclosureController();
            controller.Update(Cruise(HudProfile.Smart, ViewMode.External, nav), 0.02f);
            controller.ToggleGuide(HudElement.NavigationGuide);
            controller.Update(Cruise(HudProfile.Smart, ViewMode.External, nav), 0.02f);
            Assert.IsFalse(controller.IsVisible(HudElement.NavigationGuide), "the player's choice wins");

            var none = new DisclosureController();
            none.Update(Cruise(HudProfile.Cinematic, ViewMode.External, 0), 0.02f);
            Assert.IsFalse(none.IsVisible(HudElement.NavigationGuide), "without a mission nothing changes");
        }

        [Test]
        public void MissionActions_AreOfferedInEveryProfile()
        {
            int flaps = 1 << (int)ContextAction.Flaps;
            var d = new DisclosureController();
            d.Update(Cruise(HudProfile.Cinematic, ViewMode.External, 0, flaps), 0.02f);
            Assert.IsTrue(d.IsActionVisible(ContextAction.Flaps));
            Assert.AreEqual(DisclosureReason.Mission, d.GetActionReason(ContextAction.Flaps));
        }

        [Test]
        public void SafetyTips_AreSharedWithMissionCoaching()
        {
            int stall = FlightWarnings.Bit(FlightWarning.Stall) | FlightWarnings.Bit(FlightWarning.Overspeed);
            Assert.AreEqual(GuidanceHint.StallRecovery, FlightGuidanceAdvisor.SafetyHintFor(stall), "the most urgent first");
            Assert.AreEqual(GuidanceHint.None, FlightGuidanceAdvisor.SafetyHintFor(0));
            var advisor = new FlightGuidanceAdvisor(AircraftEnvelope.PrototypeTrainer);
            Assert.AreEqual(advisor.Text(GuidanceHint.StallRecovery), FlightGuidanceAdvisor.SafetyText(GuidanceHint.StallRecovery));
        }
    }
}
