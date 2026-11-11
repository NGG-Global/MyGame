using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.Gestures;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The tap-first systems wheel as data (docs/TAP_DISCLOSURE_DESIGN.md, milestone 2): the fit
    /// to the screen at 4:3, 16:9 and 20:9 (full touch targets, nothing covered), the focused
    /// state where an action's options take the band, the confirmation emphasis, the switch
    /// panel's relevance and the generated symbols.
    /// </summary>
    public class SystemsWheelTests
    {
        const float Height = 1080f;

        /// <summary>The game's layout around the handle, from the factories' constants (canvas units, origin bottom left).</summary>
        static SystemsWheelLayout.Space GameSpace(float width, float height = Height)
        {
            var keep = new Rect[SystemsWheelLayout.MaxKeepOut];
            int n = 0;
            keep[n++] = Rect.MinMaxRect(16f, 16f, width * TouchControlsFactory.JoystickRegionRight, height * 0.78f);
            var throttle = TouchControlsFactory.ThrottleAreaSize;
            var offset = TouchControlsFactory.ThrottleOffset;
            keep[n++] = Rect.MinMaxRect(width + offset.x - throttle.x, offset.y, width + offset.x, offset.y + throttle.y);
            keep[n++] = Rect.MinMaxRect(width - 268f - 168f, 48f, width - 268f, 160f); // BRAKE (TouchControlsFactory.BuildBrake)
            var handle = HandleRect(width);
            keep[n++] = handle;
            for (int k = 0; k < 4; k++)
            {
                float top = height - 250f - k * 84f;
                float centre = width * ContextualHudFactory.WarningColumn;
                keep[n++] = Rect.MinMaxRect(centre - ContextualHudFactory.WarningSize.x * 0.5f, top - ContextualHudFactory.WarningSize.y,
                    centre + ContextualHudFactory.WarningSize.x * 0.5f, top);
            }
            keep[n++] = Rect.MinMaxRect(0f, height - (ContextualHudFactory.TopMargin + HudStyle.MinTouchTarget + HudStyle.Gap), width, height);
            return new SystemsWheelLayout.Space
            {
                Bounds = new Rect(0f, 0f, width, height),
                Anchor = handle.center,
                KeepOut = keep,
                KeepOutCount = n,
            };
        }

        static Rect HandleRect(float width)
        {
            var size = GestureHudFactory.HandleSize;
            var position = GestureHudFactory.HandlePosition; // bottom-right anchored, pivot bottom right
            return Rect.MinMaxRect(width + position.x - size.x, position.y, width + position.x, position.y + size.y);
        }

        /// <summary>Samples every segment's wedge (with the end tolerance) across the band: inside the bounds, outside every keep-out.</summary>
        static void AssertClear(in SystemsWheelLayout.Space space, SystemsArcSettings g, float firstCentre, float step, int count, string what)
        {
            for (int i = 0; i < count; i++)
            {
                float centre = firstCentre + i * step;
                float from = centre - step * 0.5f - (i == 0 ? g.endTolerance : 0f);
                float to = centre + step * 0.5f + (i == count - 1 ? g.endTolerance : 0f);
                for (int a = 0; a <= 8; a++)
                {
                    float angle = Mathf.Lerp(from, to, a / 8f);
                    for (int r = 0; r <= 6; r++)
                    {
                        float radius = Mathf.Lerp(g.innerRadius, g.outerRadius, r / 6f);
                        var point = space.Anchor + GestureMath.FromAngle(angle) * radius;
                        Assert.IsTrue(space.Bounds.Contains(point), what + ": segment " + i + " leaves the screen at " + point);
                        Assert.IsFalse(SystemsWheelLayout.InKeepOut(space, point, 0f), what + ": segment " + i + " covers a control or a banner at " + point);
                    }
                }
            }
        }

        // ---- Fitting to the screen -----------------------------------------------------------------

        [TestCase(1440f, TestName = "Fit_SixSegments_4by3_FullTargets_NothingCovered")]
        [TestCase(1920f, TestName = "Fit_SixSegments_16by9_FullTargets_NothingCovered")]
        [TestCase(2400f, TestName = "Fit_SixSegments_20by9_FullTargets_NothingCovered")]
        public void Fit_SixSegments_FullTargets_NothingCovered(float width)
        {
            var preferred = new SystemsArcSettings();
            var space = GameSpace(width);
            var fit = SystemsWheelLayout.Fit(preferred, space, 6);
            var g = fit.Geometry;
            Assert.IsTrue(fit.Fits, "six segments fit at width " + width);
            Assert.That(fit.SegmentLength, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "a touch target along the arc");
            Assert.That(g.outerRadius - g.innerRadius, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "and across the band");
            Assert.That(g.innerRadius, Is.GreaterThanOrEqualTo(preferred.selectionInnerRadius), "clear of the handle's cancel zone");
            Assert.That(fit.Capacity, Is.GreaterThanOrEqualTo(6), "a focus can show six options at full size");
            AssertClear(space, g, g.firstSlotAngle, g.slotStep, 6, "primary at " + width);

            // A focus may use slots up to the limit the fit allows: those stay clear too.
            int options = Mathf.Min(fit.Capacity, SystemsArcModel.MaxItems);
            float optionStep = options > 1 ? Mathf.Min(g.slotStep, (g.slotCentreMax - g.firstSlotAngle) / (options - 1)) : g.slotStep;
            AssertClear(space, g, g.firstSlotAngle, optionStep, options, "options at " + width);
        }

        [Test]
        public void Fit_KeepsThePreferredFirstSlot_WhereverThereIsRoom()
        {
            var preferred = new SystemsArcSettings();
            foreach (float width in new[] { 1440f, 1920f, 2400f })
            {
                var fit = SystemsWheelLayout.Fit(preferred, GameSpace(width), 5);
                Assert.IsTrue(fit.Fits);
                Assert.AreEqual(preferred.firstSlotAngle, fit.Geometry.firstSlotAngle, 0.01f, "the same positions at width " + width);
            }
            var wide = SystemsWheelLayout.Fit(preferred, GameSpace(1920f), 6).Geometry;
            var wider = SystemsWheelLayout.Fit(preferred, GameSpace(2400f), 6).Geometry;
            Assert.AreEqual(wide.firstSlotAngle, wider.firstSlotAngle, 0.01f, "16:9 and 20:9 phones put each action in the same place");
            Assert.AreEqual(wide.slotStep, wider.slotStep, 0.01f);
        }

        [Test]
        public void Fit_AtTheSmallestInterfaceScale_StillFits()
        {
            var preferred = new SystemsArcSettings();
            float height = Height / 0.9f;
            foreach (float aspect in new[] { 4f / 3f, 16f / 9f, 20f / 9f })
            {
                var space = GameSpace(aspect * height, height);
                var fit = SystemsWheelLayout.Fit(preferred, space, 6);
                Assert.IsTrue(fit.Fits, "aspect " + aspect);
                AssertClear(space, fit.Geometry, fit.Geometry.firstSlotAngle, fit.Geometry.slotStep, 6, "scale 0.9, aspect " + aspect);
            }
        }

        [Test]
        public void Fit_NeverChangesThePreferredSettings()
        {
            var preferred = new SystemsArcSettings();
            float inner = preferred.innerRadius, outer = preferred.outerRadius, first = preferred.firstSlotAngle, step = preferred.slotStep;
            var fit = SystemsWheelLayout.Fit(preferred, GameSpace(1440f), 6);
            Assert.AreNotSame(preferred, fit.Geometry, "the fit works on a copy (the tuning asset is never written at runtime)");
            Assert.AreEqual(inner, preferred.innerRadius);
            Assert.AreEqual(outer, preferred.outerRadius);
            Assert.AreEqual(first, preferred.firstSlotAngle);
            Assert.AreEqual(step, preferred.slotStep);
            Assert.IsTrue(float.IsNaN(preferred.slotCentreMax));
        }

        [Test]
        public void Fit_WithTooLittleSpace_ReturnsTheBestItCan_AndSaysSo()
        {
            var space = GameSpace(1920f);
            space.Bounds = new Rect(space.Anchor.x - 150f, 0f, 300f, 400f); // a sliver around the handle
            var fit = SystemsWheelLayout.Fit(new SystemsArcSettings(), space, 6);
            Assert.IsFalse(fit.Fits);
            Assert.IsNotNull(fit.Geometry, "a geometry is always returned");
            var empty = SystemsWheelLayout.Fit(new SystemsArcSettings(), new SystemsWheelLayout.Space(), 6);
            Assert.IsFalse(empty.Fits);
            Assert.IsNotNull(empty.Geometry);
        }

        // ---- The focused state ----------------------------------------------------------------------

        static SystemsArcModel Wheel(int count = 6)
        {
            var settings = SystemsWheelLayout.Fit(new SystemsArcSettings(), GameSpace(1920f), count).Geometry;
            var wheel = new SystemsArcModel(settings);
            wheel.SetInnerCount(count);
            for (int i = 0; i < count; i++) wheel.InnerItem(i) = new ArcItem { Label = "A" + i, Emphasis = ArcEmphasis.Full, Available = true };
            wheel.Begin();
            return wheel;
        }

        static void SetOptions(SystemsArcModel wheel, int count)
        {
            for (int i = 0; i < count; i++) wheel.OptionItem(i) = new ArcItem { Label = "O" + i, Emphasis = ArcEmphasis.Full, Available = true };
        }

        static Vector2 At(float angle, SystemsArcModel wheel) =>
            GestureMath.FromAngle(angle) * ((wheel.Settings.innerRadius + wheel.Settings.outerRadius) * 0.5f);

        [Test]
        public void Focus_TheOptionsTakeTheBand_AroundTheirParent_FromTheFirstSlot()
        {
            var wheel = Wheel();
            SetOptions(wheel, 4);
            wheel.Focus(1, 4); // FLAPS in slot 1, four detents
            Assert.IsTrue(wheel.Focused);
            Assert.AreEqual(1, wheel.FocusParent);
            Assert.AreEqual(4, wheel.OptionCount);
            Assert.AreEqual(wheel.SlotAngle(0), wheel.OptionAngle(0), 1e-3f, "centred on the parent, then held at the first slot: UP at the top");
            for (int i = 1; i < 4; i++)
                Assert.AreEqual(wheel.OptionAngle(i - 1) + wheel.Settings.slotStep, wheel.OptionAngle(i), 1e-3f, "in order, a slot apart");
            Assert.AreEqual(wheel.SlotAngle(1), wheel.UnfoldedOptionAngle(3, 0f), 1e-3f, "the options unfold from their parent");

            wheel.Focus(5, 3); // the last slot: the fan is held inside the allowed span
            Assert.That(wheel.OptionAngle(2), Is.LessThanOrEqualTo(wheel.SlotCentreMax + 1e-3f));
            Assert.AreEqual(wheel.SlotAngle(4), wheel.OptionAngle(1), 1e-2f, "held back from the end: the last option on the last slot the screen allows");
        }

        [Test]
        public void Focus_TheFingerFindsOptions_NotThePrimarySegments_UntilUnfocused()
        {
            var wheel = Wheel();
            SetOptions(wheel, 4);
            wheel.Focus(1, 4);
            wheel.Update(At(wheel.OptionAngle(2), wheel), 1f);
            Assert.IsTrue(wheel.HasHovered);
            Assert.IsTrue(wheel.HoveredIsOption);
            Assert.AreEqual(2, wheel.HoveredIndex);
            Assert.AreEqual("O2", wheel.HoveredItem.Label);
            Assert.IsTrue(wheel.IsArmed);

            wheel.Update(At(wheel.SlotAngle(5), wheel), 1f);
            Assert.IsFalse(wheel.HasHovered, "beyond the last option there is nothing: the primary segments folded away");

            wheel.Unfocus();
            Assert.IsFalse(wheel.Focused);
            wheel.Update(At(wheel.SlotAngle(5), wheel), 1f);
            Assert.AreEqual(5, wheel.HoveredIndex);
            Assert.IsFalse(wheel.HoveredIsOption, "the primary segments are back");

            SetOptions(wheel, 2);
            wheel.Focus(0, 2);
            wheel.Begin();
            Assert.IsFalse(wheel.Focused, "a new opening starts on the primary segments");
        }

        [Test]
        public void Focus_ManyOptions_NarrowTheStep_NeverPastTheLimit()
        {
            var wheel = Wheel(3);
            float limit = wheel.SlotCentreMax;
            SetOptions(wheel, SystemsArcModel.MaxItems);
            wheel.Focus(0, SystemsArcModel.MaxItems);
            Assert.That(wheel.OptionStep, Is.LessThanOrEqualTo(wheel.Settings.slotStep + 1e-3f));
            Assert.That(wheel.OptionAngle(SystemsArcModel.MaxItems - 1), Is.LessThanOrEqualTo(limit + 1e-3f), "every option stays where the screen allows");
            Assert.That(wheel.OptionAngle(0), Is.GreaterThanOrEqualTo(wheel.SlotAngle(0) - 1e-3f));
            Assert.AreEqual(SystemsArcModel.NoItem, new SystemsArcModel().FocusParent);

            var invalid = Wheel(3);
            invalid.Focus(7, 2);
            Assert.IsFalse(invalid.Focused, "no such slot: nothing is focused");
            invalid.Focus(1, 0);
            Assert.IsFalse(invalid.Focused, "no options: nothing is focused");
        }

        [Test]
        public void Emphasis_AConfirmationIsSelectable_ADisabledItemIsNot()
        {
            var wheel = Wheel();
            for (int i = 0; i < 3; i++) wheel.OptionItem(i) = new ArcItem { Label = "S" + i, Emphasis = ArcEmphasis.Full, Available = true };
            wheel.OptionItem(2) = new ArcItem { Label = "CONFIRM", State = "ENGINE OFF", Emphasis = ArcEmphasis.Confirm, Available = true };
            wheel.Focus(2, 3);
            wheel.Update(At(wheel.OptionAngle(2), wheel), 1f);
            Assert.IsTrue(wheel.IsArmed, "the second tap of a confirmation is a choice");
            Assert.AreEqual("ENGINE OFF", wheel.HoveredItem.State);
            wheel.OptionItem(2).Emphasis = ArcEmphasis.Disabled;
            wheel.Update(At(wheel.OptionAngle(2), wheel), 1f);
            Assert.IsFalse(wheel.IsArmed);
        }

        [Test]
        public void SetSettings_MovesTheSlots_ToTheFittedGeometry()
        {
            var wheel = new SystemsArcModel();
            wheel.SetInnerCount(6);
            float before = wheel.SlotAngle(5);
            var fitted = SystemsWheelLayout.Fit(new SystemsArcSettings(), GameSpace(1440f), 6).Geometry;
            wheel.SetSettings(fitted);
            Assert.AreSame(fitted, wheel.Settings);
            Assert.AreEqual(fitted.firstSlotAngle + 5f * fitted.slotStep, wheel.SlotAngle(5), 1e-3f);
            Assert.AreNotEqual(before, wheel.SlotAngle(5));
        }

        // ---- The switch panel's relevance, and the symbols -------------------------------------------

        [Test]
        public void SwitchPanel_IsInFullWhereAPilotReachesForIt_NeverHidden()
        {
            Assert.IsTrue(DisclosureController.SwitchPanelRelevant(FlightPhase.Parked), "engine and lights before taxi");
            Assert.IsTrue(DisclosureController.SwitchPanelRelevant(FlightPhase.TakeoffRoll), "the landing light for the take-off");
            Assert.IsTrue(DisclosureController.SwitchPanelRelevant(FlightPhase.Approach));
            Assert.IsTrue(DisclosureController.SwitchPanelRelevant(FlightPhase.Landing));
            Assert.IsFalse(DisclosureController.SwitchPanelRelevant(FlightPhase.Cruise));
            Assert.AreEqual(Prominence.Full, DisclosureController.SwitchPanelProminence(HudProfile.Smart, FlightPhase.Approach));
            Assert.AreEqual(Prominence.Quiet, DisclosureController.SwitchPanelProminence(HudProfile.Smart, FlightPhase.Cruise), "quiet, still there");
            Assert.AreEqual(Prominence.Full, DisclosureController.SwitchPanelProminence(HudProfile.Detailed, FlightPhase.Cruise));
            Assert.AreEqual(Prominence.Quiet, DisclosureController.SwitchPanelProminence(HudProfile.Cinematic, FlightPhase.Approach));
            for (int p = 0; p <= (int)FlightPhase.Rollout; p++)
                for (int h = 0; h <= (int)HudProfile.Detailed; h++)
                    Assert.AreNotEqual(Prominence.Hidden, DisclosureController.SwitchPanelProminence((HudProfile)h, (FlightPhase)p), "every system stays reachable by hand");
        }

        [Test]
        public void WheelSymbols_AreGeneratedOnce_AndNoneHasNoSymbol()
        {
            Assert.IsNull(HudSprites.Icon(HudIcon.None));
            foreach (HudIcon icon in new[] { HudIcon.ParkingBrake, HudIcon.Flaps, HudIcon.Switches, HudIcon.Approach, HudIcon.Navigation, HudIcon.Assist })
            {
                var sprite = HudSprites.Icon(icon);
                Assert.IsNotNull(sprite, icon.ToString());
                Assert.AreSame(sprite, HudSprites.Icon(icon), "cached: " + icon);
                Assert.AreEqual(64f, sprite.rect.width, 1e-3f);
            }
        }
    }
}
