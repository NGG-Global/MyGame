using NUnit.Framework;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class FlightStateTests
    {
        [Test]
        public void FlightControls_ClampAndRejectNaN()
        {
            var c = new FlightControls(2f, -3f, float.NaN, 1.5f, -1f);
            Assert.AreEqual(1f, c.Pitch);
            Assert.AreEqual(-1f, c.Roll);
            Assert.AreEqual(0f, c.Yaw);
            Assert.AreEqual(1f, c.Throttle);
            Assert.AreEqual(0f, c.Brake);
            Assert.AreEqual(1f, new FlightControls(0f, 0f, 0f, 0f, 3f).Brake);
        }

        [Test]
        public void NoFlightAssist_PassesCommandsThrough()
        {
            var pilot = new FlightControls(0.3f, -0.2f, 0.1f, 0.8f, 0.5f);
            var result = NoFlightAssist.Instance.Apply(pilot, default, 0.02f);
            Assert.AreEqual(pilot.Pitch, result.Pitch);
            Assert.AreEqual(pilot.Roll, result.Roll);
            Assert.AreEqual(pilot.Yaw, result.Yaw);
            Assert.AreEqual(pilot.Throttle, result.Throttle);
            Assert.AreEqual(pilot.Brake, result.Brake);
        }

        [Test]
        public void Units_MatchDefinitions()
        {
            Assert.AreEqual(1f, FlightUnits.MetresPerSecondToKnots(1852f / 3600f), 1e-5f);
            Assert.AreEqual(1f, FlightUnits.MetresToFeet(0.3048f), 1e-5f);
            Assert.AreEqual(60f, FlightUnits.MetresPerSecondToFeetPerMinute(0.3048f), 1e-3f);
            Assert.AreEqual(50f, FlightUnits.KnotsToMetresPerSecond(FlightUnits.MetresPerSecondToKnots(50f)), 1e-4f);
        }

        [TestCase(0f, 0f)]
        [TestCase(90f, 90f)]
        [TestCase(180f, 180f)]
        [TestCase(-90f, 270f)]
        [TestCase(359f, 359f)]
        public void Heading_IsCompassStyle(float yaw, float expected)
        {
            Assert.AreEqual(expected, AttitudeMath.HeadingDegrees(Quaternion.Euler(0f, yaw, 0f)), 1e-3f);
        }

        [Test]
        public void PitchAndBank_SignConventions()
        {
            // Unity: negative X rotation raises the nose; negative Z rotation drops the right wing.
            Assert.That(AttitudeMath.PitchDegrees(Quaternion.Euler(-10f, 0f, 0f)), Is.EqualTo(10f).Within(1e-3f));
            Assert.That(AttitudeMath.BankDegrees(Quaternion.Euler(0f, 0f, -20f)), Is.EqualTo(20f).Within(1e-3f));
        }
    }
}
