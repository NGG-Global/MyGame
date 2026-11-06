using System;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// View controls for the cockpit, shown only when they are useful. For now:
    /// LOOK FWD, a compact chip under the camera button of the command cluster
    /// (a contextual camera option), which fades in while the cockpit view is
    /// turned away from rest and returns the view forward smoothly. It is the
    /// visible alternative to the double tap on the sky (and the V key). It only
    /// raises a request; it never changes flight input. A pressed button is never
    /// pulled away from under the finger. While the cluster's options panel is open
    /// over its place, it is suppressed.
    /// </summary>
    public sealed class CockpitViewControls : MonoBehaviour
    {
        public HudButton lookForward;
        public CanvasGroup lookForwardGroup;
        public HudMotionSettings motion = new HudMotionSettings();

        /// <summary>The player asked to look forward again.</summary>
        public event Action LookForwardRequested;

        HudFade _fade;
        float _opacity = 1f;
        bool _initialized, _reducedMotion, _wanted, _suppressed;

        /// <summary>Shown (or fading in) and taking touches.</summary>
        public bool LookForwardShown => _fade != null && _fade.IsShown;
        /// <summary>Held back while something else (the command cluster's open panel) takes its place.</summary>
        public bool Suppressed => _suppressed;

        /// <summary>The command cluster's options panel opened over the chip (true) or folded (false).</summary>
        public void SetSuppressed(bool suppressed)
        {
            if (suppressed == _suppressed) return;
            _suppressed = suppressed;
            Apply();
        }

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        /// <summary>The player's HUD opacity.</summary>
        public void SetOpacity(float opacity)
        {
            _opacity = opacity;
            _fade?.SetOpacityScale(opacity);
        }

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (lookForwardGroup != null) _fade = new HudFade(lookForwardGroup, interactive: true);
            _fade?.SetOpacityScale(_opacity);
            if (lookForward != null) lookForward.Clicked += RaiseLookForward;
        }

        /// <summary>Per frame from the session. Fades in place (no slide: it lives in the top bar).</summary>
        public void ShowLookForward(bool show)
        {
            EnsureInitialized();
            _wanted = show;
            Apply();
        }

        void Apply()
        {
            if (_fade == null) return;
            bool show = _wanted && !_suppressed;
            if (!show && lookForward != null && lookForward.IsPressed) return; // keep it under the finger until release
            _fade.SetTarget(show ? 1f : 0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        void LateUpdate()
        {
            if (_initialized) _fade?.Tick(Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            if (lookForward != null) lookForward.Clicked -= RaiseLookForward;
        }

        void RaiseLookForward() => LookForwardRequested?.Invoke();
    }
}
