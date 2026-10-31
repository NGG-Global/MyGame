using System;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>The player's motion preference for HUD transitions.</summary>
    public enum MotionPreference
    {
        /// <summary>Reduce motion when the operating system asks for it (Android "Remove animations").</summary>
        FollowSystem = 0,
        Reduced = 1,
        Full = 2,
    }

    /// <summary>
    /// Restrained transitions for contextual HUD elements: short fades and a small
    /// rise, no scaling, bouncing or flashing. Reduced motion removes all movement
    /// and shortens fades (instant by default).
    /// </summary>
    [Serializable]
    public sealed class HudMotionSettings
    {
        [Tooltip("Fade time for contextual elements, s. Target 0.15-0.25.")]
        [Range(0f, 0.5f)] public float fadeSeconds = 0.2f;
        [Tooltip("Warnings appear faster than other elements, s.")]
        [Range(0f, 0.5f)] public float warningFadeInSeconds = 0.12f;
        [Tooltip("Appearing elements rise this far into place, canvas units. Never with reduced motion.")]
        [Range(0f, 40f)] public float slideDistance = 10f;
        [Tooltip("Fade time with reduced motion, s. 0 = instant.")]
        [Range(0f, 0.5f)] public float reducedMotionFadeSeconds = 0f;

        public float FadeSeconds(bool reduced) => reduced ? reducedMotionFadeSeconds : fadeSeconds;
        public float WarningFadeSeconds(bool reduced) => reduced ? Mathf.Min(reducedMotionFadeSeconds, warningFadeInSeconds) : warningFadeInSeconds;
        public float Slide(bool reduced) => reduced ? 0f : slideDistance;
    }

    public static class SystemMotion
    {
        public static bool Resolve(MotionPreference preference, bool systemPrefersReduced) =>
            preference == MotionPreference.Reduced || (preference == MotionPreference.FollowSystem && systemPrefersReduced);

        /// <summary>
        /// Whether the operating system asks apps to reduce motion. Android: the
        /// developer/accessibility setting "Remove animations" sets the animator
        /// duration scale to 0. Other platforms: no built-in Unity API, so false.
        /// Call rarely (start-up, focus regained), not every frame.
        /// </summary>
        public static bool PrefersReducedMotion()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var global = new AndroidJavaClass("android.provider.Settings$Global"))
                {
                    return global.CallStatic<float>("getFloat", resolver, "animator_duration_scale", 1f) == 0f;
                }
            }
            catch (Exception)
            {
                return false; // unknown: keep the default motion
            }
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Fades one CanvasGroup between opacities with smoothstep easing and an
    /// optional small rise when it appears. A hidden target stops taking
    /// touches immediately, so a fading-out button can never be pressed.
    /// Plain class, ticked by its owner; allocation-free.
    /// </summary>
    public sealed class HudFade
    {
        readonly CanvasGroup _group;
        readonly RectTransform _rect;
        readonly Vector2 _home;
        readonly bool _interactive;
        float _alpha, _from, _to, _progress = 1f, _duration;
        float _slide;

        public HudFade(CanvasGroup group, bool interactive, float initialAlpha = 0f)
        {
            _group = group;
            _rect = (RectTransform)group.transform;
            _home = _rect.anchoredPosition;
            _interactive = interactive;
            _alpha = _from = _to = Mathf.Clamp01(initialAlpha);
            ApplyRaycasts();
            Apply(1f);
        }

        public CanvasGroup Group => _group;
        public float Alpha => _alpha;
        public float TargetAlpha => _to;
        public bool IsShown => _to > 0f;
        public bool IsAnimating => _progress < 1f;
        /// <summary>Where the element rests; it never moves anywhere else.</summary>
        public Vector2 Home => _home;

        /// <summary>Starts a transition to <paramref name="alpha"/> unless it is already the target.</summary>
        public void SetTarget(float alpha, float duration, float slideDistance)
        {
            alpha = Mathf.Clamp01(alpha);
            if (Mathf.Abs(alpha - _to) < 1e-4f) return;
            bool appearing = _to <= 0f && alpha > 0f;
            _from = _alpha;
            _to = alpha;
            _duration = Mathf.Max(0f, duration);
            _slide = appearing && _duration > 0f ? Mathf.Max(0f, slideDistance) : 0f;
            _progress = 0f;
            ApplyRaycasts();
            if (_duration <= 0f) Finish();
            else Apply(0f);
        }

        public void Tick(float deltaTime)
        {
            if (_progress >= 1f) return;
            _progress = Mathf.Min(1f, _progress + deltaTime / _duration);
            if (_progress >= 1f)
            {
                Finish();
                return;
            }
            Apply(Mathf.SmoothStep(0f, 1f, _progress));
        }

        void Finish()
        {
            _progress = 1f;
            _slide = 0f;
            Apply(1f);
        }

        void Apply(float eased)
        {
            _alpha = Mathf.Lerp(_from, _to, eased);
            _group.alpha = _alpha;
            _rect.anchoredPosition = _slide > 0f ? _home + new Vector2(0f, -_slide * (1f - eased)) : _home;
        }

        void ApplyRaycasts()
        {
            bool on = _interactive && _to > 0f;
            _group.blocksRaycasts = on;
            _group.interactable = on;
        }
    }
}
