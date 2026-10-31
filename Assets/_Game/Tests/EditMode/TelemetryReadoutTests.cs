using NUnit.Framework;
using OpenSkies.FlightState;
using OpenSkies.UI;

namespace OpenSkies.Tests
{
    public class TelemetryReadoutTests
    {
        static FlightTelemetry Telemetry(float knots, float feet, float vs = 0f, float aoa = 4.4f, bool stall = false) =>
            new FlightTelemetry
            {
                IndicatedAirspeedKnots = knots,
                AltitudeFeet = feet,
                VerticalSpeedFeetPerMinute = vs,
                AngleOfAttackDegrees = aoa,
                Throttle = 0.5f,
                StallWarning = stall,
            };

        [Test]
        public void FormatsPilotValues()
        {
            var readout = new TelemetryReadout();
            Assert.IsTrue(readout.Update(Telemetry(95.4f, 1234f, 520f), out var text));
            Assert.AreEqual("IAS 95 kt   ALT 1230 ft   VS +500 fpm   PWR 50%   AOA 4°", text);
        }

        [Test]
        public void ShowsStallWarning()
        {
            var readout = new TelemetryReadout();
            readout.Update(Telemetry(48f, 2000f, -300f, 13f, stall: true), out var text);
            StringAssert.EndsWith("STALL", text);
            StringAssert.Contains("VS -300 fpm", text);
        }

        [Test]
        public void OnlyRebuildsWhenRoundedValuesChange()
        {
            var readout = new TelemetryReadout();
            readout.Update(Telemetry(95.4f, 1234f), out var first);
            Assert.IsFalse(readout.Update(Telemetry(95.2f, 1232f), out var same));
            Assert.AreSame(first, same);
            Assert.IsTrue(readout.Update(Telemetry(96f, 1232f), out _));
            Assert.IsTrue(readout.Update(Telemetry(96f, 1232f, stall: true), out _));
        }
    }
}
