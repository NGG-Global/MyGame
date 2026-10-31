using NUnit.Framework;
using OpenSkies.FlightInput;
using OpenSkies.FlightState;
using OpenSkies.UI;

namespace OpenSkies.Tests
{
    public class HudSupportTests
    {
        [Test]
        public void ParkingBrake_HoldsFullBrake_UntilTheSystemsAreReset()
        {
            var systems = new AircraftSystems();
            var router = new FlightInputRouter();
            Assert.AreEqual(0f, systems.ApplyTo(router.Read(0.02f)).Brake);

            systems.Set(AircraftSystem.ParkingBrake, true);
            Assert.AreEqual(1f, systems.ApplyTo(router.Read(0.02f)).Brake);
            Assert.AreEqual(1f, systems.ApplyTo(router.Read(0.02f)).Brake, "latched: no finger needed");

            systems.ResetToStartState();
            Assert.IsFalse(systems.ParkingBrakeSet);
            Assert.AreEqual(0f, systems.ApplyTo(router.Read(0.02f)).Brake);
        }

        [Test]
        public void NumberStrings_FormatAndCache()
        {
            Assert.AreEqual("12.3", NumberStrings.Tenths(123));
            Assert.AreEqual("0.5", NumberStrings.Tenths(5));
            Assert.AreEqual("-0.5", NumberStrings.Tenths(-5));
            Assert.AreEqual("+500", NumberStrings.Signed(500));
            Assert.AreEqual("-50", NumberStrings.Signed(-50));
            Assert.AreEqual("0", NumberStrings.Signed(0));
            Assert.AreEqual("005", NumberStrings.Heading(5));
            Assert.AreEqual("360", NumberStrings.Heading(360));

            var cache = new NumberStrings(0, 10, v => v.ToString());
            Assert.AreSame(cache.Get(7), cache.Get(7), "cached: no new string the second time");
            Assert.AreEqual("42", cache.Get(42), "outside the range: formatted on the fly");
        }

        [Test]
        public void MotionPreference_FollowsTheSystemOnlyWhenAsked()
        {
            Assert.IsTrue(SystemMotion.Resolve(MotionPreference.Reduced, systemPrefersReduced: false));
            Assert.IsFalse(SystemMotion.Resolve(MotionPreference.Full, systemPrefersReduced: true));
            Assert.IsTrue(SystemMotion.Resolve(MotionPreference.FollowSystem, systemPrefersReduced: true));
            Assert.IsFalse(SystemMotion.Resolve(MotionPreference.FollowSystem, systemPrefersReduced: false));
        }

        [Test]
        public void MotionSettings_TargetRestrainedTransitions()
        {
            var motion = new HudMotionSettings();
            Assert.That(motion.FadeSeconds(false), Is.InRange(0.15f, 0.25f));
            Assert.That(motion.WarningFadeSeconds(false), Is.LessThanOrEqualTo(motion.FadeSeconds(false)));
            Assert.AreEqual(0f, motion.Slide(true), "no movement with reduced motion");
            Assert.That(motion.FadeSeconds(true), Is.LessThanOrEqualTo(motion.FadeSeconds(false)));
        }
    }
}
