using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Awareness;

namespace OpenSkies.Tests
{
    public class DisclosureControllerTests
    {
        const float Dt = 0.02f;
        static readonly HudElement[] DataElements =
            { HudElement.Airspeed, HudElement.Altitude, HudElement.VerticalSpeed, HudElement.Heading, HudElement.Groundspeed };

        static DisclosureInput Input(FlightPhase phase, HudProfile profile = HudProfile.Smart, float confidence = 0.95f,
            ViewMode view = ViewMode.External, int warnings = 0, float bank = 0f, bool parkingBrake = false, bool hasFlaps = false,
            float distanceNm = 20f)
        {
            return new DisclosureInput
            {
                Profile = profile,
                View = view,
                Phase = new FlightPhaseEstimate { Phase = phase, Candidate = phase, Confidence = confidence, OnGround = FlightPhases.IsGroundPhase(phase) },
                WarningMask = warnings,
                OnGround = FlightPhases.IsGroundPhase(phase),
                BankDegrees = bank,
                HasRunway = true,
                DistanceToRunwayNm = distanceNm,
                Systems = new AircraftSystemsState { HasParkingBrake = true, ParkingBrakeSet = parkingBrake, HasFlaps = hasFlaps },
            };
        }

        static void Run(DisclosureController d, DisclosureInput input, float seconds)
        {
            int steps = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(seconds / Dt));
            for (int i = 0; i < steps; i++) d.Update(input, Dt);
        }

        static IEnumerable<FlightPhase> AllPhases()
        {
            for (int i = 0; i < FlightPhases.Count; i++) yield return (FlightPhase)i;
        }

        [TestCaseSource(nameof(AllPhases))]
        public void Smart_ShowsTheTableForEachPhase(FlightPhase phase)
        {
            var d = new DisclosureController();
            d.Update(Input(phase), Dt);
            foreach (var element in DataElements)
            {
                var expected = DisclosureController.SmartShows(element, phase) ? Prominence.Full : Prominence.Hidden;
                Assert.AreEqual(expected, d.GetProminence(element), element.ToString());
            }
            Assert.AreEqual(phase == FlightPhase.Approach || phase == FlightPhase.Landing, d.IsVisible(HudElement.ApproachGuide));
        }

        [Test]
        public void Smart_UncertainPhase_ShowsSafeEssentials()
        {
            var d = new DisclosureController();
            d.Update(Input(FlightPhase.Cruise, confidence: 0.2f), Dt);
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.Airspeed));
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.Altitude));
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.VerticalSpeed));
            Assert.AreEqual(DisclosureReason.UncertainPhase, d.GetReason(HudElement.Airspeed));
        }

        [Test]
        public void Smart_StableCruise_RecedesThenComesBack()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.Cruise), 5f);
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.Heading));
            Assert.IsFalse(d.IsStableFlight);

            Run(d, Input(FlightPhase.Cruise), 6f);
            Assert.IsTrue(d.IsStableFlight);
            Assert.AreEqual(Prominence.Quiet, d.GetProminence(HudElement.Airspeed));
            Assert.AreEqual(Prominence.Quiet, d.GetProminence(HudElement.Altitude));
            Assert.AreEqual(Prominence.Hidden, d.GetProminence(HudElement.Heading));

            // A turn ends stable flight: information comes back at once.
            d.Update(Input(FlightPhase.Cruise, bank: 25f), Dt);
            Assert.IsFalse(d.IsStableFlight);
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.Airspeed));
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.Heading));
        }

        [Test]
        public void Cinematic_HidesRoutineInformationInExternalView()
        {
            var d = new DisclosureController(new DisclosureSettings { cockpitHasInstruments = false });
            d.Update(Input(FlightPhase.Approach, HudProfile.Cinematic), Dt);
            foreach (var element in DataElements) Assert.IsFalse(d.IsVisible(element), element.ToString());
            Assert.IsFalse(d.IsVisible(HudElement.ApproachGuide));
            for (int i = 0; i < ContextActions.Count; i++) Assert.IsFalse(d.IsActionVisible((ContextAction)i));

            // A cockpit without instruments: the pilot still gets the Smart information.
            d.Update(Input(FlightPhase.Approach, HudProfile.Cinematic, view: ViewMode.Cockpit), Dt);
            Assert.IsTrue(d.IsVisible(HudElement.Airspeed));
        }

        [TestCase(HudProfile.Smart)]
        [TestCase(HudProfile.Detailed)]
        [TestCase(HudProfile.Cinematic)]
        public void CockpitWithInstruments_HidesDuplicatedFlightData(HudProfile profile)
        {
            var d = new DisclosureController(new DisclosureSettings { cockpitHasInstruments = true });
            d.Update(Input(FlightPhase.Climb, profile, view: ViewMode.Cockpit), Dt);
            foreach (var element in DataElements) Assert.IsFalse(d.IsVisible(element), element.ToString());
            Assert.AreEqual(DisclosureReason.InstrumentsInView, d.GetReason(HudElement.Airspeed));
        }

        [Test]
        public void CockpitView_InstrumentsReplaceHudFlightDataAndTheNavigationGuide()
        {
            var d = new DisclosureController(); // the cockpit has instruments by default
            d.Update(Input(FlightPhase.Cruise, HudProfile.Detailed, view: ViewMode.Cockpit), Dt);
            foreach (var element in DataElements) Assert.IsFalse(d.IsVisible(element), element.ToString());
            Assert.IsFalse(d.IsVisible(HudElement.NavigationGuide), "the GPS shows the way");
            Assert.AreEqual(DisclosureReason.InstrumentsInView, d.GetReason(HudElement.NavigationGuide));

            d.Update(Input(FlightPhase.Approach, HudProfile.Smart, view: ViewMode.Cockpit, distanceNm: 3f), Dt);
            Assert.IsTrue(d.IsVisible(HudElement.ApproachGuide), "the panel has no glide path indicator: the guide stays");

            int stall = FlightWarnings.Bit(FlightWarning.Stall);
            d.Update(Input(FlightPhase.Approach, HudProfile.Smart, view: ViewMode.Cockpit, warnings: stall, distanceNm: 3f), Dt);
            Assert.IsTrue(d.IsVisible(HudElement.Airspeed), "a warning still brings its information up");
        }

        [Test]
        public void Warning_OverridesCinematic()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.Cruise, HudProfile.Cinematic), 2f);
            Assert.IsFalse(d.IsVisible(HudElement.Airspeed));

            int stall = FlightWarnings.Bit(FlightWarning.Stall);
            d.Update(Input(FlightPhase.Cruise, HudProfile.Cinematic, warnings: stall), Dt);
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.Airspeed), "shown at once");
            Assert.AreEqual(DisclosureReason.Warning, d.GetReason(HudElement.Airspeed));

            int sink = FlightWarnings.Bit(FlightWarning.SinkRate);
            d.Update(Input(FlightPhase.Approach, HudProfile.Cinematic, warnings: sink), Dt);
            Assert.IsTrue(d.IsVisible(HudElement.VerticalSpeed));
            Assert.IsTrue(d.IsVisible(HudElement.Altitude));

            // After the warning: kept briefly (no flicker), then hidden again.
            Run(d, Input(FlightPhase.Cruise, HudProfile.Cinematic), 1f);
            Assert.IsTrue(d.IsVisible(HudElement.Airspeed));
            Run(d, Input(FlightPhase.Cruise, HudProfile.Cinematic), 2f);
            Assert.IsFalse(d.IsVisible(HudElement.Airspeed));
        }

        [Test]
        public void SwitchingProfiles_InFlight_AppliesAtOnce()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.Climb, HudProfile.Smart), 3f);
            Assert.IsFalse(d.IsVisible(HudElement.Heading));

            d.Update(Input(FlightPhase.Climb, HudProfile.Detailed), Dt);
            foreach (var element in DataElements) Assert.AreEqual(Prominence.Full, d.GetProminence(element), element.ToString());
            Assert.IsTrue(d.IsActionVisible(ContextAction.ParkingBrake), "Detailed: every available system");
            Assert.IsFalse(d.IsActionEnabled(ContextAction.ParkingBrake), "but the parking brake cannot be set in the air");
            Assert.IsTrue(d.IsActionVisible(ContextAction.Navigation));

            d.Update(Input(FlightPhase.Climb, HudProfile.Cinematic), Dt);
            foreach (var element in DataElements) Assert.IsFalse(d.IsVisible(element), element.ToString());
            Assert.IsFalse(d.IsActionVisible(ContextAction.Navigation));

            d.Update(Input(FlightPhase.Climb, HudProfile.Smart), Dt);
            Assert.IsTrue(d.IsVisible(HudElement.Airspeed));
            Assert.IsTrue(d.IsVisible(HudElement.VerticalSpeed));
            Assert.IsFalse(d.IsVisible(HudElement.Heading));
        }

        [Test]
        public void BriefLossOfRelevance_DoesNotFlicker()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.Climb), 3f);
            var visible = new List<bool>();
            for (int i = 0; i < 50; i++) { d.Update(Input(FlightPhase.Cruise), Dt); visible.Add(d.IsVisible(HudElement.VerticalSpeed)); }
            for (int i = 0; i < 50; i++) { d.Update(Input(FlightPhase.Climb), Dt); visible.Add(d.IsVisible(HudElement.VerticalSpeed)); }
            CollectionAssert.DoesNotContain(visible, false, "vertical speed stays up through a one-second blip");
            Assert.IsTrue(d.IsHeld(HudElement.VerticalSpeed) == false);
        }

        [Test]
        public void Elements_StayForTheMinimumVisibleTime()
        {
            var settings = new DisclosureSettings { hideDelaySeconds = 0.5f, minimumVisibleSeconds = 2f };
            var d = new DisclosureController(settings);
            Run(d, Input(FlightPhase.Cruise), 1f);
            d.Update(Input(FlightPhase.Climb), Dt); // vertical speed appears
            Assert.IsTrue(d.IsVisible(HudElement.VerticalSpeed));
            Run(d, Input(FlightPhase.Cruise), 1.5f);
            Assert.IsTrue(d.IsVisible(HudElement.VerticalSpeed), "hide delay over, minimum time not yet");
            Run(d, Input(FlightPhase.Cruise), 0.6f);
            Assert.IsFalse(d.IsVisible(HudElement.VerticalSpeed));
        }

        [Test]
        public void Smart_ContextualActionsFollowThePhase()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.Parked), 3f);
            Assert.IsTrue(d.IsActionVisible(ContextAction.ParkingBrake));
            Assert.IsTrue(d.IsActionEnabled(ContextAction.ParkingBrake));
            Assert.IsFalse(d.IsActionVisible(ContextAction.Navigation));

            Run(d, Input(FlightPhase.Cruise), 3f);
            Assert.IsFalse(d.IsActionVisible(ContextAction.ParkingBrake));
            Assert.IsTrue(d.IsActionVisible(ContextAction.Navigation));
            Assert.IsFalse(d.IsActionVisible(ContextAction.ApproachGuide), "far from the airfield");

            Run(d, Input(FlightPhase.Approach, distanceNm: 4f), 3f);
            Assert.IsTrue(d.IsActionVisible(ContextAction.ApproachGuide));
            Assert.IsTrue(d.IsActionEngaged(ContextAction.ApproachGuide), "the guide is on, so its action can turn it off");
            Assert.IsFalse(d.IsActionVisible(ContextAction.Navigation));
            Assert.IsFalse(d.IsActionVisible(ContextAction.Flaps), "no flaps on this aircraft");
        }

        [Test]
        public void Flaps_OfferedForTakeoffAndLanding_WhenTheAircraftHasThem()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.TakeoffRoll, hasFlaps: true), 1f);
            Assert.IsTrue(d.IsActionVisible(ContextAction.Flaps));
            Run(d, Input(FlightPhase.Cruise, hasFlaps: true), 3f);
            Assert.IsFalse(d.IsActionVisible(ContextAction.Flaps));
            Run(d, Input(FlightPhase.Approach, hasFlaps: true, distanceNm: 3f), 1f);
            Assert.IsTrue(d.IsActionVisible(ContextAction.Flaps));
        }

        [Test]
        public void EngagedSystem_NeverHidden_EvenInCinematic()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.Parked, HudProfile.Cinematic, parkingBrake: true), 3f);
            Assert.IsTrue(d.IsActionVisible(ContextAction.ParkingBrake));
            Assert.IsTrue(d.IsActionEngaged(ContextAction.ParkingBrake));
            Assert.AreEqual(DisclosureReason.Engaged, d.GetActionReason(ContextAction.ParkingBrake));

            Run(d, Input(FlightPhase.Parked, HudProfile.Cinematic, parkingBrake: false), 3f);
            Assert.IsFalse(d.IsActionVisible(ContextAction.ParkingBrake), "released: back to the profile's rule");
        }

        [Test]
        public void SystemsRail_GivesManualAccessInEveryProfile()
        {
            var d = new DisclosureController();
            d.SetRailExpanded(true);
            foreach (HudProfile profile in new[] { HudProfile.Cinematic, HudProfile.Smart })
            {
                d.Update(Input(FlightPhase.Cruise, profile), Dt);
                Assert.IsTrue(d.IsActionVisible(ContextAction.ParkingBrake), profile.ToString());
                Assert.IsTrue(d.IsActionVisible(ContextAction.ApproachGuide), profile.ToString());
                Assert.IsTrue(d.IsActionVisible(ContextAction.Navigation), profile.ToString());
                Assert.IsFalse(d.IsActionVisible(ContextAction.Flaps), "unavailable systems are never offered");
            }
            d.ToggleRailExpanded();
            d.Update(Input(FlightPhase.Cruise, HudProfile.Cinematic), Dt);
            Assert.IsFalse(d.IsActionVisible(ContextAction.Navigation), "closing SYSTEMS applies at once");
            Assert.IsTrue(DisclosureController.SystemsToggleUseful(HudProfile.Smart));
            Assert.IsFalse(DisclosureController.SystemsToggleUseful(HudProfile.Detailed));
        }

        [Test]
        public void HidingAGuide_LastsUntilItStopsBeingRelevant()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.Approach, distanceNm: 3f), 1f);
            Assert.IsTrue(d.IsVisible(HudElement.ApproachGuide));

            d.ToggleGuide(HudElement.ApproachGuide);
            d.Update(Input(FlightPhase.Approach, distanceNm: 3f), Dt);
            Assert.IsFalse(d.IsVisible(HudElement.ApproachGuide), "the player's choice applies at once");
            Assert.AreEqual(DisclosureReason.PlayerHidden, d.GetReason(HudElement.ApproachGuide));
            Assert.IsTrue(d.IsActionVisible(ContextAction.ApproachGuide), "and the action to bring it back stays");
            Run(d, Input(FlightPhase.Landing, distanceNm: 1f), 3f);
            Assert.IsFalse(d.IsVisible(HudElement.ApproachGuide));

            Run(d, Input(FlightPhase.InitialClimb, distanceNm: 1f), 3f); // go-around
            Assert.AreEqual(PlayerOverride.None, d.GetOverride(HudElement.ApproachGuide));
            Run(d, Input(FlightPhase.Approach, distanceNm: 3f), 1f);
            Assert.IsTrue(d.IsVisible(HudElement.ApproachGuide), "back on the next approach");
        }

        [Test]
        public void ShowingAGuide_LastsUntilThePlayerHidesIt()
        {
            var d = new DisclosureController();
            Run(d, Input(FlightPhase.Cruise), 1f);
            Assert.IsFalse(d.IsVisible(HudElement.NavigationGuide));

            d.ToggleGuide(HudElement.NavigationGuide);
            d.Update(Input(FlightPhase.Cruise), Dt);
            Assert.IsTrue(d.IsVisible(HudElement.NavigationGuide));
            Run(d, Input(FlightPhase.Descent), 10f);
            Assert.IsTrue(d.IsVisible(HudElement.NavigationGuide));
            Assert.IsTrue(d.IsActionVisible(ContextAction.Navigation));

            d.ToggleGuide(HudElement.NavigationGuide);
            d.Update(Input(FlightPhase.Descent), Dt);
            Assert.IsFalse(d.IsVisible(HudElement.NavigationGuide));
        }

        [Test]
        public void Detailed_ShowsEverythingAvailable()
        {
            var d = new DisclosureController();
            d.Update(Input(FlightPhase.Descent, HudProfile.Detailed, distanceNm: 5f), Dt);
            foreach (var element in DataElements) Assert.AreEqual(Prominence.Full, d.GetProminence(element));
            Assert.IsTrue(d.IsVisible(HudElement.ApproachGuide), "within range of the runway");
            Assert.IsTrue(d.IsVisible(HudElement.NavigationGuide));
            for (int i = 0; i < ContextActions.Count; i++)
            {
                var action = (ContextAction)i;
                // Every available action; this input has no flaps and no assistance settings.
                bool available = action != ContextAction.Flaps && action != ContextAction.Assistance;
                Assert.AreEqual(available, d.IsActionVisible(action), action.ToString());
            }
        }
    }
}
