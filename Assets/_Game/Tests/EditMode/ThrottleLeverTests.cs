using NUnit.Framework;
using OpenSkies.FlightInput;

namespace OpenSkies.Tests
{
    public class ThrottleLeverTests
    {
        [Test]
        public void Move_ChangesAtFixedRateAndHolds()
        {
            var lever = new ThrottleLever(travelPerSecond: 0.5f);
            lever.Move(1f, 1f);
            Assert.AreEqual(0.5f, lever.Position, 1e-5f);
            lever.Move(0f, 10f);
            Assert.AreEqual(0.5f, lever.Position, 1e-5f, "a throttle stays where it was left");
            lever.Move(-1f, 0.5f);
            Assert.AreEqual(0.25f, lever.Position, 1e-5f);
        }

        [Test]
        public void Move_ClampsToRangeAndIgnoresBadInput()
        {
            var lever = new ThrottleLever(1f, 0.5f);
            lever.Move(1f, 5f);
            Assert.AreEqual(1f, lever.Position);
            lever.Move(-1f, 5f);
            Assert.AreEqual(0f, lever.Position);
            lever.Move(float.NaN, 1f);
            lever.Move(1f, -1f);
            Assert.AreEqual(0f, lever.Position);
        }
    }
}
