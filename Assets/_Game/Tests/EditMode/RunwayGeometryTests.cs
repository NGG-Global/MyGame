using NUnit.Framework;
using OpenSkies.Awareness;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class RunwayGeometryTests
    {
        static readonly RunwayInfo Runway = new RunwayInfo(Vector3.zero, 0f, 1500f, 30f);

        [TestCase(360f, 36)]
        [TestCase(0f, 36)]
        [TestCase(4f, 36)]
        [TestCase(184f, 18)]
        [TestCase(95f, 10)]
        [TestCase(-90f, 27)]
        public void Designator_FromHeading(float heading, int expected)
        {
            Assert.AreEqual(expected, RunwayGeometry.Designator(heading));
        }

        [Test]
        public void OnFinal_ForRunway36()
        {
            // 2 km south of the south threshold, 10 m right (east) of the centreline, 100 m up, tracking north.
            var r = RunwayGeometry.Evaluate(Runway, new Vector3(10f, 100f, -2750f), 0f, 150f);
            Assert.IsTrue(r.Valid);
            Assert.AreEqual(36, r.Designator);
            Assert.AreEqual(2000f, r.AlongTrackMetres, 0.01f);
            Assert.AreEqual(10f, r.CrossTrackMetres, 0.01f, "positive = right of the centreline");
            Assert.AreEqual(100f, r.HeightAboveThresholdMetres, 0.01f);
            Assert.IsFalse(r.OnRunway);
            Assert.IsTrue(r.GlidepathValid);
            Assert.AreEqual(Mathf.Atan2(100f, 2150f) * Mathf.Rad2Deg, r.GlidepathAngleDegrees, 1e-3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, r.BearingToRunwayDegrees), 0.5f, "the airfield is straight ahead");
        }

        [Test]
        public void TrackingSouth_SelectsTheReciprocalEnd()
        {
            // North of the runway, tracking south: runway 18, threshold at the north end (z = +750).
            var r = RunwayGeometry.Evaluate(Runway, new Vector3(-20f, 50f, 1750f), 185f, 150f);
            Assert.AreEqual(18, r.Designator);
            Assert.AreEqual(1000f, r.AlongTrackMetres, 0.01f);
            Assert.AreEqual(20f, r.CrossTrackMetres, 0.01f, "west of the centreline is right when looking south");
            Assert.AreEqual(5f, r.TrackErrorDegrees, 1e-3f);
            Assert.AreEqual(180f, r.BearingToRunwayDegrees, 1f);
        }

        [Test]
        public void OverTheRunway_IsOnRunwayWithNegativeAlongTrack()
        {
            var r = RunwayGeometry.Evaluate(Runway, new Vector3(5f, 0f, -700f), 0f, 150f);
            Assert.IsTrue(r.OnRunway);
            Assert.AreEqual(-50f, r.AlongTrackMetres, 0.01f);
            Assert.IsTrue(r.GlidepathValid, "still 100 m before the aim point");
            var past = RunwayGeometry.Evaluate(Runway, new Vector3(5f, 0f, -500f), 0f, 150f);
            Assert.IsFalse(past.GlidepathValid);
            var beside = RunwayGeometry.Evaluate(Runway, new Vector3(40f, 0f, -500f), 0f, 150f);
            Assert.IsFalse(beside.OnRunway);
        }

        [Test]
        public void UndefinedRunway_IsInvalid()
        {
            var r = RunwayGeometry.Evaluate(default, new Vector3(0f, 100f, 0f), 0f, 150f);
            Assert.IsFalse(r.Valid);
        }

        [TestCase(3.0f, 2)]
        [TestCase(3.3f, 3)]
        [TestCase(3.6f, 4)]
        [TestCase(2.7f, 1)]
        [TestCase(2.4f, 0)]
        public void Papi_WhiteLightsByGlidepathAngle(float angle, int white)
        {
            Assert.AreEqual(white, ApproachGuidance.PapiWhiteLights(angle, new DisclosureSettings()));
        }

        [Test]
        public void LateralDeviation_IsClampedAndSigned()
        {
            var s = new DisclosureSettings { lateralFullScaleMetres = 150f };
            Assert.AreEqual(0.5f, ApproachGuidance.LateralDeviation(75f, s), 1e-5f);
            Assert.AreEqual(-1f, ApproachGuidance.LateralDeviation(-400f, s), 1e-5f);
        }

        [Test]
        public void SoftRamp_WorksInBothDirectionsAndRejectsNaN()
        {
            Assert.AreEqual(0.5f, SoftLogic.Ramp(5f, 0f, 10f), 1e-6f);
            Assert.AreEqual(0.25f, SoftLogic.Ramp(7.5f, 10f, 0f), 1e-6f);
            Assert.AreEqual(1f, SoftLogic.Ramp(20f, 0f, 10f));
            Assert.AreEqual(0f, SoftLogic.Ramp(float.NaN, 0f, 10f));
            Assert.AreEqual(1f, SoftLogic.Ramp(3f, 3f, 3f), "equal limits: a step up");
        }
    }
}
