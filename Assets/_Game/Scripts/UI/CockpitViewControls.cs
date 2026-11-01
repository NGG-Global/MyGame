using System;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// View controls for the cockpit, shown only when they are useful. For now:
    /// LOOK FWD, in the top bar next to the other view buttons, which fades in
    /// while the cockpit view is turned away from rest and returns the view
    /// forward smoothly. It is the visible alternative to the double tap on the
    /// sky (and the V key). It only raises a request; it never changes flight
    /// input. A pressed button is never pulled away from under the finger.
    /// </summary>
    public sealed class CockpitViewControls : MonoBehaviour
    {
        public HudButton lookForward;
        public CanvasGroup lookForwardGroup;
        public HudMotionSettings motion = new HudMotionSettings();

        /// <summary>The player asked to look forward again.</summary>
        public event Action LookForwardRequested;

        HudFade _fade;
        bool _initialized, _reducedMotion;

        /// <summary>Shown (or fading in) and taking touches.</summary>
        public bool LookForwardShown => _fade != null && _fade.IsShown;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (lookForwardGroup != null) _fade = new HudFade(lookForwardGroup, interactive: true);
            if (lookForward != null) lookForward.Clicked += RaiseLookForward;
        }

        /// <summary>Per frame from the session. Fades in place (no slide: it lives in the top bar).</summary>
        public void ShowLookForward(bool show)
        {
            EnsureInitialized();
            if (_fade == null) return;
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
