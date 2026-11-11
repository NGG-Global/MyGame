using System;
using OpenSkies.Awareness;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>How far the top-left flight information cluster is open.</summary>
    public enum FlightInfoLevel
    {
        /// <summary>The capsule: the trigger, a few readings and at most one guide chip. The resting state in every profile.</summary>
        Collapsed = 0,
        /// <summary>The panel: every reading, and the navigation and approach details when there are any.</summary>
        Expanded = 1,
        /// <summary>The panel with one reading or guide enlarged and explained in its lower row.</summary>
        Focused = 2,
    }

    /// <summary>Tuning of the flight information cluster (design values, not code).</summary>
    [Serializable]
    public sealed class FlightInfoSettings
    {
        [Tooltip("Readings the collapsed capsule shows at most. Warning-related readings come first; the rest follow the fixed order IAS, ALT, VS, HDG, GS.")]
        [Range(0, FlightInfoModel.MaxCapsuleReadings)] public int capsuleReadings = FlightInfoModel.MaxCapsuleReadings;
        [Tooltip("Opacity of a quiet reading in the capsule (stable flight), like the old flight data strip.")]
        [Range(0.1f, 1f)] public float quietAlpha = 0.55f;
        [Tooltip("A change of camera view folds the open cluster, so the cockpit view always starts quiet (its instruments carry the readings).")]
        public bool collapseOnViewChange = true;
        [Tooltip("Readings and guides refresh at this interval, s.")]
        [Range(0.02f, 0.5f)] public float valueRefreshSeconds = 0.1f;

        /// <summary>A copy with every value inside its range (an asset or a test may hold anything).</summary>
        public FlightInfoSettings Sanitized() => new FlightInfoSettings
        {
            capsuleReadings = Mathf.Clamp(capsuleReadings, 0, FlightInfoModel.MaxCapsuleReadings),
            quietAlpha = float.IsNaN(quietAlpha) ? 0.55f : Mathf.Clamp(quietAlpha, 0.1f, 1f),
            collapseOnViewChange = collapseOnViewChange,
            valueRefreshSeconds = float.IsNaN(valueRefreshSeconds) ? 0.1f : Mathf.Clamp(valueRefreshSeconds, 0.02f, 0.5f),
        };
    }

    /// <summary>
    /// The state of the top-left flight information cluster (docs/TAP_DISCLOSURE_DESIGN.md,
    /// milestone 4) and what its capsule shows. Plain C#, allocation-free, EditMode-tested.
    ///
    /// Two things are kept apart on purpose:
    /// - <b>What the flight context offers</b> comes from <see cref="DisclosureController"/>: the
    ///   HUD profile, the phase of flight, stable flight, the cockpit's instruments, the
    ///   player's guide choices and the warnings. <see cref="Compose"/> turns its decisions into
    ///   the capsule's content; nothing here changes them.
    /// - <b>How far the player opened the cluster</b> (<see cref="Level"/>) changes only by the
    ///   player's taps and by collapses (an outside tap, another cluster, a camera change, a
    ///   pause, a new flight). A profile, a phase or a warning never opens or closes it.
    ///
    /// The capsule shows at most <see cref="FlightInfoSettings.capsuleReadings"/> readings the
    /// controller makes visible (warning-related first, then in the fixed order IAS, ALT, VS,
    /// HDG, GS, and shown in that order) and one guide chip: the approach guide when it is
    /// shown, otherwise the navigation guide when it is shown. The expanded panel shows every
    /// reading and the guides' details; a focused element is enlarged and explained in the
    /// panel's lower row.
    /// </summary>
    public sealed class FlightInfoModel
    {
        public const int MaxCapsuleReadings = 3;
        /// <summary>No element (nothing focused, no chip).</summary>
        public const HudElement NoElement = (HudElement)(-1);

        public const string HintLabel = "Tap a reading to explain it";

        FlightInfoSettings _settings;
        FlightInfoLevel _level;
        HudElement _focus = NoElement;

        readonly HudElement[] _capsule = new HudElement[MaxCapsuleReadings];
        readonly Prominence[] _capsuleProminence = new Prominence[MaxCapsuleReadings];
        int _capsuleCount;
        HudElement _chip = NoElement;
        int _alertMask;
        bool _panelNavigation, _panelApproach;

        public FlightInfoModel(FlightInfoSettings settings = null) => SetSettings(settings);

        public FlightInfoSettings Settings => _settings;
        public void SetSettings(FlightInfoSettings settings) => _settings = (settings ?? new FlightInfoSettings()).Sanitized();

        public FlightInfoLevel Level => _level;
        public bool IsOpen => _level != FlightInfoLevel.Collapsed;
        public bool IsFocused => _level == FlightInfoLevel.Focused;
        /// <summary>The focused reading or guide, or <see cref="NoElement"/>.</summary>
        public HudElement Focus => _level == FlightInfoLevel.Focused ? _focus : NoElement;

        // ---- Levels --------------------------------------------------------------------------------

        /// <summary>The capsule was tapped: the panel opens (nothing focused). Returns whether it was collapsed.</summary>
        public bool Expand()
        {
            if (_level != FlightInfoLevel.Collapsed) return false;
            _level = FlightInfoLevel.Expanded;
            _focus = NoElement;
            return true;
        }

        /// <summary>Everything folds back to the capsule.</summary>
        public void Collapse()
        {
            _level = FlightInfoLevel.Collapsed;
            _focus = NoElement;
        }

        /// <summary>The trigger: opens a collapsed cluster, folds an open one. Returns the new level.</summary>
        public FlightInfoLevel TapTrigger()
        {
            if (_level == FlightInfoLevel.Collapsed) Expand();
            else Collapse();
            return _level;
        }

        /// <summary>Whether an element can be focused now: any reading, a guide only while the panel shows it.</summary>
        public bool CanFocus(HudElement element)
        {
            if (FlightReadouts.IsReading(element)) return true;
            if (element == HudElement.NavigationGuide) return _panelNavigation;
            if (element == HudElement.ApproachGuide) return _panelApproach;
            return false;
        }

        /// <summary>Focuses an element of the open panel. Returns whether it is focused.</summary>
        public bool FocusOn(HudElement element)
        {
            if (!IsOpen || !CanFocus(element)) return false;
            _level = FlightInfoLevel.Focused;
            _focus = element;
            return true;
        }

        /// <summary>Back from the focused element to the panel.</summary>
        public void Unfocus()
        {
            if (_level != FlightInfoLevel.Focused) return;
            _level = FlightInfoLevel.Expanded;
            _focus = NoElement;
        }

        /// <summary>
        /// A tile of the open panel was tapped: it focuses that element; the focused one again
        /// goes back to the panel; another one moves the focus. Nothing happens while collapsed.
        /// Returns the new level.
        /// </summary>
        public FlightInfoLevel TapTile(HudElement element)
        {
            if (_level == FlightInfoLevel.Collapsed) return _level;
            if (_level == FlightInfoLevel.Focused && _focus == element) Unfocus();
            else FocusOn(element);
            return _level;
        }

        // ---- Content ---------------------------------------------------------------------------------

        /// <summary>Readings in the capsule now (0 to <see cref="MaxCapsuleReadings"/>).</summary>
        public int CapsuleCount => _capsuleCount;
        /// <summary>The capsule's reading in a cell, in display order (the fixed order).</summary>
        public HudElement CapsuleElement(int cell) => cell >= 0 && cell < _capsuleCount ? _capsule[cell] : NoElement;
        /// <summary>Full or quiet (stable flight) for a capsule cell.</summary>
        public Prominence CapsuleProminence(int cell) => cell >= 0 && cell < _capsuleCount ? _capsuleProminence[cell] : Prominence.Hidden;
        /// <summary>The capsule cell that shows an element, or -1.</summary>
        public int CapsuleCellOf(HudElement element)
        {
            for (int i = 0; i < _capsuleCount; i++)
                if (_capsule[i] == element) return i;
            return -1;
        }
        /// <summary>The guide chip of the capsule: the approach guide, the navigation guide, or <see cref="NoElement"/>.</summary>
        public HudElement Chip => _chip;
        /// <summary>The element is tied to an active warning (airspeed in a stall): drawn in the alert colour, shown first.</summary>
        public bool IsAlert(HudElement element) => (int)element >= 0 && (int)element < HudElements.Count && (_alertMask & HudElements.Bit(element)) != 0;
        /// <summary>The panel's lower row offers the navigation details (there is a destination).</summary>
        public bool PanelNavigation => _panelNavigation;
        /// <summary>The panel's lower row offers the approach details (the guide is shown, or the runway is within range).</summary>
        public bool PanelApproach => _panelApproach;

        /// <summary>
        /// The capsule's content and the panel's guides from the disclosure controller's
        /// decisions, once per frame after the controller ran. <paramref name="navigationValid"/>:
        /// there is a destination; <paramref name="runwayValid"/>: there is a runway to approach;
        /// <paramref name="approachInRange"/>: airborne within the approach guide's range
        /// (<see cref="DisclosureController.ApproachGuideInRange"/>). A focused guide that is no
        /// longer offered goes back to the panel. Never changes the level otherwise.
        /// </summary>
        public void Compose(DisclosureController disclosure, int warningMask, bool navigationValid, bool runwayValid, bool approachInRange)
        {
            _capsuleCount = 0;
            _chip = NoElement;
            _alertMask = 0;
            _panelNavigation = navigationValid;
            _panelApproach = false;
            if (disclosure != null)
            {
                for (int i = 0; i < HudElements.Count; i++)
                    if ((warningMask & DisclosureController.RelatedWarnings((HudElement)i)) != 0) _alertMask |= 1 << i;

                // Warning-related readings first, so a warning always surfaces its reading; then the fixed order.
                int capacity = _settings.capsuleReadings;
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < FlightReadouts.ReadingCount && _capsuleCount < capacity; i++)
                    {
                        var element = (HudElement)i;
                        bool alert = (_alertMask & (1 << i)) != 0;
                        if (alert != (pass == 0) || !disclosure.IsVisible(element)) continue;
                        _capsule[_capsuleCount] = element;
                        _capsuleProminence[_capsuleCount] = disclosure.GetProminence(element);
                        _capsuleCount++;
                    }
                SortCapsule();

                bool approachShown = runwayValid && disclosure.IsVisible(HudElement.ApproachGuide);
                if (approachShown) _chip = HudElement.ApproachGuide;
                else if (navigationValid && disclosure.IsVisible(HudElement.NavigationGuide)) _chip = HudElement.NavigationGuide;
                _panelApproach = approachShown || (runwayValid && approachInRange);
            }
            if (_level == FlightInfoLevel.Focused && !CanFocus(_focus)) Unfocus();
        }

        /// <summary>Display order is the fixed order, whatever order the readings were chosen in (at most three: insertion sort).</summary>
        void SortCapsule()
        {
            for (int i = 1; i < _capsuleCount; i++)
            {
                var element = _capsule[i];
                var prominence = _capsuleProminence[i];
                int j = i - 1;
                while (j >= 0 && _capsule[j] > element)
                {
                    _capsule[j + 1] = _capsule[j];
                    _capsuleProminence[j + 1] = _capsuleProminence[j];
                    j--;
                }
                _capsule[j + 1] = element;
                _capsuleProminence[j + 1] = prominence;
            }
        }
    }
}
