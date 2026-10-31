using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightState;

namespace OpenSkies.Tests
{
    public class FlightWarningMonitorTests
    {
        const float Dt = 0.02f;

        static void Run(FlightWarningMonitor monitor, FlightTelemetry t, float seconds)
        {
            int steps = UnityEngine.Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++) monitor.Update(t, Dt);
        }

        static FlightTelemetry Flying(float ias = 100f, float height = 3000f, float vs = 0f, bool stall = false) => new FlightTelemetry
        {
            IndicatedAirspeedKnots = ias, HeightAboveGroundFeet = height, AltitudeFeet = height, VerticalSpeedFeetPerMinute = vs,
            StallWarning = stall, WeightOnWheels = false,
        };

        [Test]
        public void Stall_IsImmediateAndHeldForTheMinimumTime()
        {
            var m = new FlightWarningMonitor();
            m.Update(Flying(stall: true), Dt);
            Assert.IsTrue(m.IsActive(FlightWarning.Stall), "no delay for a stall");
            Run(m, Flying(stall: false), 0.5f);
            Assert.IsTrue(m.IsActive(FlightWarning.Stall), "held for the minimum time");
            Run(m, Flying(stall: false), 1f);
            Assert.IsFalse(m.IsActive(FlightWarning.Stall));
        }

        [Test]
        public void SinkRate_NearTerrainOnly()
        {
            var m = new FlightWarningMonitor();
            Run(m, Flying(height: 500f, vs: -2000f), 1f);
            Assert.IsTrue(m.IsActive(FlightWarning.SinkRate), "2000 fpm down at 500 ft");

            m.Reset();
            Run(m, Flying(height: 500f, vs: -900f), 3f);
            Assert.IsFalse(m.IsActive(FlightWarning.SinkRate), "a normal descent");

            m.Reset();
            Run(m, Flying(height: 4000f, vs: -3500f), 3f);
            Assert.IsFalse(m.IsActive(FlightWarning.SinkRate), "far above terrain");

            m.Reset();
            Run(m, Flying(height: 15f, vs: -1500f), 3f);
            Assert.IsFalse(m.IsActive(FlightWarning.SinkRate), "inhibited in the flare");

            m.Reset();
            var onGround = Flying(height: 0f, vs: -2000f);
            onGround.WeightOnWheels = true;
            Run(m, onGround, 3f);
            Assert.IsFalse(m.IsActive(FlightWarning.SinkRate), "never on the ground");
        }

        [Test]
        public void SinkRate_NeedsToPersistAndHasHysteresis()
        {
            var settings = new FlightWarningSettings();
            var m = new FlightWarningMonitor(settings);
            float limit = settings.SinkRateLimit(500f);
            Run(m, Flying(height: 500f, vs: -(limit + 300f)), 0.5f);
            Assert.IsFalse(m.IsActive(FlightWarning.SinkRate), "a short dip is not a warning");
            Run(m, Flying(height: 500f, vs: 0f), 0.1f);
            Run(m, Flying(height: 500f, vs: -(limit + 300f)), 1f);
            Assert.IsTrue(m.IsActive(FlightWarning.SinkRate));

            Run(m, Flying(height: 500f, vs: -(limit - 100f)), 3f);
            Assert.IsTrue(m.IsActive(FlightWarning.SinkRate), "inside the clear margin: stays on");
            Run(m, Flying(height: 500f, vs: -(limit - 300f)), 0.1f);
            Assert.IsFalse(m.IsActive(FlightWarning.SinkRate));
        }

        [Test]
        public void Overspeed_WithDelayAndHysteresis()
        {
            var m = new FlightWarningMonitor();
            Run(m, Flying(ias: 165f), 0.2f);
            Assert.IsFalse(m.IsActive(FlightWarning.Overspeed));
            Run(m, Flying(ias: 165f), 0.2f);
            Assert.IsTrue(m.IsActive(FlightWarning.Overspeed));
            Run(m, Flying(ias: 158f), 2f);
            Assert.IsTrue(m.IsActive(FlightWarning.Overspeed), "within 3 kt of the limit: stays on");
            Run(m, Flying(ias: 155f), 0.1f);
            Assert.IsFalse(m.IsActive(FlightWarning.Overspeed));
        }

        [Test]
        public void ActiveWarnings_AreOrderedByPriority()
        {
            var m = new FlightWarningMonitor();
            Run(m, Flying(ias: 170f, height: 400f, vs: -3000f, stall: true), 1f);
            Assert.AreEqual(3, m.ActiveCount);
            Assert.AreEqual(FlightWarning.Stall, m.ActiveAt(0));
            Assert.AreEqual(FlightWarning.SinkRate, m.ActiveAt(1));
            Assert.AreEqual(FlightWarning.Overspeed, m.ActiveAt(2));
            Assert.AreEqual(7, m.ActiveMask);

            var custom = new FlightWarningMonitor(new FlightWarningSettings { overspeedPriority = 0, stallPriority = 2 });
            Run(custom, Flying(ias: 170f, stall: true), 1f);
            Assert.AreEqual(FlightWarning.Overspeed, custom.ActiveAt(0));
            Assert.AreEqual(FlightWarning.Stall, custom.ActiveAt(1));
        }

        [Test]
        public void EngineOff_CautionWhenPowerIsExpected()
        {
            var m = new FlightWarningMonitor();
            var airborne = Flying();
            airborne.EngineState = EngineState.Off;
            Run(m, airborne, 0.5f);
            Assert.IsFalse(m.IsActive(FlightWarning.EngineOff), "after a short delay");
            Run(m, airborne, 1f);
            Assert.IsTrue(m.IsActive(FlightWarning.EngineOff));
            Assert.AreEqual(WarningSeverity.Caution, FlightWarnings.Severity(FlightWarning.EngineOff));

            m.Reset();
            var parked = new FlightTelemetry { WeightOnWheels = true, EngineState = EngineState.Off, Throttle = 0f };
            Run(m, parked, 3f);
            Assert.IsFalse(m.IsActive(FlightWarning.EngineOff), "shut down on the ground: normal");
            parked.Throttle = 0.6f;
            Run(m, parked, 1.5f);
            Assert.IsTrue(m.IsActive(FlightWarning.EngineOff), "opening the throttle with the engine off");

            m.Reset();
            airborne.EngineState = EngineState.Starting;
            Run(m, airborne, 3f);
            Assert.IsFalse(m.IsActive(FlightWarning.EngineOff), "starting is not off");
        }

        [Test]
        public void BadSamples_NeverRaiseWarnings()
        {
            var m = new FlightWarningMonitor();
            var t = Flying(ias: float.NaN, height: float.NaN, vs: float.NaN);
            Run(m, t, 2f);
            Assert.AreEqual(0, m.ActiveCount);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var m = new FlightWarningMonitor();
            Run(m, Flying(ias: 170f, stall: true), 1f);
            m.Reset();
            Assert.AreEqual(0, m.ActiveCount);
            Assert.IsFalse(m.AnyActive);
            Assert.AreEqual(0, m.ActiveMask);
        }
    }
}
