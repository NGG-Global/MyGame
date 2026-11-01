using System;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// One panel at a time, in the free column between the joystick area and the
    /// action rail: either an enlarged instrument, or a selector for one aircraft
    /// system with large option buttons. It never covers a flight control, never
    /// pauses anything, and only raises requests; the session decides and applies
    /// them to the authoritative aircraft systems.
    ///
    /// Dismissal: the close button, a tap on the enlarged instrument, a tap on an
    /// empty part of the cockpit, tapping the same cockpit item again, or a
    /// camera change. A hidden instrument stops rendering (its face is
    /// deactivated once it has faded out), so only the cockpit copy remains.
    /// </summary>
    public sealed class CockpitFocusPanel : MonoBehaviour
    {
        public const int MaxOptions = 4;
        public const int InstrumentCount = 6;

        [Tooltip("Free area between the joystick region and the action rail; both cards live inside it.")]
        public RectTransform column;

        [Header("Instrument view")]
        public RectTransform instrumentCard;
        public CanvasGroup instrumentGroup;
        public HudButton instrumentTapToClose;
        public Text instrumentTitle;
        public Text instrumentHint;
        public RectTransform faceHost;
        public InstrumentFace[] faces = new InstrumentFace[InstrumentCount];
        public HudButton instrumentClose;

        [Header("System selector")]
        public RectTransform systemCard;
        public CanvasGroup systemGroup;
        public Text systemTitle;
        public Text systemHint;
        public Text systemState;
        public HudButton[] options = new HudButton[MaxOptions];
        public Text confirmation;
        public HudButton systemClose;

        [Header("Layout and motion")]
        public HudMotionSettings motion = new HudMotionSettings();
        public float maxInstrumentCard = 560f;
        public float maxSystemCardWidth = 600f;
        public float headerHeight = 64f;
        public float optionHeight = 96f;
        public float minimumOptionWidth = 120f;
        public float spacing = 12f;
        public float padding = 14f;
        public Color confirmColour = new Color(0.85f, 0.2f, 0.12f, 0.95f);

        /// <summary>The player wants the panel closed.</summary>
        public event Action CloseRequested;
        /// <summary>The player picked an option of the system selector (index into the labels given to <see cref="ShowSystem"/>).</summary>
        public event Action<int> OptionChosen;

        enum Mode { None, Instrument, System }

        Mode _mode;
        InstrumentKind _instrument;
        AircraftSystem _system;
        int _optionCount;
        int _confirmOption = -1;
        HudFade _instrumentFade, _systemFade;
        bool _initialized, _reducedMotion;
        readonly Color[] _engagedColours = new Color[MaxOptions];

        public bool IsOpen => _mode != Mode.None;
        public int OptionCount => _optionCount;
        public int PendingConfirmation => _confirmOption;

        public bool IsShowingInstrument(out InstrumentKind kind)
        {
            kind = _instrument;
            return _mode == Mode.Instrument;
        }

        public bool IsShowingSystem(out AircraftSystem system)
        {
            system = _system;
            return _mode == Mode.System;
        }

        /// <summary>The enlarged face being shown, or null.</summary>
        public InstrumentFace ActiveFace => _mode == Mode.Instrument ? faces[(int)_instrument] : null;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            RoundCard(instrumentCard);
            RoundCard(systemCard);
            if (instrumentGroup != null) _instrumentFade = new HudFade(instrumentGroup, interactive: true);
            if (systemGroup != null) _systemFade = new HudFade(systemGroup, interactive: true);
            if (instrumentClose != null) instrumentClose.Clicked += RaiseClose;
            if (instrumentTapToClose != null) instrumentTapToClose.Clicked += RaiseClose;
            if (systemClose != null) systemClose.Clicked += RaiseClose;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == null) continue;
                int index = i; // one closure per option, created once
                options[i].Clicked += () => OptionChosen?.Invoke(index);
                _engagedColours[i] = options[i].engagedColor;
            }
            SetFacesActive(-1);
            Layout();
        }

        void OnRectTransformDimensionsChange()
        {
            if (_initialized) Layout();
        }

        // ---- Showing ----------------------------------------------------------------------

        public void ShowInstrument(InstrumentKind kind, string title, string hint)
        {
            EnsureInitialized();
            _mode = Mode.Instrument;
            _instrument = kind;
            _confirmOption = -1;
            SetFacesActive((int)kind);
            SetText(instrumentTitle, title);
            SetText(instrumentHint, hint);
            _systemFade?.SetTarget(0f, 0f, 0f); // one panel at a time: the other goes at once
            _instrumentFade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), motion.Slide(_reducedMotion));
            Layout();
        }

        /// <summary>Shows a selector with up to <see cref="MaxOptions"/> options. Labels should be cached strings.</summary>
        public void ShowSystem(AircraftSystem system, string title, string hint, string[] labels, int count)
        {
            EnsureInitialized();
            _mode = Mode.System;
            _system = system;
            _optionCount = Mathf.Clamp(count, 0, MaxOptions);
            _confirmOption = -1;
            SetText(systemTitle, title);
            SetText(systemHint, hint);
            SetText(confirmation, string.Empty);
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == null) continue;
                bool used = i < _optionCount;
                options[i].gameObject.SetActive(used);
                if (used) options[i].SetLabel(labels[i]);
                options[i].SetEngagedColor(_engagedColours[i]);
            }
            _instrumentFade?.SetTarget(0f, 0f, 0f);
            _systemFade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), motion.Slide(_reducedMotion));
            Layout();
        }

        /// <summary>Per-frame state of the selector: which option is the current one, which can be chosen, and a state line.</summary>
        public void UpdateSystem(int selected, int enabledMask, string state)
        {
            if (_mode != Mode.System) return;
            for (int i = 0; i < _optionCount; i++)
            {
                var option = options[i];
                if (option == null) continue;
                bool pending = i == _confirmOption;
                option.SetEngagedColor(pending ? confirmColour : _engagedColours[i]);
                option.SetEngaged(i == selected || pending);
                option.SetInteractable((enabledMask & (1 << i)) != 0);
            }
            SetText(systemState, state);
        }

        /// <summary>A hazardous choice waits for a second tap on the same option; the message says so.</summary>
        public void SetConfirmation(int option, string message)
        {
            _confirmOption = option;
            SetText(confirmation, message);
        }

        public void ClearConfirmation()
        {
            _confirmOption = -1;
            SetText(confirmation, string.Empty);
        }

        public void Close()
        {
            if (_mode == Mode.None) return;
            _mode = Mode.None;
            _confirmOption = -1;
            float fade = motion.FadeSeconds(_reducedMotion);
            _instrumentFade?.SetTarget(0f, fade, 0f);
            _systemFade?.SetTarget(0f, fade, 0f);
        }

        /// <summary>Draws the enlarged instrument (if one is shown) from the shared readings.</summary>
        public void Render(in InstrumentReadings readings)
        {
            var face = ActiveFace;
            if (face != null) face.Render(readings);
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            float dt = Time.unscaledDeltaTime;
            _instrumentFade?.Tick(dt);
            _systemFade?.Tick(dt);
            // Once the enlarged instrument has faded out it stops existing: no second copy is drawn.
            if (_mode != Mode.Instrument && _instrumentFade != null && !_instrumentFade.IsAnimating && _instrumentFade.Alpha <= 0f)
                SetFacesActive(-1);
        }

        void RaiseClose() => CloseRequested?.Invoke();

        void SetFacesActive(int active)
        {
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i] == null) continue;
                bool on = i == active;
                if (faces[i].gameObject.activeSelf != on) faces[i].gameObject.SetActive(on);
            }
        }

        static void SetText(Text text, string value)
        {
            if (text != null && !ReferenceEquals(text.text, value)) text.text = value ?? string.Empty;
        }

        // ---- Layout -----------------------------------------------------------------------

        /// <summary>Sizes both cards to the free column (any landscape screen and safe area) and lays out the options.</summary>
        public void Layout()
        {
            if (column == null) return;
            Vector2 area = column.rect.size;
            if (area.x <= 0f || area.y <= 0f) return;

            if (instrumentCard != null)
            {
                float side = Mathf.Min(Mathf.Min(area.x, area.y), maxInstrumentCard); // two-argument Min: no params array
                instrumentCard.sizeDelta = new Vector2(side, side);
                if (faceHost != null)
                {
                    float face = Mathf.Max(0f, Mathf.Min(side - 2f * padding, side - headerHeight - padding));
                    faceHost.sizeDelta = new Vector2(face, face);
                    faceHost.anchoredPosition = new Vector2(0f, -0.5f * (headerHeight - padding));
                }
            }

            if (systemCard == null) return;
            float width = Mathf.Min(area.x, maxSystemCardWidth);
            int count = Mathf.Max(1, _optionCount);
            float inner = width - 2f * padding;
            int columns = count;
            while (columns > 1 && (inner - (columns - 1) * spacing) / columns < minimumOptionWidth) columns = (columns + 1) / 2;
            int rows = (count + columns - 1) / columns;
            float buttonWidth = (inner - (columns - 1) * spacing) / columns;
            float stateHeight = 40f, confirmHeight = 30f;
            float height = headerHeight + stateHeight + rows * optionHeight + (rows - 1) * spacing + confirmHeight + 2f * padding;
            systemCard.sizeDelta = new Vector2(width, Mathf.Min(height, area.y));

            float top = -(headerHeight + stateHeight + padding);
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == null) continue;
                var rect = (RectTransform)options[i].transform;
                int row = i / columns, col = i % columns;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(buttonWidth, optionHeight);
                rect.anchoredPosition = new Vector2(padding + col * (buttonWidth + spacing), top - row * (optionHeight + spacing));
            }
        }

        /// <summary>Runtime-generated sprites cannot be stored in a scene: the cards get their rounded shape here.</summary>
        static void RoundCard(RectTransform rect)
        {
            var image = rect != null ? rect.GetComponent<Image>() : null;
            if (image != null && image.sprite == null) HudStyle.Round(image, HudStyle.PanelRadius);
        }
    }
}
