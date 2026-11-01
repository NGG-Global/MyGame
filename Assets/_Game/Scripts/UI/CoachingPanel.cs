using System;
using OpenSkies.Missions;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The one coaching message of a mission, at the bottom of the free column
    /// (between the joystick region and the action rail, below the panel band and
    /// the cockpit instruments), so it never covers the runway ahead, the flight
    /// controls or the instruments. A thin accent shows the kind of message;
    /// a "?" means a short explanation is available: tap the message to read it.
    /// It fades in place, changes text only when the cue changes, and never moves.
    /// </summary>
    public sealed class CoachingPanel : MonoBehaviour
    {
        public CanvasGroup group;
        public HudButton button;
        public Image accent;
        public Text label;
        public Text message;
        [Tooltip("\"?\" when an explanation is available, \"×\" while it is shown.")]
        public Text detailGlyph;
        public HudMotionSettings motion = new HudMotionSettings();

        // Tone colours from the style roles: amber is kept for safety, so a hint never looks like a caution.
        public Color instructionColor = HudStyle.TextSecondary;
        public Color hintColor = HudStyle.Selected;
        public Color successColor = HudStyle.Active;
        public Color safetyColor = HudStyle.Caution;

        HudFade _fade;
        bool _reducedMotion, _initialized;
        int _shownVersion = int.MinValue;
        CoachTone _shownTone = (CoachTone)(-1);

        /// <summary>The player tapped the message (shows or hides the explanation).</summary>
        public event Action DetailRequested;

        public bool Shown => _fade != null && _fade.IsShown;
        public string CurrentText => message != null ? message.text : string.Empty;
        public string CurrentLabel => label != null ? label.text : string.Empty;
        public CoachTone CurrentTone => _shownTone;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (group != null) _fade = new HudFade(group, interactive: true);
            if (button != null) button.Clicked += () => DetailRequested?.Invoke();
            if (accent != null && accent.sprite == null)
            {
                accent.sprite = HudSprites.RoundedRect;
                accent.type = Image.Type.Sliced;
            }
        }

        /// <summary>Shows a cue; cheap when it has not changed (call every frame).</summary>
        public void Present(in CoachingCue cue)
        {
            EnsureInitialized();
            if (cue.Version == _shownVersion) return;
            _shownVersion = cue.Version;
            if (cue.Visible)
            {
                SetText(label, cue.Label);
                SetText(message, cue.Text);
                SetText(detailGlyph, cue.HasDetail ? (cue.ShowingDetail ? "×" : "?") : string.Empty);
                if (cue.Tone != _shownTone)
                {
                    _shownTone = cue.Tone;
                    if (accent != null) accent.color = ToneColor(cue.Tone);
                }
            }
            _fade?.SetTarget(cue.Visible ? 1f : 0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        public void Hide()
        {
            EnsureInitialized();
            _shownVersion = int.MinValue;
            _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        Color ToneColor(CoachTone tone)
        {
            switch (tone)
            {
                case CoachTone.Hint: return hintColor;
                case CoachTone.Success: return successColor;
                case CoachTone.Safety: return safetyColor;
                default: return instructionColor;
            }
        }

        static void SetText(Text text, string value)
        {
            if (text != null && !ReferenceEquals(text.text, value)) text.text = value ?? string.Empty;
        }

        void LateUpdate() => _fade?.Tick(Time.unscaledDeltaTime);
    }
}
