using OpenSkies.Missions;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Subtle frames around the controls a coaching message refers to (stick,
    /// throttle, brake, flaps, camera, reset): a soft amber outline that breathes
    /// slowly (steady with reduced motion). The frames take no touches and sit on
    /// top of the control, so they never block it. A frame shows only while its
    /// control is visible.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the joystick and the HUD have placed and faded their controls
    public sealed class ControlHighlights : MonoBehaviour
    {
        public const int TargetCount = 6;

        [Tooltip("The safe area the frames are placed in.")]
        public RectTransform area;
        [Tooltip("Index = bit of CoachHighlight: stick, throttle, brake, flaps, camera, reset.")]
        public RectTransform[] targets = new RectTransform[TargetCount];
        public Image[] frames = new Image[TargetCount];
        public Color color = new Color(1f, 0.82f, 0.35f, 1f);
        [Tooltip("Frame margin around the control, canvas units.")]
        public float padding = 10f;
        [Tooltip("One slow breath, s.")]
        public float pulseSeconds = 1.8f;
        [Range(0f, 1f)] public float minAlpha = 0.3f;
        [Range(0f, 1f)] public float maxAlpha = 0.75f;

        readonly CanvasGroup[] _groups = new CanvasGroup[TargetCount];
        readonly Vector3[] _corners = new Vector3[4];
        int _mask;
        bool _reducedMotion, _initialized;

        public int Mask => _mask;
        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        /// <summary>Whether a frame is on screen now (its control is visible).</summary>
        public bool IsShowing(CoachHighlight highlight)
        {
            int i = IndexOf(highlight);
            return i >= 0 && frames[i] != null && frames[i].enabled;
        }

        public void SetTarget(CoachHighlight highlight, RectTransform target)
        {
            int i = IndexOf(highlight);
            if (i < 0) return;
            targets[i] = target;
            _groups[i] = target != null ? target.GetComponentInParent<CanvasGroup>() : null;
        }

        public void Present(CoachHighlight mask) => _mask = (int)mask;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            for (int i = 0; i < TargetCount; i++)
            {
                if (targets[i] != null && _groups[i] == null) _groups[i] = targets[i].GetComponentInParent<CanvasGroup>();
                if (frames[i] == null) continue;
                if (frames[i].sprite == null)
                {
                    frames[i].sprite = HudSprites.RoundedOutline;
                    frames[i].type = Image.Type.Sliced;
                }
                frames[i].raycastTarget = false;
                frames[i].enabled = false;
            }
        }

        void LateUpdate()
        {
            EnsureInitialized();
            float alpha = maxAlpha * 0.85f;
            if (!_reducedMotion && pulseSeconds > 0f)
            {
                float phase = Mathf.Repeat(Time.unscaledTime / pulseSeconds, 1f);
                alpha = Mathf.Lerp(minAlpha, maxAlpha, 0.5f - 0.5f * Mathf.Cos(phase * 2f * Mathf.PI));
            }
            for (int i = 0; i < TargetCount; i++)
            {
                var frame = frames[i];
                if (frame == null) continue;
                var target = targets[i];
                bool wanted = (_mask & (1 << i)) != 0 && target != null && area != null && target.gameObject.activeInHierarchy
                              && (_groups[i] == null || _groups[i].alpha > 0.5f);
                if (frame.enabled != wanted) frame.enabled = wanted;
                if (!wanted) continue;

                target.GetWorldCorners(_corners);
                Vector2 min = area.InverseTransformPoint(_corners[0]);
                Vector2 max = area.InverseTransformPoint(_corners[2]);
                var rect = frame.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                Vector2 centre = (min + max) * 0.5f - area.rect.center;
                rect.anchoredPosition = centre;
                rect.sizeDelta = (max - min) + new Vector2(2f * padding, 2f * padding);
                Color c = color;
                c.a = alpha;
                frame.color = c;
            }
        }

        static int IndexOf(CoachHighlight highlight)
        {
            int bits = (int)highlight;
            for (int i = 0; i < TargetCount; i++)
                if (bits == 1 << i) return i;
            return -1;
        }
    }
}
