using NUnit.Framework;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class SafeAreaMathTests
    {
        [Test]
        public void FullScreen_GivesFullAnchors()
        {
            Assert.IsTrue(SafeAreaMath.TryGetAnchors(new Rect(0, 0, 1920, 1080), 1920, 1080, out var min, out var max));
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        [Test]
        public void LandscapeNotch_InsetsSidesAndBottom()
        {
            // 2400x1080 landscape screen: 80 px camera cutout on each side, 48 px gesture bar at the bottom.
            Assert.IsTrue(SafeAreaMath.TryGetAnchors(new Rect(80, 48, 2240, 1032), 2400, 1080, out var min, out var max));
            Assert.AreEqual(80f / 2400f, min.x, 1e-5f);
            Assert.AreEqual(48f / 1080f, min.y, 1e-5f);
            Assert.AreEqual(2320f / 2400f, max.x, 1e-5f);
            Assert.AreEqual(1f, max.y, 1e-5f);
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
