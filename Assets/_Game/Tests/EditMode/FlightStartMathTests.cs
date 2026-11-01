using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.Gameplay;
using OpenSkies.Missions;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Scenario starts from the aircraft data: steady flight that the flight model can
    /// actually hold, and a take-off run estimate in line with the offline validation
    /// (docs/FLIGHT_MODEL.md). Ranges are PROTOTYPE expectations for the default trainer.
    /// </summary>
    public class FlightStartMathTests
    {
        AircraftConfiguration _c;

        [SetUp]
        public void SetUp() => _c = TestAircraftData.Create();

        [TearDown]
        public void TearDown() => TestAircraftData.Destroy(_c);

        static float Knots(float metresPerSecond) => FlightUnits.MetresPerSecondToKnots(metresPerSecond);

        [Test]
        public void StickFreeSpeed_IsACruiseSpeedClean_AndAnApproachSpeedWithFullFlap()
        {
            float clean = Knots(FlightStartMath.StickFreeSpeed(_c, 0f));
            float full = Knots(FlightStartMath.StickFreeSpeed(_c, 1f));
            Assert.That(clean, Is.InRange(85f, 105f));
            Assert.That(full, Is.InRange(62f, 78f));
            float stallFull = Knots(Aerodynamics.StallSpeed(_c, 1f));
            Assert.That(full / stallFull, Is.InRange(1.3f, 1.7f), "a sensible approach margin over the stall");
        }

        [Test]
        public void Solve_LevelFlight_IsFeasible_WithModeratePower_AndBalancesTheForces()
        {
            float speed = FlightStartMath.StickFreeSpeed(_c, 0f, 450f);
            var level = FlightStartMath.Solve(_c, speed, 0f, 0f, 450f);
            Assert.IsTrue(level.Feasible);
            Assert.That(level.Throttle, Is.InRange(0.25f, 0.75f));
            Assert.That(level.AngleOfAttack * Mathf.Rad2Deg, Is.InRange(0f, 6f));

            // The same balance, checked with the aerodynamics the aircraft flies with.
            var start = FlightStartMath.Airborne(new Vector3(0f, 450f, 0f), 30f, level, 0f);
            Vector3 bodyVelocity = Quaternion.Inverse(start.Rotation) * start.Velocity;
            var surfaces = new FlightControls(0f, 0f, 0f, level.Throttle, 0f, 0f, true);
            var aero = Aerodynamics.Compute(bodyVelocity, Vector3.zero, surfaces, Atmosphere.Density(450f), _c);
            Vector3 thrust = Vector3.forward * EngineModel.ThrustAt(level.Throttle, aero.Airspeed, _c);
            Vector3 world = start.Rotation * (aero.Force + thrust) + Vector3.down * (_c.mass * 9.81f);
            Assert.That(world.magnitude / (_c.mass * 9.81f), Is.LessThan(0.03f), "less than 3 % of the weight unbalanced");
            Assert.AreEqual(30f, AttitudeMath.HeadingDegrees(start.Rotation), 0.5f);
            Assert.AreEqual(0f, AttitudeMath.BankDegrees(start.Rotation), 0.5f);
        }

        [Test]
        public void FinalApproach_IsAligned_OnTheGlidePath_ConfiguredAndBelowTheStallWarning()
        {
            var runway = new RunwayInfo(Vector3.zero, 0f, 1500f, 30f);
            var settings = new LandingChallengeSettings();
            var request = new MissionStart
            {
                Kind = MissionStartKind.FinalApproach, DistanceBeforeThresholdMetres = 3000f, FlapDetent = -1, GlidepathDegrees = 3f,
                LandingHeadingDegrees = 0f,
            };
            Assert.IsTrue(MissionStarts.TryCreate(request, _c, runway, 150f, settings, out var state, out int detent));
            Assert.AreEqual(_c.flapDetentDegrees.Length - 1, detent, "full flap");
            Assert.AreEqual(1f, state.Flaps, 1e-4f);
            Assert.IsTrue(state.Airborne);
            Assert.AreEqual(0f, state.Position.x, 0.01f, "on the centreline");
            Assert.AreEqual(-750f - 3000f, state.Position.z, 0.01f);
            float glidepath = Mathf.Atan2(state.Position.y, 3000f + 150f) * Mathf.Rad2Deg;
            Assert.AreEqual(3f, glidepath, 0.05f);
            Assert.That(state.Position.y * FlightUnits.FeetPerMetre, Is.InRange(450f, 650f), "a reasonable height");

            float stall = Aerodynamics.StallSpeed(_c, 1f);
            float speed = state.Velocity.magnitude;
            Assert.That(speed / stall, Is.InRange(settings.minSpeedFactor - 0.01f, settings.maxSpeedFactor + 0.01f));
            float descentFpm = -FlightUnits.MetresPerSecondToFeetPerMinute(state.Velocity.y);
            Assert.That(descentFpm, Is.InRange(300f, 450f), "about a 3° descent at approach speed");
            Assert.That(state.Throttle, Is.InRange(0.1f, 0.7f));
            Assert.AreEqual(0f, AttitudeMath.HeadingDegrees(state.Rotation), 0.5f);

            var telemetry = TelemetryMath.Compose(new TelemetrySource
            {
                Velocity = state.Velocity, TrueAirspeed = speed, Rotation = state.Rotation,
                Aerodynamics = Aerodynamics.Compute(Quaternion.Inverse(state.Rotation) * state.Velocity, Vector3.zero,
                    new FlightControls(0f, 0f, 0f, state.Throttle, 0f, state.Flaps, true), Atmosphere.Density(state.Position.y), _c),
                FlapPosition = state.Flaps, AirDensity = Atmosphere.Density(state.Position.y), Altitude = state.Position.y,
                HeightAboveGround = state.Position.y,
            }, _c);
            Assert.IsFalse(telemetry.StallWarning, "a safe start, not a stall");
        }

        [Test]
        public void TakeoffRun_MatchesTheValidatedBehaviour()
        {
            float liftOff = FlightUnits.KnotsToMetresPerSecond(Knots(Aerodynamics.StallSpeed(_c, 0f)) * new TutorialSettings().liftOffFactor);
            float run = FlightStartMath.TakeoffGroundRunMetres(_c, liftOff);
            Assert.That(run, Is.InRange(250f, 420f), "offline validation: lift-off at about 66 kt after about 340 m");
            Assert.That(run, Is.LessThan(1500f * 0.5f), "well within the prototype runway");

            var weak = Object.Instantiate(_c);
            weak.maxStaticThrust = 50f;
            weak.maxPower = 2000f;
            Assert.IsTrue(float.IsPositiveInfinity(FlightStartMath.TakeoffGroundRunMetres(weak, liftOff)), "cannot accelerate: no estimate");
            Object.DestroyImmediate(weak);
        }
    }
}
