using NUnit.Framework;
using OpenSkies.Options;
using OpenSkies.Platform;

namespace OpenSkies.Tests
{
    /// <summary>Graphics presets, frame-rate targets, haptic limits and frame statistics.</summary>
    public class PlatformTests
    {
        [Test]
        public void GraphicsPresets_AreOrderedFromCheapestToRichest()
        {
            var saver = GraphicsPresets.For(GraphicsPreset.BatterySaver);
            var balanced = GraphicsPresets.For(GraphicsPreset.Balanced);
            var high = GraphicsPresets.For(GraphicsPreset.HighQuality);

            Assert.LessOrEqual(saver.RenderScale, balanced.RenderScale);
            Assert.LessOrEqual(balanced.RenderScale, high.RenderScale);
            Assert.LessOrEqual(high.RenderScale, 1f, "never supersampled");
            Assert.IsFalse(saver.Shadows, "battery saver draws no shadows");
            Assert.IsTrue(balanced.Shadows && high.Shadows);
            Assert.Less(balanced.ShadowDistance, high.ShadowDistance);
            Assert.LessOrEqual(saver.LodBias, balanced.LodBias, "vegetation and detail distance");
            Assert.LessOrEqual(balanced.LodBias, high.LodBias);
            Assert.LessOrEqual(saver.Msaa, balanced.Msaa);
            Assert.LessOrEqual(balanced.Msaa, high.Msaa);
            Assert.LessOrEqual(saver.ViewDistance, balanced.ViewDistance);
            Assert.LessOrEqual(balanced.ViewDistance, high.ViewDistance);
            foreach (var p in new[] { saver, balanced, high })
            {
                Assert.That(p.RenderScale, Is.InRange(0.5f, 1f));
                Assert.That(p.Msaa == 1 || p.Msaa == 2 || p.Msaa == 4);
                Assert.Greater(p.ViewDistance, 3000f, "far enough for the coast and the landmarks");
            }
        }

        [Test]
        public void FrameRate_SixtyOnlyWhereTheDisplayCanShowIt()
        {
            Assert.AreEqual(30, FrameRatePolicy.Target(FrameRatePreference.Fps30, 120f));
            Assert.AreEqual(60, FrameRatePolicy.Target(FrameRatePreference.Fps60, 60f));
            Assert.AreEqual(60, FrameRatePolicy.Target(FrameRatePreference.Fps60, 59.94f), "NTSC-style 59.94 Hz counts as 60");
            Assert.AreEqual(30, FrameRatePolicy.Target(FrameRatePreference.Fps60, 30f), "a 30 Hz display stays at 30");
            Assert.AreEqual(60, FrameRatePolicy.Target(FrameRatePreference.Fps60, 0f), "unknown refresh rate: the OS caps it");
        }

        [Test]
        public void Haptics_RespectTheSwitchTheDeviceAndTheLimits()
        {
            var policy = new HapticPolicy();
            Assert.IsFalse(policy.Allow(HapticEvent.Confirm, 0f, deviceSupported: false), "no device, no pulse");
            policy.Enabled = false;
            Assert.IsFalse(policy.Allow(HapticEvent.Confirm, 0f, true), "the player switched haptics off");
            policy.Enabled = true;

            Assert.IsTrue(policy.Allow(HapticEvent.Selection, 1f, true));
            Assert.IsFalse(policy.Allow(HapticEvent.Selection, 1.02f, true), "not twice in a burst");
            Assert.IsFalse(policy.Allow(HapticEvent.Confirm, 1.03f, true), "global gap between any two pulses");
            Assert.IsTrue(policy.Allow(HapticEvent.Confirm, 1.2f, true));

            Assert.IsTrue(policy.Allow(HapticEvent.Warning, 5f, true));
            Assert.IsFalse(policy.Allow(HapticEvent.Warning, 6f, true), "warnings pulse once, never continuously");
            Assert.IsTrue(policy.Allow(HapticEvent.Warning, 9.5f, true));
        }

        [Test]
        public void Haptics_NeverPulseMoreThanTheLimitsAllow_EvenWhenAskedEveryFrame()
        {
            var policy = new HapticPolicy();
            int pulses = 0;
            for (int frame = 0; frame < 600; frame++) // 10 s at 60 FPS, asking every frame
                if (policy.Allow(HapticEvent.TouchdownSoft, frame / 60f, true)) pulses++;
            Assert.LessOrEqual(pulses, 17, "at most one every 0.6 s");
        }

        [Test]
        public void AndroidHapticConstants_FallBackBeforeApi30()
        {
            Assert.AreEqual(16, AndroidHaptics.Constant(HapticEvent.Confirm, 30));
            Assert.AreEqual(1, AndroidHaptics.Constant(HapticEvent.Confirm, 29));
            Assert.AreEqual(17, AndroidHaptics.Constant(HapticEvent.Warning, 33));
            Assert.AreEqual(0, AndroidHaptics.Constant(HapticEvent.Warning, 26));
        }

        [Test]
        public void FrameStatistics_AverageAndPercentiles()
        {
            var stats = new FrameStatistics(100);
            for (int i = 1; i <= 100; i++) stats.Add(i); // 1..100 ms
            Assert.AreEqual(50.5f, stats.Average, 1e-4f);
            Assert.AreEqual(50f, stats.Percentile(50f));
            Assert.AreEqual(95f, stats.Percentile(95f));
            Assert.AreEqual(99f, stats.Percentile(99f));
            Assert.AreEqual(100f, stats.Max);
            Assert.AreEqual(50, stats.CountAbove(50f));

            stats.Add(float.NaN);
            stats.Add(-1f);
            Assert.AreEqual(100, stats.Count, "bad samples are ignored");

            for (int i = 0; i < 100; i++) stats.Add(33.3f); // the window slides
            Assert.AreEqual(33.3f, stats.Average, 1e-3f);
            Assert.AreEqual(33.3f, stats.Percentile(99f), 1e-3f);
            stats.Clear();
            Assert.AreEqual(0f, stats.Average);
            Assert.AreEqual(0f, stats.Percentile(95f));
        }
    }
}
