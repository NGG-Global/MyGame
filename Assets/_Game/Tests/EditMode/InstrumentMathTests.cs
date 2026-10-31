using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class InstrumentMathTests
    {
        [TestCase(0f, 0f)]
        [TestCase(40f, 30f)]
        [TestCase(120f, 180f)]
        [TestCase(200f, 330f)]
        [TestCase(260f, 330f)]
        [TestCase(-5f, 0f)]
        public void Airspeed_DialAngles(float knots, float degrees)
        {
            Assert.AreEqual(degrees, InstrumentMath.AirspeedAngle(knots), 1e-3f);
        }

        [Test]
        public void Altimeter_TwoNeedles()
        {
            Assert.AreEqual(90f, InstrumentMath.AltimeterHundredsAngle(1250f), 1e-3f);
            Assert.AreEqual(45f, InstrumentMath.AltimeterThousandsAngle(1250f), 1e-3f);
            Assert.AreEqual(0f, InstrumentMath.AltimeterHundredsAngle(3000f), 1e-3f);
            Assert.AreEqual(324f, InstrumentMath.AltimeterHundredsAngle(-100f), 1e-3f, "below sea level wraps");
        }

        [Test]
        public void VerticalSpeed_ZeroAtNineOClock()
        {
            Assert.AreEqual(270f, InstrumentMath.VerticalSpeedAngle(0f), 1e-3f);
            Assert.AreEqual(440f, InstrumentMath.VerticalSpeedAngle(2000f), 1e-3f);
            Assert.AreEqual(100f, InstrumentMath.VerticalSpeedAngle(-5000f), 1e-3f, "pegged");
            Assert.That(InstrumentMath.VerticalSpeedAngle(500f), Is.GreaterThan(270f), "climb turns clockwise, upwards");
        }

        [Test]
        public void AttitudeAndHeading_Directions()
        {
            Assert.That(InstrumentMath.AttitudePitchOffset(10f, 6f, 30f), Is.EqualTo(-60f).Within(1e-4f), "nose up: horizon moves down");
            Assert.AreEqual(-180f, InstrumentMath.AttitudePitchOffset(80f, 6f, 30f), 1e-4f, "limited");
            Assert.AreEqual(30f, InstrumentMath.AttitudeRoll(30f), 1e-4f, "right bank turns the horizon counter-clockwise");
            Assert.AreEqual(270f, InstrumentMath.HeadingCardRotation(-90f), 1e-4f);
        }

        [Test]
        public void Map_PositionsTargetsHeadingUp()
        {
            var ahead = InstrumentMath.MapPosition(90f, 90f, 2000f, 4000f, 100f, out bool inRange);
            Assert.IsTrue(inRange);
            Assert.AreEqual(0f, ahead.x, 1e-3f);
            Assert.AreEqual(50f, ahead.y, 1e-3f);

            var right = InstrumentMath.MapPosition(180f, 90f, 1000f, 4000f, 100f, out _);
            Assert.That(right.x, Is.GreaterThan(24f), "bearing right of the heading: right of the aircraft");

            var far = InstrumentMath.MapPosition(0f, 0f, 9000f, 4000f, 100f, out inRange);
            Assert.IsFalse(inRange);
            Assert.AreEqual(100f, far.magnitude, 1e-3f, "pinned to the range ring");
        }

        [Test]
        public void Map_RangeHasHysteresis()
        {
            Assert.AreEqual(2f, InstrumentMath.MapRangeNm(1f, 0f));
            Assert.AreEqual(5f, InstrumentMath.MapRangeNm(1.9f, 2f), "zooms out near the edge");
            Assert.AreEqual(5f, InstrumentMath.MapRangeNm(1.5f, 5f), "does not zoom straight back in");
            Assert.AreEqual(2f, InstrumentMath.MapRangeNm(0.7f, 5f));
            Assert.AreEqual(50f, InstrumentMath.MapRangeNm(500f, 2f));
        }

        [Test]
        public void Readings_ComeFromSharedTelemetryAndGeometry()
        {
            var t = new FlightTelemetry
            {
                IndicatedAirspeedKnots = 92f, AltitudeFeet = 2500f, VerticalSpeedFeetPerMinute = -400f, HeadingDegrees = 10f,
                PitchDegrees = 3f, BankDegrees = -15f, GroundspeedKnots = 95f,
            };
            var runway = RunwayGeometry.Evaluate(new RunwayInfo(Vector3.zero, 0f, 1500f, 30f), new Vector3(0f, 760f, -9000f), 5f, 150f);
            var r = InstrumentReadings.From(t, runway);
            Assert.AreEqual(92f, r.AirspeedKnots);
            Assert.AreEqual(-15f, r.BankDegrees);
            Assert.IsTrue(r.HasRunway);
            Assert.AreEqual(36, r.RunwayDesignator);
            Assert.AreEqual(5f, r.TrackDegrees, 1e-3f, "track from the runway geometry");
            Assert.AreEqual(9000f, r.RunwayDistanceMetres, 1f);

            var noRunway = InstrumentReadings.From(t, default);
            Assert.IsFalse(noRunway.HasRunway);
            Assert.AreEqual(10f, noRunway.TrackDegrees, "falls back to the heading");
        }
    }
}
