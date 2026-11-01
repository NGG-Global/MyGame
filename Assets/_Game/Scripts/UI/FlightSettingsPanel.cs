using System;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>Profile options the player can switch in the settings card.</summary>
    public enum FlightSettingOption
    {
        /// <summary>Assisted: gentle stall prevention.</summary>
        StallPrevention = 0,
        /// <summary>Assisted: flying tips.</summary>
        Guidance = 1,
        /// <summary>Standard: automatic pitch trim.</summary>
        AutoTrim = 2,
    }

    /// <summary>What the settings card shows.</summary>
    public struct FlightSettingsState
    {
        public FlightAssistProfile Profile;
        public ControlSensitivity Sensitivity;
        public bool StallPrevention;
        public bool Guidance;
        public bool AutoTrim;
    }

    /// <summary>
    /// The flight assistance selector: profile (Assisted, Standard, Manual), stick
    /// sensitivity, and the selected profile's options, with large buttons in the
    /// free column (the same place as the cockpit focus panel, never both). It
    /// never pauses the game and only raises requests; the session applies them.
    /// </summary>
    public sealed class FlightSettingsPanel : MonoBehaviour
    {
        public const int OptionButtons = 2;

        public RectTransform column;
        public RectTransform card;
        public CanvasGroup group;
        public Text title;
        public Text description;
        public Text sensitivityLabel;
        public HudButton close;
        [Tooltip("Index = FlightAssistProfile.")]
        public HudButton[] profiles = new HudButton[FlightAssistProfiles.Count];
        [Tooltip("Index = ControlSensitivity.")]
        public HudButton[] sensitivities = new HudButton[3];
        public HudButton[] options = new HudButton[OptionButtons];
        public Text optionNote;

        [Header("Layout and motion")]
        public HudMotionSettings motion = new HudMotionSettings();
        public float maxWidth = 600f;
        public float padding = 14f;
        public float spacing = 10f;
        public float buttonHeight = 96f;
        public float headerHeight = 56f;
        public float descriptionHeight = 64f;
        public float labelHeight = 30f;

        public event Action<FlightAssistProfile> ProfileChosen;
        public event Action<ControlSensitivity> SensitivityChosen;
        public event Action<FlightSettingOption> OptionToggled;
        public event Action CloseRequested;

        static readonly string[] OptionLabelsAssisted = { "STALL AID", "TIPS" };
        static readonly string[] OptionLabelsStandard = { "AUTO TRIM", "" };

        readonly FlightSettingOption[] _optionMeaning = new FlightSettingOption[OptionButtons];
        int _optionCount;
        HudFade _fade;
        bool _initialized, _reducedMotion, _open;
        FlightSettingsState _shown;
        bool _hasShown;

        public bool IsOpen => _open;
        public int VisibleOptionCount => _optionCount;
        public FlightSettingOption OptionMeaning(int index) => _optionMeaning[index];

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            RoundCard(card);
            if (group != null) _fade = new HudFade(group, interactive: true);
            if (close != null) close.Clicked += () => CloseRequested?.Invoke();
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] == null) continue;
                var profile = (FlightAssistProfile)i;
                profiles[i].SetLabel(FlightAssistProfiles.Label(profile));
                profiles[i].Clicked += () => ProfileChosen?.Invoke(profile);
            }
            for (int i = 0; i < sensitivities.Length; i++)
            {
                if (sensitivities[i] == null) continue;
                var sensitivity = (ControlSensitivity)i;
                sensitivities[i].SetLabel(FlightAssistProfiles.SensitivityLabel(sensitivity));
                sensitivities[i].Clicked += () => SensitivityChosen?.Invoke(sensitivity);
            }
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == null) continue;
                int index = i;
                options[i].Clicked += () => { if (index < _optionCount) OptionToggled?.Invoke(_optionMeaning[index]); };
            }
            Layout();
        }

        void OnRectTransformDimensionsChange()
        {
            if (_initialized) Layout();
        }

        public void Open(in FlightSettingsState state)
        {
            EnsureInitialized();
            _open = true;
            _hasShown = false;
            Present(state);
            _fade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), motion.Slide(_reducedMotion));
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        /// <summary>Shows the current settings; cheap when nothing changed (call every frame while open).</summary>
        public void Present(in FlightSettingsState state)
        {
            if (!_open) return;
            if (_hasShown && Same(state, _shown)) return;
            _hasShown = true;
            _shown = state;

            for (int i = 0; i < profiles.Length; i++)
                if (profiles[i] != null) profiles[i].SetEngaged(i == (int)state.Profile);
            for (int i = 0; i < sensitivities.Length; i++)
                if (sensitivities[i] != null) sensitivities[i].SetEngaged(i == (int)state.Sensitivity);
            SetText(description, FlightAssistProfiles.Description(state.Profile));

            switch (state.Profile)
            {
                case FlightAssistProfile.Assisted:
                    _optionCount = 2;
                    _optionMeaning[0] = FlightSettingOption.StallPrevention;
                    _optionMeaning[1] = FlightSettingOption.Guidance;
                    SetOption(0, OptionLabelsAssisted[0], state.StallPrevention);
                    SetOption(1, OptionLabelsAssisted[1], state.Guidance);
                    SetText(optionNote, string.Empty);
                    break;
                case FlightAssistProfile.Standard:
                    _optionCount = 1;
                    _optionMeaning[0] = FlightSettingOption.AutoTrim;
                    SetOption(0, OptionLabelsStandard[0], state.AutoTrim);
                    SetText(optionNote, string.Empty);
                    break;
                default:
                    _optionCount = 0;
                    SetText(optionNote, "Trim: Home / End keys or the gamepad d-pad. Touch trim is not available yet.");
                    break;
            }
            for (int i = 0; i < options.Length; i++)
                if (options[i] != null && options[i].gameObject.activeSelf != i < _optionCount) options[i].gameObject.SetActive(i < _optionCount);
            Layout();
        }

        void SetOption(int index, string label, bool on)
        {
            var button = options[index];
            if (button == null) return;
            button.SetLabel(label);
            button.SetEngaged(on);
        }

        static bool Same(in FlightSettingsState a, in FlightSettingsState b) =>
            a.Profile == b.Profile && a.Sensitivity == b.Sensitivity && a.StallPrevention == b.StallPrevention
            && a.Guidance == b.Guidance && a.AutoTrim == b.AutoTrim;

        static void SetText(Text text, string value)
        {
            if (text != null && !ReferenceEquals(text.text, value)) text.text = value ?? string.Empty;
        }

        void LateUpdate()
        {
            if (_initialized) _fade?.Tick(Time.unscaledDeltaTime);
        }

        // ---- Layout ---------------------------------------------------------------------------------

        /// <summary>Sizes the card to the free column (any landscape screen and safe area) and places the rows.</summary>
        public void Layout()
        {
            if (column == null || card == null) return;
            Vector2 area = column.rect.size;
            if (area.x <= 0f || area.y <= 0f) return;

            float width = Mathf.Min(area.x, maxWidth);
            float inner = width - 2f * padding;
            float y = -padding;
            y -= headerHeight;
            float third = (inner - 2f * spacing) / 3f;
            for (int i = 0; i < profiles.Length; i++) Place(profiles[i], padding + i * (third + spacing), y, third);
            y -= buttonHeight + spacing;
            Place(description, padding, y, inner, descriptionHeight);
            y -= descriptionHeight;
            Place(sensitivityLabel, padding, y, inner, labelHeight);
            y -= labelHeight;
            for (int i = 0; i < sensitivities.Length; i++) Place(sensitivities[i], padding + i * (third + spacing), y, third);
            y -= buttonHeight + spacing;
            int count = Mathf.Max(1, _optionCount);
            float optionWidth = (inner - (count - 1) * spacing) / count;
            for (int i = 0; i < options.Length; i++) Place(options[i], padding + i * (optionWidth + spacing), y, optionWidth);
            Place(optionNote, padding, y, inner, buttonHeight);
            y -= buttonHeight + padding;
            card.sizeDelta = new Vector2(width, Mathf.Min(-y, area.y));
        }

        void Place(Component item, float x, float top, float width, float height = -1f)
        {
            if (item == null) return;
            var rect = (RectTransform)item.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, top);
            rect.sizeDelta = new Vector2(width, height > 0f ? height : buttonHeight);
        }

        /// <summary>Runtime-generated sprites cannot be stored in a scene: the card gets its rounded shape here.</summary>
        static void RoundCard(RectTransform rect)
        {
            var image = rect != null ? rect.GetComponent<Image>() : null;
            if (image != null && image.sprite == null) HudStyle.Round(image, HudStyle.PanelRadius);
        }
    }
}
