using NUnit.Framework;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class LandingGearTests
    {
        AircraftConfiguration _config;
        WheelDefinition _main;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<AircraftConfiguration>();
            _main = _config.wheels[0];
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        Vector3 Force(float compression, Vector3 velocity, float brake = 0f) =>
            LandingGear.ContactForce(compression, velocity, Vector3.up, Vector3.forward, brake, _main, _config);

        [Test]
        public void NoCompression_NoForce()
        {
            Assert.AreEqual(Vector3.zero, Force(0f, Vector3.zero));
        }

        [Test]
        public void Suspension_PushesUpMoreWhenCompressedMore()
        {
            float light = Force(0.05f, Vector3.zero).y;
            float heavy = Force(0.1f, Vector3.zero).y;
            Assert.That(light, Is.GreaterThan(0f));
            Assert.That(heavy, Is.GreaterThan(light));
            Assert.AreEqual(_main.springRate * 0.1f, heavy, 1e-2f);
        }

        [Test]
        public void Suspension_NeverPullsDown()
        {
            // Wheel moving up away from the ground fast: damper would pull, but contact cannot.
            Assert.That(Force(0.01f, new Vector3(0f, 10f, 0f)).y, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void Brakes_OpposeRollingMoreThanRollingResistance()
        {
            Vector3 rolling = Force(0.1f, new Vector3(0f, 0f, 20f));
            Vector3 braking = Force(0.1f, new Vector3(0f, 0f, 20f), brake: 1f);
            Assert.That(rolling.z, Is.LessThan(0f));
            Assert.That(braking.z, Is.LessThan(rolling.z));
        }

        [Test]
        public void SideForce_OpposesSidewaysSlip()
        {
            Assert.That(Force(0.1f, new Vector3(1f, 0f, 10f)).x, Is.LessThan(0f));
            Assert.That(Force(0.1f, new Vector3(-1f, 0f, 10f)).x, Is.GreaterThan(0f));
        }

        [Test]
        public void AtRest_NoHorizontalForce()
        {
            Vector3 f = Force(0.1f, Vector3.zero, brake: 1f);
            Assert.AreEqual(0f, f.x, 1e-4f);
            Assert.AreEqual(0f, f.z, 1e-4f);
        }
    }
}
