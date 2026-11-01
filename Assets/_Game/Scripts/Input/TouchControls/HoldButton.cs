using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// Active only while a finger holds it (used for the wheel brakes). It can be
    /// made unavailable (e.g. in the air): it then fades out, stops taking
    /// touches and releases any finger holding it.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class HoldButton : TouchControlBase
    {
        public Image background;
        public Text label;
        public string ownerName = "Brake";
        [Range(0f, 1f)] public float idleAlpha = 0.3f;
        [Range(0f, 1f)] public float activeAlpha = 0.8f;
        [Tooltip("Fill while held: a clear active state that does not rely on brightness alone.")]
        public Color activeColor = TouchControlStyle.Selected;
        public Color activeLabelColor = new Color(0.04f, 0.06f, 0.08f, 1f);
        [Tooltip("Fade time when becoming available or unavailable, seconds.")]
        public float fadeTime = 0.2f;

        CanvasGroup _group;
        bool _available = true;

        public override string OwnerName => ownerName;
        public bool IsHeld => IsEngaged;
        public bool IsAvailable => _available;

        void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (background != null && background.sprite == null) TouchControlStyle.Round(background, outline: false);
            SetHighlight(false);
        }

        public void SetAvailable(bool available)
        {
            if (available == _available) return;
            _available = available;
            if (!available) ForceRelease();
            if (_group != null) _group.blocksRaycasts = available;
        }

        void Update()
        {
            if (_group == null) return;
            float target = _available ? 1f : 0f;
            float step = fadeTime > 0f ? Time.unscaledDeltaTime / fadeTime : 1f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, step);
        }

        protected override bool CanEngage() => base.CanEngage() && _available;
        protected override void OnEngage(PointerEventData eventData) => SetHighlight(true);
        protected override void OnMove(PointerEventData eventData) { } // held, wherever the finger slides
        protected override void OnRelease() => SetHighlight(false);

        void SetHighlight(bool engaged)
        {
            float a = engaged ? activeAlpha : idleAlpha;
            if (background != null) background.color = engaged ? activeColor : Color.white;
            SetAlpha(background, a);
            if (label != null) label.color = engaged ? activeLabelColor : TouchControlStyle.TextPrimary;
            SetAlpha(label, engaged ? 1f : a + 0.3f);
        }
    }
}
