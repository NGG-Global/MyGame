using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The top-left flight information cluster as data (docs/TAP_DISCLOSURE_DESIGN.md, milestone
    /// 4): its levels, what the capsule shows in each profile, phase, view and warning (from the
    /// real <see cref="DisclosureController"/>), the guides, the readouts, and its geometry at
    /// 4:3, 16:10, 16:9, 19.5:9 and 20:9 at every interface scale the game allows.
    /// </summary>
    public class FlightInfoTests
    {
        const float Dt = 0.02f;
        const float Height = 1080f;

        static readonly HudProfile[] Profiles = { HudProfile.Cinematic, HudProfile.Smart, HudProfile.Detailed };

        static DisclosureInput Input(FlightPhase phase, HudProfile profile = HudProfile.Smart, ViewMode view = ViewMode.External, int warnings = 0,
            float distanceNm = 20f, float confidence = 0.95f)
        {
            return new DisclosureInput
            {
                Profile = profile,
                View = view,
                Phase = new FlightPhaseEstimate { Phase = phase, Candidate = phase, Confidence = confidence, OnGround = FlightPhases.IsGroundPhase(phase) },
                WarningMask = warnings,
                OnGround = FlightPhases.IsGroundPhase(phase),
                HasRunway = true,
                DistanceToRunwayNm = distanceNm,
                HasNavigationTarget = true,
                Systems = new AircraftSystemsState { HasParkingBrake = true, HasFlaps = true },
            };
        }

        static void Run(DisclosureController d, DisclosureInput input, float seconds)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
            for (int i = 0; i < steps; i++) d.Update(input, Dt);
        }

        /// <summary>Composes the model from a controller that has seen one input, with a destination and a runway.</summary>
        static FlightInfoModel Composed(DisclosureController d, DisclosureInput input, FlightInfoModel model = null)
        {
            model = model ?? new FlightInfoModel();
            d.Update(input, Dt);
            model.Compose(d, input.WarningMask, true, true, d.ApproachGuideInRange(input.OnGround, input.DistanceToRunwayNm));
            return model;
        }

        static HudElement[] Capsule(FlightInfoModel m)
        {
            var list = new HudElement[m.CapsuleCount];
            for (int i = 0; i < list.Length; i++) list[i] = m.CapsuleElement(i);
            return list;
        }

        static readonly int Stall = FlightWarnings.Bit(FlightWarning.Stall);
        static readonly int SinkRate = FlightWarnings.Bit(FlightWarning.SinkRate);

        // ---- Levels ----------------------------------------------------------------------------------

        [Test]
        public void StartsCollapsed_TapsExpandFocusAndGoBack_AndTheTriggerFoldsEverything()
        {
            var m = new FlightInfoModel();
            Assert.AreEqual(FlightInfoLevel.Collapsed, m.Level);
            Assert.AreEqual(FlightInfoModel.NoElement, m.Focus);
            Assert.AreEqual(FlightInfoLevel.Collapsed, m.TapTile(HudElement.Airspeed), "a collapsed cluster has no tiles to tap");

            Assert.IsTrue(m.Expand());
            Assert.AreEqual(FlightInfoLevel.Expanded, m.Level);
            Assert.IsFalse(m.Expand(), "already open");

            Assert.AreEqual(FlightInfoLevel.Focused, m.TapTile(HudElement.Altitude));
            Assert.AreEqual(HudElement.Altitude, m.Focus);
            Assert.AreEqual(FlightInfoLevel.Focused, m.TapTile(HudElement.Heading), "another tile moves the focus");
            Assert.AreEqual(HudElement.Heading, m.Focus);
            Assert.AreEqual(FlightInfoLevel.Expanded, m.TapTile(HudElement.Heading), "the focused tile again goes back to the panel");
            Assert.AreEqual(FlightInfoModel.NoElement, m.Focus);

            m.FocusOn(HudElement.Groundspeed);
            m.Unfocus();
            Assert.AreEqual(FlightInfoLevel.Expanded, m.Level, "the detail goes back one level");

            m.FocusOn(HudElement.Airspeed);
            Assert.AreEqual(FlightInfoLevel.Collapsed, m.TapTrigger(), "the trigger folds everything, even from a focus");
            Assert.AreEqual(FlightInfoModel.NoElement, m.Focus);
            Assert.AreEqual(FlightInfoLevel.Expanded, m.TapTrigger(), "and opens a collapsed cluster");
        }

        [Test]
        public void Guides_CanBeFocusedOnlyWhileThePanelOffersThem()
        {
            var m = new FlightInfoModel();
            var d = new DisclosureController();
            Composed(d, Input(FlightPhase.Parked), m);
            m.Expand();
            Assert.IsTrue(m.PanelNavigation, "a destination exists");
            Assert.IsFalse(m.PanelApproach, "on the ground: no approach details");
            Assert.IsFalse(m.FocusOn(HudElement.ApproachGuide));
            Assert.IsTrue(m.FocusOn(HudElement.NavigationGuide));

            // The destination goes away (a mission cleared it): the focus goes back to the panel, the panel stays.
            d.Update(Input(FlightPhase.Parked), Dt);
            m.Compose(d, 0, false, true, false);
            Assert.AreEqual(FlightInfoLevel.Expanded, m.Level);
            Assert.IsFalse(m.PanelNavigation);
        }

        // ---- Profiles and expansion are independent -------------------------------------------------------

        [Test]
        public void EveryProfile_CollapsesAndExpandsIndependently_AndAProfileChangeNeverMovesTheLevel()
        {
            var d = new DisclosureController();
            var m = new FlightInfoModel();
            foreach (var profile in Profiles)
            {
                Composed(d, Input(FlightPhase.Cruise, profile), m);
                Assert.AreEqual(FlightInfoLevel.Collapsed, m.Level, profile + ": collapsed at rest");
                m.Expand();
                Composed(d, Input(FlightPhase.Cruise, profile), m);
                Assert.AreEqual(FlightInfoLevel.Expanded, m.Level, profile + ": expands");
                m.Collapse();
                Composed(d, Input(FlightPhase.Cruise, profile), m);
                Assert.AreEqual(FlightInfoLevel.Collapsed, m.Level, profile + ": collapses without changing the profile");
            }

            // Open in Detailed, switch to Cinematic and back: still open; collapsed in Detailed: still collapsed.
            m.Expand();
            foreach (var profile in Profiles)
            {
                Composed(d, Input(FlightPhase.Cruise, profile), m);
                Assert.AreEqual(FlightInfoLevel.Expanded, m.Level, "a profile change never folds the panel (" + profile + ")");
            }
            m.Collapse();
            foreach (var profile in Profiles)
            {
                Composed(d, Input(FlightPhase.Cruise, profile), m);
                Assert.AreEqual(FlightInfoLevel.Collapsed, m.Level, "a profile change never opens it (" + profile + ")");
            }
        }

        [Test]
        public void PhaseChangesAndWarnings_NeverExpandAPlayerCollapsedCluster_NorFoldAnOpenOne()
        {
            var phases = new[]
            {
                FlightPhase.Parked, FlightPhase.Taxi, FlightPhase.TakeoffRoll, FlightPhase.InitialClimb, FlightPhase.Climb, FlightPhase.Cruise,
                FlightPhase.Descent, FlightPhase.Approach, FlightPhase.Landing, FlightPhase.Rollout,
            };
            foreach (var profile in Profiles)
            {
                var d = new DisclosureController();
                var collapsed = new FlightInfoModel();
                collapsed.Expand();
                collapsed.Collapse(); // the player folded it
                var open = new FlightInfoModel();
                open.Expand();
                open.FocusOn(HudElement.Airspeed);
                foreach (var phase in phases)
                {
                    int warnings = FlightPhases.IsGroundPhase(phase) ? 0 : Stall | SinkRate;
                    for (int i = 0; i < 50; i++)
                    {
                        var input = Input(phase, profile, warnings: i < 25 ? 0 : warnings, distanceNm: 3f);
                        d.Update(input, Dt);
                        collapsed.Compose(d, input.WarningMask, true, true, d.ApproachGuideInRange(input.OnGround, 3f));
                        open.Compose(d, input.WarningMask, true, true, d.ApproachGuideInRange(input.OnGround, 3f));
                    }
                    Assert.AreEqual(FlightInfoLevel.Collapsed, collapsed.Level, $"{profile} / {phase}: stays collapsed");
                    Assert.AreEqual(FlightInfoLevel.Focused, open.Level, $"{profile} / {phase}: an open cluster stays open");
                    Assert.AreEqual(HudElement.Airspeed, open.Focus);
                }
            }
        }

        // ---- What the capsule shows -----------------------------------------------------------------------

        [Test]
        public void Smart_CapsuleShowsTheEssentialsOfThePhase_InTheFixedOrder()
        {
            var d = new DisclosureController();
            CollectionAssert.AreEqual(new HudElement[0], Capsule(Composed(d, Input(FlightPhase.Parked))), "parked: just the trigger");
            d = new DisclosureController();
            CollectionAssert.AreEqual(new[] { HudElement.Groundspeed }, Capsule(Composed(d, Input(FlightPhase.Taxi))));
            d = new DisclosureController();
            CollectionAssert.AreEqual(new[] { HudElement.Airspeed }, Capsule(Composed(d, Input(FlightPhase.TakeoffRoll))));
            d = new DisclosureController();
            CollectionAssert.AreEqual(new[] { HudElement.Airspeed, HudElement.Altitude, HudElement.VerticalSpeed }, Capsule(Composed(d, Input(FlightPhase.Climb))));
            d = new DisclosureController();
            CollectionAssert.AreEqual(new[] { HudElement.Airspeed, HudElement.Altitude, HudElement.Heading }, Capsule(Composed(d, Input(FlightPhase.Cruise))));
            d = new DisclosureController();
            CollectionAssert.AreEqual(new[] { HudElement.Airspeed, HudElement.Altitude, HudElement.VerticalSpeed }, Capsule(Composed(d, Input(FlightPhase.Approach, distanceNm: 3f))));
        }

        [Test]
        public void Smart_StableCruise_IsQuieter()
        {
            var d = new DisclosureController();
            var m = new FlightInfoModel();
            Run(d, Input(FlightPhase.Cruise), d.Settings.stableFlightSeconds + d.Settings.hideDelaySeconds + 2f);
            Composed(d, Input(FlightPhase.Cruise), m);
            Assert.IsTrue(d.IsStableFlight);
            CollectionAssert.AreEqual(new[] { HudElement.Airspeed, HudElement.Altitude }, Capsule(m));
            Assert.AreEqual(Prominence.Quiet, m.CapsuleProminence(0));
            Assert.AreEqual(Prominence.Quiet, m.CapsuleProminence(1));
        }

        [Test]
        public void Detailed_KeepsTheCapsuleCompact_MoreOnExpansion()
        {
            var d = new DisclosureController();
            var m = Composed(d, Input(FlightPhase.Cruise, HudProfile.Detailed));
            Assert.AreEqual(FlightInfoModel.MaxCapsuleReadings, m.CapsuleCount, "never more than three readings at rest");
            CollectionAssert.AreEqual(new[] { HudElement.Airspeed, HudElement.Altitude, HudElement.VerticalSpeed }, Capsule(m));
            for (int i = 0; i < FlightReadouts.ReadingCount; i++)
                Assert.IsTrue(m.CanFocus((HudElement)i), "every reading is on the panel");
            Assert.That(FlightInfoLayout.MaxCapsuleWidth, Is.LessThan(FlightInfoLayout.Panel.width), "the capsule is smaller than the panel");
        }

        [Test]
        public void Cinematic_IsTheQuietestRest_JustTheTrigger()
        {
            var d = new DisclosureController();
            var m = Composed(d, Input(FlightPhase.Cruise, HudProfile.Cinematic));
            Assert.AreEqual(0, m.CapsuleCount);
            Assert.AreEqual(FlightInfoModel.NoElement, m.Chip, "no guide unless the player asks for one");
        }

        [Test]
        public void Warnings_SurfaceTheirReadingInTheCollapsedCapsule_InEveryProfileAndView()
        {
            foreach (var profile in Profiles)
                foreach (var view in new[] { ViewMode.External, ViewMode.Cockpit })
                {
                    var d = new DisclosureController();
                    var m = new FlightInfoModel();
                    Composed(d, Input(FlightPhase.Cruise, profile, view, Stall), m);
                    Assert.AreEqual(FlightInfoLevel.Collapsed, m.Level, "a warning never opens the cluster");
                    Assert.GreaterOrEqual(m.CapsuleCellOf(HudElement.Airspeed), 0, $"{profile} / {view}: the stall surfaces the airspeed");
                    Assert.IsTrue(m.IsAlert(HudElement.Airspeed), "in the alert colour");
                    Assert.AreEqual(Prominence.Full, m.CapsuleProminence(m.CapsuleCellOf(HudElement.Airspeed)));
                }
        }

        [Test]
        public void Warnings_ComeFirst_WhenTheCapsuleIsFull()
        {
            var d = new DisclosureController();
            var m = new FlightInfoModel(new FlightInfoSettings { capsuleReadings = 1 });
            Composed(d, Input(FlightPhase.Cruise, HudProfile.Detailed, warnings: SinkRate), m);
            CollectionAssert.AreEqual(new[] { HudElement.Altitude }, Capsule(m), "the sink rate's altitude before the routine airspeed");
            m = new FlightInfoModel(new FlightInfoSettings { capsuleReadings = 2 });
            Composed(d, Input(FlightPhase.Cruise, HudProfile.Detailed, warnings: SinkRate), m);
            CollectionAssert.AreEqual(new[] { HudElement.Altitude, HudElement.VerticalSpeed }, Capsule(m));
            Assert.IsFalse(m.IsAlert(HudElement.Airspeed));
        }

        [Test]
        public void Cockpit_AvoidsDuplicatingTheInstruments_ButKeepsWhatTheyLack()
        {
            foreach (var profile in Profiles)
            {
                var d = new DisclosureController();
                var m = Composed(d, Input(FlightPhase.Cruise, profile, ViewMode.Cockpit));
                Assert.AreEqual(0, m.CapsuleCount, profile + ": the instruments show the readings");
                Assert.AreEqual(FlightInfoModel.NoElement, m.Chip, profile + ": the GPS map carries the way to the airfield");
                d.ToggleGuide(HudElement.NavigationGuide); // the player asks for it: their choice wins, even over the GPS
                Composed(d, Input(FlightPhase.Cruise, profile, ViewMode.Cockpit), m);
                Assert.AreEqual(HudElement.NavigationGuide, m.Chip, profile + ": a requested guide is never dropped by the view");
                Assert.AreEqual(0, m.CapsuleCount, profile + ": still no duplicated readings");
            }
            // The panel has no glide path indicator: on approach the capsule keeps the approach chip in the cockpit.
            var smart = new DisclosureController();
            var approach = Composed(smart, Input(FlightPhase.Approach, HudProfile.Smart, ViewMode.Cockpit, distanceNm: 3f));
            Assert.AreEqual(0, approach.CapsuleCount);
            Assert.AreEqual(HudElement.ApproachGuide, approach.Chip);
            // A mission's checkpoint is not on the GPS: it stays.
            var mission = new DisclosureController();
            var input = Input(FlightPhase.Cruise, HudProfile.Smart, ViewMode.Cockpit);
            input.MissionGuides = HudElements.Bit(HudElement.NavigationGuide);
            Assert.AreEqual(HudElement.NavigationGuide, Composed(mission, input).Chip);
        }

        // ---- Guides and navigation ------------------------------------------------------------------------

        [Test]
        public void Chip_ShowsTheApproachWhenShown_OtherwiseTheRequestedNavigation()
        {
            var d = new DisclosureController();
            var m = Composed(d, Input(FlightPhase.Cruise));
            Assert.AreEqual(FlightInfoModel.NoElement, m.Chip, "Smart offers navigation on request");
            d.ToggleGuide(HudElement.NavigationGuide);
            Composed(d, Input(FlightPhase.Cruise), m);
            Assert.AreEqual(HudElement.NavigationGuide, m.Chip, "requested: the capsule carries it");

            Composed(d, Input(FlightPhase.Approach, distanceNm: 3f), m);
            Assert.AreEqual(HudElement.ApproachGuide, m.Chip, "on approach the runway's lights take the chip");
            Assert.IsTrue(d.IsVisible(HudElement.NavigationGuide), "the navigation guide is still on");
            Assert.IsTrue(m.PanelNavigation && m.PanelApproach, "the panel shows both");

            // No destination, no navigation chip.
            var none = new DisclosureController();
            var n = new FlightInfoModel();
            none.Update(Input(FlightPhase.Cruise), Dt);
            none.ToggleGuide(HudElement.NavigationGuide);
            none.Update(Input(FlightPhase.Cruise), Dt);
            n.Compose(none, 0, false, true, false);
            Assert.AreEqual(FlightInfoModel.NoElement, n.Chip);
            Assert.IsFalse(n.PanelNavigation);
        }

        [Test]
        public void NavigationRequested_PersistsThroughExpandingAndCollapsing()
        {
            var d = new DisclosureController();
            var m = Composed(d, Input(FlightPhase.Cruise));
            d.ToggleGuide(HudElement.NavigationGuide);
            for (int round = 0; round < 3; round++)
            {
                m.Expand();
                Composed(d, Input(FlightPhase.Cruise), m);
                m.FocusOn(HudElement.NavigationGuide);
                Composed(d, Input(FlightPhase.Cruise), m);
                m.Collapse();
                Composed(d, Input(FlightPhase.Cruise), m);
                Assert.AreEqual(PlayerOverride.Show, d.GetOverride(HudElement.NavigationGuide), "collapsing never touches the player's choice");
                Assert.IsTrue(d.IsVisible(HudElement.NavigationGuide));
                Assert.AreEqual(HudElement.NavigationGuide, m.Chip, "and the capsule keeps showing it");
            }
        }

        [Test]
        public void Panel_OffersTheApproach_WhenShownOrAirborneWithinRange()
        {
            var d = new DisclosureController();
            Assert.IsFalse(Composed(d, Input(FlightPhase.Cruise, distanceNm: 20f)).PanelApproach, "far away");
            Assert.IsTrue(Composed(d, Input(FlightPhase.Cruise, distanceNm: 6f)).PanelApproach, "within the guide's range, airborne");
            Assert.IsFalse(Composed(d, Input(FlightPhase.Taxi, distanceNm: 0.2f)).PanelApproach, "on the ground");
            var m = new FlightInfoModel();
            d.Update(Input(FlightPhase.Cruise, distanceNm: 6f), Dt);
            m.Compose(d, 0, true, false, true);
            Assert.IsFalse(m.PanelApproach, "no runway, no approach");
            Assert.IsTrue(d.ApproachGuideInRange(false, d.Settings.approachGuideRangeNm));
            Assert.IsFalse(d.ApproachGuideInRange(true, 1f));
        }

        [Test]
        public void Settings_AreSanitised()
        {
            var s = new FlightInfoSettings { capsuleReadings = 9, quietAlpha = float.NaN, valueRefreshSeconds = -1f }.Sanitized();
            Assert.AreEqual(FlightInfoModel.MaxCapsuleReadings, s.capsuleReadings);
            Assert.AreEqual(0.55f, s.quietAlpha, 1e-5f);
            Assert.That(s.valueRefreshSeconds, Is.InRange(0.02f, 0.5f));
            Assert.AreEqual(0, new FlightInfoSettings { capsuleReadings = -3 }.Sanitized().capsuleReadings);
        }

        // ---- Readouts ----------------------------------------------------------------------------------------

        [Test]
        public void Readouts_RoundEachReadingToItsStep_AndCacheTheText()
        {
            var t = new FlightTelemetry
            {
                IndicatedAirspeedKnots = 111.6f, AltitudeFeet = 2403f, VerticalSpeedFeetPerMinute = 512f, HeadingDegrees = 0.2f, GroundspeedKnots = float.NaN,
            };
            Assert.AreEqual("112", FlightReadouts.Text(HudElement.Airspeed, FlightReadouts.Quantise(HudElement.Airspeed, t)));
            Assert.AreEqual("2400", FlightReadouts.Text(HudElement.Altitude, FlightReadouts.Quantise(HudElement.Altitude, t)));
            Assert.AreEqual("+500", FlightReadouts.Text(HudElement.VerticalSpeed, FlightReadouts.Quantise(HudElement.VerticalSpeed, t)));
            Assert.AreEqual("360", FlightReadouts.Text(HudElement.Heading, FlightReadouts.Quantise(HudElement.Heading, t)), "north is 360");
            Assert.AreEqual("0", FlightReadouts.Text(HudElement.Groundspeed, FlightReadouts.Quantise(HudElement.Groundspeed, t)), "a bad sample shows 0");
            Assert.AreEqual(FlightReadouts.NoValue, FlightReadouts.Quantise(HudElement.NavigationGuide, t));
            Assert.AreSame(FlightReadouts.Text(HudElement.Airspeed, 112), FlightReadouts.Text(HudElement.Airspeed, 112), "cached: no allocation");
            Assert.AreEqual(350, FlightReadouts.CompassDegrees(-10f));
            Assert.AreEqual(10, FlightReadouts.Tenths(1852f));
            Assert.AreEqual("1.0 NM", FlightReadouts.NauticalMiles(FlightReadouts.Tenths(1852f)));
            Assert.AreEqual("RWY 09", FlightReadouts.Runway(9));
            Assert.AreEqual("184°", FlightReadouts.Bearing(184));
            Assert.AreEqual("IAS KT", FlightReadouts.Label(HudElement.Airspeed));
            Assert.AreEqual(-1, FlightReadouts.PapiWhite(default, new DisclosureSettings()), "no glide path: lights off");
        }

        // ---- Geometry ------------------------------------------------------------------------------------------

        [Test]
        public void Trigger_HasOneHome_InTheCapsuleAndThePanel()
        {
            var trigger = FlightInfoLayout.TriggerHit;
            Assert.That(trigger.width, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget));
            Assert.That(trigger.height, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget));
            for (int n = 0; n <= FlightInfoModel.MaxCapsuleReadings; n++)
                foreach (bool chip in new[] { false, true })
                {
                    var hit = FlightInfoLayout.CapsuleHit(n, chip);
                    Assert.AreEqual(trigger.xMin, hit.xMin, 1e-3f, "the capsule grows from the corner, never moves");
                    Assert.AreEqual(trigger.yMax, hit.yMax, 1e-3f);
                    Assert.That(hit.width, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget));
                    Assert.AreEqual(HudStyle.MinTouchTarget, hit.height, 1e-3f);
                    AssertInside(FlightInfoLayout.CapsuleVisual(n, chip), hit, "the pill inside its touch target");
                    if (chip) AssertInside(FlightInfoLayout.CapsuleChip(n), FlightInfoLayout.CapsuleVisual(n, true), "the chip inside the pill");
                    for (int c = 0; c < n; c++) AssertInside(FlightInfoLayout.CapsuleCell(c), FlightInfoLayout.CapsuleVisual(n, chip), "cell " + c);
                }
            // The trigger's symbol sits at the same point whether the capsule or the panel shows it.
            Assert.AreEqual(FlightInfoLayout.CapsuleTrigger.center.y, trigger.center.y, 1e-3f);
            Assert.AreEqual(FlightInfoLayout.CapsuleTrigger.center.x, trigger.xMin + FlightInfoLayout.CapsuleVisualHeight * 0.5f, 1e-3f);
            AssertInside(trigger, FlightInfoLayout.Panel, "the panel opens around the trigger");
        }

        [Test]
        public void Panel_TilesFollowTheCapsuleOrder_AsFullTargets_WithoutOverlap()
        {
            var panel = FlightInfoLayout.Panel;
            var targets = new List<Rect> { FlightInfoLayout.TriggerHit };
            for (int i = 0; i < FlightReadouts.ReadingCount; i++)
            {
                var tile = FlightInfoLayout.Tile(i);
                if (i > 0) Assert.That(tile.xMin, Is.GreaterThan(FlightInfoLayout.Tile(i - 1).xMax - 1e-3f), "left to right in the fixed order");
                targets.Add(tile);
            }
            targets.Add(FlightInfoLayout.RegionA);
            targets.Add(FlightInfoLayout.RegionB);
            for (int i = 0; i < targets.Count; i++)
            {
                Assert.That(targets[i].height, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "target " + i);
                Assert.That(targets[i].width, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "target " + i);
                AssertInside(targets[i], panel, "target " + i);
                for (int j = i + 1; j < targets.Count; j++) Assert.IsFalse(targets[i].Overlaps(targets[j]), $"targets {i} and {j}");
            }
            AssertInside(FlightInfoLayout.RegionA, FlightInfoLayout.LowerRow, "A");
            AssertInside(FlightInfoLayout.RegionB, FlightInfoLayout.LowerRow, "B");
            Assert.That(FlightInfoLayout.Tile(0).yMin, Is.GreaterThanOrEqualTo(FlightInfoLayout.LowerRow.yMax), "the lower row under the tiles");
            // Values must fit: the widest common readings at the tile's value size (built-in font, about 0.56 em a digit).
            Assert.That(4f * 0.56f * HudStyle.ValueSize + 0.6f * HudStyle.ValueSize, Is.LessThanOrEqualTo(FlightInfoLayout.TileWidth - 12f), "\"+1500\" fits a tile");
            Assert.That(5f * 0.56f * HudStyle.ValueSize, Is.LessThanOrEqualTo(FlightInfoLayout.TileWidth - 12f), "\"12000\" fits a tile");
            Assert.That(5f * 0.56f * HudStyle.TitleSize, Is.LessThanOrEqualTo(FlightInfoLayout.CellWidth - 12f), "\"12000\" fits a capsule cell");
        }

        static IEnumerable<TestCaseData> Screens()
        {
            foreach (float aspect in new[] { 4f / 3f, 16f / 10f, 16f / 9f, 19.5f / 9f, 20f / 9f })
                foreach (float requested in new[] { 0.9f, 1f, 1.1f })
                    yield return new TestCaseData(aspect, requested).SetName($"Cluster_StaysClear_{aspect:0.00}_{requested:0.0}");
        }

        [TestCaseSource(nameof(Screens))]
        public void Cluster_OnEveryScreen_StaysOnScreen_AndClearOfFlightControlsWarningsAndTheRightHandControls(float aspect, float requested)
        {
            var layout = UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop, TouchControlsFactory.JoystickRegionTop);
            float scale = UiScaleMath.Effective(requested, aspect, layout);
            float height = Height / scale, width = aspect * height;
            var safe = new Rect(0f, 0f, width, height);
            string at = $" at aspect {aspect:0.00}, scale {scale:0.000}";

            var keepClear = KeepClear(width, height);
            var cluster = new List<Rect>
            {
                FlightInfoLayout.CapsuleHit(FlightInfoModel.MaxCapsuleReadings, true), FlightInfoLayout.TriggerHit, FlightInfoLayout.Panel,
                FlightInfoLayout.RegionA, FlightInfoLayout.RegionB,
            };
            for (int i = 0; i < FlightReadouts.ReadingCount; i++) cluster.Add(FlightInfoLayout.Tile(i));
            foreach (var local in cluster)
            {
                var r = FlightInfoLayout.InSafeArea(local, safe);
                Assert.IsTrue(r.xMin >= 0f && r.yMin >= 0f && r.xMax <= width && r.yMax <= height, "on screen" + at);
                foreach (var other in keepClear) Assert.IsFalse(r.Overlaps(other), $"{r} over {other}{at}");
            }
            // The command cluster's open panel and the flight information's open panel never meet (and are never open together).
            var info = FlightInfoLayout.InSafeArea(FlightInfoLayout.Panel, safe);
            var commands = CommandClusterLayout.InSafeArea(CommandClusterLayout.Panel, safe);
            Assert.IsFalse(info.Overlaps(commands), at);
            // Reach: the whole cluster sits in the top band, left of the command cluster's row.
            Assert.That(info.yMin, Is.GreaterThan(height * TouchControlsFactory.JoystickRegionTop), "above the joystick region" + at);
            Assert.That(info.xMax + HudStyle.Gap, Is.LessThanOrEqualTo(width - CommandClusterLayout.RowWidth + 1e-3f), "left of the top bar" + at);
        }

        [Test]
        public void ShorterSafeArea_StillKeepsTheOpenPanelAboveTheJoystickRegion()
        {
            var layout = UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop, TouchControlsFactory.JoystickRegionTop);
            foreach (float share in new[] { 1f, 0.97f, 0.93f, 0.9f })
                foreach (float aspect in new[] { 4f / 3f, 16f / 9f, 20f / 9f })
                {
                    float scale = UiScaleMath.Effective(1.1f, aspect, layout, share);
                    float safeHeight = Height * share / scale;
                    float joystickTopFromTop = safeHeight * (1f - TouchControlsFactory.JoystickRegionTop);
                    Assert.That(FlightInfoLayout.Bottom, Is.LessThan(joystickTopFromTop), $"share {share}, aspect {aspect:0.00}, scale {scale:0.000}");
                }
        }

        [Test]
        public void ScaleLimit_KnowsTheCluster_AndTheJoystickTopMirrorsTheTouchControls()
        {
            Assert.AreEqual(TouchControlsFactory.JoystickRegionTop, UiScaleMath.DefaultJoystickRegionTop, 1e-6f, "layers do not share code: keep the two equal");
            var layout = UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop);
            Assert.AreEqual(FlightInfoLayout.Bottom, layout.InfoClusterBottom, 1e-3f);
            Assert.AreEqual(FlightInfoLayout.Right, layout.InfoClusterRight, 1e-3f);
            Assert.AreEqual(CommandClusterLayout.RowWidth, layout.TopBarWidth, 1e-3f);
            Assert.That(FlightInfoLayout.Bottom, Is.LessThanOrEqualTo(212f + 1e-3f), "no deeper than the old guides");
            Assert.AreEqual(1.1f, UiScaleMath.Effective(1.1f, 20f / 9f, layout), 1e-3f, "wide phones keep 110 %");
        }

        [Test]
        public void TriggerSymbol_IsGenerated_AndDistinct()
        {
            var gauge = HudSprites.Icon(HudIcon.FlightData);
            Assert.IsNotNull(gauge);
            Assert.AreSame(gauge, HudSprites.Icon(HudIcon.FlightData), "generated once");
            Assert.AreNotSame(gauge, HudSprites.Icon(HudIcon.Navigation), "not the compass");
        }

        // ---- Helpers ------------------------------------------------------------------------------------------

        /// <summary>The flight controls, the handle, the warning slots and the command cluster's permanent controls, in canvas coordinates.</summary>
        static List<Rect> KeepClear(float width, float height)
        {
            var throttle = Rect.MinMaxRect(width + TouchControlsFactory.ThrottleOffset.x - TouchControlsFactory.ThrottleAreaSize.x, TouchControlsFactory.ThrottleOffset.y,
                width + TouchControlsFactory.ThrottleOffset.x, TouchControlsFactory.ThrottleAreaTop);
            var joystick = Rect.MinMaxRect(16f, 16f, width * TouchControlsFactory.JoystickRegionRight, height * TouchControlsFactory.JoystickRegionTop);
            var brake = Rect.MinMaxRect(width - 268f - 168f, 48f, width - 268f, 160f);
            var handle = Rect.MinMaxRect(width + ContextualHudFactory.RailBase.x - ContextualHudFactory.RailButtonSize.x, ContextualHudFactory.RailBase.y,
                width + ContextualHudFactory.RailBase.x, ContextualHudFactory.RailBase.y + ContextualHudFactory.RailButtonSize.y);
            var list = new List<Rect> { throttle, joystick, brake, handle };
            for (int k = 0; k < ContextualHud.WarningSlotCount; k++)
            {
                float top = height - 250f - k * 84f, centre = width * ContextualHudFactory.WarningColumn;
                list.Add(Rect.MinMaxRect(centre - ContextualHudFactory.WarningSize.x * 0.5f, top - ContextualHudFactory.WarningSize.y,
                    centre + ContextualHudFactory.WarningSize.x * 0.5f, top));
            }
            var safe = new Rect(0f, 0f, width, height);
            foreach (var r in new[] { CommandClusterLayout.CameraHit, CommandClusterLayout.OptionsHit, CommandClusterLayout.PauseHit, CommandClusterLayout.LookForwardHit })
                list.Add(CommandClusterLayout.InSafeArea(r, safe));
            return list;
        }

        static void AssertInside(Rect inner, Rect outer, string what)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1e-3f), what);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1e-3f), what);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1e-3f), what);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1e-3f), what);
        }
    }
}
