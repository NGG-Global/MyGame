using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// Floating stick for pitch and roll. Sits on a large invisible region in
    /// the lower left; the base appears where the thumb lands (kept inside the
    /// region), the knob follows the thumb, and output returns to neutral on
    /// release. It never levels the aircraft by itself.
    /// </summary>
    public sealed class FloatingJoystick : TouchControlBase
    {
        public RectTransform visualRoot;
        public RectTransform knob;
        public Image baseFill;
        public Image baseRing;
        public Image knobImage;
        public JoystickSettings settings = new JoystickSettings();

        [Tooltip("Resting position of the idle hint, as a fraction of the region.")]
        public Vector2 restingPosition = new Vector2(0.42f, 0.4f);
        [Range(0f, 1f)] public float idleAlpha = 0.16f;
        [Range(0f, 1f)] public float activeAlpha = 0.55f;

        RectTransform _region;
        Vector2 _base;
        Vector2 _deflection;

        public override string OwnerName => "Joystick";

        /// <summary>Raw deflection inside the unit circle, screen axes (+x right, +y up). Shaping happens in the provider.</summary>
        public Vector2 Deflection => _deflection;

        void Awake()
        {
            _region = (RectTransform)transform;
            if (baseFill != null && baseFill.sprite == null) baseFill.sprite = TouchControlSprites.Disc;
            if (baseRing != null && baseRing.sprite == null) baseRing.sprite = TouchControlSprites.Ring;
            if (knobImage != null && knobImage.sprite == null) knobImage.sprite = TouchControlSprites.Disc;
            if (visualRoot != null)
            {
                // Local pointer coordinates are relative to the region's pivot.
                visualRoot.anchorMin = visualRoot.anchorMax = _region.pivot;
                visualRoot.sizeDelta = Vector2.one * (settings.radius * 2f);
            }
            ShowIdle();
        }

        protected override void OnEngage(PointerEventData eventData)
        {
            if (!ToLocal(_region, eventData, out var local)) local = RestingPoint();
            _base = JoystickMath.ClampBase(local, _region.rect, settings.radius);
            _deflection = JoystickMath.Deflection(local - _base, settings.radius);
            UpdateVisual(activeAlpha);
        }

        protected override void OnMove(PointerEventData eventData)
        {
            // Movement outside the region still steers: the pointer stays ours until release.
            if (!ToLocal(_region, eventData, out var local)) return;
            _deflection = JoystickMath.Deflection(local - _base, settings.radius);
            UpdateVisual(activeAlpha);
        }

        protected override void OnRelease()
        {
            _deflection = Vector2.zero;
            ShowIdle();
        }

        /// <summary>Screen rotation, resolution or safe-area change: keep everything inside the region.</summary>
        void OnRectTransformDimensionsChange()
        {
            if (_region == null) return;
            if (IsEngaged)
            {
                _base = JoystickMath.ClampBase(_base, _region.rect, settings.radius);
                UpdateVisual(activeAlpha);
            }
            else
            {
                ShowIdle();
            }
        }

        void ShowIdle()
        {
            if (_region == null) return;
            _base = RestingPoint();
            UpdateVisual(idleAlpha);
        }

        Vector2 RestingPoint()
        {
            Rect r = _region.rect;
            var point = new Vector2(r.xMin + r.width * restingPosition.x, r.yMin + r.height * restingPosition.y);
            return JoystickMath.ClampBase(point, r, settings.radius);
        }

        void UpdateVisual(float alpha)
        {
            if (visualRoot != null) visualRoot.anchoredPosition = _base;
            if (knob != null) knob.anchoredPosition = _deflection * settings.radius;
            SetAlpha(baseFill, alpha * 0.35f);
            SetAlpha(baseRing, alpha);
            SetAlpha(knobImage, alpha * (IsEngaged ? 1.4f : 1f));
        }
    }
}
