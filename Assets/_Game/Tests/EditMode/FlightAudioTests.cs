using NUnit.Framework;
using OpenSkies.Audio;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The sound model in plain C#: engine RPM and load, airflow, rolling, cockpit
    /// enclosure, touchdowns, alert timing and the generated placeholder sounds. These
    /// prove the behaviour of the formulas, not how they sound: that needs ears
    /// (docs/PROTOTYPE_TEST_PLAN.md, audio checks).
    /// </summary>
    public class FlightAudioTests
    {
        static readonly EngineAudioSettings Engine = new EngineAudioSettings();
        static readonly AirflowAudioSettings Airflow = new AirflowAudioSettings();
        static readonly GroundAudioSettings Ground = new GroundAudioSettings();
        static readonly AmbienceAudioSettings Ambience = new AmbienceAudioSettings();
        static readonly ViewAudioSettings View = new ViewAudioSettings();

        static FlightAudioTargets Compute(FlightAudioInput input) => FlightAudioMath.Compute(input, Engine, Airflow, Ground, Ambience, View);

        static FlightAudioInput Running(float power, float ias, bool onGround = false, bool cockpit = false) => new FlightAudioInput
        {
            EnginePower = power,
            Engine = EngineState.Running,
            IndicatedAirspeedKnots = ias,
            GroundspeedKnots = ias,
            WeightOnWheels = onGround,
            HeightAboveGroundFeet = onGround ? 0f : 1500f,
            Cockpit = cockpit,
        };

        [Test]
        public void EngineRpm_RisesWithPowerAndAirspeed_LikeAFixedPitchPropeller()
        {
            float idle = FlightAudioMath.EngineRpm(EngineState.Running, 0f, 0f, Engine);
            float fullStatic = FlightAudioMath.EngineRpm(EngineState.Running, 1f, 0f, Engine);
            float fullFast = FlightAudioMath.EngineRpm(EngineState.Running, 1f, 120f, Engine);
            Assert.AreEqual(Engine.idleRpm, idle, 1e-4f);
            Assert.AreEqual(Engine.staticRpm, fullStatic, 1e-4f, "full power at rest stops short of maximum RPM");
            Assert.AreEqual(1f, fullFast, 1e-4f, "airspeed unloads the propeller");
            Assert.Less(idle, fullStatic);

            float previous = -1f;
            for (float power = 0f; power <= 1.0001f; power += 0.1f)
            {
                float rpm = FlightAudioMath.EngineRpm(EngineState.Running, power, 60f, Engine);
                Assert.Greater(rpm, previous, "monotonic in power");
                previous = rpm;
            }
        }

        [Test]
        public void EngineOff_IsSilent_AndThePropellerWindmills()
        {
            Assert.AreEqual(0f, FlightAudioMath.EngineLoudness(EngineState.Off, 1f, Engine));
            Assert.AreEqual(0f, FlightAudioMath.EngineRpm(EngineState.Off, 0f, 0f, Engine), 1e-5f, "stopped on the ground");
            Assert.Greater(FlightAudioMath.EngineRpm(EngineState.Off, 0f, 90f, Engine), 0.2f, "windmilling in flight");
            Assert.AreEqual(Engine.crankingVolume, FlightAudioMath.EngineLoudness(EngineState.Starting, 0f, Engine));

            var off = Compute(new FlightAudioInput { Engine = EngineState.Off, IndicatedAirspeedKnots = 90f });
            Assert.AreEqual(0f, off.EngineLowVolume + off.EngineHighVolume, 1e-5f);
            Assert.Greater(off.AirflowVolume, 0f, "the wind is still there");
        }

        [Test]
        public void EngineLoad_MakesItLouderAndBrighter_AtTheSameRpm()
        {
            float rpm = 0.6f;
            Assert.Greater(FlightAudioMath.EngineBrightness(rpm, 1f, Engine), FlightAudioMath.EngineBrightness(rpm, 0f, Engine));
            Assert.Greater(FlightAudioMath.EngineLoudness(EngineState.Running, 1f, Engine), FlightAudioMath.EngineLoudness(EngineState.Running, 0.2f, Engine));

            var idle = Compute(Running(0f, 0f, onGround: true));
            var full = Compute(Running(1f, 0f, onGround: true));
            Assert.Greater(full.EnginePitch, idle.EnginePitch, "higher RPM, higher pitch");
            Assert.Greater(full.EngineHighVolume, full.EngineLowVolume, "full power is mostly the bright layer");
            Assert.Greater(idle.EngineLowVolume, idle.EngineHighVolume, "idle is mostly the low layer");
            float idleTotal = Mathf.Sqrt(idle.EngineLowVolume * idle.EngineLowVolume + idle.EngineHighVolume * idle.EngineHighVolume);
            Assert.AreEqual(FlightAudioMath.EngineLoudness(EngineState.Running, 0f, Engine), idleTotal, 1e-4f, "equal-power crossfade");
        }

        [Test]
        public void Airflow_FollowsAirspeed_WithSideslipFlapsAndBuffet()
        {
            Assert.AreEqual(0f, FlightAudioMath.AirflowLevel(10f, 0f, 0f, false, Airflow), "silent below the start speed");
            float slow = FlightAudioMath.AirflowLevel(60f, 0f, 0f, false, Airflow);
            float fast = FlightAudioMath.AirflowLevel(140f, 0f, 0f, false, Airflow);
            Assert.Greater(fast, slow);
            Assert.Greater(FlightAudioMath.AirflowLevel(60f, 8f, 0f, false, Airflow), slow, "sideslip roars");
            Assert.Greater(FlightAudioMath.AirflowLevel(60f, 0f, 1f, false, Airflow), slow, "flaps add noise");
            Assert.Greater(FlightAudioMath.AirflowLevel(60f, 0f, 0f, true, Airflow), slow, "buffet near the stall");
            Assert.LessOrEqual(FlightAudioMath.AirflowLevel(400f, 30f, 1f, true, Airflow), 1f);
        }

        [Test]
        public void Rolling_OnlyWithWheelsOnTheGround()
        {
            Assert.AreEqual(0f, Compute(Running(0.5f, 40f, onGround: false)).RollingVolume);
            Assert.Greater(Compute(Running(0.5f, 40f, onGround: true)).RollingVolume, 0f);
            Assert.AreEqual(0f, Compute(Running(0f, 0f, onGround: true)).RollingVolume, 1e-5f, "at rest");
        }

        [Test]
        public void Cockpit_SoundsEnclosed()
        {
            var outside = Compute(Running(0.7f, 100f));
            var inside = Compute(Running(0.7f, 100f, cockpit: true));
            Assert.Less(inside.AircraftCutoffHz, outside.AircraftCutoffHz);
            Assert.Less(inside.EnvironmentCutoffHz, outside.EnvironmentCutoffHz);
            Assert.Less(inside.AirflowVolume, outside.AirflowVolume, "the cabin keeps the wind out");
            Assert.AreEqual(View.externalCutoff, outside.AircraftCutoffHz);
        }

        [Test]
        public void Ambience_FadesWithHeight_AndGrowsNearTheCoast()
        {
            float ground = FlightAudioMath.AmbienceLevel(0f, 0f, Ambience);
            Assert.Greater(FlightAudioMath.AmbienceLevel(0f, 1f, Ambience), ground, "surf at the shoreline");
            Assert.AreEqual(0f, FlightAudioMath.AmbienceLevel(3000f, 1f, Ambience), 1e-5f, "nothing at altitude");
        }

        [Test]
        public void Smoothing_IsFrameRateIndependent_AndDecibelsAreSafe()
        {
            float oneStep = FlightAudioMath.Smooth(0f, 1f, 0.1f, 1f / 30f);
            float twoSteps = FlightAudioMath.Smooth(FlightAudioMath.Smooth(0f, 1f, 0.1f, 1f / 60f), 1f, 0.1f, 1f / 60f);
            Assert.AreEqual(oneStep, twoSteps, 1e-5f);
            Assert.AreEqual(1f, FlightAudioMath.Smooth(0f, 1f, 0f, 0.02f), "zero time constant snaps");
            Assert.AreEqual(0f, FlightAudioMath.LinearToDecibels(1f), 1e-4f);
            Assert.AreEqual(-6.02f, FlightAudioMath.LinearToDecibels(0.5f), 0.01f);
            Assert.AreEqual(-80f, FlightAudioMath.LinearToDecibels(0f));
        }

        [Test]
        public void BadTelemetry_NeverProducesBadAudio()
        {
            var input = new FlightAudioInput
            {
                EnginePower = float.NaN, Engine = EngineState.Running, IndicatedAirspeedKnots = float.PositiveInfinity,
                GroundspeedKnots = float.NaN, SideslipDegrees = float.NaN, FlapPosition = float.NaN, HeightAboveGroundFeet = float.NaN,
                CoastProximity = float.NaN, WeightOnWheels = true,
            };
            var t = Compute(input);
            foreach (float v in new[] { t.EngineRpm, t.EnginePitch, t.EngineLowVolume, t.EngineHighVolume, t.AirflowVolume, t.AirflowPitch,
                         t.RollingVolume, t.RollingPitch, t.AmbienceVolume })
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
        }

        // ---- Touchdown ---------------------------------------------------------------------------------

        [Test]
        public void Touchdown_HardnessFromTheLastAirborneSinkRate()
        {
            var detector = new TouchdownDetector();
            Assert.IsFalse(detector.Update(false, -400f, 60f, 0.02f, Ground, out _), "first sample only primes");
            for (int i = 0; i < 50; i++) Assert.IsFalse(detector.Update(false, -400f, 60f, 0.02f, Ground, out _));
            Assert.IsTrue(detector.Update(true, -50f, 60f, 0.02f, Ground, out float intensity), "the step the wheels touch");
            Assert.AreEqual(400f, detector.LastSinkRateFpm, 1e-3f, "from the last airborne sample, not the compressed one");
            Assert.AreEqual(FlightAudioMath.TouchdownIntensity(400f, Ground), intensity, 1e-5f);
            Assert.IsFalse(detector.Update(true, 0f, 58f, 0.02f, Ground, out _), "once");

            Assert.Less(FlightAudioMath.TouchdownIntensity(80f, Ground), FlightAudioMath.TouchdownIntensity(600f, Ground));
            Assert.Less(FlightAudioMath.TouchdownVolume(0f, Ground), FlightAudioMath.TouchdownVolume(1f, Ground));
        }

        [Test]
        public void Touchdown_NotForABriefHop_ASlowSettleOrAReset()
        {
            var detector = new TouchdownDetector();
            detector.Update(true, 0f, 40f, 0.02f, Ground, out _);
            for (int i = 0; i < 10; i++) detector.Update(false, -100f, 40f, 0.02f, Ground, out _); // 0.2 s: a bounce over a seam
            Assert.IsFalse(detector.Update(true, 0f, 40f, 0.02f, Ground, out _), "airborne too briefly");

            for (int i = 0; i < 50; i++) detector.Update(false, -100f, 5f, 0.02f, Ground, out _);
            Assert.IsFalse(detector.Update(true, 0f, 5f, 0.02f, Ground, out _), "too slow to be a landing");

            for (int i = 0; i < 50; i++) detector.Update(false, -500f, 60f, 0.02f, Ground, out _);
            detector.Reset(); // RESET puts the aircraft on the runway
            Assert.IsFalse(detector.Update(true, 0f, 0f, 0.02f, Ground, out _), "placing the aircraft is not a touchdown");
        }

        // ---- Alerts --------------------------------------------------------------------------------------

        [Test]
        public void Alerts_StallHornWhileOn_WarningToneRepeats_CautionChimesOnce()
        {
            var s = new AlertAudioSettings();
            var alerts = new AlertSoundScheduler();
            alerts.Update(true, 0, 0, 0f, s);
            Assert.IsTrue(alerts.StallHorn);
            Assert.IsFalse(alerts.PlayWarningTone, "the stall has its own horn");

            alerts.Update(false, 1 << 2, 0, 1f, s);
            Assert.IsFalse(alerts.StallHorn);
            Assert.IsTrue(alerts.PlayWarningTone, "at once when a red warning appears");
            alerts.Update(false, 1 << 2, 0, 1.5f, s);
            Assert.IsFalse(alerts.PlayWarningTone);
            alerts.Update(false, 1 << 2, 0, 1f + s.warningRepeatSeconds + 0.01f, s);
            Assert.IsTrue(alerts.PlayWarningTone, "repeats while active");

            alerts.Update(false, 0, 1 << 1, 10f, s);
            Assert.IsTrue(alerts.PlayCautionChime);
            alerts.Update(false, 0, 1 << 1, 10.1f, s);
            Assert.IsFalse(alerts.PlayCautionChime, "a caution chimes when it appears, not continuously");
            alerts.Update(false, 0, 0, 11f, s);
            alerts.Update(false, 0, 1 << 1, 12f, s);
            Assert.IsFalse(alerts.PlayCautionChime, "the same caution flickering does not chime again at once");
            alerts.Update(false, 0, 0, 30f, s);
            alerts.Update(false, 0, 1 << 1, 31f, s);
            Assert.IsTrue(alerts.PlayCautionChime, "it does once enough time has passed");
        }

        // ---- Placeholder sounds -----------------------------------------------------------------------------

        [Test]
        public void Placeholders_AreBoundedDeterministic_AndLoopsAreSeamless()
        {
            for (int i = 0; i < PlaceholderAudio.Count; i++)
            {
                var sound = (PlaceholderSound)i;
                float[] a = PlaceholderAudio.Samples(sound, out int rate);
                float[] b = PlaceholderAudio.Samples(sound, out _);
                Assert.Greater(a.Length, rate / 100, sound + " has content");
                Assert.AreEqual(a.Length, b.Length);
                float peak = 0f, meanStep = 0f;
                for (int k = 0; k < a.Length; k++)
                {
                    Assert.AreEqual(a[k], b[k], sound + " is deterministic");
                    Assert.IsFalse(float.IsNaN(a[k]));
                    peak = Mathf.Max(peak, Mathf.Abs(a[k]));
                    if (k > 0) meanStep += Mathf.Abs(a[k] - a[k - 1]);
                }
                Assert.LessOrEqual(peak, 1f, sound + " never clips");
                Assert.Greater(peak, 0.1f, sound + " is audible");
                if (!PlaceholderAudio.IsLoop(sound)) continue;
                meanStep /= a.Length - 1;
                float seam = Mathf.Abs(a[0] - a[a.Length - 1]);
                Assert.Less(seam, Mathf.Max(0.02f, meanStep * 6f), sound + " loops without a click");
            }
        }
    }
}
