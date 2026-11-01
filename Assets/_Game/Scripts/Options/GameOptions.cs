using System;

namespace OpenSkies.Options
{
    /// <summary>Rendering presets. They change only how the world is drawn, never the flight model.</summary>
    public enum GraphicsPreset
    {
        BatterySaver = 0,
        Balanced = 1,
        HighQuality = 2,
    }

    /// <summary>The player's frame-rate preference; 60 applies only where the display supports it.</summary>
    public enum FrameRatePreference
    {
        Fps30 = 0,
        Fps60 = 1,
    }

    /// <summary>Same values as the HUD's motion preference (stored under the same key).</summary>
    public enum MotionOption
    {
        FollowSystem = 0,
        Reduced = 1,
        Full = 2,
    }

    /// <summary>Volume channels: one per mixer group under Master.</summary>
    public enum VolumeChannel
    {
        Master = 0,
        Aircraft = 1,
        Environment = 2,
        Interface = 3,
        Warnings = 4,
    }

    /// <summary>
    /// Presentation, comfort and device options the player sets in the settings screen.
    /// Flight assistance, stick sensitivity and the HUD profile have their own stores
    /// (one source of truth each); the settings screen shows them through the session.
    /// None of these values can reach the flight model.
    /// </summary>
    [Serializable]
    public struct GameOptions
    {
        /// <summary>Interface scale relative to the 1080-unit reference height (0.9-1.1); limited per screen by the layout.</summary>
        public float UiScale;
        /// <summary>Opacity of routine HUD information and buttons (0.6-1). Warnings are never dimmed.</summary>
        public float HudOpacity;
        public MotionOption Motion;
        public bool Haptics;
        public GraphicsPreset Graphics;
        public FrameRatePreference FrameRate;
        public float MasterVolume;
        public float AircraftVolume;
        public float EnvironmentVolume;
        public float InterfaceVolume;
        public float WarningVolume;

        public float Volume(VolumeChannel channel)
        {
            switch (channel)
            {
                case VolumeChannel.Aircraft: return AircraftVolume;
                case VolumeChannel.Environment: return EnvironmentVolume;
                case VolumeChannel.Interface: return InterfaceVolume;
                case VolumeChannel.Warnings: return WarningVolume;
                default: return MasterVolume;
            }
        }

        public void SetVolume(VolumeChannel channel, float value)
        {
            switch (channel)
            {
                case VolumeChannel.Aircraft: AircraftVolume = value; break;
                case VolumeChannel.Environment: EnvironmentVolume = value; break;
                case VolumeChannel.Interface: InterfaceVolume = value; break;
                case VolumeChannel.Warnings: WarningVolume = value; break;
                default: MasterVolume = value; break;
            }
        }
    }

    /// <summary>Defaults, limits and steps for <see cref="GameOptions"/>. Plain C#, EditMode-tested.</summary>
    public static class GameOptionsRules
    {
        public const int VolumeChannelCount = 5;
        public const int GraphicsPresetCount = 3;
        public const int MotionOptionCount = 3;

        public const float MinUiScale = 0.9f;
        public const float MaxUiScale = 1.1f;
        public const float UiScaleStep = 0.1f;
        public const float MinHudOpacity = 0.6f;
        public const float HudOpacityStep = 0.1f;
        public const float VolumeStep = 0.1f;
        /// <summary>Warning sounds can be turned down, not off: banners carry the text, the sound is the cue that makes you look.</summary>
        public const float MinWarningVolume = 0.2f;

        public static GameOptions Defaults => new GameOptions
        {
            UiScale = 1f,
            HudOpacity = 1f,
            Motion = MotionOption.FollowSystem,
            Haptics = true,
            Graphics = GraphicsPreset.Balanced,
            FrameRate = FrameRatePreference.Fps30,
            MasterVolume = 0.8f,
            AircraftVolume = 0.8f,
            EnvironmentVolume = 0.7f,
            InterfaceVolume = 0.6f,
            WarningVolume = 0.9f,
        };

        /// <summary>Clamps every value to its range and step, replaces bad numbers and unknown enum values with the defaults.</summary>
        public static GameOptions Sanitize(GameOptions options)
        {
            var d = Defaults;
            options.UiScale = Stepped(options.UiScale, MinUiScale, MaxUiScale, UiScaleStep, d.UiScale);
            options.HudOpacity = Stepped(options.HudOpacity, MinHudOpacity, 1f, HudOpacityStep, d.HudOpacity);
            if (!IsDefined(options.Motion)) options.Motion = d.Motion;
            if (!IsDefined(options.Graphics)) options.Graphics = d.Graphics;
            if (!IsDefined(options.FrameRate)) options.FrameRate = d.FrameRate;
            for (int c = 0; c < VolumeChannelCount; c++)
            {
                var channel = (VolumeChannel)c;
                float min = MinimumVolume(channel);
                options.SetVolume(channel, Stepped(options.Volume(channel), min, 1f, VolumeStep, d.Volume(channel)));
            }
            return options;
        }

        /// <summary>Field-by-field equality without boxing (called every frame while the settings screen is open).</summary>
        public static bool Same(in GameOptions a, in GameOptions b) =>
            a.UiScale == b.UiScale && a.HudOpacity == b.HudOpacity && a.Motion == b.Motion && a.Haptics == b.Haptics
            && a.Graphics == b.Graphics && a.FrameRate == b.FrameRate && a.MasterVolume == b.MasterVolume
            && a.AircraftVolume == b.AircraftVolume && a.EnvironmentVolume == b.EnvironmentVolume
            && a.InterfaceVolume == b.InterfaceVolume && a.WarningVolume == b.WarningVolume;

        public static float MinimumVolume(VolumeChannel channel) => channel == VolumeChannel.Warnings ? MinWarningVolume : 0f;

        /// <summary>One step up or down for a stepper control, within the channel's range.</summary>
        public static float StepVolume(VolumeChannel channel, float current, int direction) =>
            Stepped(current + Math.Sign(direction) * VolumeStep, MinimumVolume(channel), 1f, VolumeStep, current);

        public static float StepUiScale(float current, int direction) =>
            Stepped(current + Math.Sign(direction) * UiScaleStep, MinUiScale, MaxUiScale, UiScaleStep, 1f);

        public static float StepHudOpacity(float current, int direction) =>
            Stepped(current + Math.Sign(direction) * HudOpacityStep, MinHudOpacity, 1f, HudOpacityStep, 1f);

        /// <summary>Whole percent, for labels and storage.</summary>
        public static int Percent(float value) => (int)Math.Round(value * 100f, MidpointRounding.AwayFromZero);

        static bool IsDefined(MotionOption value) => value >= MotionOption.FollowSystem && value <= MotionOption.Full;
        static bool IsDefined(GraphicsPreset value) => value >= GraphicsPreset.BatterySaver && value <= GraphicsPreset.HighQuality;
        static bool IsDefined(FrameRatePreference value) => value == FrameRatePreference.Fps30 || value == FrameRatePreference.Fps60;

        /// <summary>Clamps to [min, max] and snaps to the step grid that starts at min; NaN and infinity give the fallback.</summary>
        static float Stepped(float value, float min, float max, float step, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
            if (value < min) value = min;
            if (value > max) value = max;
            float steps = (float)Math.Round((value - min) / step, MidpointRounding.AwayFromZero);
            float snapped = min + steps * step;
            if (snapped > max) snapped = max;
            // Remove float noise (0.7000001) so stored and displayed values stay exact.
            return (float)Math.Round(snapped, 3);
        }
    }

    /// <summary>Where options are kept between sessions.</summary>
    public interface IOptionsStore
    {
        GameOptions Load(in GameOptions defaults);
        void Save(in GameOptions options);
    }

    /// <summary>Keeps options in memory (tests, and sessions that must not touch the player's saved choices).</summary>
    public sealed class MemoryOptionsStore : IOptionsStore
    {
        GameOptions _options;
        bool _saved;

        public int SaveCount { get; private set; }

        public GameOptions Load(in GameOptions defaults) => _saved ? _options : defaults;

        public void Save(in GameOptions options)
        {
            _options = options;
            _saved = true;
            SaveCount++;
        }
    }
}
