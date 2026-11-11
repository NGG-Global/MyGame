using System;
using OpenSkies.Awareness;
using OpenSkies.Gestures;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// The top-right command cluster's behaviour (docs/TAP_DISCLOSURE_DESIGN.md, milestone 3).
    /// Plain C#, owned by <see cref="FlightSession"/>. The cluster's requests go to the
    /// authoritative places: the camera to <see cref="FlightSession.ToggleCamera"/> (flight input
    /// is never touched), the HUD profile to <see cref="FlightSession.SetHudProfile"/>, RESET to
    /// <see cref="FlightSession.ResetFlight"/> after its confirming tap; pause, SKIP TUTORIAL and
    /// SETTINGS are raised for the game modes.
    ///
    /// The options panel is a root surface of the <see cref="DisclosureCoordinator"/> and the HUD
    /// selector its child: opening the panel replaces any other expanded cluster (the wheel, a
    /// cockpit card, the flight settings, the coaching explanation); a tap outside folds it and is
    /// consumed by the interaction surface; the trigger again folds it; the selector's title row
    /// goes back one level. There is no close button. A collapse, whatever its cause, drops a
    /// waiting confirmation, so dismissing the panel can never reset the flight.
    /// </summary>
    public sealed class CommandClusterSession : IDisposable
    {
        readonly FlightSession _flight;
        readonly CommandCluster _view;
        readonly DisclosureCoordinator _coordinator;
        readonly CommandClusterModel _model;
        readonly int _optionsLayer = DisclosureCoordinator.None, _selectorLayer = DisclosureCoordinator.None;

        /// <summary>The pause button: the game modes open (or close) the pause menu.</summary>
        public event Action PauseRequested;
        /// <summary>SKIP TUTORIAL was confirmed by its second tap.</summary>
        public event Action SkipConfirmed;
        /// <summary>SETTINGS: the game modes pause and open the settings.</summary>
        public event Action SettingsRequested;

        public CommandClusterSession(FlightSession flight, CommandCluster view, DisclosureCoordinator coordinator)
        {
            _flight = flight;
            _view = view;
            _coordinator = coordinator;
            _model = new CommandClusterModel(view != null ? view.settings : null);
            if (_coordinator != null)
            {
                _optionsLayer = _coordinator.Register("Command options", IsOnPanel, CollapseOptions);
                _selectorLayer = _coordinator.Register("HUD selector", IsOnPanel, CollapseSelector, true, _optionsLayer);
            }
            if (_view == null) return;
            _view.CameraTapped += OnCamera;
            _view.OptionsTapped += OnOptions;
            _view.PauseTapped += OnPause;
            _view.HudRowTapped += OnHudRow;
            _view.SelectorBackTapped += OnSelectorBack;
            _view.ProfileTapped += OnProfile;
            _view.ResetTapped += OnReset;
            _view.SettingsTapped += OnSettings;
            _view.SkipTapped += OnSkip;
        }

        public CommandClusterModel Model => _model;
        public CommandCluster View => _view;
        public bool IsOpen => _model.IsOpen;
        public CommandLevel Level => _model.Level;
        public int OptionsLayer => _optionsLayer;
        public int SelectorLayer => _selectorLayer;

        /// <summary>What the coach frames for "tap RESET": the options trigger while folded, RESET itself once the panel shows it.</summary>
        public RectTransform ResetHighlightTarget => _view == null ? null : (_model.Level == CommandLevel.Options ? _view.ResetVisual : _view.OptionsVisual);
        /// <summary>What the coach frames for "tap CAM": the camera, always one tap away.</summary>
        public RectTransform CameraHighlightTarget => _view != null ? _view.CameraVisual : null;

        /// <summary>The game modes offer SKIP TUTORIAL while the First Flight runs.</summary>
        public void SetSkipAvailable(bool available)
        {
            if (available == _model.SkipAvailable) return;
            _model.SetSkipAvailable(available);
            Refresh();
        }

        /// <summary>Per frame from the flight session. A frozen flight (pause, entry screen) folds the panel.</summary>
        public void Update(bool blocked)
        {
            if (blocked && _model.IsOpen) Collapse(DisclosureCollapseReason.Blocked);
            _model.Tick(Time.unscaledTime);
            Refresh();
        }

        /// <summary>Folds the panel (and the selector), dropping any waiting confirmation.</summary>
        public void Collapse(DisclosureCollapseReason reason)
        {
            if (_coordinator != null && _coordinator.IsActive(_optionsLayer)) _coordinator.Close(_optionsLayer, reason);
            else _model.Collapse();
            Refresh();
        }

        static bool Blocked => Time.timeScale <= 0f;

        void Refresh()
        {
            if (_view != null) _view.Present(_model, _flight.ActiveHudProfile, _flight.CurrentView == ViewMode.Cockpit);
        }

        // ---- The view's requests -----------------------------------------------------------------------

        /// <summary>One tap, always: the view changes and flight input is untouched (the sticks and the throttle keep their fingers).</summary>
        void OnCamera()
        {
            if (_model.IsOpen) Collapse(DisclosureCollapseReason.Owner);
            _flight.ToggleCamera();
            Refresh();
        }

        void OnOptions()
        {
            if (_model.IsOpen)
            {
                Collapse(DisclosureCollapseReason.Trigger);
                return;
            }
            if (Blocked) return; // never over a frozen flight (the pause interfaces are deliberate to leave)
            _model.Open();
            if (_coordinator != null && !_coordinator.Open(_optionsLayer)) _model.Collapse();
            Refresh();
        }

        void OnPause()
        {
            if (_model.IsOpen) Collapse(DisclosureCollapseReason.Owner);
            PauseRequested?.Invoke();
        }

        void OnHudRow()
        {
            if (!_model.OpenHudSelector()) return;
            _coordinator?.Open(_selectorLayer);
            Refresh();
        }

        void OnSelectorBack()
        {
            if (_model.Level != CommandLevel.HudSelector) return;
            if (_coordinator != null && _coordinator.IsActive(_selectorLayer)) _coordinator.Close(_selectorLayer, DisclosureCollapseReason.ParentTap);
            else _model.Back();
            Refresh();
        }

        /// <summary>A profile chosen directly: applied (presentation only), and the cluster folds: the choice is complete.</summary>
        void OnProfile(int index)
        {
            if (_model.Level != CommandLevel.HudSelector || index < 0 || index >= CommandClusterModel.ProfileCount) return;
            _flight.SetHudProfile(CommandClusterModel.ProfileAt(index));
            Collapse(DisclosureCollapseReason.Owner);
        }

        void OnReset()
        {
            var result = _model.Tap(CommandConfirm.Reset, Time.unscaledTime);
            if (result == ConfirmTapResult.Confirmed)
            {
                Collapse(DisclosureCollapseReason.Owner);
                _flight.ResetFlight();
            }
            Refresh();
        }

        void OnSkip()
        {
            var result = _model.Tap(CommandConfirm.SkipTutorial, Time.unscaledTime);
            if (result == ConfirmTapResult.Confirmed)
            {
                Collapse(DisclosureCollapseReason.Owner);
                SkipConfirmed?.Invoke();
            }
            Refresh();
        }

        void OnSettings()
        {
            if (_model.Level != CommandLevel.Options) return;
            Collapse(DisclosureCollapseReason.Owner);
            SettingsRequested?.Invoke();
        }

        // ---- The coordinator ---------------------------------------------------------------------------

        bool IsOnPanel(Vector2 screenPoint) => _model.IsOpen && _view != null && ScreenRects.Contains(_view.panel, screenPoint);

        void CollapseOptions(DisclosureCollapseReason reason)
        {
            _model.Collapse();
            Refresh();
        }

        void CollapseSelector(DisclosureCollapseReason reason)
        {
            if (_model.Level == CommandLevel.HudSelector) _model.Back();
            Refresh();
        }

        public void Dispose()
        {
            if (_view != null)
            {
                _view.CameraTapped -= OnCamera;
                _view.OptionsTapped -= OnOptions;
                _view.PauseTapped -= OnPause;
                _view.HudRowTapped -= OnHudRow;
                _view.SelectorBackTapped -= OnSelectorBack;
                _view.ProfileTapped -= OnProfile;
                _view.ResetTapped -= OnReset;
                _view.SettingsTapped -= OnSettings;
                _view.SkipTapped -= OnSkip;
            }
            if (_coordinator != null)
            {
                _coordinator.Unregister(_selectorLayer);
                _coordinator.Unregister(_optionsLayer);
            }
        }
    }
}
