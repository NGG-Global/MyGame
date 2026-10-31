using NUnit.Framework;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class FlapsAndEngineTests
    {
        AircraftConfiguration _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<AircraftConfiguration>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        static Vector3 Flow(float speed, float alphaDegrees)
        {
            float a = alphaDegrees * Mathf.Deg2Rad;
            return new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a)) * speed;
        }

        AerodynamicForces Compute(float flaps, float alphaDegrees = 4f, float speed = 35f) =>
            Aerodynamics.Compute(Flow(speed, alphaDegrees), Vector3.zero, new FlightControls(0f, 0f, 0f, 0f, 0f, flaps),
                Atmosphere.SeaLevelDensity, _config);

        [Test]
        public void Flaps_AddLiftAndDrag()
        {
            var up = Compute(0f);
            var full = Compute(1f);
            Assert.That(full.LiftCoefficient, Is.GreaterThan(up.LiftCoefficient + 0.4f));
            Assert.That(full.DragCoefficient, Is.GreaterThan(up.DragCoefficient + 0.03f));
            Assert.That(full.Torque.x, Is.GreaterThan(up.Torque.x), "nose-down pitch moment (+X torque lowers the nose)");
        }

        [Test]
        public void Flaps_LowerTheStallSpeedButStallTheWingEarlier()
        {
            float clean = Aerodynamics.StallSpeed(_config, 0f);
            float full = Aerodynamics.StallSpeed(_config, 1f);
            Assert.That(full, Is.LessThan(clean));
            float cleanKnots = FlightUnits.MetresPerSecondToKnots(clean);
            Assert.That(cleanKnots, Is.InRange(40f, 65f), "plausible for a light trainer (prototype tuning)");

            Aerodynamics.LiftCoefficient((_config.stallAngle - 1f) * Mathf.Deg2Rad, 1f, _config, out float depth);
            Assert.That(depth, Is.GreaterThan(0f), "with full flap the wing is already stalling 1° below the clean critical angle");
        }

        [Test]
        public void FlapsUp_LeaveTheCleanModelUnchanged()
        {
            float a = 5f * Mathf.Deg2Rad;
            Assert.AreEqual(Aerodynamics.LiftCoefficient(a, _config, out _), Aerodynamics.LiftCoefficient(a, 0f, _config, out _), 1e-6f);
            Assert.AreEqual(Aerodynamics.DragCoefficient(0.5f, a, 0f, _config), Aerodynamics.DragCoefficient(0.5f, a, 0f, 0f, _config), 1e-6f);
        }

        [Test]
        public void Ignition_OffStopsTheEngine_OnStartsItAfterCranking()
        {
            var engine = new EngineModel(_config);
            Assert.AreEqual(EngineState.Running, engine.State);
            for (int i = 0; i < 200; i++) engine.Update(1f, true, 0.02f);
            Assert.That(engine.PowerFraction, Is.GreaterThan(0.9f));

            engine.Update(1f, false, 0.02f);
            Assert.AreEqual(EngineState.Off, engine.State);
            for (int i = 0; i < 400; i++) engine.Update(1f, false, 0.02f); // 8 s: power decays with the engine time constant
            Assert.AreEqual(0f, engine.PowerFraction, 1e-3f, "no power with the ignition off, whatever the throttle");

            engine.Update(1f, true, 0.02f);
            Assert.AreEqual(EngineState.Starting, engine.State);
            int steps = Mathf.CeilToInt(_config.engineStartSeconds / 0.02f) + 1;
            for (int i = 0; i < steps - 2; i++) engine.Update(1f, true, 0.02f);
            Assert.AreEqual(EngineState.Starting, engine.State, "still cranking");
            Assert.AreEqual(0f, engine.PowerFraction, 1e-3f);
            for (int i = 0; i < 3; i++) engine.Update(1f, true, 0.02f);
            Assert.AreEqual(EngineState.Running, engine.State);

            engine.Reset();
            Assert.AreEqual(EngineState.Running, engine.State);
            Assert.AreEqual(0f, engine.PowerFraction);
        }
    }
}
