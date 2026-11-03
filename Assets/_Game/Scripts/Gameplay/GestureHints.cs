using OpenSkies.Missions;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Gesture discovery without a tutorial: when a gesture first becomes relevant
    /// (flaps are offered, the cockpit opens, the sheet rises, the stations appear),
    /// a small cue shows it once; the moment the player performs it, the gesture is
    /// learnt and remembered in the player's progress, and its cue never returns.
    /// Cues are sparse (one at a time, a cooldown between them, at most twice per
    /// session each), never interrupt, and can be switched off in the settings.
    /// The policy is plain C#; the view is told what to draw.
    /// </summary>
    public sealed class GestureHints
    {
        public const int Count = 7;

        readonly PlayerProgress _progress;
        readonly IGestureHintView _view;
        readonly int[] _shownThisSession = new int[Count];
        GestureId _showing = (GestureId)(-1);
        bool _hasShowing, _enabled = true;
        float _shownAt = float.NegativeInfinity, _lastHiddenAt = float.NegativeInfinity;

        /// <summary>A cue stays this long at most, s.</summary>
        public float ShowSeconds = 7f;
        /// <summary>At least this long between two cues, s.</summary>
        public float CooldownSeconds = 12f;
        /// <summary>A cue is offered at most this many times in one session.</summary>
        public int PerSessionLimit = 2;

        public GestureHints(PlayerProgress progress, IGestureHintView view)
        {
            _progress = progress;
            _view = view;
        }

        public bool Enabled => _enabled;
        public bool IsShowing => _hasShowing;
        public GestureId Showing => _showing;
        public bool HasView => _view != null;

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled) Hide();
        }

        public bool HasLearned(GestureId gesture) => _progress != null && _progress.HasLearned(gesture);

        /// <summary>
        /// Offers a cue for a gesture that is relevant now, at a root-local anchor,
        /// travelling in a direction. Returns whether it is shown (not learnt, enabled,
        /// nothing else showing, cooldown over, session limit not reached).
        /// </summary>
        public bool Offer(GestureId gesture, Vector2 anchor, Vector2 direction, float distance, string caption, Rect bounds, float now)
        {
            if (!_enabled || _view == null || HasLearned(gesture)) return false;
            if (_hasShowing) return _showing == gesture;
            int i = (int)gesture;
            if (i < 0 || i >= Count || _shownThisSession[i] >= PerSessionLimit) return false;
            if (now - _lastHiddenAt < CooldownSeconds) return false;
            _shownThisSession[i]++;
            _showing = gesture;
            _hasShowing = true;
            _shownAt = now;
            _view.Show(anchor, direction, distance, caption, bounds);
            return true;
        }

        /// <summary>The same, anchored at a screen point (the sessions know screen positions of controls and instruments).</summary>
        public bool OfferAtScreen(GestureId gesture, Vector2 screenAnchor, Vector2 direction, float distance, string caption, float now)
        {
            if (_view == null) return false;
            return Offer(gesture, _view.ToLocal(screenAnchor), direction, distance, caption, _view.Bounds, now);
        }

        /// <summary>The player performed the gesture: remembered for good; its cue, if showing, goes at once.</summary>
        public void Learned(GestureId gesture, float now)
        {
            _progress?.MarkGestureLearned(gesture);
            if (_hasShowing && _showing == gesture) Hide(now);
        }

        /// <summary>Per frame: a cue times out on its own.</summary>
        public void Tick(float now)
        {
            if (_hasShowing && now - _shownAt >= ShowSeconds) Hide(now);
        }

        public void Hide() => Hide(Time.unscaledTime);

        void Hide(float now)
        {
            if (!_hasShowing) return;
            _hasShowing = false;
            _lastHiddenAt = now;
            _view?.Hide();
        }
    }
}
