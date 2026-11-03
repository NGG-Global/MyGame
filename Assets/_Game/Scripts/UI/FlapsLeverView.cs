using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The flap lever that appears under the thumb when the arc hands over to
    /// flaps: a vertical track with one mark per detent, the knob following the
    /// finger with detent snapping, the previewed detent in cyan and the committed
    /// one in white, so the preview is never mistaken for the aircraft's state.
    /// Draws what <see cref="FlapsLeverModel"/> says; never changes the aircraft.
    /// </summary>
    public sealed class FlapsLeverView : MonoBehaviour
    {
        public const int MaxDetents = 6;

        [Tooltip("Placed at the lever's zero point (the finger when it appeared).")]
        public RectTransform root;
        public CanvasGroup group;
        public Image plate;
        public Image track;
        public Image[] marks = new Image[MaxDetents];
        public Text[] labels = new Text[MaxDetents];
        public Image knob;
        public Text knobLabel;
        public Text title;
        public Text note;
        public FlapsLeverSettings settings = new FlapsLeverSettings();

        [Header("Look")]
        public Color trackColor = HudStyle.Hairline;
        public Color markColor = HudStyle.TextSecondary;
        public Color committedMarkColor = HudStyle.TextPrimary;
        public Color previewMarkColor = HudStyle.Selected;
        public Color knobColor = HudStyle.Selected;
        public Color knobUnchangedColor = new Color(1f, 1f, 1f, 0.9f);
        public Color cancelledColor = new Color(1f, 1f, 1f, 0.35f);
        public float trackWidth = 6f;
        public float markLength = 28f;
        public float plateWidth = 300f;
        public float platePadding = 70f;
        public float labelOffset = -70f;
        public HudMotionSettings motion = new HudMotionSettings();

        const string NoChange = "Release: flaps unchanged";
        const string ReleaseToSet = "Release to set";
        const string Cancelled = "Release to cancel";
        const string Title = "FLAPS";

        HudFade _fade;
        float _knobY;
        bool _reducedMotion, _initialized;
        string[] _detentLabels = new string[0];
        float _opacity = 1f;

        public bool IsShown => _fade != null && _fade.IsShown;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        public void SetOpacity(float opacity)
        {
            _opacity = Mathf.Clamp01(opacity);
            _fade?.SetOpacityScale(_opacity);
        }

        /// <summary>The detent captions (UP, 10°, 20°, 30°), built once by the session from the aircraft data.</summary>
        public void SetDetentLabels(string[] detentLabels) => _detentLabels = detentLabels ?? new string[0];

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (group != null) _fade = new HudFade(group, interactive: false);
            _fade?.SetOpacityScale(_opacity);
            if (plate != null && plate.sprite == null) HudStyle.Round(plate, HudStyle.PanelRadius);
            if (knob != null && knob.sprite == null) HudStyle.Round(knob, HudStyle.ControlRadius);
            if (title != null) title.text = Title;
        }

        /// <summary>The lever appears at the finger (a canvas-local point in the parent of <see cref="root"/>).</summary>
        public void Show(FlapsLeverModel model, Vector2 localOrigin, Rect bounds)
        {
            EnsureInitialized();
            if (root == null || model == null) return;
            // Keep the whole track inside the given bounds: the finger mapping is unchanged, only the drawing shifts.
            float top = model.DetentOffset(0) - platePadding;       // negative = up
            float bottom = model.DetentOffset(model.DetentCount - 1) + platePadding;
            var position = localOrigin;
            if (position.y - top > bounds.yMax) position.y = bounds.yMax + top;
            if (position.y - bottom < bounds.yMin) position.y = bounds.yMin + bottom;
            position.x = Mathf.Clamp(position.x, bounds.xMin + plateWidth * 0.5f, bounds.xMax - plateWidth * 0.5f);
            root.anchoredPosition = position;
            if (plate != null)
            {
                plate.rectTransform.anchoredPosition = new Vector2(0f, -(top + bottom) * 0.5f);
                plate.rectTransform.sizeDelta = new Vector2(plateWidth, bottom - top);
            }
            if (title != null) title.rectTransform.anchoredPosition = new Vector2(0f, -top - 26f);
            if (note != null) note.rectTransform.anchoredPosition = new Vector2(0f, -bottom + 24f);
            if (track != null)
            {
                float trackTop = model.DetentOffset(0), trackBottom = model.DetentOffset(model.DetentCount - 1);
                track.rectTransform.anchoredPosition = new Vector2(0f, -(trackTop + trackBottom) * 0.5f);
                track.rectTransform.sizeDelta = new Vector2(trackWidth, Mathf.Max(1f, trackBottom - trackTop));
                track.color = trackColor;
            }
            for (int i = 0; i < MaxDetents; i++)
            {
                bool present = i < model.DetentCount;
                if (marks[i] != null)
                {
                    marks[i].enabled = present;
                    if (present)
                    {
                        marks[i].rectTransform.anchoredPosition = new Vector2(0f, -model.DetentOffset(i));
                        marks[i].rectTransform.sizeDelta = new Vector2(markLength, 3f);
                    }
                }
                if (labels[i] != null)
                {
                    labels[i].enabled = present;
                    if (present)
                    {
                        labels[i].rectTransform.anchoredPosition = new Vector2(labelOffset, -model.DetentOffset(i));
                        labels[i].text = i < _detentLabels.Length ? _detentLabels[i] : i.ToString();
                    }
                }
            }
            _knobY = 0f;
            _fade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), 0f);
            Present(model);
        }

        public void Hide() => _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);

        /// <summary>Per frame while shown.</summary>
        public void Present(FlapsLeverModel model)
        {
            if (model == null || !model.IsActive) return;
            float targetY = -model.KnobOffset;
            _knobY = _reducedMotion || settings.settleSeconds <= 0f ? targetY : HudEasing.Approach(_knobY, targetY, settings.settleSeconds * 0.5f, Time.unscaledDeltaTime);
            if (knob != null)
            {
                knob.rectTransform.anchoredPosition = new Vector2(0f, _knobY);
                knob.color = model.Cancelled ? cancelledColor : (model.Changed ? knobColor : knobUnchangedColor);
            }
            if (knobLabel != null)
            {
                string text = model.Preview < _detentLabels.Length ? _detentLabels[model.Preview] : string.Empty;
                if (!ReferenceEquals(knobLabel.text, text)) knobLabel.text = text;
                knobLabel.color = model.Cancelled ? HudStyle.TextQuiet : HudStyle.TextOnAccent;
            }
            for (int i = 0; i < model.DetentCount && i < MaxDetents; i++)
            {
                bool committed = i == model.Current, preview = i == model.Preview && !model.Cancelled;
                if (marks[i] != null)
                {
                    marks[i].color = preview ? previewMarkColor : (committed ? committedMarkColor : markColor);
                    marks[i].rectTransform.sizeDelta = new Vector2(committed || preview ? markLength + 10f : markLength, committed ? 4f : 3f);
                }
                if (labels[i] != null) labels[i].color = preview ? previewMarkColor : (committed ? committedMarkColor : markColor);
            }
            if (note != null)
            {
                string text = model.Cancelled ? Cancelled : (model.Changed ? ReleaseToSet : NoChange);
                if (!ReferenceEquals(note.text, text)) note.text = text;
                note.color = model.Cancelled ? HudStyle.TextQuiet : HudStyle.TextSecondary;
            }
            if (track != null) track.color = model.Cancelled ? cancelledColor : trackColor;
        }

        void LateUpdate()
        {
            if (_initialized) _fade?.Tick(Time.unscaledDeltaTime);
        }
    }
}
