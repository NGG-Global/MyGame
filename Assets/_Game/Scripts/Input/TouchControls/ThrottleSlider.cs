using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// Vertical throttle, 0-100 %. Drag is relative (touching never makes the
    /// throttle jump) and the touch area is larger than the visible track. The
    /// slider only reports movement; the router owns the throttle value and
    /// feeds it back for display, so keyboard and touch stay in agreement and
    /// nothing else (camera changes, other fingers) can reset it.
    /// </summary>
    public sealed class ThrottleSlider : TouchControlBase
    {
        public RectTransform track;
        public RectTransform fill;
        public RectTransform handle;
        public Image trackImage;
        public Image fillImage;
        public Image handleImage;
        public Text label;
        [Tooltip("Reference marks at 25, 50 and 75 %.")]
        public Image[] ticks = new Image[0];

        [Tooltip("1 = the full track height covers 0-100 %. Lower values give finer control.")]
        [Range(0.2f, 2f)] public float dragGain = 1f;
        [Range(0f, 1f)] public float idleAlpha = 0.3f;
        [Range(0f, 1f)] public float activeAlpha = 0.75f;

        static readonly string[] PercentLabels = BuildLabels();
        float _lastY;
        float _pendingDelta;
        float _shownThrottle = -1f;
        int _shownPercent = -1;

        public override string OwnerName => "Throttle";

        void Awake()
        {
            if (trackImage != null && trackImage.sprite == null) TouchControlStyle.Round(trackImage, outline: true);
            if (fillImage != null && fillImage.sprite == null) TouchControlStyle.Round(fillImage, outline: false);
            if (handleImage != null && handleImage.sprite == null) { handleImage.sprite = TouchControlSprites.RoundedRect; handleImage.type = Image.Type.Sliced; }
            SetHighlight(false);
        }

        /// <summary>Throttle movement since the last call, as a fraction of full travel.</summary>
        public float ConsumeDelta()
        {
            float delta = _pendingDelta;
            _pendingDelta = 0f;
            return delta;
        }

        public void ClearPending() => _pendingDelta = 0f;

        /// <summary>Displays the router's throttle. Cheap when unchanged; no allocations.</summary>
        public void ShowThrottle(float throttle)
        {
            throttle = Mathf.Clamp01(throttle);
            if (Mathf.Abs(throttle - _shownThrottle) < 0.0005f) return;
            _shownThrottle = throttle;
            if (fill != null) fill.anchorMax = new Vector2(1f, throttle);
            if (handle != null)
            {
                handle.anchorMin = new Vector2(handle.anchorMin.x, throttle);
                handle.anchorMax = new Vector2(handle.anchorMax.x, throttle);
            }
            int percent = Mathf.RoundToInt(throttle * 100f);
            if (label != null && percent != _shownPercent)
            {
                _shownPercent = percent;
                label.text = PercentLabels[percent];
            }
        }

        protected override void OnEngage(PointerEventData eventData)
        {
            _lastY = ToLocal(track, eventData, out var local) ? local.y : 0f;
            SetHighlight(true);
        }

        protected override void OnMove(PointerEventData eventData)
        {
            if (!ToLocal(track, eventData, out var local)) return;
            _pendingDelta += ThrottleDragMath.Delta(_lastY, local.y, track.rect.height, dragGain);
            _lastY = local.y;
        }

        protected override void OnRelease() => SetHighlight(false); // throttle keeps its value

        void SetHighlight(bool engaged)
        {
            float a = engaged ? activeAlpha : idleAlpha;
            SetAlpha(trackImage, a);
            SetAlpha(fillImage, a * 0.6f);
            SetAlpha(handleImage, Mathf.Min(1f, a * 1.3f));
            if (label != null) SetAlpha(label, Mathf.Min(1f, a * 1.2f));
            for (int i = 0; i < ticks.Length; i++) SetAlpha(ticks[i], a * 0.8f);
        }


        static string[] BuildLabels()
        {
            var labels = new string[101];
            for (int i = 0; i <= 100; i++) labels[i] = i + "%";
            return labels;
        }
    }
}
