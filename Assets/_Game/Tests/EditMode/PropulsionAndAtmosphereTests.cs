using NUnit.Framework;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class PropulsionAndAtmosphereTests
    {
        AircraftConfiguration _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<AircraftConfiguration>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void Thrust_IncreasesWithThrottle()
        {
            float idle = EngineModel.ThrustAt(0f, 20f, _config);
            float half = EngineModel.ThrustAt(0.5f, 20f, _config);
            float full = EngineModel.ThrustAt(1f, 20f, _config);
            Assert.AreEqual(0f, idle);
            Assert.That(half, Is.GreaterThan(idle));
            Assert.That(full, Is.GreaterThan(half));
        }

        [Test]
        public void Thrust_IsStaticLimitedSlowAndPowerLimitedFast()
        {
            Assert.AreEqual(_config.maxStaticThrust, EngineModel.ThrustAt(1f, 0f, _config), 1e-3f);
            float fast = EngineModel.ThrustAt(1f, 60f, _config);
            Assert.AreEqual(_config.propellerEfficiency * _config.maxPower / 60f, fast, 1e-2f);
            Assert.That(fast, Is.LessThan(_config.maxStaticThrust));
        }

        [Test]
        public void Engine_FollowsThrottleWithLag()
        {
            var engine = new EngineModel(_config);
            engine.Update(1f, 0.02f);
            Assert.That(engine.PowerFraction, Is.InRange(0.001f, 0.2f), "power responds, but not instantly");
            for (int i = 0; i < 500; i++) engine.Update(1f, 0.02f);
            Assert.AreEqual(1f, engine.PowerFraction, 1e-3f);
            Assert.That(engine.Thrust(20f), Is.GreaterThan(0f));

            engine.Reset();
            Assert.AreEqual(0f, engine.PowerFraction);
            Assert.AreEqual(0f, engine.Thrust(20f));
        }

        [Test]
        public void Atmosphere_MatchesStandardValues()
        {
            Assert.AreEqual(1.225f, Atmosphere.Density(0f), 1e-4f);
            Assert.AreEqual(0.9093f, Atmosphere.Density(3000f), 2e-3f); // ISA table value at 3000 m
            Assert.That(Atmosphere.Density(1000f), Is.LessThan(Atmosphere.Density(0f)));
            Assert.AreEqual(1.225f, Atmosphere.Density(-50f), 1e-4f, "clamped below sea level");
        }

        [Test]
        public void IndicatedAirspeed_EqualsTrueAtSeaLevelAndIsLowerAloft()
        {
            Assert.AreEqual(50f, Atmosphere.IndicatedFromTrueAirspeed(50f, Atmosphere.Density(0f)), 1e-3f);
            Assert.That(Atmosphere.IndicatedFromTrueAirspeed(50f, Atmosphere.Density(3000f)), Is.LessThan(50f));
        }
    }
}
