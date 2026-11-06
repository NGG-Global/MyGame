using System;
using OpenSkies.Awareness;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>How far the top-right command cluster is expanded.</summary>
    public enum CommandLevel
    {
        /// <summary>Only the primary row (camera, options, pause) and, when useful, LOOK FWD.</summary>
        Collapsed = 0,
        /// <summary>The options panel: the HUD row (secondary) and the session actions (tertiary).</summary>
        Options = 1,
        /// <summary>The HUD profile selector, in the options panel's place.</summary>
        HudSelector = 2,
    }

    /// <summary>A destructive session action that needs a second, deliberate tap.</summary>
    public enum CommandConfirm
    {
        None = 0,
        Reset = 1,
        SkipTutorial = 2,
    }

    /// <summary>What a tap on a confirmable action did.</summary>
    public enum ConfirmTapResult
    {
        /// <summary>Nothing (not offered now, or a bounce too soon after the first tap).</summary>
        Ignored = 0,
        /// <summary>The first tap: the action waits for its confirmation; nothing happened yet.</summary>
        Armed = 1,
        /// <summary>The second tap within the window: the action goes ahead.</summary>
        Confirmed = 2,
    }

    /// <summary>Timing of the command cluster's confirmations (tuning, not code).</summary>
    [Serializable]
    public sealed class CommandClusterSettings
    {
        [Tooltip("A first tap on RESET or SKIP TUTORIAL waits this long for the confirming tap, s.")]
        [Range(1f, 10f)] public float confirmWindowSeconds = 3f;
        [Tooltip("A second tap sooner than this after the first is a bounce or an accidental double tap and is ignored, s.")]
        [Range(0f, 1f)] public float confirmMinSeconds = 0.4f;

        /// <summary>A copy with every value inside its range (an asset or a test may hold anything).</summary>
        public CommandClusterSettings Sanitized()
        {
            float window = float.IsNaN(confirmWindowSeconds) ? 3f : Mathf.Clamp(confirmWindowSeconds, 1f, 10f);
            float min = float.IsNaN(confirmMinSeconds) ? 0.4f : Mathf.Clamp(confirmMinSeconds, 0f, 1f);
            return new CommandClusterSettings { confirmWindowSeconds = window, confirmMinSeconds = Mathf.Min(min, window * 0.5f) };
        }
    }

    /// <summary>
    /// The state of the top-right command cluster (docs/TAP_DISCLOSURE_DESIGN.md, milestone 3):
    /// collapsed, the options panel, or the HUD selector in its place; and which destructive
    /// session action, if any, waits for its confirming tap. Plain C#, allocation-free,
    /// EditMode-tested. It decides nothing about the flight: the session applies a confirmed
    /// action through <c>FlightSession</c> and tells the disclosure coordinator what is open.
    ///
    /// Safety: an action is confirmed only by a second tap on the same action, no sooner than
    /// <see cref="CommandClusterSettings.confirmMinSeconds"/> (a double tap or a bounce is not
    /// a confirmation) and no later than the window; any change of level, a collapse, or a tap
    /// on another action drops the waiting confirmation.
    /// </summary>
    public sealed class CommandClusterModel
    {
        /// <summary>The selector's order: least to most information (the density ladder).</summary>
        public static readonly HudProfile[] ProfileOrder = { HudProfile.Cinematic, HudProfile.Smart, HudProfile.Detailed };
        public const int ProfileCount = 3;

        public const string HudLabel = "HUD";
        public const string ResetLabel = "RESET";
        public const string ResetConfirmLabel = "TAP AGAIN\nTO RESET";
        public const string SkipLabel = "SKIP\nTUTORIAL";
        public const string SkipConfirmLabel = "TAP AGAIN\nTO SKIP";
        public const string SettingsLabel = "SETTINGS";
        public const string ChaseLabel = "CHASE";
        public const string CockpitLabel = "COCKPIT";
        public const string LookForwardLabel = "LOOK FWD";

        static readonly string[] ProfileLabels = { "CINEMATIC", "SMART", "DETAILED" };
        static readonly string[] SelectorTitles = { "HUD: CINEMATIC", "HUD: SMART", "HUD: DETAILED" };

        CommandClusterSettings _settings;
        CommandLevel _level;
        CommandConfirm _armed;
        float _armedAt;
        bool _skipAvailable;

        public CommandClusterModel(CommandClusterSettings settings = null) => SetSettings(settings);

        public CommandClusterSettings Settings => _settings;
        public CommandLevel Level => _level;
        public bool IsOpen => _level != CommandLevel.Collapsed;
        /// <summary>The action waiting for its confirming tap, or None.</summary>
        public CommandConfirm Armed => _armed;
        /// <summary>SKIP TUTORIAL is offered (the First Flight is running).</summary>
        public bool SkipAvailable => _skipAvailable;

        public void SetSettings(CommandClusterSettings settings) => _settings = (settings ?? new CommandClusterSettings()).Sanitized();

        // ---- Levels -----------------------------------------------------------------------------------

        /// <summary>The options trigger: the panel opens (from collapsed) with nothing armed.</summary>
        public void Open()
        {
            _level = CommandLevel.Options;
            _armed = CommandConfirm.None;
        }

        /// <summary>The HUD row: the selector takes the panel's place. Only from the options level.</summary>
        public bool OpenHudSelector()
        {
            if (_level != CommandLevel.Options) return false;
            _level = CommandLevel.HudSelector;
            _armed = CommandConfirm.None;
            return true;
        }

        /// <summary>One level back: the selector to the options, the options to collapsed.</summary>
        public void Back()
        {
            _level = _level == CommandLevel.HudSelector ? CommandLevel.Options : CommandLevel.Collapsed;
            _armed = CommandConfirm.None;
        }

        /// <summary>Everything folds; nothing waits for a confirmation.</summary>
        public void Collapse()
        {
            _level = CommandLevel.Collapsed;
            _armed = CommandConfirm.None;
        }

        // ---- Confirmations ----------------------------------------------------------------------------

        /// <summary>Whether SKIP TUTORIAL is offered. Withdrawn, it can no longer be waiting for a confirmation.</summary>
        public void SetSkipAvailable(bool available)
        {
            _skipAvailable = available;
            if (!available && _armed == CommandConfirm.SkipTutorial) _armed = CommandConfirm.None;
        }

        /// <summary>
        /// A tap on RESET or SKIP TUTORIAL at <paramref name="now"/> (unscaled seconds). The first
        /// tap arms the action; a second tap on the same action between the minimum and the window
        /// confirms it (and disarms); sooner is ignored (still armed); later arms it afresh. Only on
        /// the options level, and SKIP only while it is offered.
        /// </summary>
        public ConfirmTapResult Tap(CommandConfirm action, float now)
        {
            if (_level != CommandLevel.Options || action == CommandConfirm.None) return ConfirmTapResult.Ignored;
            if (action == CommandConfirm.SkipTutorial && !_skipAvailable) return ConfirmTapResult.Ignored;
            if (float.IsNaN(now) || float.IsInfinity(now)) return ConfirmTapResult.Ignored;
            if (_armed == action)
            {
                float elapsed = now - _armedAt;
                if (elapsed >= 0f && elapsed < _settings.confirmMinSeconds) return ConfirmTapResult.Ignored;
                if (elapsed >= 0f && elapsed <= _settings.confirmWindowSeconds)
                {
                    _armed = CommandConfirm.None;
                    return ConfirmTapResult.Confirmed;
                }
            }
            _armed = action;
            _armedAt = now;
            return ConfirmTapResult.Armed;
        }

        /// <summary>Drops the waiting confirmation (a tap elsewhere in the panel).</summary>
        public void Disarm() => _armed = CommandConfirm.None;

        /// <summary>Per frame: a confirmation not given within the window lapses.</summary>
        public void Tick(float now)
        {
            if (_armed == CommandConfirm.None) return;
            float elapsed = now - _armedAt;
            if (!(elapsed >= 0f) || elapsed > _settings.confirmWindowSeconds) _armed = CommandConfirm.None;
        }

        // ---- Labels (cached strings: presenting never allocates) --------------------------------------

        public static HudProfile ProfileAt(int index) =>
            index >= 0 && index < ProfileCount ? ProfileOrder[index] : HudProfile.Smart;

        public static int IndexOf(HudProfile profile)
        {
            for (int i = 0; i < ProfileCount; i++)
                if (ProfileOrder[i] == profile) return i;
            return 1;
        }

        public static string ProfileLabel(HudProfile profile) => ProfileLabels[IndexOf(profile)];

        /// <summary>The selector's title row (beside its back chevron): the profile shown now, so the choice is never colour alone.</summary>
        public static string SelectorTitle(HudProfile profile) => SelectorTitles[IndexOf(profile)];

        public static string CameraLabel(bool cockpit) => cockpit ? CockpitLabel : ChaseLabel;

        public string ResetCaption => _armed == CommandConfirm.Reset ? ResetConfirmLabel : ResetLabel;
        public string SkipCaption => _armed == CommandConfirm.SkipTutorial ? SkipConfirmLabel : SkipLabel;
    }
}
