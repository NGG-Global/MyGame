using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class SafeAreaMathTests
    {
        [Test]
        public void FullScreen_GivesFullAnchors()
        {
            Assert.IsTrue(SafeAreaMath.TryGetAnchors(new Rect(0, 0, 1080, 1920), 1080, 1920, out var min, out var max));
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        [Test]
        public void Notch_InsetsTopAndBottom()
        {
            // 1080x2400 portrait screen with an 80px notch and a 48px home indicator.
            Assert.IsTrue(SafeAreaMath.TryGetAnchors(new Rect(0, 48, 1080, 2272), 1080, 2400, out var min, out var max));
            Assert.AreEqual(0f, min.x, 1e-5f);
            Assert.AreEqual(0.02f, min.y, 1e-5f);
            Assert.AreEqual(1f, max.x, 1e-5f);
            Assert.AreEqual(2320f / 2400f, max.y, 1e-5f);
        }

        [Test]
        public void InvalidScreen_ReturnsFalseAndFullAnchors()
        {
            Assert.IsFalse(SafeAreaMath.TryGetAnchors(new Rect(0, 0, 10, 10), 0, 0, out var min, out var max));
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }
    }
}
