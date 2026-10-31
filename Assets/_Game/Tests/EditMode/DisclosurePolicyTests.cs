using NUnit.Framework;
using OpenSkies.FlightState;

namespace OpenSkies.Tests
{
    public class DisclosurePolicyTests
    {
        [Test]
        public void DevelopmentOverlay_NeverShownInReleaseBuilds()
        {
            Assert.IsFalse(DisclosurePolicy.ShowDevelopmentOverlay(enabledInSettings: true, isDevelopmentBuild: false));
            Assert.IsFalse(DisclosurePolicy.ShowDevelopmentOverlay(enabledInSettings: false, isDevelopmentBuild: true));
            Assert.IsTrue(DisclosurePolicy.ShowDevelopmentOverlay(enabledInSettings: true, isDevelopmentBuild: true));
        }

        [Test]
        public void BrakeControl_OnlyWithWeightOnWheels()
        {
            Assert.IsTrue(DisclosurePolicy.ShowBrakeControl(new FlightTelemetry { WeightOnWheels = true }));
            Assert.IsFalse(DisclosurePolicy.ShowBrakeControl(new FlightTelemetry { WeightOnWheels = false, AltitudeFeet = 500f }));
        }

        [TestCase(TouchControlsVisibility.Auto, true, false, false, true)]   // phone
        [TestCase(TouchControlsVisibility.Auto, false, true, false, true)]   // Editor (mouse testing)
        [TestCase(TouchControlsVisibility.Auto, false, false, true, true)]   // touchscreen laptop / tablet browser
        [TestCase(TouchControlsVisibility.Auto, false, false, false, false)] // desktop WebGL with keyboard
        [TestCase(TouchControlsVisibility.Always, false, false, false, true)]
        [TestCase(TouchControlsVisibility.Never, true, true, true, false)]
        public void TouchControls_Visibility(TouchControlsVisibility mode, bool mobile, bool editor, bool touchscreen, bool expected)
        {
            Assert.AreEqual(expected, DisclosurePolicy.ShowTouchControls(mode, mobile, editor, touchscreen));
        }
    }
}
