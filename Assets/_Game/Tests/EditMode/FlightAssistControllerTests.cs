using System;
using NUnit.Framework;
using OpenSkies.Assistance;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The assistance controller with scripted telemetry: what it may and may not
    /// do, independent of any flight model. Flight behaviour is in
    /// <see cref="FlightAssistScenarioTests"/>.
    /// </summary>
    public class FlightAssistControllerTests
    {
        const float Dt = 0.02f;

        static FlightTelemetry Air(float ias = 94f, float alpha = 2f, float bank = 0f, float q = 0f, float p = 0f, float r = 0f,
            float beta = 0f, float pitch = 2f, float flaps = 0f, bool onGround = false) => new FlightTelemetry
        {
            IndicatedAirspeedKnots = ias, TrueAirspeedKnots = ias, AngleOfAttackDegrees = alpha, BankDegrees = bank,
            PitchRateDegreesPerSecond = q, RollRateDegreesPerSecond = p, YawRateDegreesPerSecond = r, SideslipDegrees = beta,
            PitchDegrees = pitch, FlapPosition = flaps, WeightOnWheels = onGround,
        };

        static FlightAssistController Controller(FlightAssistProfile profile)
        {
            var c = new FlightAssistController(new FlightAssistTuning(), profile);
            c.SetProfile(profile, immediate: true);
            return c;
        }

        static FlightControls Run(FlightAssistController c, FlightControls pilot, FlightTelemetry t, float seconds)
        {
            var output = pilot;
            for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++) output = c.Apply(pilot, t, Dt);
            return output;
        }

        [Test]
        public void Manual_PassesThePilotsCommandsThroughExactly()
        {
            var c = Controller(FlightAssistProfile.Manual);
            var random = new System.Random(7);
            for (int i = 0; i < 2000; i++)
            {
                var pilot = new FlightControls(R(random), R(random), R(random), (float)random.NextDouble(), 0f, 0.5f, true);
                var t = Air(ias: 40f + 100f * (float)random.NextDouble(), alpha: 20f * R(random), bank: 70f * R(random), q: 20f * R(random),
                    p: 40f * R(random), r: 10f * R(random), beta: 8f * R(random), pitch: 40f * R(random));
                var output = c.Apply(pilot, t, Dt);
                Assert.AreEqual(pilot.Pitch, output.Pitch, $"step {i}");
                Assert.AreEqual(pilot.Roll, output.Roll);
                Assert.AreEqual(pilot.Yaw, output.Yaw);
            }
        }

        static float R(System.Random random) => (float)(random.NextDouble() * 2.0 - 1.0);

        [TestCase(FlightAssistProfile.Assisted)]
        [TestCase(FlightAssistProfile.Standard)]
        [TestCase(FlightAssistProfile.Manual)]
        public void NeverTouchesThrottleBrakesFlapsOrIgnition(FlightAssistProfile profile)
        {
            var c = Controller(profile);
            var pilot = new FlightControls(0.1f, -0.2f, 0f, 0.63f, 0.4f, 0.7f, false);
            var output = Run(c, pilot, Air(alpha: 13f, bank: 50f, q: 5f, beta: 4f, pitch: 30f), 5f);
            Assert.AreEqual(0.63f, output.Throttle, "no auto-throttle: there is no hidden autopilot");
            Assert.AreEqual(0.4f, output.Brake);
            Assert.AreEqual(0.7f, output.Flaps);
            Assert.IsFalse(output.Ignition);
        }

        [TestCase(FlightAssistProfile.Assisted)]
        [TestCase(FlightAssistProfile.Standard)]
        public void OnTheGround_NothingIsAdded(FlightAssistProfile profile)
        {
            var c = Controller(profile);
            var pilot = new FlightControls(0.2f, 0f, 0.3f, 1f);
            var output = Run(c, pilot, Air(ias: 60f, beta: 5f, r: 10f, p: 3f, onGround: true), 3f);
            Assert.AreEqual(pilot.Pitch, output.Pitch, "no corrections on the runway");
            Assert.AreEqual(pilot.Roll, output.Roll);
            Assert.AreEqual(pilot.Yaw, output.Yaw, "the rudder also steers the nosewheel: never touched on the ground");
            Assert.AreEqual(0f, c.State.Authority);
        }

        [Test]
        public void EngagesGraduallyAfterLiftOff_AndStaysEngagedWhenSlow()
        {
            var c = Controller(FlightAssistProfile.Assisted);
            var tuning = c.Tuning;
            Run(c, FlightControls.Neutral, Air(ias: 30f), 2f);
            Assert.AreEqual(0f, c.State.Authority, "below the engage speed after lift-off");
            float previous = 0f;
            for (int i = 0; i < 50; i++)
            {
                c.Apply(FlightControls.Neutral, Air(ias: 60f), Dt);
                Assert.That(c.State.Authority - previous, Is.LessThanOrEqualTo(Dt / tuning.engageSeconds + 1e-5f), "fades in");
                previous = c.State.Authority;
            }
            Run(c, FlightControls.Neutral, Air(ias: 60f), 2f);
            Assert.AreEqual(1f, c.State.Authority);
            Run(c, FlightControls.Neutral, Air(ias: 25f, alpha: 14f), 1f);
            Assert.AreEqual(1f, c.State.Authority, "still engaged when slow, so stall prevention keeps working");
            Run(c, FlightControls.Neutral, Air(ias: 25f, onGround: true), 1f);
            Assert.AreEqual(0f, c.State.Authority, "touchdown: off");
        }

        [TestCase(FlightAssistProfile.Assisted)]
        [TestCase(FlightAssistProfile.Standard)]
        public void FullDeflection_AlwaysReachesFullDeflection(FlightAssistProfile profile)
        {
            var c = Controller(profile);
            var disturbed = Air(alpha: 3f, bank: 40f, q: -15f, p: 30f, r: -8f, beta: 6f);
            Run(c, FlightControls.Neutral, disturbed, 3f);
            foreach (float s in new[] { 1f, -1f })
            {
                var output = c.Apply(new FlightControls(0f, s, s, 0.5f), disturbed, Dt);
                Assert.AreEqual(s, output.Roll, 1e-6f, "full roll authority");
                Assert.AreEqual(s, output.Yaw, 1e-6f, "full rudder authority");
                output = c.Apply(new FlightControls(-1f, 0f, 0f, 0.5f), disturbed, Dt);
                Assert.AreEqual(-1f, output.Pitch, 1e-6f, "full nose-down authority");
            }
        }

        [Test]
        public void StrongDeliberateInput_IsNotFought()
        {
            var c = Controller(FlightAssistProfile.Assisted);
            // Rolling fast to the right with the stick held hard right: no roll damping against it.
            var rolling = Air(bank: 20f, p: 40f, beta: 0f);
            Run(c, FlightControls.Neutral, Air(), 2f);
            var output = Run(c, new FlightControls(0f, 0.8f, 0f, 0.5f), rolling, 2f);
            Assert.AreEqual(0.8f, output.Roll, 1e-4f, "the pilot's strong input is delivered unchanged");
            // A light input, rolling at the rate it produces (about 10°/s at cruise): damped, so Assisted
            // rolls more gently, but still clearly in the pilot's direction.
            output = Run(c, new FlightControls(0f, 0.2f, 0f, 0.5f), Air(bank: 20f, p: 10f), 2f);
            Assert.That(output.Roll, Is.LessThan(0.2f).And.GreaterThan(0.1f));
        }

        [Test]
        public void AssistedHoldsTheBankItWasLeftIn_AndLevelsSmallBanks()
        {
            var c = Controller(FlightAssistProfile.Assisted);
            Run(c, FlightControls.Neutral, Air(bank: 22f), 1f);
            Assert.IsTrue(c.State.BankHeld);
            Assert.AreEqual(22f, c.State.BankTarget, 1e-4f);
            var output = Run(c, FlightControls.Neutral, Air(bank: 18f), 1f);
            Assert.That(output.Roll, Is.GreaterThan(0f), "rolls gently back towards the held bank");
            Assert.That(output.Roll, Is.LessThanOrEqualTo(c.Tuning.maxBankCorrection + 1e-5f), "gently");

            c = Controller(FlightAssistProfile.Assisted);
            Run(c, FlightControls.Neutral, Air(bank: 4f), 1f);
            Assert.AreEqual(0f, c.State.BankTarget, "small banks are levelled");
            c = Controller(FlightAssistProfile.Assisted);
            Run(c, FlightControls.Neutral, Air(bank: -60f), 1f);
            Assert.AreEqual(-30f, c.State.BankTarget, "steep banks ease back to 30°");
        }

        [Test]
        public void Standard_NeverLevelsTheWings()
        {
            var c = Controller(FlightAssistProfile.Standard);
            var output = Run(c, FlightControls.Neutral, Air(bank: 25f), 5f);
            Assert.AreEqual(0f, output.Roll, 1e-6f, "no auto-levelling in Standard");
            Assert.IsFalse(c.State.BankHeld);
        }

        [Test]
        public void Coordination_RudderTowardsTheSlip_OnlyInAssistedAndStandard()
        {
            foreach (var profile in new[] { FlightAssistProfile.Assisted, FlightAssistProfile.Standard })
            {
                var c = Controller(profile);
                var output = Run(c, FlightControls.Neutral, Air(beta: 3f), 2f);
                Assert.That(output.Yaw, Is.GreaterThan(0.1f), $"{profile}: airflow from the right, right rudder");
                output = Run(Controller(profile), new FlightControls(0f, 0f, -0.8f, 0.5f), Air(beta: 3f), 2f);
                Assert.AreEqual(-0.8f, output.Yaw, 1e-4f, $"{profile}: deliberate rudder (a slip) is not fought");
            }
            Assert.AreEqual(0f, Run(Controller(FlightAssistProfile.Manual), FlightControls.Neutral, Air(beta: 3f), 2f).Yaw,
                "manual rudder");
        }

        [Test]
        public void StallPrevention_LimitsNoseUpElevator_OnlyWhenAssisted()
        {
            var nearStall = Air(ias: 55f, alpha: 13f, pitch: 10f);
            var assisted = Controller(FlightAssistProfile.Assisted);
            Run(assisted, FlightControls.Neutral, Air(), 2f);
            var output = Run(assisted, new FlightControls(1f, 0f, 0f, 0f), nearStall, 2f);
            Assert.That(output.Pitch, Is.LessThan(0.5f), "full aft stick is limited near the critical angle");
            Assert.That(output.Pitch, Is.GreaterThanOrEqualTo(assisted.Tuning.stallProtectionMinimumElevator - 1e-4f), "gently: never a hard push");
            Assert.That(assisted.State.StallProtection, Is.GreaterThan(0.9f));

            foreach (var profile in new[] { FlightAssistProfile.Standard, FlightAssistProfile.Manual })
            {
                var c = Controller(profile);
                Run(c, FlightControls.Neutral, Air(), 2f);
                Assert.AreEqual(1f, Run(c, new FlightControls(1f, 0f, 0f, 0f), nearStall, 2f).Pitch, 1e-6f, $"{profile}: warnings only");
            }

            var flaps = Controller(FlightAssistProfile.Assisted);
            Run(flaps, FlightControls.Neutral, Air(flaps: 1f), 2f);
            Run(flaps, new FlightControls(1f, 0f, 0f, 0f), Air(ias: 50f, alpha: 11.5f, flaps: 1f), 2f);
            Assert.That(flaps.State.StallProtection, Is.GreaterThan(0.9f), "flaps lower the critical angle: protection earlier");
        }

        [Test]
        public void AutomaticTrim_TakesOverASteadyHold_AndHoldsTheAngleOfAttack()
        {
            var c = Controller(FlightAssistProfile.Assisted);
            Run(c, FlightControls.Neutral, Air(), 2f);
            Run(c, new FlightControls(0.15f, 0f, 0f, 1f), Air(alpha: 4f, q: 0f), 3f); // a steady climb held with the stick
            Assert.AreEqual(0f, c.Trim, 1e-6f, "the trim does not move while the pilot holds the stick");
            Run(c, FlightControls.Neutral, Air(alpha: 4f), 1f);
            Assert.That(c.Trim, Is.GreaterThan(0.1f), "on release the trim takes over most of the held stick");
            Assert.IsTrue(c.State.TrimHeld);
            Assert.AreEqual(4f, c.State.AngleOfAttackTarget, 0.1f);

            float trim = c.Trim;
            Run(c, FlightControls.Neutral, Air(alpha: 3f), 2f);
            Assert.That(c.Trim, Is.GreaterThan(trim), "below the held angle: more nose-up trim");
            Assert.That(c.Trim - trim, Is.LessThanOrEqualTo(c.Tuning.trimRate * 2f + 1e-4f), "at the trim motor's rate");
        }

        [Test]
        public void AutomaticTrim_NeverHoldsAnAngleCloseToTheStall()
        {
            var c = Controller(FlightAssistProfile.Standard);
            Run(c, FlightControls.Neutral, Air(alpha: 12f, ias: 60f), 3f);
            var k = c.Tuning;
            Assert.That(c.State.AngleOfAttackTarget, Is.LessThanOrEqualTo(15f - k.stallProtectionStartMargin - 1f + 1e-4f));
            Assert.That(c.Trim, Is.LessThan(0f), "trims nose down, towards a safe speed");
        }

        [Test]
        public void ManualTrim_MovesAtTheTrimRate_InEveryProfile()
        {
            foreach (FlightAssistProfile profile in Enum.GetValues(typeof(FlightAssistProfile)))
            {
                var c = Controller(profile);
                c.SetTrimInput(1f);
                Run(c, new FlightControls(0.5f, 0f, 0f, 0.5f), Air(), 1f); // stick held: no automatic trim
                Assert.AreEqual(c.Tuning.manualTrimRate, c.Trim, 1e-3f, $"{profile}");
                c.SetTrimInput(0f);
                var output = c.Apply(FlightControls.Neutral, Air(), Dt);
                if (profile == FlightAssistProfile.Manual)
                    Assert.AreEqual(c.Trim, output.Pitch, 1e-6f, "Manual: the stick is neutral, the trim sets the elevator");
            }
        }

        [Test]
        public void ProfileChange_BlendsSmoothly_AndKeepsTheTrim()
        {
            var c = Controller(FlightAssistProfile.Assisted);
            Run(c, new FlightControls(0.15f, 0f, 0f, 1f), Air(alpha: 4f), 3f);
            // Wings level at the held angle of attack (so the automatic trim has nothing to do), with some
            // sideslip and rotation for the damping and coordination to work on.
            var disturbed = Air(alpha: 4f, beta: 1f, q: 1f, p: -2f);
            Run(c, FlightControls.Neutral, disturbed, 2f);
            float trim = c.Trim;
            Assert.That(trim, Is.GreaterThan(0.05f));

            var previous = c.Apply(FlightControls.Neutral, disturbed, Dt);
            c.SetProfile(FlightAssistProfile.Manual);
            Assert.AreEqual(0f, c.State.Blend, "a blend, not a switch");
            float limit = 2f * c.Tuning.correctionRate * Dt + 1e-4f;
            for (int i = 0; i < 200; i++)
            {
                var output = c.Apply(FlightControls.Neutral, disturbed, Dt);
                Assert.That(Mathf.Abs(output.Roll - previous.Roll), Is.LessThanOrEqualTo(limit), $"roll step {i}");
                Assert.That(Mathf.Abs(output.Yaw - previous.Yaw), Is.LessThanOrEqualTo(limit), $"rudder step {i}");
                Assert.That(Mathf.Abs(output.Pitch - previous.Pitch), Is.LessThanOrEqualTo(limit), $"pitch step {i}");
                previous = output;
            }
            Assert.AreEqual(1f, c.State.Blend);
            Assert.AreEqual(trim, c.Trim, 1e-5f, "the trim stays: changing profile never makes the aircraft pitch");
            Assert.AreEqual(trim, c.Apply(FlightControls.Neutral, Air(), Dt).Pitch, 1e-5f, "Manual now: only the trim remains");
        }

        [Test]
        public void BadTelemetry_NeverProducesBadCommands()
        {
            var c = Controller(FlightAssistProfile.Assisted);
            Run(c, FlightControls.Neutral, Air(bank: 20f, beta: 2f), 2f);
            var bad = Air(ias: float.NaN, alpha: float.PositiveInfinity, bank: float.NaN, q: float.NaN, beta: float.NegativeInfinity);
            for (int i = 0; i < 100; i++)
            {
                var output = c.Apply(new FlightControls(0.1f, 0.1f, 0f, 0.5f), bad, Dt);
                Assert.IsFalse(float.IsNaN(output.Pitch) || float.IsNaN(output.Roll) || float.IsNaN(output.Yaw));
            }
            Assert.AreEqual(0f, c.State.Authority, 1e-6f, "unusable data: the assistance steps back smoothly");
            Assert.IsFalse(float.IsNaN(c.Trim));
        }

        [Test]
        public void Reset_StartsAFreshFlight()
        {
            var c = Controller(FlightAssistProfile.Assisted);
            c.SetTrimInput(1f);
            Run(c, FlightControls.Neutral, Air(bank: 20f), 2f);
            c.SetProfile(FlightAssistProfile.Standard);
            c.Reset();
            var s = c.State;
            Assert.AreEqual(0f, s.Trim);
            Assert.AreEqual(0f, s.Authority);
            Assert.AreEqual(1f, s.Blend);
            Assert.AreEqual(1f, s.NoseUpLimit);
            Assert.AreEqual(FlightAssistProfile.Standard, s.Profile, "the player's choice is kept");
            Assert.AreEqual(0.3f, c.Apply(new FlightControls(0.3f, 0f, 0f, 0f), Air(onGround: true), Dt).Pitch, 1e-6f, "trim input cleared");
        }
    }
}
