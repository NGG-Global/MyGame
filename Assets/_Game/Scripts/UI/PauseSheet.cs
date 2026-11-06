using System;
using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The pause sheet: a bottom-anchored sheet that rises when the flight pauses
    /// and is dragged between two stops. The first stop holds the flight's choices
    /// (RESUME, RESTART, CHANGE MODE and one mode-specific action); pulling higher
    /// reveals the settings embedded in the sheet. It tracks the finger, settles on a
    /// stop with one soft overshoot, and is dismissed by dragging it down or RESUME.
    /// A tap on the dimmed world collapses the settings back to the first stop and does
    /// nothing at the first stop: resuming is a deliberate action
    /// (docs/TAP_DISCLOSURE_DESIGN.md). Draws what <see cref="SheetModel"/> says; the
    /// session decides what the buttons do and when the flight is frozen.
    /// </summary>
    public sealed class PauseSheet : MonoBehaviour
    {
        public const int MaxButtons = 4;

        public CanvasGroup group;
        [Tooltip("Dims the world above the sheet; a tap on it folds the settings back to the first stop, never resumes.")]
        public Image scrim;
        public HudButton scrimButton;
        [Tooltip("The sheet itself, anchored to the bottom of the safe area; its height follows the model.")]
        public RectTransform sheet;
        public Image surface;
        [Tooltip("The grip band at the top of the sheet: drag it (route Sheet).")]
        public GestureHotspot grip;
        public Image gripBar;
        public Text title;
        public Text subtitle;
        public HudButton[] buttons = new HudButton[MaxButtons];
        [Tooltip("What pulling higher reveals.")]
        public Text pullHint;
        public CanvasGroup secondStopGroup;
        [Tooltip("The settings embedded in the second stop (built by SettingsScreenFactory.BuildEmbedded).")]
        public SettingsScreen settings;
        public SheetSettings sheetSettings = new SheetSettings();
        public HudMotionSettings motion = new HudMotionSettings();
        [Tooltip("Dimming of the world at the first stop.")]
        [Range(0f, 1f)] public float scrimAlpha = 0.45f;
        public float gripHeight = 48f;

        /// <summary>A first-stop button was tapped (index in the order given to <see cref="Open"/>).</summary>
        public event Action<int> ButtonClicked;
        /// <summary>The player dismissed the sheet (drag down or a swipe): resume.</summary>
        public event Action DismissRequested;
        /// <summary>A tap on the dimmed world at the first stop: consumed, nothing resumes (for hosts and tests).</summary>
        public event Action OutsideTapped;
        /// <summary>The sheet reached or left the second stop (the settings): the host presents them while it is there.</summary>
        public event Action<bool> SettingsShown;

        SheetModel _model;
        bool _initialized, _reducedMotion, _open, _settingsShown;
        int _count;
        float _lastDragY, _lastDragTime, _velocity;
        Vector2 _dragLocal;

        public SheetModel Model => _model;
        public bool IsOpen => _open;
        public int Stop => _model != null ? _model.Stop : SheetModel.Closed;
        public bool SettingsVisible => _settingsShown;
        public int ButtonCount => _count;

        public void SetReducedMotion(bool reduced)
        {
            _reducedMotion = reduced;
            if (settings != null) settings.SetReducedMotion(reduced);
        }

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _model = new SheetModel(sheetSettings);
            if (surface != null && surface.sprite == null) HudStyle.Round(surface, HudStyle.PanelRadius);
            if (gripBar != null && gripBar.sprite == null) HudStyle.Round(gripBar, 4f);
            if (scrimButton != null) scrimButton.Clicked += OnScrimTapped;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                int index = i; // one closure per button, created once
                buttons[i].Clicked += () => { if (_open && index < _count) ButtonClicked?.Invoke(index); };
            }
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            Apply();
        }

        /// <summary>Opens at the first stop with the flight's choices (cached strings; up to four).</summary>
        public void Open(string heading, string text, string button0, string button1 = null, string button2 = null, string button3 = null)
        {
            EnsureInitialized();
            float screenHeight = sheet != null && sheet.parent is RectTransform parent ? parent.rect.height : 1080f;
            _model.SetLimits(screenHeight);
            _open = true;
            SetText(title, heading);
            SetText(subtitle, text);
            _count = 0;
            Label(button0); Label(button1); Label(button2); Label(button3);
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i] != null) buttons[i].gameObject.SetActive(i < _count);
            LayoutButtons();
            _model.Open(SheetModel.FirstStop, instant: _reducedMotion);
            if (group != null)
            {
                group.blocksRaycasts = true;
                group.interactable = true;
            }
            if (settings != null && !settings.IsOpen) settings.Open(default); // the host presents the real view each frame
            Apply();
        }

        /// <summary>Changes a button's caption while open (the destination choice).</summary>
        public void SetButtonLabel(int index, string label)
        {
            if (index >= 0 && index < buttons.Length && buttons[index] != null) buttons[index].SetLabel(label);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _model.Close(instant: _reducedMotion);
            if (group != null)
            {
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            if (settings != null) settings.Close();
            SetSettingsShown(false);
            Apply();
        }

        /// <summary>The tap-first contract: outside the expanded settings collapses them to the paused sheet; outside the sheet itself does nothing.</summary>
        void OnScrimTapped()
        {
            if (!_open || _model == null) return;
            if (_model.Stop == SheetModel.SecondStop || _settingsShown) _model.GoTo(SheetModel.FirstStop);
            else OutsideTapped?.Invoke();
        }

        /// <summary>Jumps to a stop (SETTINGS from a keyboard, or a tap on the grip).</summary>
        public void GoTo(int stop)
        {
            if (!_open) return;
            if (stop == SheetModel.Closed) { DismissRequested?.Invoke(); return; }
            _model.GoTo(stop);
        }

        /// <summary>The grip's gestures, forwarded by the session from the interaction surface (route Sheet).</summary>
        public void OnGesture(in GestureEvent e)
        {
            if (!_open || _model == null) return;
            switch (e.Kind)
            {
                case GestureKind.Drag:
                    switch (e.Type)
                    {
                        case GestureEventType.Began:
                            _model.BeginDrag(LocalY(e.Origin));
                            _lastDragY = LocalY(e.Origin);
                            _lastDragTime = e.Time;
                            _velocity = 0f;
                            _model.Drag(LocalY(e.Position));
                            break;
                        case GestureEventType.Changed:
                        {
                            float y = LocalY(e.Position);
                            float dt = e.Time - _lastDragTime;
                            if (dt > 1e-4f) _velocity = Mathf.Lerp(_velocity, (y - _lastDragY) / dt, 0.5f);
                            _lastDragY = y;
                            _lastDragTime = e.Time;
                            _model.Drag(y);
                            break;
                        }
                        case GestureEventType.Completed:
                        case GestureEventType.Cancelled:
                        {
                            int stop = _model.Release(e.Type == GestureEventType.Completed ? _velocity : 0f);
                            if (stop == SheetModel.Closed) DismissRequested?.Invoke();
                            else HudButton.Report(UiFeedback.Tick);
                            break;
                        }
                    }
                    break;
                case GestureKind.Tap:
                    if (e.Type == GestureEventType.Completed) _model.GoTo(_model.Stop == SheetModel.FirstStop ? SheetModel.SecondStop : SheetModel.FirstStop);
                    break;
            }
        }

        float LocalY(Vector2 screenPoint)
        {
            var parent = sheet != null ? sheet.parent as RectTransform : null;
            if (parent == null) return screenPoint.y;
            var canvas = parent.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, camera, out _dragLocal);
            return _dragLocal.y;
        }

        void Label(string label)
        {
            if (string.IsNullOrEmpty(label) || _count >= buttons.Length) return;
            if (buttons[_count] != null) buttons[_count].SetLabel(label);
            _count++;
        }

        void LayoutButtons()
        {
            if (sheet == null) return;
            float width = sheet.rect.width;
            if (width <= 0f) width = 1180f;
            const float pad = 24f, gap = 12f, height = HudStyle.MinTouchTarget;
            int count = Mathf.Max(1, _count);
            // Four across when each is wide enough, otherwise two rows of two.
            int columns = (width - 2f * pad - (count - 1) * gap) / count >= 150f ? count : 2;
            float buttonWidth = (width - 2f * pad - (columns - 1) * gap) / columns;
            float top = -(gripHeight + 60f);
            for (int i = 0; i < _count; i++)
            {
                var button = buttons[i];
                if (button == null) continue;
                var rect = (RectTransform)button.transform;
                int row = i / columns, col = i % columns;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(pad + col * (buttonWidth + gap), top - row * (height + gap));
                rect.sizeDelta = new Vector2(buttonWidth, height);
            }
        }

        void Apply()
        {
            if (_model == null) return;
            float height = _model.Height;
            if (sheet != null) sheet.sizeDelta = new Vector2(sheet.sizeDelta.x, Mathf.Max(0f, height));
            float first = _model.StopHeight(SheetModel.FirstStop);
            float openness = first > 0f ? Mathf.Clamp01(height / first) : 0f;
            if (group != null) group.alpha = openness > 0.01f || _open ? 1f : 0f;
            if (scrim != null)
            {
                var c = scrim.color;
                c.a = scrimAlpha * openness;
                scrim.color = c;
            }
            float depth = _model.Depth;
            if (secondStopGroup != null)
            {
                secondStopGroup.alpha = depth;
                bool interactive = depth > 0.6f;
                secondStopGroup.blocksRaycasts = interactive;
                secondStopGroup.interactable = interactive;
            }
            if (pullHint != null)
            {
                var c = pullHint.color;
                c.a = (1f - depth) * 0.74f * openness;
                pullHint.color = c;
            }
            SetSettingsShown(_open && depth > 0.6f);
        }

        void SetSettingsShown(bool shown)
        {
            if (shown == _settingsShown) return;
            _settingsShown = shown;
            SettingsShown?.Invoke(shown);
        }

        static void SetText(Text text, string value)
        {
            if (text == null) return;
            if (!ReferenceEquals(text.text, value)) text.text = value ?? string.Empty;
            text.enabled = !string.IsNullOrEmpty(value);
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            _model.Tick(_reducedMotion ? 10f : Time.unscaledDeltaTime);
            Apply();
        }
    }
}
