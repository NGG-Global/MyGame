using System;
using System.Reflection;
using NUnit.Framework;
using OpenSkies.Assistance;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Repeatable flight scenarios in the <see cref="FlightLab"/> (the game's own
    /// flight model and telemetry, deterministic, 50 Hz) for each assistance
    /// profile. Every scenario also checks, at every step, that assistance never
    /// produces an abrupt or invalid command: each correction moves no faster than
    /// its rate limit, the trim no faster than its motor, and nothing is NaN.
    ///
    /// The thresholds describe the intended behaviour with margin; the measured
    /// values are in docs/FLIGHT_ASSISTANCE.md. All aircraft data are prototype
    /// tuning values, so these are design checks, not certification.
    /// </summary>
    public class FlightAssistScenarioTests
    {
        AircraftConfiguration _config;

        [SetUp]
        public void SetUp() => _config = TestAircraftData.Create();

        [TearDown]
        public void TearDown() => TestAircraftData.Destroy(_config);

        static FlightControls Stick(float pitch, float roll, float throttle, float flaps = 0f, float yaw = 0f) =>
            new FlightControls(pitch, roll, yaw, throttle, 0f, flaps, true);

        /// <summary>Flies one scenario and checks smoothness on every step.</summary>
        sealed class Flight
        {
            public readonly FlightLab Lab;
            public readonly FlightAssistController Assist;
            public readonly float LevelThrottle;
            readonly float _correctionStep, _trimStep;
            FlightAssistState _previous;
            FlightControls _previousCommand;
            bool _started;

            public float MaxAngleOfAttack, MaxPitch, MaxBank, MaxSideslip, MaxCommandStepWhilePilotSteady;
            public float StallWarningSeconds;

            public Flight(AircraftConfiguration config, FlightAssistProfile profile, float altitude = 1000f, float elevator = 0f, float flaps = 0f)
            {
                Lab = FlightLab.Level(config, altitude, out LevelThrottle, elevator, flaps);
                Assist = new FlightAssistController(new FlightAssistTuning(), profile);
                Assist.SetProfile(profile, immediate: true);
                Lab.Assist = Assist;
                var k = Assist.Tuning;
                _correctionStep = k.correctionRate * FlightLab.Dt + 1e-5f;
                _trimStep = (k.correctionRate + k.trimRate + k.manualTrimRate) * FlightLab.Dt + 1e-5f;
            }

            public FlightTelemetry T => Lab.Telemetry;

            /// <summary>Flies on; <paramref name="pilotSteady"/> says when the pilot's input is not changing (for the command-step check).</summary>
            public void Fly(float seconds, Func<FlightLab, FlightControls> pilot, Func<FlightLab, bool> pilotSteady = null, Action<FlightLab> each = null)
            {
                Lab.Run(seconds, pilot, lab =>
                {
                    var s = Assist.State;
                    var t = lab.Telemetry;
                    var c = lab.LastCommands;
                    Assert.IsFalse(float.IsNaN(c.Pitch) || float.IsNaN(c.Roll) || float.IsNaN(c.Yaw), $"NaN command at {lab.Time:0.00} s");
                    if (_started)
                    {
                        Assert.That(Mathf.Abs(s.PitchCorrection - _previous.PitchCorrection), Is.LessThanOrEqualTo(_correctionStep), $"pitch correction jump at {lab.Time:0.00} s");
                        Assert.That(Mathf.Abs(s.RollCorrection - _previous.RollCorrection), Is.LessThanOrEqualTo(_correctionStep), $"roll correction jump at {lab.Time:0.00} s");
                        Assert.That(Mathf.Abs(s.RudderCorrection - _previous.RudderCorrection), Is.LessThanOrEqualTo(_correctionStep), $"rudder correction jump at {lab.Time:0.00} s");
                        Assert.That(Mathf.Abs(s.NoseUpLimit - _previous.NoseUpLimit), Is.LessThanOrEqualTo(_correctionStep), $"limit jump at {lab.Time:0.00} s");
                        Assert.That(Mathf.Abs(s.Trim - _previous.Trim), Is.LessThanOrEqualTo(_trimStep), $"trim jump at {lab.Time:0.00} s");
                        if (pilotSteady == null || pilotSteady(lab))
                        {
                            float step = Mathf.Max(Mathf.Abs(c.Pitch - _previousCommand.Pitch),
                                Mathf.Max(Mathf.Abs(c.Roll - _previousCommand.Roll), Mathf.Abs(c.Yaw - _previousCommand.Yaw)));
                            MaxCommandStepWhilePilotSteady = Mathf.Max(MaxCommandStepWhilePilotSteady, step);
                        }
                    }
                    _previous = s;
                    _previousCommand = c;
                    _started = true;
                    MaxAngleOfAttack = Mathf.Max(MaxAngleOfAttack, t.AngleOfAttackDegrees);
                    MaxPitch = Mathf.Max(MaxPitch, t.PitchDegrees);
                    MaxBank = Mathf.Max(MaxBank, Mathf.Abs(t.BankDegrees));
                    MaxSideslip = Mathf.Max(MaxSideslip, Mathf.Abs(t.SideslipDegrees));
                    if (t.StallWarning) StallWarningSeconds += FlightLab.Dt;
                    each?.Invoke(lab);
                });
            }
        }

        static readonly FlightAssistProfile[] Profiles = { FlightAssistProfile.Assisted, FlightAssistProfile.Standard, FlightAssistProfile.Manual };

        /// <summary>Largest change of a command per 20 ms step while the pilot's input is steady: well below a jump.</summary>
        const float SmoothCommandStep = 0.035f;

        // ---- Steady cruise ---------------------------------------------------------------------

        [Test]
        public void SteadyCruise_HandsOff_EveryProfileHoldsTheTrimmedFlight()
        {
            foreach (var profile in Profiles)
            {
                var f = new Flight(_config, profile);
                float altitude = f.Lab.AltitudeFeet, speed = f.T.IndicatedAirspeedKnots;
                f.Fly(60f, l => Stick(0f, 0f, f.LevelThrottle));
                Assert.That(Mathf.Abs(f.Lab.AltitudeFeet - altitude), Is.LessThan(30f), $"{profile}: altitude");
                Assert.That(Mathf.Abs(f.T.IndicatedAirspeedKnots - speed), Is.LessThan(2f), $"{profile}: airspeed");
                Assert.That(f.MaxBank, Is.LessThan(1f), $"{profile}: wings level");
                Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(0.005f), $"{profile}: nothing to correct, nothing moves");
            }
        }

        [Test]
        public void PitchDisturbance_EveryProfileSettles_AssistedAndStandardWithoutDivergence()
        {
            foreach (var profile in Profiles)
            {
                var f = new Flight(_config, profile);
                f.Fly(5f, l => Stick(0f, 0f, f.LevelThrottle));
                float early = 0f, late = 0f, earlyLow = 999f, earlyHigh = 0f, lateLow = 999f, lateHigh = 0f;
                f.Fly(80f, l => Stick(l.Time < 6.2f ? 0.3f : 0f, 0f, f.LevelThrottle), l => l.Time > 6.5f, l =>
                {
                    float ias = l.Telemetry.IndicatedAirspeedKnots;
                    if (l.Time > 10f && l.Time < 40f) { earlyLow = Mathf.Min(earlyLow, ias); earlyHigh = Mathf.Max(earlyHigh, ias); }
                    if (l.Time > 55f) { lateLow = Mathf.Min(lateLow, ias); lateHigh = Mathf.Max(lateHigh, ias); }
                });
                early = earlyHigh - earlyLow;
                late = lateHigh - lateLow;
                Assert.That(late, Is.LessThanOrEqualTo(early + 0.5f), $"{profile}: the speed oscillation dies away ({early:0.0} kt then {late:0.0} kt)");
                Assert.That(late, Is.LessThan(15f), $"{profile}: settled");
                Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep), $"{profile}: smooth");
            }
        }

        // ---- Banked turns ------------------------------------------------------------------------

        [Test]
        public void BankedTurn_Released_AssistedHoldsIt_StandardDoesNotLevel_ManualIsTheAircraftAlone()
        {
            var results = new float[3];
            for (int i = 0; i < Profiles.Length; i++)
            {
                var profile = Profiles[i];
                var f = new Flight(_config, profile);
                float altitude = f.Lab.AltitudeFeet, lowest = altitude, bankAt10 = 0f, bankAt30 = 0f, slip = 0f;
                f.Fly(60f, l => Stick(0f, l.Time < 1.5f ? 0.5f : 0f, f.LevelThrottle), l => l.Time > 1.6f, l =>
                {
                    lowest = Mathf.Min(lowest, l.AltitudeFeet);
                    if (l.Time > 3f) slip = Mathf.Max(slip, Mathf.Abs(l.Telemetry.SideslipDegrees)); // the established turn
                    if (Mathf.Abs(l.Time - 10f) < 0.005f) bankAt10 = l.Telemetry.BankDegrees;
                    if (Mathf.Abs(l.Time - 30f) < 0.005f) bankAt30 = l.Telemetry.BankDegrees;
                });
                results[i] = slip;
                Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep), $"{profile}: smooth");
                switch (profile)
                {
                    case FlightAssistProfile.Assisted:
                        foreach (float bank in new[] { bankAt10, bankAt30, f.T.BankDegrees })
                            Assert.That(bank, Is.InRange(25f, 33f), "Assisted holds the bank it was left in");
                        Assert.That(altitude - lowest, Is.LessThan(150f), "and nearly its altitude (turn compensation)");
                        break;
                    case FlightAssistProfile.Standard:
                        Assert.That(bankAt30, Is.GreaterThan(20f), "Standard never levels the wings (only the aircraft's own stability)");
                        break;
                }
            }
            Assert.That(results[0], Is.LessThan(0.6f), "Assisted: coordinated");
            Assert.That(results[1], Is.LessThan(0.6f), "Standard: coordinated");
            Assert.That(results[2], Is.GreaterThan(results[1]), "Manual: the rudder is the pilot's, so the turn slips more");
        }

        [Test]
        public void SteepBank_Released_AssistedEasesBackTo30Degrees()
        {
            var f = new Flight(_config, FlightAssistProfile.Assisted);
            f.Fly(40f, l => Stick(0f, l.Time < 3f ? 0.6f : 0f, f.LevelThrottle), l => l.Time > 3.1f);
            Assert.That(f.MaxBank, Is.GreaterThan(60f), "the pilot rolled steeply: nothing stopped it");
            Assert.That(f.T.BankDegrees, Is.InRange(26f, 34f), "then it eased back to the held limit");
            Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep));
        }

        // ---- Climb -------------------------------------------------------------------------------

        [Test]
        public void Climb_HeldThenReleased_AssistedKeepsClimbing_StandardStaysSafe()
        {
            foreach (var profile in new[] { FlightAssistProfile.Assisted, FlightAssistProfile.Standard })
            {
                var f = new Flight(_config, profile, altitude: 500f);
                float lowestVs = 9999f, lowSpeed = 999f, highSpeed = 0f;
                f.Fly(60f, l => Stick(l.Time < 2f ? 0.3f : (l.Time < 10f ? 0.1f : 0f), 0f, 1f), l => l.Time > 10.2f, l =>
                {
                    if (l.Time <= 12f) return;
                    lowestVs = Mathf.Min(lowestVs, l.Telemetry.VerticalSpeedFeetPerMinute);
                    lowSpeed = Mathf.Min(lowSpeed, l.Telemetry.IndicatedAirspeedKnots);
                    highSpeed = Mathf.Max(highSpeed, l.Telemetry.IndicatedAirspeedKnots);
                });
                Assert.That(lowSpeed, Is.GreaterThan(60f), $"{profile}: never slows towards the stall");
                Assert.That(f.StallWarningSeconds, Is.EqualTo(0f), $"{profile}: no stall warning");
                if (profile == FlightAssistProfile.Assisted)
                {
                    Assert.That(lowestVs, Is.GreaterThan(-200f), "Assisted: let go in a climb, it keeps climbing");
                    Assert.That(highSpeed - lowSpeed, Is.LessThan(25f), "at a steady speed");
                    Assert.That(f.Assist.Trim, Is.GreaterThan(0.03f), "the trim took over the held stick");
                }
                Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep), $"{profile}: smooth");
            }
        }

        // ---- Stall approach and recovery ---------------------------------------------------------------

        /// <summary>Stall practice: idle, the pilot raises the nose progressively to hold altitude, then recovers with power.</summary>
        static Flight StallPractice(AircraftConfiguration config, FlightAssistProfile profile, out float recoverySeconds)
        {
            var f = new Flight(config, profile, altitude: 1500f);
            float target = f.Lab.AltitudeFeet, stick = 0f;
            f.Fly(40f, l =>
            {
                float error = target - l.AltitudeFeet;
                stick = Mathf.Clamp(stick + (0.004f * error - 0.002f * l.Telemetry.VerticalSpeedFeetPerMinute) * FlightLab.Dt, 0f, 1f);
                return Stick(stick, 0f, 0f);
            }, l => false);
            float recovered = -1f, start = f.Lab.Time;
            f.Fly(20f, l => Stick(0f, 0f, 1f), l => l.Time > start + 0.5f, l =>
            {
                if (recovered < 0f && l.Telemetry.VerticalSpeedFeetPerMinute > 0f) recovered = l.Time - start;
            });
            recoverySeconds = recovered;
            return f;
        }

        [Test]
        public void StallApproach_AssistedStaysClearOfTheStall_UnprotectedProfilesStall_AllRecover()
        {
            float critical = _config.stallAngle;
            foreach (var profile in Profiles)
            {
                var f = StallPractice(_config, profile, out float recovery);
                if (profile == FlightAssistProfile.Assisted)
                {
                    Assert.That(f.MaxAngleOfAttack, Is.LessThan(critical - 2f), "Assisted: gently held below the stall");
                    Assert.AreEqual(0f, f.StallWarningSeconds, "without even a warning");
                }
                else
                {
                    Assert.That(f.MaxAngleOfAttack, Is.GreaterThan(critical), $"{profile}: the same aircraft stalls when unprotected");
                    Assert.That(f.StallWarningSeconds, Is.GreaterThan(1f), $"{profile}: and the warning says so");
                }
                Assert.That(recovery, Is.InRange(0f, 12f), $"{profile}: recovers with stick released and full power");
                Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep), $"{profile}: smooth");
            }
        }

        [Test]
        public void ZoomClimb_AssistedLimitsTheNose_ButDoesNotInventLift()
        {
            var assisted = new Flight(_config, FlightAssistProfile.Assisted, altitude: 1500f);
            assisted.Fly(15f, l => Stick(1f, 0f, 0f), l => l.Time > 0.1f);
            var manual = new Flight(_config, FlightAssistProfile.Manual, altitude: 1500f);
            manual.Fly(15f, l => Stick(1f, 0f, 0f));

            Assert.That(manual.MaxAngleOfAttack, Is.GreaterThan(20f), "Manual: full aft stick at idle stalls the wing");
            Assert.That(assisted.MaxPitch, Is.LessThan(30f), "Assisted: the steep nose-up attitude is eased off");
            Assert.That(assisted.MaxAngleOfAttack, Is.LessThan(_config.stallAngle), "and the wing stays below its critical angle");
            Assert.That(assisted.T.IndicatedAirspeedKnots, Is.LessThan(manual.Lab.Telemetry.IndicatedAirspeedKnots + 60f), "no energy appears from nowhere");
            Assert.That(assisted.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep));
        }

        // ---- Crosswind disturbance -----------------------------------------------------------------

        [Test]
        public void CrosswindGust_AssistedRecoversWingsLevel_StandardDampsIt_AllSmooth()
        {
            var maxBank = new float[3];
            for (int i = 0; i < Profiles.Length; i++)
            {
                var f = new Flight(_config, Profiles[i]);
                f.Fly(5f, l => Stick(0f, 0f, f.LevelThrottle));
                f.Lab.Wind = new Vector3(8f, 0f, 0f); // a sudden 8 m/s (16 kt) crosswind from the left
                f.Fly(30f, l => Stick(0f, 0f, f.LevelThrottle));
                maxBank[i] = f.MaxBank;
                Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep), $"{Profiles[i]}: smooth");
                Assert.That(Mathf.Abs(f.T.SideslipDegrees), Is.LessThan(0.5f), $"{Profiles[i]}: weathervanes into the new relative wind");
                if (Profiles[i] == FlightAssistProfile.Assisted)
                    Assert.That(Mathf.Abs(f.T.BankDegrees), Is.LessThan(1f), "Assisted: back to wings level");
            }
            Assert.That(maxBank[0], Is.LessThan(4f), "Assisted: a small upset");
            Assert.That(maxBank[0], Is.LessThan(maxBank[2]), "smaller than the aircraft alone");
            Assert.That(maxBank[1], Is.LessThan(maxBank[2]), "Standard damps it too");
        }

        // ---- Final approach ------------------------------------------------------------------------------

        [Test]
        public void FinalApproach_FullFlap_AssistedAndStandardHoldASteadyDescent()
        {
            foreach (var profile in Profiles)
            {
                var f = new Flight(_config, profile, altitude: 300f, elevator: 0.12f, flaps: 1f);
                float power = f.LevelThrottle * 0.6f, lowSpeed = 999f, highSpeed = 0f, lowVs = 0f, highVs = -9999f;
                f.Fly(60f, l => Stick(l.Time < 4f ? 0.12f : 0f, 0f, power, 1f), l => l.Time > 4.2f, l =>
                {
                    if (l.Time <= 15f) return;
                    var t = l.Telemetry;
                    lowSpeed = Mathf.Min(lowSpeed, t.IndicatedAirspeedKnots);
                    highSpeed = Mathf.Max(highSpeed, t.IndicatedAirspeedKnots);
                    lowVs = Mathf.Min(lowVs, t.VerticalSpeedFeetPerMinute);
                    highVs = Mathf.Max(highVs, t.VerticalSpeedFeetPerMinute);
                });
                Assert.AreEqual(0f, f.StallWarningSeconds, $"{profile}: no stall warning on the approach");
                Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep), $"{profile}: smooth");
                if (profile == FlightAssistProfile.Manual) continue;
                Assert.That(lowSpeed, Is.GreaterThan(57f), $"{profile}: approach speed held ({lowSpeed:0}-{highSpeed:0} kt)");
                Assert.That(highSpeed, Is.LessThan(68f), $"{profile}: approach speed held ({lowSpeed:0}-{highSpeed:0} kt)");
                Assert.That(lowVs, Is.GreaterThan(-800f), $"{profile}: steady descent ({lowVs:0} to {highVs:0} fpm)");
                Assert.That(highVs, Is.LessThan(150f), $"{profile}: still descending ({lowVs:0} to {highVs:0} fpm)");
            }
        }

        // ---- Mode switching under load ------------------------------------------------------------------

        [Test]
        public void ModeSwitchingInAClimbingTurn_NoAbruptCommandOrMotion()
        {
            var f = new Flight(_config, FlightAssistProfile.Assisted);
            var sequence = new[]
            {
                FlightAssistProfile.Manual, FlightAssistProfile.Standard, FlightAssistProfile.Assisted,
                FlightAssistProfile.Manual, FlightAssistProfile.Assisted, FlightAssistProfile.Standard,
            };
            int next = 0;
            float maxPitchRate = 0f, maxRollRate = 0f;
            f.Fly(30f, l => Stick(l.Time < 2f ? 0.2f : 0.08f, l.Time < 1.2f ? 0.5f : 0f, 0.9f), l => l.Time > 2.1f, l =>
            {
                if (next < sequence.Length && l.Time >= 4f + 4f * next) f.Assist.SetProfile(sequence[next++]);
                if (l.Time <= 3f) return;
                maxPitchRate = Mathf.Max(maxPitchRate, Mathf.Abs(l.Telemetry.PitchRateDegreesPerSecond));
                maxRollRate = Mathf.Max(maxRollRate, Mathf.Abs(l.Telemetry.RollRateDegreesPerSecond));
            });
            Assert.AreEqual(sequence.Length, next, "every switch happened under load");
            Assert.That(f.MaxCommandStepWhilePilotSteady, Is.LessThan(SmoothCommandStep), "no command jump at any switch");
            Assert.That(maxPitchRate, Is.LessThan(6f), "no pitch jolt");
            Assert.That(maxRollRate, Is.LessThan(4f), "no roll jolt");
        }

        // ---- Same physics, same telemetry ---------------------------------------------------------------

        [Test]
        public void ManualWithTheAssistanceLayer_FliesExactlyLikeNoAssistance()
        {
            var direct = FlightLab.Level(_config, 1000f, out float throttle);
            var manual = new Flight(_config, FlightAssistProfile.Manual);
            Func<FlightLab, FlightControls> pilot = l => Stick(0.1f * Mathf.Sin(l.Time), l.Time < 2f ? 0.4f : -0.1f, throttle, 0f, 0.2f);
            direct.Run(30f, pilot);
            manual.Fly(30f, pilot);
            Assert.AreEqual(direct.Position, manual.Lab.Position, "identical trajectory");
            Assert.AreEqual(direct.Rotation, manual.Lab.Rotation);
            Assert.AreEqual(direct.Telemetry.IndicatedAirspeedKnots, manual.T.IndicatedAirspeedKnots);
        }

        [Test]
        public void NoProfileChangesTheAircraftData()
        {
            var before = Snapshot(_config);
            foreach (var profile in Profiles)
            {
                StallPractice(_config, profile, out _);
                var f = new Flight(_config, profile);
                f.Fly(20f, l => Stick(0f, l.Time < 1.5f ? 0.5f : 0f, f.LevelThrottle));
            }
            Assert.AreEqual(before, Snapshot(_config), "assistance only changes commands, never the aircraft's constants");
        }

        static string Snapshot(AircraftConfiguration c)
        {
            var text = new System.Text.StringBuilder();
            foreach (var field in typeof(AircraftConfiguration).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = field.GetValue(c);
                if (value is Array array)
                    foreach (var item in array) text.Append(field.Name).Append('=').Append(item).Append(';');
                else
                    text.Append(field.Name).Append('=').Append(value).Append(';');
            }
            return text.ToString();
        }
    }
}
