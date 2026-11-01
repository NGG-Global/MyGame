using OpenSkies.Options;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// The player's options between sessions (PlayerPrefs). Fractions are stored as whole
    /// percent so they round-trip exactly. The motion preference shares the key the HUD
    /// has always used, so an earlier choice carries over.
    /// </summary>
    public sealed class PlayerPrefsOptionsStore : IOptionsStore
    {
        const string Prefix = "OpenSkies.Options.";
        const string MotionKey = "OpenSkies.Hud.Motion"; // same key as HudPreferences
        const string UiScaleKey = Prefix + "UiScale";
        const string HudOpacityKey = Prefix + "HudOpacity";
        const string HapticsKey = Prefix + "Haptics";
        const string GraphicsKey = Prefix + "Graphics";
        const string FrameRateKey = Prefix + "FrameRate";
        static readonly string[] VolumeKeys =
        {
            Prefix + "Volume.Master", Prefix + "Volume.Aircraft", Prefix + "Volume.Environment", Prefix + "Volume.Interface",
            Prefix + "Volume.Warnings",
        };

        public GameOptions Load(in GameOptions defaults)
        {
            var o = defaults;
            o.UiScale = PlayerPrefs.GetInt(UiScaleKey, GameOptionsRules.Percent(defaults.UiScale)) / 100f;
            o.HudOpacity = PlayerPrefs.GetInt(HudOpacityKey, GameOptionsRules.Percent(defaults.HudOpacity)) / 100f;
            o.Motion = (MotionOption)PlayerPrefs.GetInt(MotionKey, (int)defaults.Motion);
            o.Haptics = PlayerPrefs.GetInt(HapticsKey, defaults.Haptics ? 1 : 0) != 0;
            o.Graphics = (GraphicsPreset)PlayerPrefs.GetInt(GraphicsKey, (int)defaults.Graphics);
            o.FrameRate = (FrameRatePreference)PlayerPrefs.GetInt(FrameRateKey, (int)defaults.FrameRate);
            for (int c = 0; c < GameOptionsRules.VolumeChannelCount; c++)
            {
                var channel = (VolumeChannel)c;
                o.SetVolume(channel, PlayerPrefs.GetInt(VolumeKeys[c], GameOptionsRules.Percent(defaults.Volume(channel))) / 100f);
            }
            return GameOptionsRules.Sanitize(o); // unknown or damaged values fall back to the defaults
        }

        public void Save(in GameOptions options)
        {
            PlayerPrefs.SetInt(UiScaleKey, GameOptionsRules.Percent(options.UiScale));
            PlayerPrefs.SetInt(HudOpacityKey, GameOptionsRules.Percent(options.HudOpacity));
            PlayerPrefs.SetInt(MotionKey, (int)options.Motion);
            PlayerPrefs.SetInt(HapticsKey, options.Haptics ? 1 : 0);
            PlayerPrefs.SetInt(GraphicsKey, (int)options.Graphics);
            PlayerPrefs.SetInt(FrameRateKey, (int)options.FrameRate);
            for (int c = 0; c < GameOptionsRules.VolumeChannelCount; c++)
                PlayerPrefs.SetInt(VolumeKeys[c], GameOptionsRules.Percent(options.Volume((VolumeChannel)c)));
            PlayerPrefs.Save(); // mobile apps can be killed without a clean quit
        }
    }
}
