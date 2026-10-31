using NUnit.Framework;
using OpenSkies.FlightInput;

namespace OpenSkies.Tests
{
    public class FlightInputRouterTests
    {
        sealed class FakeProvider : IFlightInputProvider
        {
            public float Pitch, Roll, Yaw, ThrottleDelta, Brake;
            public int Resets;

            public void Contribute(ref FlightInputFrame frame, float deltaTime)
            {
                frame.Pitch += Pitch;
                frame.Roll += Roll;
                frame.Yaw += Yaw;
                frame.ThrottleDelta += ThrottleDelta;
                frame.RequestBrake(Brake);
                ThrottleDelta = 0f; // like a slider: movement is consumed once
            }

            public void ResetInput() => Resets++;
        }

        [Test]
        public void MergesProvidersAndClampsAxes()
        {
            var router = new FlightInputRouter();
            var keyboard = new FakeProvider { Pitch = 1f, Yaw = 0.5f };
            var touch = new FakeProvider { Pitch = 0.6f, Roll = -0.3f, Brake = 1f };
            router.AddProvider(keyboard);
            router.AddProvider(touch);

            var c = router.Read(0.02f);
            Assert.AreEqual(1f, c.Pitch, "sum is clamped to full deflection");
            Assert.AreEqual(-0.3f, c.Roll, 1e-6f);
            Assert.AreEqual(0.5f, c.Yaw, 1e-6f);
            Assert.AreEqual(1f, c.Brake);
        }

        [Test]
        public void ThrottleHoldsWhenInputStops()
        {
            var router = new FlightInputRouter();
            var slider = new FakeProvider { ThrottleDelta = 0.65f };
            var joystick = new FakeProvider { Pitch = 0.8f };
            router.AddProvider(slider);
            router.AddProvider(joystick);

            Assert.AreEqual(0.65f, router.Read(0.02f).Throttle, 1e-6f);

            joystick.Pitch = 0f; // joystick released
            var after = router.Read(0.02f);
            Assert.AreEqual(0f, after.Pitch);
            Assert.AreEqual(0.65f, after.Throttle, 1e-6f, "releasing the joystick must not change throttle");
            Assert.AreEqual(0.65f, router.Read(0.02f).Throttle, 1e-6f);
        }

        [Test]
        public void KeyboardAndSliderShareOneThrottle()
        {
            var router = new FlightInputRouter();
            var keyboard = new FakeProvider();
            var slider = new FakeProvider();
            router.AddProvider(keyboard);
            router.AddProvider(slider);

            slider.ThrottleDelta = 0.5f;
            router.Read(0.02f);
            keyboard.ThrottleDelta = 0.2f;
            router.Read(0.02f);
            Assert.AreEqual(0.7f, router.Throttle, 1e-5f);

            slider.ThrottleDelta = 5f;
            router.Read(0.02f);
            Assert.AreEqual(1f, router.Throttle, "clamped to 100 %");
        }

        [Test]
        public void Reset_IdlesThrottleAndResetsProviders()
        {
            var router = new FlightInputRouter();
            var provider = new FakeProvider { ThrottleDelta = 0.9f };
            router.AddProvider(provider);
            router.Read(0.02f);

            router.ResetControls();

            Assert.AreEqual(0f, router.Throttle);
            Assert.AreEqual(1, provider.Resets);
            Assert.AreEqual(0f, router.Current.Throttle);
        }

        [Test]
        public void ProviderAddedTwiceCountsOnce()
        {
            var router = new FlightInputRouter();
            var provider = new FakeProvider { Roll = 0.4f };
            router.AddProvider(provider);
            router.AddProvider(provider);
            Assert.AreEqual(0.4f, router.Read(0.02f).Roll, 1e-6f);
        }
    }
}
