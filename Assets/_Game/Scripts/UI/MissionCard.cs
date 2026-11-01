using System;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// A small card in the free column (where the cockpit focus panel and the
    /// flight settings card open, never at the same time) for the pause menu and
    /// mission results: a title, a few short lines, an optional footnote and up
    /// to four large buttons. Modal cards (pause) dim the screen and take every
    /// touch around them, so nothing changes behind a paused game; result cards
    /// leave the flight controls usable.
    /// </summary>
    public sealed class MissionCard : MonoBehaviour
    {
        public const int MaxButtons = 4;

        public RectTransform column;
        public RectTransform card;
        public CanvasGroup group;
        [Tooltip("Full-screen dimming behind a modal card; it takes the touches around the card.")]
        public Image scrim;
        public Text title;
        public Text body;
        public Text footnote;
        public HudButton[] buttons = new HudButton[MaxButtons];
        public HudMotionSettings motion = new HudMotionSettings();
        public float maxWidth = 600f;
        public float padding = 16f;
        public float spacing = 10f;
        public float buttonHeight = 96f;
        public float titleHeight = 52f;
        public float lineHeight = 30f;
        public float footnoteHeight = 28f;
        [Tooltip("Two buttons sit side by side when each can be at least this wide.")]
        public float sideBySideMinWidth = 150f;

        HudFade _fade;
        bool _initialized, _open, _modal, _suppressed, _reducedMotion;
        int _count;

        /// <summary>A button was tapped (its index in the order given to <see cref="Show"/>).</summary>
        public event Action<int> ButtonClicked;

        public bool IsOpen => _open;
        public bool IsModal => _open && _modal;
        public int ButtonCount => _count;
        public bool Suppressed => _suppressed;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (group != null) _fade = new HudFade(group, interactive: true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                int index = i; // one closure per button, created once
                buttons[i].Clicked += () => { if (_open && index < _count) ButtonClicked?.Invoke(index); };
            }
            if (scrim != null) scrim.enabled = false;
        }

        void OnRectTransformDimensionsChange()
        {
            if (_initialized && _open) Layout();
        }

        /// <summary>Opens the card (texts are built by the caller once, not per frame).</summary>
        public void Show(string heading, string text, string note, bool modal, string button0, string button1 = null, string button2 = null,
            string button3 = null)
        {
            EnsureInitialized();
            _open = true;
            _modal = modal;
            SetText(title, heading);
            SetText(body, text);
            SetText(footnote, note);
            _count = 0;
            Label(button0);
            Label(button1);
            Label(button2);
            Label(button3);
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i] != null) buttons[i].gameObject.SetActive(i < _count);
            if (scrim != null) scrim.enabled = modal;
            Layout();
            Apply();
        }

        /// <summary>Changes a button's caption while open (e.g. the destination choice).</summary>
        public void SetButtonLabel(int index, string label)
        {
            if (index >= 0 && index < buttons.Length && buttons[index] != null) buttons[index].SetLabel(label);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (scrim != null) scrim.enabled = false;
            Apply();
        }

        /// <summary>Hidden while another panel uses the free column; shown again afterwards. Modal cards are never suppressed.</summary>
        public void SetSuppressed(bool suppressed)
        {
            if (suppressed == _suppressed) return;
            _suppressed = suppressed;
            Apply();
        }

        void Apply()
        {
            bool visible = _open && (!_suppressed || _modal);
            _fade?.SetTarget(visible ? 1f : 0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        void Label(string label)
        {
            if (string.IsNullOrEmpty(label) || _count >= buttons.Length) return;
            if (buttons[_count] != null) buttons[_count].SetLabel(label);
            _count++;
        }

        static void SetText(Text text, string value)
        {
            if (text == null) return;
            text.text = value ?? string.Empty;
            text.enabled = !string.IsNullOrEmpty(value);
        }

        /// <summary>Sizes the card to the free column and stacks its rows.</summary>
        public void Layout()
        {
            if (column == null || card == null) return;
            Vector2 area = column.rect.size;
            if (area.x <= 0f || area.y <= 0f) return;

            float width = Mathf.Min(area.x, maxWidth);
            float inner = width - 2f * padding;
            float y = -padding;
            Place(title, padding, y, inner, titleHeight);
            y -= titleHeight;
            if (body != null && body.enabled)
            {
                float height = Lines(body.text) * lineHeight;
                Place(body, padding, y, inner, height);
                y -= height + spacing;
            }
            if (footnote != null && footnote.enabled)
            {
                Place(footnote, padding, y, inner, footnoteHeight);
                y -= footnoteHeight + spacing;
            }

            bool sideBySide = _count == 2 && (inner - spacing) * 0.5f >= sideBySideMinWidth;
            for (int i = 0; i < _count; i++)
            {
                if (sideBySide)
                {
                    float half = (inner - spacing) * 0.5f;
                    Place(buttons[i], padding + i * (half + spacing), y, half, buttonHeight);
                }
                else
                {
                    Place(buttons[i], padding, y, inner, buttonHeight);
                    y -= buttonHeight + spacing;
                }
            }
            if (sideBySide) y -= buttonHeight + spacing;
            y -= padding - spacing;
            card.sizeDelta = new Vector2(width, Mathf.Min(-y, area.y));
        }

        static int Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int lines = 1;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') lines++;
            return lines;
        }

        static void Place(Component item, float x, float top, float width, float height)
        {
            if (item == null) return;
            var rect = (RectTransform)item.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, top);
            rect.sizeDelta = new Vector2(width, height);
        }

        void LateUpdate() => _fade?.Tick(Time.unscaledDeltaTime);
    }
}
