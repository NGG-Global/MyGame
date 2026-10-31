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
    }
}
