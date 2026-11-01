using System;
using OpenSkies.Missions;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The entry screen: Free Flight (from the runway or in the air), First
    /// Flight and Landing Challenge, over a dimmed view of the aircraft on the
    /// runway. Minimal by design: three large choices with one line each, and a
    /// second choice only for Free Flight. It takes every touch while open.
    /// </summary>
    public sealed class ModeSelectScreen : MonoBehaviour
    {
        public CanvasGroup group;
        public Text title;
        public Text subtitle;
        public HudButton freeFlightButton;
        public HudButton firstFlightButton;
        public HudButton landingButton;
        public Text freeFlightNote;
        public Text firstFlightNote;
        public Text landingNote;
        [Tooltip("The two Free Flight starts, shown after Free Flight is tapped.")]
        public CanvasGroup freeFlightOptions;
        public HudButton fromRunwayButton;
        public HudButton inTheAirButton;
        public HudMotionSettings motion = new HudMotionSettings();

        HudFade _fade, _optionsFade;
        bool _initialized, _open, _reducedMotion;

        /// <summary>The player chose a mode (and, for Free Flight, where to start).</summary>
        public event Action<GameMode, FreeFlightStart> Chosen;

        public bool IsOpen => _open;
        public bool FreeFlightOptionsShown => _optionsFade != null && _optionsFade.IsShown;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (group != null) _fade = new HudFade(group, interactive: true, initialAlpha: group.alpha);
            _open = group != null && group.alpha > 0f; // built open: it covers the scene until a mode is chosen
            if (freeFlightOptions != null) _optionsFade = new HudFade(freeFlightOptions, interactive: true);
            if (freeFlightButton != null) freeFlightButton.Clicked += ToggleFreeFlightOptions;
            if (firstFlightButton != null) firstFlightButton.Clicked += () => Choose(GameMode.FirstFlight, FreeFlightStart.Runway);
            if (landingButton != null) landingButton.Clicked += () => Choose(GameMode.LandingChallenge, FreeFlightStart.Runway);
            if (fromRunwayButton != null) fromRunwayButton.Clicked += () => Choose(GameMode.FreeFlight, FreeFlightStart.Runway);
            if (inTheAirButton != null) inTheAirButton.Clicked += () => Choose(GameMode.FreeFlight, FreeFlightStart.Airborne);
        }

        /// <summary>
        /// Opens the screen. <paramref name="recommendFirstFlight"/> marks the First Flight
        /// for a new player; the notes show progress (built once per opening).
        /// </summary>
        public void Open(bool recommendFirstFlight, string firstFlightStatus, string landingStatus)
        {
            EnsureInitialized();
            _open = true;
            if (firstFlightButton != null) firstFlightButton.SetEngaged(recommendFirstFlight);
            if (firstFlightNote != null)
            {
                firstFlightNote.text = firstFlightStatus ?? string.Empty;
                firstFlightNote.color = recommendFirstFlight ? new Color(0.08f, 0.08f, 0.08f, 0.9f) : new Color(1f, 1f, 1f, 0.7f);
            }
            if (landingNote != null) landingNote.text = landingStatus ?? string.Empty;
            _optionsFade?.SetTarget(0f, 0f, 0f);
            _fade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _optionsFade?.SetTarget(0f, 0f, 0f);
            _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        public void ToggleFreeFlightOptions()
        {
            if (!_open || _optionsFade == null) return;
            bool show = !_optionsFade.IsShown;
            _optionsFade.SetTarget(show ? 1f : 0f, motion.FadeSeconds(_reducedMotion), show ? motion.Slide(_reducedMotion) : 0f);
            if (freeFlightButton != null) freeFlightButton.SetEngaged(show);
        }

        void Choose(GameMode mode, FreeFlightStart start)
        {
            if (!_open) return;
            Chosen?.Invoke(mode, start);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            _fade?.Tick(dt);
            _optionsFade?.Tick(dt);
        }
    }
}
