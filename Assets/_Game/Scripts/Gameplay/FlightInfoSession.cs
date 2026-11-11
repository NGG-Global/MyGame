using System;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.Gestures;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// The top-left flight information cluster's behaviour (docs/TAP_DISCLOSURE_DESIGN.md,
    /// milestone 4). Plain C#, owned by <see cref="FlightSession"/>.
    ///
    /// What the cluster shows comes from the flight session's own calculations: the telemetry
    /// as the altimeter shows it, the runway and navigation relations, and the disclosure
    /// controller's decisions (profile, phase, cockpit, warnings, the player's guide choices).
    /// This class never changes any of them: collapsing the cluster does not touch a guide, a
    /// profile or the flight.
    ///
    /// How far the cluster is open is the player's: a tap on the capsule opens the panel (a root
    /// surface of the <see cref="DisclosureCoordinator"/>, so it replaces any other expanded
    /// cluster and an outside tap folds it, consumed by the interaction surface); a tap on a tile
    /// focuses that reading or guide (a child surface); the focused tile again, or the focused
    /// detail, goes back to the panel; the trigger folds everything. A camera change, a pause and
    /// a new flight fold it too, so the cockpit view always starts quiet. Nothing else opens or
    /// folds it: not a profile change, not a phase change, not a warning. No close button.
    /// </summary>
    public sealed class FlightInfoSession : IDisposable
    {
        readonly FlightInfoCluster _view;
        readonly DisclosureCoordinator _coordinator;
        readonly FlightInfoModel _model;
        readonly int _panelLayer = DisclosureCoordinator.None, _focusLayer = DisclosureCoordinator.None;
        bool _hasView;
        ViewMode _lastView;

        public FlightInfoSession(FlightInfoCluster view, DisclosureCoordinator coordinator, HudExplanations explanations)
        {
            _view = view;
            _coordinator = coordinator;
            _model = new FlightInfoModel(view != null ? view.settings : null);
            if (_coordinator != null)
            {
                _panelLayer = _coordinator.Register("Flight information", IsOnPanel, CollapsePanel);
                _focusLayer = _coordinator.Register("Flight information focus", IsOnPanel, CollapseFocus, true, _panelLayer);
            }
            if (_view == null) return;
            _view.SetExplanations(explanations);
            _view.CapsuleTapped += OnCapsule;
            _view.TriggerTapped += OnTrigger;
            _view.TileTapped += OnTile;
            _view.DetailTapped += OnDetail;
            _view.ShowLevel(_model);
        }

        public FlightInfoModel Model => _model;
        public FlightInfoCluster View => _view;
        public FlightInfoLevel Level => _model.Level;
        public bool IsOpen => _model.IsOpen;
        public HudElement Focus => _model.Focus;
        public int PanelLayer => _panelLayer;
        public int FocusLayer => _focusLayer;

        static bool Blocked => Time.timeScale <= 0f;

        /// <summary>
        /// Per frame from the flight session, before disclosure. A frozen flight folds the cluster
        /// (the pause interfaces take over); a camera change folds it, so the cockpit view, whose
        /// instruments carry the readings, always starts quiet.
        /// </summary>
        public void Update(bool blocked, ViewMode view)
        {
            if (blocked && _model.IsOpen) Collapse(DisclosureCollapseReason.Blocked);
            if (!_hasView)
            {
                _hasView = true;
                _lastView = view;
            }
            else if (view != _lastView)
            {
                _lastView = view;
                if (_model.IsOpen && _model.Settings.collapseOnViewChange) Collapse(DisclosureCollapseReason.Owner);
            }
        }

        /// <summary>
        /// The cluster's content from this frame's values, after the disclosure controller ran:
        /// <paramref name="shown"/> is the telemetry as the HUD shows it (altitude with the
        /// altimeter's subscale). Allocation-free.
        /// </summary>
        public void Present(in FlightTelemetry shown, in RunwayRelation runway, in NavigationRelation navigation, DisclosureController disclosure,
            int warningMask, float distanceToRunwayNm)
        {
            if (disclosure == null) return;
            bool wasFocused = _model.IsFocused;
            bool inRange = runway.Valid && disclosure.ApproachGuideInRange(shown.WeightOnWheels, distanceToRunwayNm);
            _model.Compose(disclosure, warningMask, navigation.Valid, runway.Valid, inRange);
            // A focused guide that is no longer offered (the destination went away) went back to the panel.
            if (wasFocused && !_model.IsFocused) _coordinator?.NotifyClosed(_focusLayer);
            if (_view != null) _view.Present(_model, shown, runway, navigation, disclosure.Settings);
        }

        /// <summary>Opens the panel (the capsule's tap). Never over a frozen flight.</summary>
        public void Expand()
        {
            if (_model.IsOpen || Blocked) return;
            _model.Expand();
            if (_coordinator != null && !_coordinator.Open(_panelLayer)) _model.Collapse();
            Refresh();
        }

        /// <summary>Folds the panel and its focus. The guides, the profile and the flight are untouched.</summary>
        public void Collapse(DisclosureCollapseReason reason)
        {
            if (_coordinator != null && _coordinator.IsActive(_panelLayer)) _coordinator.Close(_panelLayer, reason);
            else _model.Collapse();
            Refresh();
        }

        /// <summary>Focuses a reading or a guide of the open panel (a tile's tap).</summary>
        public void FocusOn(HudElement element)
        {
            if (!_model.IsOpen) return;
            if (_model.IsFocused && _model.Focus == element)
            {
                Unfocus();
                return;
            }
            if (!_model.FocusOn(element)) return;
            _coordinator?.Open(_focusLayer);
            Refresh();
        }

        /// <summary>Back from the focused element to the panel.</summary>
        public void Unfocus()
        {
            if (!_model.IsFocused) return;
            if (_coordinator != null && _coordinator.IsActive(_focusLayer)) _coordinator.Close(_focusLayer, DisclosureCollapseReason.ParentTap);
            else _model.Unfocus();
            Refresh();
        }

        void Refresh()
        {
            if (_view != null) _view.ShowLevel(_model);
        }

        // ---- The view's taps -----------------------------------------------------------------------

        void OnCapsule() => Expand();

        void OnTrigger()
        {
            if (_model.IsOpen) Collapse(DisclosureCollapseReason.Trigger);
            else Expand();
        }

        void OnTile(HudElement element) => FocusOn(element);

        void OnDetail() => Unfocus();

        // ---- The coordinator ---------------------------------------------------------------------------

        bool IsOnPanel(Vector2 screenPoint) => _model.IsOpen && _view != null && _view.panel != null && ScreenRects.Contains(_view.panel, screenPoint);

        void CollapsePanel(DisclosureCollapseReason reason)
        {
            _model.Collapse();
            Refresh();
        }

        void CollapseFocus(DisclosureCollapseReason reason)
        {
            _model.Unfocus();
            Refresh();
        }

        public void Dispose()
        {
            if (_view != null)
            {
                _view.CapsuleTapped -= OnCapsule;
                _view.TriggerTapped -= OnTrigger;
                _view.TileTapped -= OnTile;
                _view.DetailTapped -= OnDetail;
            }
            if (_coordinator != null)
            {
                _coordinator.Unregister(_focusLayer);
                _coordinator.Unregister(_panelLayer);
            }
        }
    }
}
