using NUnit.Framework;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The design tokens: the touch controls' mirror matches the HUD's, critical colours
    /// keep readable contrast (WCAG ratios, composited over the worst background), and
    /// the interface scale limits follow the layout.
    /// </summary>
    public class VisualStyleTests
    {
        [Test]
        public void TouchControlStyle_MirrorsHudStyle()
        {
            Assert.AreEqual(HudStyle.TextPrimary, TouchControlStyle.TextPrimary);
            Assert.AreEqual(HudStyle.TextShadow, TouchControlStyle.TextShadow);
            Assert.AreEqual(HudStyle.Selected, TouchControlStyle.Selected);
            Assert.AreEqual(HudStyle.ControlRadius, TouchControlStyle.ControlRadius);
            Assert.AreEqual(HudStyle.MinTouchTarget, TouchControlStyle.MinTouchTarget);
            Assert.AreEqual(HudStyle.ButtonSize, TouchControlStyle.ButtonSize);
        }

        /// <summary>A translucent colour drawn over a background.</summary>
        static Color Over(Color top, Color background) =>
            new Color(Mathf.Lerp(background.r, top.r, top.a), Mathf.Lerp(background.g, top.g, top.a), Mathf.Lerp(background.b, top.b, top.a), 1f);

        [Test]
        public void WarningBanners_AreReadableOverAnySky()
        {
            foreach (var sky in new[] { Color.white, Color.black, new Color(0.55f, 0.7f, 0.9f) })
            {
                var red = Over(HudStyle.Warning, sky);
                var amber = Over(HudStyle.Caution, sky);
                Assert.GreaterOrEqual(HudStyle.Contrast(Color.white, red), 4.5f, "white on a red banner");
                Assert.GreaterOrEqual(HudStyle.Contrast(HudStyle.TextOnAccent, amber), 4.5f, "dark text on an amber banner");
            }
        }

        [Test]
        public void SelectedButtons_AndPanels_AreReadable()
        {
            foreach (var behind in new[] { Color.white, Color.black })
            {
                var selected = Over(HudStyle.Selected, behind);
                Assert.GreaterOrEqual(HudStyle.Contrast(HudStyle.TextOnAccent, selected), 4.5f, "dark text on a selected button");
                var panel = Over(HudStyle.PanelSurface, behind);
                Assert.GreaterOrEqual(HudStyle.Contrast(Over(HudStyle.TextPrimary, panel), panel), 7f, "text on a panel");
                Assert.GreaterOrEqual(HudStyle.Contrast(Over(HudStyle.TextSecondary, panel), panel), 4.5f, "secondary text on a panel");
            }
        }

        [Test]
        public void FlightValues_KeepLargeTextContrast_OnTheirPlate_EvenOverAWhiteSky()
        {
            var plate = Over(HudStyle.DataBacking, Color.white);
            var value = Over(HudStyle.TextPrimary, plate);
            Assert.GreaterOrEqual(HudStyle.Contrast(value, plate), 3f, "WCAG AA for large text (values are 42 units)");
            var alert = Over(HudStyle.AlertValue, plate);
            Assert.GreaterOrEqual(HudStyle.Contrast(alert, plate), 1.5f, "an alert value is also shown by its banner, never colour alone");
        }

        [Test]
        public void SeverityIsNotColourAlone()
        {
            // Different symbols for warnings and cautions (the banner also carries the word and a sound).
            Assert.AreNotSame(HudSprites.WarningIcon, HudSprites.CautionIcon);
            Assert.AreNotEqual(HudStyle.Warning, HudStyle.Caution);
        }

        // ---- Interface scale --------------------------------------------------------------------------

        static UiScaleMath.Layout Layout => UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop);

        [TestCase(4f / 3f)]
        [TestCase(16f / 10f)]
        [TestCase(16f / 9f)]
        [TestCase(20f / 9f)]
        public void UiScale_TheDefaultAlwaysFits_AndTheLimitNeverDropsBelowIt(float aspect)
        {
            float max = UiScaleMath.MaxScale(aspect, Layout);
            Assert.GreaterOrEqual(max, 1f, "the 100 % layout fits every supported screen");
            Assert.AreEqual(1f, UiScaleMath.Effective(1f, aspect, Layout), 1e-5f);
            Assert.AreEqual(0.9f, UiScaleMath.Effective(0.9f, aspect, Layout), 1e-5f);
            Assert.LessOrEqual(UiScaleMath.Effective(1.1f, aspect, Layout), max + 1e-5f);
        }

        [Test]
        public void UiScale_IsLimitedOnTablets_NotOnPhones()
        {
            Assert.Less(UiScaleMath.Effective(1.1f, 4f / 3f, Layout), 1.05f, "4:3: little horizontal room");
            Assert.AreEqual(1.1f, UiScaleMath.Effective(1.1f, 20f / 9f, Layout), 1e-3f, "wide phones get 110 % (less a 2-unit clearance)");
            Assert.Less(UiScaleMath.MaxScale(16f / 9f, Layout, 0.9f), UiScaleMath.MaxScale(16f / 9f, Layout, 1f), "a shorter safe area has less room");
            var reference = UiScaleMath.ReferenceResolution(1.1f);
            Assert.AreEqual(1080f / 1.1f, reference.y, 1e-3f, "height matching: a larger scale means a smaller reference height");
        }
    }
}
