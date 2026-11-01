using NUnit.Framework;
using OpenSkies.Assistance;
using OpenSkies.FlightInput;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>The pure functions behind assistance, sensitivity and the shared telemetry.</summary>
    public class AssistMathTests
    {
        [Test]
        public void OverrideWeight_FullNearCentre_GoneForStrongInput_Monotonic()
        {
            Assert.AreEqual(1f, AssistMath.OverrideWeight(0f, 0.15f, 0.7f));
            Assert.AreEqual(1f, AssistMath.OverrideWeight(-0.15f, 0.15f, 0.7f));
            Assert.AreEqual(0f, AssistMath.OverrideWeight(0.7f, 0.15f, 0.7f), "strong deliberate input: no stabilisation");
            Assert.AreEqual(0f, AssistMath.OverrideWeight(-1f, 0.15f, 0.7f));
            float previous = 1f;
            for (float s = 0f; s <= 1f; s += 0.05f)
            {
                float w = AssistMath.OverrideWeight(s, 0.15f, 0.7f);
                Assert.That(w, Is.LessThanOrEqualTo(previous + 1e-6f));
                previous = w;
            }
            Assert.AreEqual(1f, AssistMath.OverrideWeight(float.NaN, 0.15f, 0.7f), "bad input counts as released");
        }

        [Test]
        public void Compose_KeepsFullAuthority_IsContinuousAndMonotonic()
        {
            foreach (float b in new[] { -0.6f, -0.2f, 0f, 0.3f, 0.95f, 2f })
            {
                Assert.AreEqual(1f, AssistMath.Compose(1f, b), 1e-6f, $"full stick reaches +1 with base {b}");
                Assert.AreEqual(-1f, AssistMath.Compose(-1f, b), 1e-6f, $"full stick reaches -1 with base {b}");
                Assert.AreEqual(Mathf.Clamp(b, -0.95f, 0.95f), AssistMath.Compose(0f, b), 1e-6f, "centre gives the base");
                float previous = AssistMath.Compose(-1f, b);
                for (int i = 1; i <= 200; i++)
                {
                    float value = AssistMath.Compose(-1f + i * 0.01f, b);
                    Assert.That(value, Is.GreaterThanOrEqualTo(previous), "monotonic");
                    Assert.That(value - previous, Is.LessThan(0.03f), "continuous: slope at most 1.95");
                    previous = value;
                }
            }
            for (float s = -1f; s <= 1f; s += 0.125f)
                Assert.AreEqual(s, AssistMath.Compose(s, 0f), "no base: the stick passes through exactly");
        }

        [Test]
        public void RateLimit_MovesAtMostTheRate()
        {
            Assert.AreEqual(0.03f, AssistMath.RateLimit(0f, 1f, 1.5f, 0.02f), 1e-6f);
            Assert.AreEqual(-0.03f, AssistMath.RateLimit(0f, -1f, 1.5f, 0.02f), 1e-6f);
            Assert.AreEqual(0.01f, AssistMath.RateLimit(0f, 0.01f, 1.5f, 0.02f), 1e-6f);
            Assert.AreEqual(0.2f, AssistMath.RateLimit(0.2f, float.NaN, 1.5f, 0.02f), "bad target: hold");
        }

        [Test]
        public void BankTarget_LevelsSmallBanks_HoldsModerate_EasesSteepBack()
        {
            Assert.AreEqual(0f, AssistMath.BankTarget(4f, 6f, 30f));
            Assert.AreEqual(0f, AssistMath.BankTarget(-5.9f, 6f, 30f));
            Assert.AreEqual(20f, AssistMath.BankTarget(20f, 6f, 30f));
            Assert.AreEqual(-30f, AssistMath.BankTarget(-30f, 6f, 30f));
            Assert.AreEqual(30f, AssistMath.BankTarget(70f, 6f, 30f), "steep: back to the held limit, same side");
            Assert.AreEqual(-30f, AssistMath.BankTarget(-50f, 6f, 30f));
        }

        [Test]
        public void TurnAngleOfAttack_AddsTheLoadFactor()
        {
            Assert.AreEqual(2f, AssistMath.TurnAngleOfAttack(2f, -3.5f, 0f), 1e-5f, "wings level: unchanged");
            // 60°: twice the lift, so twice the angle above the zero-lift angle.
            Assert.AreEqual(-3.5f + 2f * 5.5f, AssistMath.TurnAngleOfAttack(2f, -3.5f, 60f), 1e-3f);
            Assert.AreEqual(AssistMath.TurnAngleOfAttack(2f, -3.5f, 60f), AssistMath.TurnAngleOfAttack(2f, -3.5f, -85f), 1e-4f, "capped at 60°");
        }

        [Test]
        public void StallAndAttitudeProtection_Levels()
        {
            Assert.AreEqual(0f, AssistMath.StallProtection(5f, 15f, 0f, 0.5f, 6f, 2f), "far from the stall");
            Assert.AreEqual(1f, AssistMath.StallProtection(13.5f, 15f, 0f, 0.5f, 6f, 2f), "within the full margin");
            Assert.AreEqual(0.5f, AssistMath.StallProtection(11f, 15f, 0f, 0.5f, 6f, 2f), 1e-5f);
            Assert.That(AssistMath.StallProtection(9f, 15f, 10f, 0.5f, 6f, 2f), Is.GreaterThan(0.4f), "a fast pull is anticipated");
            Assert.AreEqual(0f, AssistMath.StallProtection(9f, 15f, -10f, 0.5f, 6f, 2f), "pitching down: no lead");

            Assert.AreEqual(0f, AssistMath.AttitudeProtection(10f, 15f, 30f));
            Assert.AreEqual(0.5f, AssistMath.AttitudeProtection(22.5f, 15f, 30f), 1e-5f);
            Assert.AreEqual(1f, AssistMath.AttitudeProtection(60f, 15f, 30f));
            Assert.AreEqual(1f, AssistMath.NoseUpLimit(0f, 0.15f));
            Assert.AreEqual(0.15f, AssistMath.NoseUpLimit(1f, 0.15f), 1e-6f);
        }

        [Test]
        public void CoordinatedYawRate_TurnsTowardsTheLowWing()
        {
            Assert.AreEqual(0f, AssistMath.CoordinatedYawRate(0f, 50f), 1e-6f);
            float right = AssistMath.CoordinatedYawRate(30f, 50f);
            Assert.AreEqual(9.81f * 0.5f / 50f * Mathf.Rad2Deg, right, 1e-3f);
            Assert.AreEqual(-right, AssistMath.CoordinatedYawRate(-30f, 50f), 1e-5f);
            Assert.AreEqual(0f, AssistMath.CoordinatedYawRate(30f, 0.5f), "no airspeed, no turn rate");
        }

        // ---- Sensitivity ------------------------------------------------------------------

        [TestCase(ControlSensitivity.Low)]
        [TestCase(ControlSensitivity.Medium)]
        [TestCase(ControlSensitivity.High)]
        public void Sensitivity_KeepsFullAuthority_IsOddAndMonotonic(ControlSensitivity sensitivity)
        {
            Assert.AreEqual(0f, ControlResponse.Shape(0f, sensitivity));
            Assert.AreEqual(1f, ControlResponse.Shape(1f, sensitivity), 1e-6f, "full deflection, full command");
            Assert.AreEqual(-1f, ControlResponse.Shape(-1f, sensitivity), 1e-6f);
            float previous = -1f;
            for (float x = -1f; x <= 1.0001f; x += 0.02f)
            {
                float y = ControlResponse.Shape(x, sensitivity);
                Assert.That(y, Is.GreaterThanOrEqualTo(previous - 1e-6f));
                Assert.AreEqual(-y, ControlResponse.Shape(-x, sensitivity), 1e-6f, "the same both ways");
                previous = y;
            }
            Assert.AreEqual(0f, ControlResponse.Shape(float.NaN, sensitivity));
        }

        [Test]
        public void Sensitivity_LowIsSofter_HighIsQuicker_MediumIsDirect()
        {
            Assert.AreEqual(0.3f, ControlResponse.Shape(0.3f, ControlSensitivity.Medium));
            Assert.That(ControlResponse.Shape(0.3f, ControlSensitivity.Low), Is.LessThan(0.2f));
            Assert.That(ControlResponse.Shape(0.3f, ControlSensitivity.High), Is.GreaterThan(0.4f));
            var pilot = new FlightControls(0.3f, -0.3f, 0.3f, 0.7f, 0.2f, 0.5f, false);
            var shaped = ControlResponse.Apply(pilot, ControlSensitivity.Low);
            Assert.AreEqual(0.7f, shaped.Throttle);
            Assert.AreEqual(0.2f, shaped.Brake);
            Assert.AreEqual(0.5f, shaped.Flaps);
            Assert.IsFalse(shaped.Ignition);
            Assert.AreEqual(-shaped.Pitch, shaped.Roll, 1e-6f);
        }

        // ---- Shared telemetry -------------------------------------------------------------------

        [Test]
        public void Telemetry_BodyRatesUsePilotConventions()
        {
            var config = TestAircraftData.Create();
            try
            {
                var source = new TelemetrySource
                {
                    Velocity = new Vector3(0f, 0f, 50f), TrueAirspeed = 50f, Rotation = Quaternion.identity,
                    // Unity body axes: +X lowers the nose, +Y yaws right, +Z rolls left.
                    BodyAngularVelocity = new Vector3(-0.1f, 0.2f, -0.3f), AirDensity = 1.225f, Controls = FlightControls.Neutral,
                };
                var t = TelemetryMath.Compose(source, config);
                Assert.AreEqual(0.1f * Mathf.Rad2Deg, t.PitchRateDegreesPerSecond, 1e-3f, "nose up");
                Assert.AreEqual(0.2f * Mathf.Rad2Deg, t.YawRateDegreesPerSecond, 1e-3f, "nose right");
                Assert.AreEqual(0.3f * Mathf.Rad2Deg, t.RollRateDegreesPerSecond, 1e-3f, "right wing down");
            }
            finally { TestAircraftData.Destroy(config); }
        }

        [Test]
        public void Telemetry_StallWarningMovesWithTheFlaps()
        {
            var config = TestAircraftData.Create();
            try
            {
                // 11° angle of attack: clean, 4° below the 15° critical angle (no warning); full flap
                // lowers the critical angle to 13°, inside the 3° warning margin.
                var aero = new AerodynamicForces { AngleOfAttack = 11f * Mathf.Deg2Rad, Airspeed = 30f };
                var source = new TelemetrySource { TrueAirspeed = 30f, Rotation = Quaternion.identity, Aerodynamics = aero, AirDensity = 1.225f };
                Assert.IsFalse(TelemetryMath.Compose(source, config).StallWarning);
                source.FlapPosition = 1f;
                Assert.IsTrue(TelemetryMath.Compose(source, config).StallWarning);
                source.WeightOnWheels = true;
                Assert.IsFalse(TelemetryMath.Compose(source, config).StallWarning, "never on the ground");
            }
            finally { TestAircraftData.Destroy(config); }
        }

        [Test]
        public void Envelope_ComesFromTheAircraftData()
        {
            var config = TestAircraftData.Create();
            try
            {
                var envelope = TelemetryMath.Envelope(config, 160f);
                Assert.AreEqual(config.stallAngle, envelope.CriticalAngleOfAttackClean);
                Assert.AreEqual(config.stallAngle + config.flapStallAngleChange, envelope.CriticalAngleOfAttackFullFlap);
                Assert.AreEqual(14f, envelope.CriticalAngleOfAttack(0.5f), 1e-5f);
                Assert.AreEqual(-config.liftCoefficientAtZeroAlpha / config.liftCurveSlope * Mathf.Rad2Deg, envelope.ZeroLiftAngleOfAttackClean, 1e-4f);
                Assert.AreEqual(FlightUnits.MetresPerSecondToKnots(Aerodynamics.StallSpeed(config, 0f)), envelope.StallSpeedCleanKnots, 1e-3f);
                Assert.AreEqual(160f, envelope.NeverExceedSpeedKnots);
                // The fallback values in FlightState match the default aircraft, within rounding.
                var fallback = AircraftEnvelope.PrototypeTrainer;
                Assert.AreEqual(fallback.CriticalAngleOfAttackClean, envelope.CriticalAngleOfAttackClean, 0.01f);
                Assert.AreEqual(fallback.ZeroLiftAngleOfAttackFullFlap, envelope.ZeroLiftAngleOfAttackFullFlap, 0.1f);
                Assert.AreEqual(fallback.StallSpeedCleanKnots, envelope.StallSpeedCleanKnots, 1f);
                Assert.AreEqual(fallback.StallSpeedFullFlapKnots, envelope.StallSpeedFullFlapKnots, 1f);
            }
            finally { TestAircraftData.Destroy(config); }
        }
    }
}
