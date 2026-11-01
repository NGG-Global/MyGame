using NUnit.Framework;
using OpenSkies.Options;

namespace OpenSkies.Tests
{
    /// <summary>The player's options: defaults, limits, steps and storage.</summary>
    public class GameOptionsTests
    {
        [Test]
        public void Defaults_AreAlreadyValid()
        {
            var d = GameOptionsRules.Defaults;
            Assert.IsTrue(GameOptionsRules.Same(d, GameOptionsRules.Sanitize(d)));
            Assert.AreEqual(1f, d.UiScale);
            Assert.AreEqual(1f, d.HudOpacity);
            Assert.AreEqual(GraphicsPreset.Balanced, d.Graphics);
            Assert.AreEqual(FrameRatePreference.Fps30, d.FrameRate, "30 FPS is the baseline target");
            Assert.IsTrue(d.Haptics);
        }

        [Test]
        public void Sanitize_ClampsSnapsAndReplacesBadValues()
        {
            var o = GameOptionsRules.Defaults;
            o.UiScale = 3f;
            o.HudOpacity = 0.1f;
            o.MasterVolume = float.NaN;
            o.AircraftVolume = 0.73f;
            o.WarningVolume = 0f;
            o.Motion = (MotionOption)42;
            o.Graphics = (GraphicsPreset)(-1);
            o.FrameRate = (FrameRatePreference)7;

            var s = GameOptionsRules.Sanitize(o);
            Assert.AreEqual(GameOptionsRules.MaxUiScale, s.UiScale, 1e-4f);
            Assert.AreEqual(GameOptionsRules.MinHudOpacity, s.HudOpacity, 1e-4f);
            Assert.AreEqual(GameOptionsRules.Defaults.MasterVolume, s.MasterVolume, 1e-4f, "NaN gives the default");
            Assert.AreEqual(0.7f, s.AircraftVolume, 1e-4f, "snapped to the 10 % grid");
            Assert.AreEqual(GameOptionsRules.MinWarningVolume, s.WarningVolume, 1e-4f, "warnings can be turned down, never off");
            Assert.AreEqual(GameOptionsRules.Defaults.Motion, s.Motion);
            Assert.AreEqual(GameOptionsRules.Defaults.Graphics, s.Graphics);
            Assert.AreEqual(GameOptionsRules.Defaults.FrameRate, s.FrameRate);
        }

        [Test]
        public void Steps_StayInsideTheirRanges()
        {
            float v = 0.1f;
            v = GameOptionsRules.StepVolume(VolumeChannel.Master, v, -1);
            Assert.AreEqual(0f, v, 1e-4f);
            v = GameOptionsRules.StepVolume(VolumeChannel.Master, v, -1);
            Assert.AreEqual(0f, v, 1e-4f, "never below silence");
            Assert.AreEqual(1f, GameOptionsRules.StepVolume(VolumeChannel.Aircraft, 1f, 1), 1e-4f, "never above full");
            Assert.AreEqual(GameOptionsRules.MinWarningVolume, GameOptionsRules.StepVolume(VolumeChannel.Warnings, 0.2f, -1), 1e-4f);

            Assert.AreEqual(1.1f, GameOptionsRules.StepUiScale(1f, 1), 1e-4f);
            Assert.AreEqual(1.1f, GameOptionsRules.StepUiScale(1.1f, 1), 1e-4f);
            Assert.AreEqual(0.9f, GameOptionsRules.StepUiScale(0.9f, -1), 1e-4f);
            Assert.AreEqual(0.6f, GameOptionsRules.StepHudOpacity(0.6f, -1), 1e-4f);
            Assert.AreEqual(1f, GameOptionsRules.StepHudOpacity(0.9f, 1), 1e-4f);
        }

        [Test]
        public void Steps_AreExactAfterManyTaps()
        {
            float v = 0f;
            for (int i = 0; i < 7; i++) v = GameOptionsRules.StepVolume(VolumeChannel.Environment, v, 1);
            Assert.AreEqual(70, GameOptionsRules.Percent(v), "no drift from repeated float steps");
            for (int i = 0; i < 7; i++) v = GameOptionsRules.StepVolume(VolumeChannel.Environment, v, -1);
            Assert.AreEqual(0, GameOptionsRules.Percent(v));
        }

        [Test]
        public void Volume_ByChannel_RoundTrips()
        {
            var o = GameOptionsRules.Defaults;
            for (int c = 0; c < GameOptionsRules.VolumeChannelCount; c++)
            {
                var channel = (VolumeChannel)c;
                o.SetVolume(channel, 0.3f + 0.1f * c);
                Assert.AreEqual(0.3f + 0.1f * c, o.Volume(channel), 1e-6f, channel.ToString());
            }
        }

        [Test]
        public void MemoryStore_GivesDefaultsUntilSaved()
        {
            var store = new MemoryOptionsStore();
            var defaults = GameOptionsRules.Defaults;
            Assert.IsTrue(GameOptionsRules.Same(defaults, store.Load(defaults)));
            var changed = defaults;
            changed.Graphics = GraphicsPreset.BatterySaver;
            store.Save(changed);
            Assert.AreEqual(GraphicsPreset.BatterySaver, store.Load(defaults).Graphics);
            Assert.AreEqual(1, store.SaveCount);
        }
    }
}
