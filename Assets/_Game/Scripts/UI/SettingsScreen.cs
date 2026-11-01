using System;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.Options;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>Every setting on the settings screen.</summary>
    public enum SettingId
    {
        HudProfile = 0,
        UiScale,
        HudOpacity,
        Motion,
        AssistProfile,
        Sensitivity,
        Haptics,
        MasterVolume,
        AircraftVolume,
        EnvironmentVolume,
        InterfaceVolume,
        WarningVolume,
        Graphics,
        FrameRate,
    }

    /// <summary>The settings screen's tabs.</summary>
    public enum SettingsPage
    {
        Display = 0,
        Controls = 1,
        Sound = 2,
        Graphics = 3,
    }

    /// <summary>What the settings screen shows: the current value of every setting and what the device supports.</summary>
    public struct SettingsView
    {
        public GameOptions Options;
        /// <summary>The UI scale this screen can actually use (the layout limits it on narrow screens).</summary>
        public float EffectiveUiScale;
        public HudProfile HudProfile;
        public FlightAssistProfile AssistProfile;
        public ControlSensitivity Sensitivity;
        public bool HapticsSupported;
        public bool Fps60Supported;
    }

    /// <summary>One row: a label and either up to three segments or a minus/value/plus stepper.</summary>
    [Serializable]
    public sealed class SettingsRow
    {
        public SettingId id;
        public RectTransform root;
        public Text label;
        [Tooltip("A short line under the label (limits, device support).")]
        public Text note;
        public HudButton[] segments = new HudButton[0];
        public HudButton minus;
        public HudButton plus;
        public Text value;

        public bool IsStepper => minus != null && plus != null;
    }

    /// <summary>
    /// The settings screen: display, controls, sound and graphics, four short pages
    /// of large segmented choices and steppers. Opened from the pause menu and the
    /// entry screen over a paused world; it takes every touch while open. It only
    /// shows values and raises requests; the session applies, limits and stores them.
    /// </summary>
    public sealed class SettingsScreen : MonoBehaviour
    {
        public const int PageCount = 4;

        public CanvasGroup group;
        public RectTransform panel;
        public Text title;
        public HudButton doneButton;
        [Tooltip("Index = SettingsPage.")]
        public HudButton[] tabs = new HudButton[PageCount];
        [Tooltip("Index = SettingsPage.")]
        public CanvasGroup[] pages = new CanvasGroup[PageCount];
        public SettingsRow[] rows = new SettingsRow[0];
        public Text footnote;
        public HudMotionSettings motion = new HudMotionSettings();

        /// <summary>A segment was chosen (index from the left).</summary>
        public event Action<SettingId, int> OptionChosen;
        /// <summary>A stepper moved (+1 or -1).</summary>
        public event Action<SettingId, int> Stepped;
        public event Action CloseRequested;

        static readonly string[] Percent = BuildPercent();
        static readonly string[] Footnotes =
        {
            "UI scale is limited on narrow screens so that no control overlaps another.",
            "Haptics follow your device's touch feedback setting too. Every cue also has a sound or a visual.",
            "Warning sounds can be turned down, not off: the banners carry the words, the sound makes you look.",
            "Graphics settings change how the world is drawn, never how the aircraft flies.",
        };

        HudFade _fade;
        HudFade[] _pageFades;
        bool _initialized, _open, _reducedMotion, _hasShown;
        SettingsPage _page;
        SettingsView _shown;

        public bool IsOpen => _open;
        public SettingsPage Page => _page;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (panel != null)
            {
                var image = panel.GetComponent<Image>();
                if (image != null && image.sprite == null) HudStyle.Round(image, HudStyle.PanelRadius);
            }
            if (group != null) _fade = new HudFade(group, interactive: true);
            _pageFades = new HudFade[PageCount];
            for (int i = 0; i < PageCount; i++)
            {
                if (i < pages.Length && pages[i] != null) _pageFades[i] = new HudFade(pages[i], interactive: true, initialAlpha: pages[i].alpha);
                if (i < tabs.Length && tabs[i] != null)
                {
                    var page = (SettingsPage)i;
                    tabs[i].Clicked += () => ShowPage(page);
                }
            }
            if (doneButton != null) doneButton.Clicked += () => CloseRequested?.Invoke();
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                if (row == null) continue;
                var id = row.id;
                for (int s = 0; s < row.segments.Length; s++)
                {
                    if (row.segments[s] == null) continue;
                    int index = s; // one closure per button, created once
                    row.segments[s].Clicked += () => { if (_open) OptionChosen?.Invoke(id, index); };
                }
                if (row.minus != null) row.minus.Clicked += () => { if (_open) Stepped?.Invoke(id, -1); };
                if (row.plus != null) row.plus.Clicked += () => { if (_open) Stepped?.Invoke(id, 1); };
            }
        }

        public void Open(in SettingsView view)
        {
            EnsureInitialized();
            _open = true;
            _hasShown = false;
            ShowPage(_page);
            Present(view);
            _fade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        public void ShowPage(SettingsPage page)
        {
            EnsureInitialized();
            _page = page;
            for (int i = 0; i < PageCount; i++)
            {
                bool on = i == (int)page;
                _pageFades[i]?.SetTarget(on ? 1f : 0f, 0f, 0f); // tabs switch at once: no cross-fade to wait for
                if (i < tabs.Length && tabs[i] != null) tabs[i].SetEngaged(on);
            }
            if (footnote != null) footnote.text = Footnotes[(int)page];
        }

        /// <summary>Shows the values; cheap when nothing changed (call every frame while open).</summary>
        public void Present(in SettingsView view)
        {
            if (!_open) return;
            if (_hasShown && Same(view, _shown)) return;
            _hasShown = true;
            _shown = view;
            var o = view.Options;
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                if (row == null) continue;
                switch (row.id)
                {
                    case SettingId.HudProfile: Select(row, HudProfileIndex(view.HudProfile)); break;
                    case SettingId.Motion: Select(row, (int)o.Motion); break;
                    case SettingId.AssistProfile: Select(row, (int)view.AssistProfile); break;
                    case SettingId.Sensitivity: Select(row, (int)view.Sensitivity); break;
                    case SettingId.Haptics:
                        Select(row, o.Haptics && view.HapticsSupported ? 0 : 1);
                        SetEnabled(row, view.HapticsSupported);
                        SetNote(row, view.HapticsSupported ? string.Empty : "Not available on this device");
                        break;
                    case SettingId.Graphics: Select(row, (int)o.Graphics); break;
                    case SettingId.FrameRate:
                        Select(row, o.FrameRate == FrameRatePreference.Fps60 && view.Fps60Supported ? 1 : 0);
                        if (row.segments.Length > 1 && row.segments[1] != null) row.segments[1].SetInteractable(view.Fps60Supported);
                        SetNote(row, view.Fps60Supported ? "60 uses more battery and may warm the device" : "This display cannot show 60 FPS");
                        break;
                    case SettingId.UiScale:
                        SetValue(row, Percent[GameOptionsRules.Percent(o.UiScale)]);
                        bool limited = view.EffectiveUiScale < o.UiScale - 0.001f;
                        SetNote(row, limited ? "Limited to " + Percent[GameOptionsRules.Percent(view.EffectiveUiScale)] + " on this screen" : string.Empty);
                        SetStepper(row, o.UiScale > GameOptionsRules.MinUiScale + 0.001f, o.UiScale < GameOptionsRules.MaxUiScale - 0.001f);
                        break;
                    case SettingId.HudOpacity:
                        SetValue(row, Percent[GameOptionsRules.Percent(o.HudOpacity)]);
                        SetStepper(row, o.HudOpacity > GameOptionsRules.MinHudOpacity + 0.001f, o.HudOpacity < 0.999f);
                        break;
                    default:
                        var channel = VolumeChannelOf(row.id);
                        float volume = o.Volume(channel);
                        SetValue(row, Percent[GameOptionsRules.Percent(volume)]);
                        SetStepper(row, volume > GameOptionsRules.MinimumVolume(channel) + 0.001f, volume < 0.999f);
                        break;
                }
            }
        }

        /// <summary>Segment order of the HUD profile row: the order the HUD button cycles (Smart, Detailed, Cinematic).</summary>
        public static int HudProfileIndex(HudProfile profile) =>
            profile == HudProfile.Smart ? 0 : (profile == HudProfile.Detailed ? 1 : 2);

        public static HudProfile HudProfileAt(int index) =>
            index == 0 ? HudProfile.Smart : (index == 1 ? HudProfile.Detailed : HudProfile.Cinematic);

        public static VolumeChannel VolumeChannelOf(SettingId id)
        {
            switch (id)
            {
                case SettingId.AircraftVolume: return VolumeChannel.Aircraft;
                case SettingId.EnvironmentVolume: return VolumeChannel.Environment;
                case SettingId.InterfaceVolume: return VolumeChannel.Interface;
                case SettingId.WarningVolume: return VolumeChannel.Warnings;
                default: return VolumeChannel.Master;
            }
        }

        public SettingsRow Row(SettingId id)
        {
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null && rows[i].id == id) return rows[i];
            return null;
        }

        static void Select(SettingsRow row, int index)
        {
            for (int s = 0; s < row.segments.Length; s++)
                if (row.segments[s] != null) row.segments[s].SetEngaged(s == index);
        }

        static void SetEnabled(SettingsRow row, bool enabled)
        {
            for (int s = 0; s < row.segments.Length; s++)
                if (row.segments[s] != null) row.segments[s].SetInteractable(enabled);
        }

        static void SetStepper(SettingsRow row, bool canDecrease, bool canIncrease)
        {
            if (row.minus != null) row.minus.SetInteractable(canDecrease);
            if (row.plus != null) row.plus.SetInteractable(canIncrease);
        }

        static void SetValue(SettingsRow row, string text)
        {
            if (row.value != null && !ReferenceEquals(row.value.text, text)) row.value.text = text;
        }

        static void SetNote(SettingsRow row, string text)
        {
            if (row.note != null && row.note.text != text) row.note.text = text;
        }

        static bool Same(in SettingsView a, in SettingsView b) =>
            GameOptionsRules.Same(a.Options, b.Options) && Mathf.Approximately(a.EffectiveUiScale, b.EffectiveUiScale) && a.HudProfile == b.HudProfile
            && a.AssistProfile == b.AssistProfile && a.Sensitivity == b.Sensitivity && a.HapticsSupported == b.HapticsSupported
            && a.Fps60Supported == b.Fps60Supported;

        static string[] BuildPercent()
        {
            var labels = new string[201];
            for (int i = 0; i < labels.Length; i++) labels[i] = i + "%";
            return labels;
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            float dt = Time.unscaledDeltaTime;
            _fade?.Tick(dt);
            for (int i = 0; i < _pageFades.Length; i++) _pageFades[i]?.Tick(dt);
        }
    }
}
