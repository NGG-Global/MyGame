using NUnit.Framework;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class AerodynamicsTests
    {
        AircraftConfiguration _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<AircraftConfiguration>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        /// <summary>Body-axis velocity for a given airspeed and angle of attack (flow from below for positive alpha).</summary>
        static Vector3 Flow(float airspeed, float alphaDegrees)
        {
            float a = alphaDegrees * Mathf.Deg2Rad;
            return new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a)) * airspeed;
        }

        AerodynamicForces Compute(float airspeed, float alphaDegrees, FlightControls surfaces = default, Vector3 angularVelocity = default) =>
            Aerodynamics.Compute(Flow(airspeed, alphaDegrees), angularVelocity, surfaces, Atmosphere.SeaLevelDensity, _config);

        [Test]
        public void ZeroAirflow_ProducesNoForceOrMoment()
        {
            var result = Compute(0f, 5f, new FlightControls(1f, 1f, 1f, 1f));
            Assert.AreEqual(Vector3.zero, result.Force);
            Assert.AreEqual(Vector3.zero, result.Torque);
            Assert.AreEqual(0f, result.Lift.magnitude, 1e-6f);
        }

        [Test]
        public void Lift_IncreasesWithAirspeed()
        {
            float previous = 0f;
            foreach (float speed in new[] { 10f, 20f, 30f, 40f, 50f, 60f })
            {
                float lift = Compute(speed, 5f).Lift.magnitude;
                Assert.That(lift, Is.GreaterThan(previous), $"lift at {speed} m/s");
                previous = lift;
            }
        }

        [Test]
        public void Lift_ScalesWithSquareOfAirspeed()
        {
            float lift30 = Compute(30f, 4f).Lift.magnitude;
            float lift60 = Compute(60f, 4f).Lift.magnitude;
            Assert.AreEqual(4f, lift60 / lift30, 1e-3f);
        }

        [Test]
        public void Lift_IsUpwardInLevelFlight()
        {
            var result = Compute(50f, 3f);
            Assert.That(result.Lift.y, Is.GreaterThan(0f));
            Assert.That(result.Drag.z, Is.LessThan(0f), "drag opposes forward motion");
        }

        [Test]
        public void LiftCoefficient_RisesLinearlyThenFallsPastCriticalAngle()
        {
            float stall = _config.stallAngle;
            float atStall = Aerodynamics.LiftCoefficient(stall * Mathf.Deg2Rad, _config, out float depthAtStall);
            float below = Aerodynamics.LiftCoefficient((stall - 5f) * Mathf.Deg2Rad, _config, out _);
            // Within the stall transition lift keeps falling (beyond it, flat-plate lift k·sin2α takes over).
            float past = Aerodynamics.LiftCoefficient((stall + 3f) * Mathf.Deg2Rad, _config, out float depthPast);
            float deep = Aerodynamics.LiftCoefficient((stall + _config.stallTransition) * Mathf.Deg2Rad, _config, out _);

            Assert.That(atStall, Is.GreaterThan(below));
            Assert.That(past, Is.LessThan(atStall), "lift must drop after the critical angle");
            Assert.That(deep, Is.LessThan(past));
            Assert.AreEqual(0f, depthAtStall);
            Assert.That(depthPast, Is.GreaterThan(0f));
        }

        [Test]
        public void LiftCoefficient_IsContinuousAtCriticalAngle()
        {
            float a = _config.stallAngle * Mathf.Deg2Rad;
            float justBelow = Aerodynamics.LiftCoefficient(a - 1e-4f, _config, out _);
            float justAbove = Aerodynamics.LiftCoefficient(a + 1e-4f, _config, out _);
            Assert.AreEqual(justBelow, justAbove, 1e-3f);
        }

        [Test]
        public void LiftForce_DropsPastCriticalAngleAtSameAirspeed()
        {
            float atStall = Compute(40f, _config.stallAngle).Lift.magnitude;
            float pastStall = Compute(40f, _config.stallAngle + 8f).Lift.magnitude;
            Assert.That(pastStall, Is.LessThan(atStall));
        }

        [Test]
        public void Drag_IncludesInducedDragAndRisesWhenStalled()
        {
            float cdZeroLift = Aerodynamics.DragCoefficient(0f, 0f, 0f, _config);
            float cdWithLift = Aerodynamics.DragCoefficient(1f, 0.1f, 0f, _config);
            Assert.AreEqual(_config.parasiticDrag, cdZeroLift, 1e-6f);
            Assert.That(cdWithLift, Is.GreaterThan(cdZeroLift));

            float attached = Compute(40f, 5f).DragCoefficient;
            float stalled = Compute(40f, _config.stallAngle + 15f).DragCoefficient;
            Assert.That(stalled, Is.GreaterThan(attached * 3f));
        }

        // Unity axes: +X torque lowers the nose, +Y yaws right, +Z rolls left.
        [Test]
        public void Elevator_PitchesAboutXAxis()
        {
            var neutral = Compute(50f, 2f).Torque;
            var noseUp = Compute(50f, 2f, new FlightControls(1f, 0f, 0f, 0f)).Torque;
            var noseDown = Compute(50f, 2f, new FlightControls(-1f, 0f, 0f, 0f)).Torque;
            Assert.That(noseUp.x, Is.LessThan(neutral.x));
            Assert.That(noseDown.x, Is.GreaterThan(neutral.x));
            Assert.AreEqual(neutral.y, noseUp.y, 1e-3f);
            Assert.AreEqual(neutral.z, noseUp.z, 1e-3f);
        }

        [Test]
        public void Ailerons_RollAboutZAxis()
        {
            var neutral = Compute(50f, 2f).Torque;
            var right = Compute(50f, 2f, new FlightControls(0f, 1f, 0f, 0f)).Torque;
            Assert.That(right.z, Is.LessThan(neutral.z), "roll right = negative Z torque");
            Assert.AreEqual(neutral.x, right.x, 1e-3f);
            Assert.AreEqual(neutral.y, right.y, 1e-3f);
        }

        [Test]
        public void Rudder_YawsAboutYAxis()
        {
            var neutral = Compute(50f, 2f).Torque;
            var right = Compute(50f, 2f, new FlightControls(0f, 0f, 1f, 0f)).Torque;
            Assert.That(right.y, Is.GreaterThan(neutral.y), "right rudder = positive Y torque");
            Assert.AreEqual(neutral.x, right.x, 1e-3f);
            Assert.AreEqual(neutral.z, right.z, 1e-3f);
        }

        [Test]
        public void ControlEffectiveness_GrowsWithAirspeed()
        {
            var full = new FlightControls(1f, 0f, 0f, 0f);
            float slow = Compute(15f, 2f, full).Torque.x - Compute(15f, 2f).Torque.x;
            float fast = Compute(45f, 2f, full).Torque.x - Compute(45f, 2f).Torque.x;
            Assert.AreEqual(9f, fast / slow, 1e-2f, "control moment scales with dynamic pressure (V²)");
        }

        [Test]
        public void Damping_OpposesRotation()
        {
            var still = Compute(50f, 2f).Torque;
            var rotating = Compute(50f, 2f, default, new Vector3(0.5f, 0.5f, 0.5f)).Torque;
            Vector3 damping = rotating - still;
            Assert.That(damping.x, Is.LessThan(0f));
            Assert.That(damping.y, Is.LessThan(0f));
            Assert.That(damping.z, Is.LessThan(0f));
        }

        [Test]
        public void PitchStability_RestoresAngleOfAttack()
        {
            // More angle of attack must give a more nose-down (+X) moment.
            Assert.That(Compute(50f, 8f).Torque.x, Is.GreaterThan(Compute(50f, 2f).Torque.x));
        }

        [Test]
        public void Sideslip_ProducesWeathercockAndDihedralMoments()
        {
            // Aircraft moving to its right relative to the nose (airflow from the right).
            Vector3 slipping = new Vector3(5f, 0f, 50f);
            var result = Aerodynamics.Compute(slipping, Vector3.zero, default, Atmosphere.SeaLevelDensity, _config);
            Assert.That(result.Sideslip, Is.GreaterThan(0f));
            Assert.That(result.Torque.y, Is.GreaterThan(0f), "weathercock: nose yaws into the airflow");
            Assert.That(result.Torque.z, Is.GreaterThan(0f), "dihedral: rolls away from the slip");
            Assert.That(result.SideForce.x, Is.LessThan(0f), "side force opposes the slip");
        }
    }
}
