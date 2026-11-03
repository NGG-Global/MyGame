using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// A compact explanation that appears next to the element the player holds
    /// (a flight value, a guide, a warning, an arc item): a title and one or two
    /// short lines on a small plate, placed below or above the element and kept
    /// inside the safe area and out of the joystick region. It takes no touches and
    /// disappears when the finger lifts. The hold ring shows the long press filling
    /// under the finger before the plate appears.
    /// </summary>
    public sealed class ExplanationPlate : MonoBehaviour
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image plate;
        public Text title;
        public Text body;
        [Tooltip("A small ring under the finger that fills during the long press.")]
        public Image holdRing;
        public HudMotionSettings motion = new HudMotionSettings();
        public float width = 440f;
        public float minHeight = 96f;
        public float padding = 14f;
        public float gapToTarget = 12f;

        HudFade _fade;
        float _ringAlpha;
        bool _reducedMotion, _initialized;
        float _opacity = 1f;

        public bool IsShown => _fade != null && _fade.IsShown;
        public string CurrentTitle => title != null ? title.text : string.Empty;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        public void SetOpacity(float opacity)
        {
            _opacity = Mathf.Clamp01(opacity);
            _fade?.SetOpacityScale(_opacity);
        }

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (group != null) _fade = new HudFade(group, interactive: false);
            _fade?.SetOpacityScale(_opacity);
            if (plate != null && plate.sprite == null) HudStyle.Round(plate, HudStyle.PanelRadius);
            if (holdRing != null)
            {
                if (holdRing.sprite == null) holdRing.sprite = HudSprites.Ring;
                holdRing.type = Image.Type.Filled;
                holdRing.fillMethod = Image.FillMethod.Radial360;
                holdRing.fillOrigin = (int)Image.Origin360.Top;
                holdRing.fillClockwise = true;
                holdRing.fillAmount = 0f;
                holdRing.raycastTarget = false;
                SetRingAlpha(0f);
            }
        }

        /// <summary>The long press is filling under the finger (progress 0-1); 0 hides the ring.</summary>
        public void ShowHold(Vector2 localFinger, float progress)
        {
            EnsureInitialized();
            if (holdRing == null) return;
            holdRing.rectTransform.anchoredPosition = localFinger;
            holdRing.fillAmount = Mathf.Clamp01(progress);
            _ringAlpha = progress > 0f ? 1f : 0f;
            SetRingAlpha(_ringAlpha * _opacity * (_reducedMotion ? 1f : HudEasing.EaseOut(Mathf.Clamp01(progress * 3f))));
        }

        public void HideHold()
        {
            _ringAlpha = 0f;
            SetRingAlpha(0f);
        }

        void SetRingAlpha(float alpha)
        {
            if (holdRing == null) return;
            var c = holdRing.color;
            c.a = alpha * 0.8f;
            holdRing.color = c;
        }

        /// <summary>
        /// Shows an explanation for an element whose rectangle (canvas-local, in the parent of
        /// <see cref="root"/>) is given; the plate goes below it, or above when there is no room,
        /// and never outside <paramref name="bounds"/>.
        /// </summary>
        public void Show(string heading, string text, Rect target, Rect bounds)
        {
            EnsureInitialized();
            if (root == null) return;
            if (title != null && !ReferenceEquals(title.text, heading)) title.text = heading ?? string.Empty;
            if (body != null && !ReferenceEquals(body.text, text)) body.text = text ?? string.Empty;

            float height = Mathf.Max(minHeight, padding * 2f + (title != null ? 28f : 0f) + (body != null ? body.preferredHeight + 4f : 0f));
            root.sizeDelta = new Vector2(width, height);
            float x = Mathf.Clamp(target.center.x, bounds.xMin + width * 0.5f, bounds.xMax - width * 0.5f);
            float below = target.yMin - gapToTarget - height * 0.5f;
            float above = target.yMax + gapToTarget + height * 0.5f;
            float y = below - height * 0.5f >= bounds.yMin ? below : above;
            y = Mathf.Clamp(y, bounds.yMin + height * 0.5f, bounds.yMax - height * 0.5f);
            root.anchoredPosition = new Vector2(x, y);
            HideHold();
            _fade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), motion.Slide(_reducedMotion));
        }

        public void Hide() => _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);

        void LateUpdate()
        {
            if (_initialized) _fade?.Tick(Time.unscaledDeltaTime);
        }
    }
}
