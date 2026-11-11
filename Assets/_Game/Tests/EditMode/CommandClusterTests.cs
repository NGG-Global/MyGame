using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The top-right command cluster as data (docs/TAP_DISCLOSURE_DESIGN.md, milestone 3): its
    /// levels, the deliberate confirmation of RESET and SKIP TUTORIAL, the direct HUD profile
    /// choice, and its geometry at 4:3, 16:9 and 20:9 at every interface scale the game allows.
    /// </summary>
    public class CommandClusterTests
    {
        const float Height = 1080f;

        static CommandClusterModel Model() => new CommandClusterModel(new CommandClusterSettings { confirmWindowSeconds = 3f, confirmMinSeconds = 0.4f });

        // ---- Levels -------------------------------------------------------------------------------------

        [Test]
        public void StartsCollapsed_OpensToTheOptions_AndGoesBackOneLevelAtATime()
        {
            var m = Model();
            Assert.AreEqual(CommandLevel.Collapsed, m.Level);
            Assert.IsFalse(m.IsOpen);
            Assert.IsFalse(m.OpenHudSelector(), "the selector opens only from the options");

            m.Open();
            Assert.AreEqual(CommandLevel.Options, m.Level);
            Assert.IsTrue(m.OpenHudSelector());
            Assert.AreEqual(CommandLevel.HudSelector, m.Level);
            m.Back();
            Assert.AreEqual(CommandLevel.Options, m.Level, "the selector goes back to the options");
            m.Back();
            Assert.AreEqual(CommandLevel.Collapsed, m.Level);

            m.Open();
            m.OpenHudSelector();
            m.Collapse();
            Assert.AreEqual(CommandLevel.Collapsed, m.Level, "a collapse folds every level at once");
        }

        // ---- Confirmations ------------------------------------------------------------------------------

        [Test]
        public void Reset_NeedsASecondDeliberateTap_WithinTheWindow()
        {
            var m = Model();
            m.Open();
            Assert.AreEqual(ConfirmTapResult.Armed, m.Tap(CommandConfirm.Reset, 10f), "the first tap only arms");
            Assert.AreEqual(CommandConfirm.Reset, m.Armed);
            Assert.AreEqual(CommandClusterModel.ResetConfirmLabel, m.ResetCaption, "the caption says what the next tap does");
            Assert.AreEqual(ConfirmTapResult.Confirmed, m.Tap(CommandConfirm.Reset, 11f));
            Assert.AreEqual(CommandConfirm.None, m.Armed);
            Assert.AreEqual(CommandClusterModel.ResetLabel, m.ResetCaption);
        }

        [Test]
        public void Reset_ADoubleTapOrABounce_IsNeverAConfirmation()
        {
            var m = Model();
            m.Open();
            m.Tap(CommandConfirm.Reset, 5f);
            Assert.AreEqual(ConfirmTapResult.Ignored, m.Tap(CommandConfirm.Reset, 5.1f), "a double tap");
            Assert.AreEqual(ConfirmTapResult.Ignored, m.Tap(CommandConfirm.Reset, 5.35f));
            Assert.AreEqual(CommandConfirm.Reset, m.Armed, "still waiting for a deliberate tap");
            Assert.AreEqual(ConfirmTapResult.Confirmed, m.Tap(CommandConfirm.Reset, 5.5f));
        }

        [Test]
        public void Reset_TheConfirmationLapses_AfterTheWindow()
        {
            var m = Model();
            m.Open();
            m.Tap(CommandConfirm.Reset, 1f);
            m.Tick(3.9f);
            Assert.AreEqual(CommandConfirm.Reset, m.Armed, "within the window");
            m.Tick(4.1f);
            Assert.AreEqual(CommandConfirm.None, m.Armed, "lapsed");
            Assert.AreEqual(ConfirmTapResult.Armed, m.Tap(CommandConfirm.Reset, 4.2f), "a late tap starts again, it never confirms");

            // Without a tick in between, a tap after the window still only arms.
            var late = Model();
            late.Open();
            late.Tap(CommandConfirm.Reset, 0f);
            Assert.AreEqual(ConfirmTapResult.Armed, late.Tap(CommandConfirm.Reset, 3.5f));
        }

        [Test]
        public void Confirmation_IsDropped_ByACollapse_ALevelChange_OrAnotherAction()
        {
            var m = Model();
            m.Open();
            m.Tap(CommandConfirm.Reset, 0f);
            m.Collapse();
            Assert.AreEqual(CommandConfirm.None, m.Armed, "dismissing the menu never leaves RESET armed");
            m.Open();
            Assert.AreEqual(ConfirmTapResult.Armed, m.Tap(CommandConfirm.Reset, 1f), "reopened: the first tap arms again");

            m.OpenHudSelector();
            Assert.AreEqual(CommandConfirm.None, m.Armed, "the HUD selector drops it");
            m.Back();
            m.Tap(CommandConfirm.Reset, 2f);
            m.SetSkipAvailable(true);
            Assert.AreEqual(ConfirmTapResult.Armed, m.Tap(CommandConfirm.SkipTutorial, 2.6f), "another action arms itself instead");
            Assert.AreEqual(ConfirmTapResult.Armed, m.Tap(CommandConfirm.Reset, 3.2f), "RESET has to start again");
            m.Disarm();
            Assert.AreEqual(CommandConfirm.None, m.Armed);
        }

        [Test]
        public void Confirmations_AreIgnored_WhenThePanelIsNotShowingThem()
        {
            var m = Model();
            Assert.AreEqual(ConfirmTapResult.Ignored, m.Tap(CommandConfirm.Reset, 0f), "collapsed: RESET is not on screen");
            m.Open();
            m.OpenHudSelector();
            Assert.AreEqual(ConfirmTapResult.Ignored, m.Tap(CommandConfirm.Reset, 0f), "the selector is showing, not RESET");
            m.Back();
            Assert.AreEqual(ConfirmTapResult.Ignored, m.Tap(CommandConfirm.SkipTutorial, 0f), "SKIP only while the tutorial runs");
            Assert.AreEqual(ConfirmTapResult.Ignored, m.Tap(CommandConfirm.None, 0f));
            Assert.AreEqual(ConfirmTapResult.Ignored, m.Tap(CommandConfirm.Reset, float.NaN));
        }

        [Test]
        public void Skip_IsConfirmedLikeReset_AndWithdrawingItDisarmsIt()
        {
            var m = Model();
            m.SetSkipAvailable(true);
            m.Open();
            Assert.AreEqual(ConfirmTapResult.Armed, m.Tap(CommandConfirm.SkipTutorial, 0f));
            Assert.AreEqual(CommandClusterModel.SkipConfirmLabel, m.SkipCaption);
            m.SetSkipAvailable(false);
            Assert.AreEqual(CommandConfirm.None, m.Armed, "the tutorial ended: nothing waits");
            m.SetSkipAvailable(true);
            m.Tap(CommandConfirm.SkipTutorial, 1f);
            Assert.AreEqual(ConfirmTapResult.Confirmed, m.Tap(CommandConfirm.SkipTutorial, 2f));
        }

        [Test]
        public void Settings_AreSanitized_SoNoTuningCanMakeADoubleTapConfirm()
        {
            var s = new CommandClusterSettings { confirmWindowSeconds = float.NaN, confirmMinSeconds = -5f }.Sanitized();
            Assert.AreEqual(3f, s.confirmWindowSeconds);
            Assert.AreEqual(0f, s.confirmMinSeconds);
            s = new CommandClusterSettings { confirmWindowSeconds = 0.1f, confirmMinSeconds = 9f }.Sanitized();
            Assert.That(s.confirmWindowSeconds, Is.GreaterThanOrEqualTo(1f));
            Assert.That(s.confirmMinSeconds, Is.LessThan(s.confirmWindowSeconds), "a window that can always be met");
        }

        // ---- HUD profiles -------------------------------------------------------------------------------

        [Test]
        public void HudSelector_OffersEveryProfile_InDensityOrder_ByDirectIndex()
        {
            Assert.AreEqual(3, CommandClusterModel.ProfileCount);
            Assert.AreEqual(HudProfile.Cinematic, CommandClusterModel.ProfileAt(0));
            Assert.AreEqual(HudProfile.Smart, CommandClusterModel.ProfileAt(1));
            Assert.AreEqual(HudProfile.Detailed, CommandClusterModel.ProfileAt(2));
            foreach (HudProfile p in new[] { HudProfile.Cinematic, HudProfile.Smart, HudProfile.Detailed })
            {
                Assert.AreEqual(p, CommandClusterModel.ProfileAt(CommandClusterModel.IndexOf(p)), "every profile is one direct choice");
                StringAssert.Contains(CommandClusterModel.ProfileLabel(p), CommandClusterModel.SelectorTitle(p), "the title names the shown profile");
            }
            Assert.AreEqual(HudProfile.Smart, CommandClusterModel.ProfileAt(-1));
            Assert.AreEqual(HudProfile.Smart, CommandClusterModel.ProfileAt(7));
            Assert.AreEqual("CHASE", CommandClusterModel.CameraLabel(false));
            Assert.AreEqual("COCKPIT", CommandClusterModel.CameraLabel(true));
            Assert.AreSame(CommandClusterModel.SelectorTitle(HudProfile.Detailed), CommandClusterModel.SelectorTitle(HudProfile.Detailed), "cached strings");
        }

        // ---- Geometry -----------------------------------------------------------------------------------

        [Test]
        public void PrimaryRow_IsOrderedByFrequency_WithIndependentVisualAndTouchSizes()
        {
            Rect camera = CommandClusterLayout.CameraHit, options = CommandClusterLayout.OptionsHit, pause = CommandClusterLayout.PauseHit;
            Assert.That(camera.xMax, Is.EqualTo(-CommandClusterLayout.RightMargin), "the camera is in the corner");
            Assert.That(options.xMax, Is.LessThan(camera.xMin), "options left of the camera");
            Assert.That(pause.xMax, Is.LessThan(options.xMin), "pause left of the options, the farthest");
            foreach (var hit in new[] { camera, options, pause, CommandClusterLayout.LookForwardHit })
            {
                Assert.That(hit.width, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget));
                Assert.That(hit.height, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget));
            }
            Assert.IsFalse(camera.Overlaps(options) || options.Overlaps(pause) || camera.Overlaps(pause));

            // Visual weight falls with importance; every visual is smaller than its touch target.
            float cameraArea = Area(CommandClusterLayout.CameraVisualSize), optionsArea = Area(CommandClusterLayout.OptionsVisualSize),
                pauseArea = Area(CommandClusterLayout.PauseVisualSize);
            Assert.That(cameraArea, Is.GreaterThan(optionsArea));
            Assert.That(optionsArea, Is.GreaterThan(pauseArea), "pause is restrained");
            AssertInside(CommandClusterLayout.Centred(camera, CommandClusterLayout.CameraVisualSize), camera, "camera visual");
            AssertInside(CommandClusterLayout.Centred(options, CommandClusterLayout.OptionsVisualSize), options, "options visual");
            AssertInside(CommandClusterLayout.Centred(pause, CommandClusterLayout.PauseVisualSize), pause, "pause visual");
            AssertInside(CommandClusterLayout.Centred(CommandClusterLayout.LookForwardHit, CommandClusterLayout.LookForwardVisualSize),
                CommandClusterLayout.LookForwardHit, "LOOK FWD visual");
            Assert.That(CommandClusterLayout.PauseVisualSize.y, Is.LessThan(HudStyle.MinTouchTarget));
            Assert.That(CommandClusterLayout.RowWidth, Is.LessThanOrEqualTo(360f), "a compact cluster, not a toolbar");
        }

        [Test]
        public void Panel_HoldsEveryRowAndSlot_AsFullTouchTargets_WithoutOverlap()
        {
            var panel = CommandClusterLayout.Panel;
            var row = CommandClusterLayout.SecondaryRow;
            AssertInside(row, panel, "secondary row");
            Assert.That(row.height, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget));
            for (int i = 0; i < CommandClusterLayout.SlotCount; i++)
            {
                var slot = CommandClusterLayout.Slot(i);
                AssertInside(slot, panel, "slot " + i);
                Assert.That(slot.width, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "slot " + i);
                Assert.That(slot.height, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "slot " + i);
                Assert.IsFalse(slot.Overlaps(row), "slot " + i + " under the secondary row");
                for (int j = i + 1; j < CommandClusterLayout.SlotCount; j++)
                    Assert.IsFalse(slot.Overlaps(CommandClusterLayout.Slot(j)), $"slots {i} and {j}");
            }
            Assert.That(panel.yMax, Is.LessThanOrEqualTo(CommandClusterLayout.CameraHit.yMin), "the panel opens under the row, never over it");
            Assert.That(CommandClusterLayout.SlotVisualHeight, Is.LessThan(CommandClusterLayout.RowVisualHeight), "tertiary actions are smaller than the secondary row");
            Assert.AreNotEqual(CommandClusterLayout.ResetSlot, CommandClusterLayout.SkipSlot);
            Assert.AreNotEqual(CommandClusterLayout.ResetSlot, CommandClusterLayout.SettingsSlot);
            // RESET is never where the trigger is (a second tap meant to close the panel lands on the trigger, never on RESET).
            Assert.IsFalse(CommandClusterLayout.Slot(CommandClusterLayout.ResetSlot).Overlaps(CommandClusterLayout.OptionsHit));
        }

        [Test]
        public void Panel_StaysInsideTheRailInset_SoItCanNeverReachTheWarningColumn()
        {
            Assert.That(CommandClusterLayout.PanelInset, Is.LessThan(ContextualHudFactory.RailLeftInset - HudStyle.Gap),
                "the scale limit already keeps the warning column clear of the rail's inset");
            Assert.That(CommandClusterLayout.RowWidth, Is.LessThan(ContextualHudFactory.RailLeftInset));
        }

        [TestCase(4f / 3f, 0.9f)]
        [TestCase(4f / 3f, 1.1f)]
        [TestCase(16f / 9f, 0.9f)]
        [TestCase(16f / 9f, 1.1f)]
        [TestCase(20f / 9f, 0.9f)]
        [TestCase(20f / 9f, 1.1f)]
        public void Cluster_OnAPhoneOrTablet_StaysOnScreen_AndClearOfFlightControlsWarningsAndData(float aspect, float requested)
        {
            var layout = UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop);
            float scale = UiScaleMath.Effective(requested, aspect, layout);
            float height = Height / scale, width = aspect * height;
            var safe = new Rect(0f, 0f, width, height);
            string at = $" at aspect {aspect:0.00}, scale {scale:0.000}";

            var throttle = Rect.MinMaxRect(width + TouchControlsFactory.ThrottleOffset.x - TouchControlsFactory.ThrottleAreaSize.x, TouchControlsFactory.ThrottleOffset.y,
                width + TouchControlsFactory.ThrottleOffset.x, TouchControlsFactory.ThrottleAreaTop);
            var joystick = Rect.MinMaxRect(0f, 0f, width * TouchControlsFactory.JoystickRegionRight, height * 0.78f);
            var brake = Rect.MinMaxRect(width - 268f - 168f, 48f, width - 268f, 160f);
            var handle = Rect.MinMaxRect(width + ContextualHudFactory.RailBase.x - ContextualHudFactory.RailButtonSize.x, ContextualHudFactory.RailBase.y,
                width + ContextualHudFactory.RailBase.x, ContextualHudFactory.RailBase.y + ContextualHudFactory.RailButtonSize.y);
            var keepClear = new System.Collections.Generic.List<Rect> { throttle, joystick, brake, handle };
            for (int k = 0; k < 4; k++)
            {
                float top = height - 250f - k * 84f, centre = width * ContextualHudFactory.WarningColumn;
                keepClear.Add(Rect.MinMaxRect(centre - ContextualHudFactory.WarningSize.x * 0.5f, top - ContextualHudFactory.WarningSize.y,
                    centre + ContextualHudFactory.WarningSize.x * 0.5f, top));
            }
            // The flight information cluster (top left): its widest capsule and its open panel.
            keepClear.Add(FlightInfoLayout.InSafeArea(FlightInfoLayout.CapsuleHit(FlightInfoModel.MaxCapsuleReadings, true), safe));
            keepClear.Add(FlightInfoLayout.InSafeArea(FlightInfoLayout.Panel, safe));

            var cluster = new System.Collections.Generic.List<Rect>
            {
                CommandClusterLayout.CameraHit, CommandClusterLayout.OptionsHit, CommandClusterLayout.PauseHit, CommandClusterLayout.LookForwardHit,
                CommandClusterLayout.Panel, CommandClusterLayout.SecondaryRow,
            };
            for (int i = 0; i < CommandClusterLayout.SlotCount; i++) cluster.Add(CommandClusterLayout.Slot(i));
            foreach (var local in cluster)
            {
                var r = CommandClusterLayout.InSafeArea(local, safe);
                Assert.IsTrue(r.xMin >= 0f && r.yMin >= 0f && r.xMax <= width && r.yMax <= height, "on screen" + at);
                foreach (var other in keepClear) Assert.IsFalse(r.Overlaps(other), $"{r} over {other}{at}");
            }
            // Reach: the whole cluster sits in the top-right corner a right thumb reaches from the throttle.
            var panel = CommandClusterLayout.InSafeArea(CommandClusterLayout.Panel, safe);
            Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(width * 0.5f), "right half" + at);
            Assert.That(panel.yMin, Is.GreaterThan(throttle.yMax), "above the throttle" + at);
        }

        [Test]
        public void SafeArea_PlacesTheClusterFromTheSafeEdge_NotTheScreenEdge()
        {
            // A 20:9 phone with a cut-out: the safe area is inset on the right and at the top.
            var safe = Rect.MinMaxRect(0f, 0f, 2400f - 110f, 1080f - 30f);
            var camera = CommandClusterLayout.InSafeArea(CommandClusterLayout.CameraHit, safe);
            Assert.AreEqual(safe.xMax - CommandClusterLayout.RightMargin, camera.xMax, 1e-3f);
            Assert.AreEqual(safe.yMax - CommandClusterLayout.TopMargin, camera.yMax, 1e-3f);
            var panel = CommandClusterLayout.InSafeArea(CommandClusterLayout.Panel, safe);
            Assert.That(panel.xMax, Is.LessThanOrEqualTo(safe.xMax));
            Assert.That(panel.yMax, Is.LessThanOrEqualTo(safe.yMax));
            Assert.AreEqual(new Vector2(-CommandClusterLayout.RightMargin, -CommandClusterLayout.TopMargin),
                CommandClusterLayout.AnchoredPosition(CommandClusterLayout.CameraHit));
        }

        [Test]
        public void ScaleLimit_IsNoTighterThanWithTheOldSessionColumn()
        {
            // MENU and SKIP used to reach 336 units below the top edge over the throttle.
            Assert.That(CommandClusterLayout.ColumnBottom, Is.LessThanOrEqualTo(336f));
            var layout = UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop);
            Assert.AreEqual(CommandClusterLayout.ColumnBottom, layout.SessionColumnBottom, 1e-3f, "the scale limit knows the open panel");
            Assert.AreEqual(CommandClusterLayout.RowBottom, layout.TopBarBottom, 1e-3f);
        }

        static float Area(Vector2 size) => size.x * size.y;

        static void AssertInside(Rect inner, Rect outer, string what)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1e-3f), what);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1e-3f), what);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1e-3f), what);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1e-3f), what);
        }
    }
}
