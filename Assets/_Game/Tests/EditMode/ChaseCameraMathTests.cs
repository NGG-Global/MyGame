using NUnit.Framework;
using OpenSkies.CameraSystems;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class ChaseCameraMathTests
    {
        [Test]
        public void DesiredPosition_IsBehindAndAbove()
        {
            var p = ChaseCameraMath.DesiredPosition(new Vector3(0f, 100f, 0f), Quaternion.Euler(0f, 90f, 0f), 10f, 3f);
            Assert.AreEqual(-10f, p.x, 1e-4f); // heading east, so behind is west
            Assert.AreEqual(103f, p.y, 1e-4f);
            Assert.AreEqual(0f, p.z, 1e-4f);
        }

        [Test]
        public void DesiredPosition_IgnoresBankAndPitch()
        {
            var level = ChaseCameraMath.DesiredPosition(Vector3.zero, Quaternion.Euler(0f, 30f, 0f), 12f, 4f);
            var banked = ChaseCameraMath.DesiredPosition(Vector3.zero, Quaternion.Euler(-20f, 30f, -45f), 12f, 4f);
            Assert.AreEqual(level.y, banked.y, 1e-4f);
            Assert.AreEqual(level.x, banked.x, 1e-3f);
            Assert.AreEqual(level.z, banked.z, 1e-3f);
        }

        [Test]
        public void DesiredPosition_HandlesVerticalNose()
        {
            var p = ChaseCameraMath.DesiredPosition(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), 10f, 2f);
            Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z));
        }

        [Test]
        public void SmoothingFactor_IsBoundedAndFrameRateIndependent()
        {
            Assert.AreEqual(0f, ChaseCameraMath.SmoothingFactor(5f, 0f));
            Assert.AreEqual(1f, ChaseCameraMath.SmoothingFactor(0f, 0.016f));
            float t = ChaseCameraMath.SmoothingFactor(5f, 0.016f);
            Assert.That(t, Is.InRange(0f, 1f));

            // Two half steps must cover the same distance as one full step.
            float half = ChaseCameraMath.SmoothingFactor(5f, 0.05f);
            float full = ChaseCameraMath.SmoothingFactor(5f, 0.1f);
            Assert.AreEqual(full, 1f - (1f - half) * (1f - half), 1e-5f);
        }

        [Test]
        public void OrbitPosition_WithoutOffsets_IsTheChasePosition()
        {
            var target = new Vector3(10f, 100f, -5f);
            var rotation = Quaternion.Euler(0f, 135f, 0f);
            Assert.That(Vector3.Distance(ChaseCameraMath.OrbitPosition(target, rotation, 14f, 4f, 0f, 0f),
                ChaseCameraMath.DesiredPosition(target, rotation, 14f, 4f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void OrbitPosition_YawSwingsAroundTheTarget_PitchRaisesTheCamera()
        {
            var target = Vector3.zero;
            var rotation = Quaternion.Euler(0f, 0f, 0f); // heading north: behind is -Z
            var behind = ChaseCameraMath.OrbitPosition(target, rotation, 10f, 3f, 0f, 0f);
            Assert.AreEqual(-10f, behind.z, 1e-4f);
            var side = ChaseCameraMath.OrbitPosition(target, rotation, 10f, 3f, 90f, 0f);
            Assert.AreEqual(10f, Mathf.Abs(side.x), 1e-3f, "90° of yaw: beside the aircraft");
            Assert.AreEqual(0f, side.z, 1e-3f);
            Assert.AreEqual(10f, Vector3.Distance(new Vector3(side.x, 0f, side.z), Vector3.zero), 1e-3f, "same distance");
            var above = ChaseCameraMath.OrbitPosition(target, rotation, 10f, 3f, 0f, 60f);
            Assert.That(above.y, Is.GreaterThan(behind.y), "positive pitch looks down from higher up");
            Assert.That(Mathf.Abs(above.z), Is.LessThan(10f));
            var tooFar = ChaseCameraMath.OrbitPosition(target, rotation, 10f, 3f, 0f, 200f);
            Assert.IsFalse(float.IsNaN(tooFar.y), "pitch is clamped");
        }
    }
}
