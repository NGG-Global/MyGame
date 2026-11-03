using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>What a gesture cue can be told to draw (the view, or a fake in tests).</summary>
    public interface IGestureHintView
    {
        bool IsShown { get; }
        void Show(Vector2 anchor, Vector2 direction, float distance, string text, Rect bounds);
        void Hide();
        /// <summary>Screen pixels to the view's local units (the safe-area corner).</summary>
        Vector2 ToLocal(Vector2 screenPoint);
        Rect Bounds { get; }
    }

    /// <summary>
    /// A contextual gesture cue: a small dot that travels a short way from an anchor
    /// in the gesture's direction, over and over, with a one-line caption beside it.
    /// It takes no touches, never covers flight information (the session places it),
    /// and stops the moment the gesture is performed. With reduced motion the dot
    /// rests at the end of its path and only the caption remains.
    /// </summary>
    public sealed class GestureHintView : MonoBehaviour, IGestureHintView
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image dot;
        public Image trail;
        public Image captionPlate;
        public Text caption;
        public HudMotionSettings motion = new HudMotionSettings();
        [Tooltip("One travel of the dot, s.")]
        public float cycleSeconds = 1.6f;
        [Tooltip("The dot rests at the start this share of the cycle before travelling.")]
        [Range(0f, 0.6f)] public float restShare = 0.25f;
        public float captionWidth = 420f;

        HudFade _fade;
        Vector2 _direction = Vector2.up;
        float _distance = 120f;
        float _phase;
        bool _initialized, _reducedMotion;

        public bool IsShown => _fade != null && _fade.IsShown;
        public string CurrentCaption => caption != null ? caption.text : string.Empty;

        /// <summary>The safe area (the root's parent) in local units.</summary>
        public Rect Bounds => root != null && root.parent is RectTransform parent ? parent.rect : new Rect(0f, 0f, 1920f, 1080f);

        public Vector2 ToLocal(Vector2 screenPoint)
        {
            var parent = root != null ? root.parent as RectTransform : null;
            if (parent == null) return screenPoint;
            var canvas = parent.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, camera, out var local);
            return local;
        }

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (group != null) _fade = new HudFade(group, interactive: false);
            if (dot != null && dot.sprite == null) dot.sprite = HudSprites.Disc;
            if (trail != null && trail.sprite == null) HudStyle.Round(trail, 3f);
            if (captionPlate != null && captionPlate.sprite == null) HudStyle.Round(captionPlate, HudStyle.BannerRadius);
        }

        /// <summary>
        /// Shows the cue at <paramref name="anchor"/> (root-local units), travelling along
        /// <paramref name="direction"/> for <paramref name="distance"/> units, with a caption
        /// placed on the side away from the travel, kept inside <paramref name="bounds"/>.
        /// </summary>
        public void Show(Vector2 anchor, Vector2 direction, float distance, string text, Rect bounds)
        {
            EnsureInitialized();
            if (root == null) return;
            _direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.up;
            _distance = Mathf.Max(20f, distance);
            _phase = 0f;
            root.anchoredPosition = anchor;
            if (trail != null)
            {
                trail.rectTransform.anchoredPosition = _direction * (_distance * 0.5f);
                trail.rectTransform.sizeDelta = new Vector2(6f, _distance);
                trail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg - 90f);
            }
            if (caption != null && !ReferenceEquals(caption.text, text)) caption.text = text ?? string.Empty;
            if (captionPlate != null)
            {
                // The caption sits beside the path, on the side with more room, and never past the bounds.
                Vector2 side = Mathf.Abs(_direction.x) > Mathf.Abs(_direction.y) ? Vector2.up : Vector2.left;
                Vector2 plate = side * 70f;
                if (side == Vector2.left && anchor.x - captionWidth - 40f < bounds.xMin) plate = Vector2.right * 70f;
                if (side == Vector2.up && anchor.y + 110f > bounds.yMax) plate = Vector2.down * 70f;
                var rect = captionPlate.rectTransform;
                rect.pivot = plate.x < 0f ? new Vector2(1f, 0.5f) : (plate.x > 0f ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f));
                rect.anchoredPosition = plate;
                rect.sizeDelta = new Vector2(captionWidth, 44f);
            }
            Place(_reducedMotion ? 1f : 0f, _reducedMotion ? 1f : 0.2f);
            _fade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        public void Hide() => _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);

        void Place(float travel, float alpha)
        {
            if (dot == null) return;
            dot.rectTransform.anchoredPosition = _direction * (_distance * travel);
            var c = dot.color;
            c.a = alpha;
            dot.color = c;
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            float dt = Time.unscaledDeltaTime;
            _fade?.Tick(dt);
            if (!IsShown || _reducedMotion || cycleSeconds <= 0f) return;
            _phase = Mathf.Repeat(_phase + dt / cycleSeconds, 1f);
            float rest = Mathf.Clamp(restShare, 0f, 0.6f);
            if (_phase < rest)
            {
                Place(0f, Mathf.Lerp(0.2f, 1f, _phase / Mathf.Max(0.01f, rest)));
                return;
            }
            float t = (_phase - rest) / (1f - rest);
            Place(HudEasing.EaseInOut(t), 1f - HudEasing.EaseOut(Mathf.Clamp01((t - 0.7f) / 0.3f)));
        }
    }
}
