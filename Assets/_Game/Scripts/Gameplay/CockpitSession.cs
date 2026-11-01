using System;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.Cockpit;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Ties the cockpit together for one flight: taps from
    /// <see cref="CockpitInteractionManager"/> open the focus panel (an enlarged
    /// instrument, or a selector for a system), choices go through the shared
    /// <see cref="SystemChangeGate"/> into the authoritative
    /// <see cref="AircraftSystems"/>, and instruments are drawn from the shared
    /// telemetry. The item the open panel refers to is framed in the cockpit, and
    /// LOOK FWD is offered while the view is turned away. The cockpit is only
    /// active in cockpit view; any camera change closes the panel. Nothing here
    /// touches flight controls or time.
    /// </summary>
    public sealed class CockpitSession : IDisposable
    {
        const string EngineOffConfirmation = "Engine OFF in flight: tap OFF again to confirm.";
        const string ConfirmGeneric = "Tap again to confirm.";

        static readonly string[] OnOff = { "OFF", "ON" };
        static readonly string[] ParkingBrakeOptions = { "RELEASE", "SET" };
        static readonly string[] EngineStates = { "ENGINE RUNNING", "ENGINE STARTING", "ENGINE OFF" };

        readonly AircraftSystems _systems;
        readonly SystemChangeGate _gate;
        readonly CockpitRig _rig;
        readonly CockpitInteractionManager _interaction;
        readonly CockpitFocusPanel _panel;
        readonly CockpitViewControls _viewControls;
        readonly AircraftLightsView _lights;
        readonly CameraRig _camera;
        readonly string[] _flapOptions;
        readonly string[][] _flapStates;   // [selected detent][actual degrees], built on first use

        bool _hasView;
        ViewMode _view;
        bool _onGround;
        FlightTelemetry _telemetry;
        int _shownConfirmation = -1;
        int _shownSelection = NoSelection;

        const int NoSelection = -1;

        public CockpitSession(AircraftSystems systems, SystemChangeGate gate, CockpitRig rig, CockpitInteractionManager interaction,
            CockpitFocusPanel panel, CockpitViewControls viewControls, AircraftLightsView lights, CameraRig camera, PointerOwnership ownership,
            in AirspeedMarkings markings)
        {
            _systems = systems ?? throw new ArgumentNullException(nameof(systems));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _rig = rig;
            _interaction = interaction;
            _panel = panel;
            _viewControls = viewControls;
            _lights = lights;
            _camera = camera;

            int detents = systems.FlapDetentCount;
            _flapOptions = new string[detents];
            for (int i = 0; i < detents; i++)
                _flapOptions[i] = i == 0 ? "UP" : Mathf.RoundToInt(systems.FlapDetentDegrees(i)) + "°";
            _flapStates = new string[detents][];

            if (_rig != null)
            {
                for (int i = 0; i < _rig.switchViews.Length; i++)
                    if (_rig.switchViews[i] != null) _rig.switchViews[i].Bind(systems);
                var cockpitAirspeed = _rig.instruments[(int)InstrumentKind.Airspeed];
                if (cockpitAirspeed != null) cockpitAirspeed.ConfigureAirspeed(markings);
            }
            if (_lights != null) _lights.Bind(systems);
            if (_interaction != null)
            {
                if (camera != null) _interaction.cameraRig = camera;
                if (ownership != null) _interaction.Initialize(ownership);
                if (_rig != null) _interaction.SetInteractables(_rig.interactables);
                _interaction.InteractableTapped += OnInteractableTapped;
                _interaction.EmptyTapped += Close;
            }
            if (_panel != null)
            {
                var focusAirspeed = _panel.faces[(int)InstrumentKind.Airspeed];
                if (focusAirspeed != null) focusAirspeed.ConfigureAirspeed(markings);
                _panel.CloseRequested += Close;
                _panel.OptionChosen += OnOptionChosen;
            }
            if (_viewControls != null) _viewControls.LookForwardRequested += LookForward;
        }

        public void Dispose()
        {
            if (_interaction != null)
            {
                _interaction.InteractableTapped -= OnInteractableTapped;
                _interaction.EmptyTapped -= Close;
            }
            if (_panel != null)
            {
                _panel.CloseRequested -= Close;
                _panel.OptionChosen -= OnOptionChosen;
            }
            if (_viewControls != null) _viewControls.LookForwardRequested -= LookForward;
        }

        public bool IsPanelOpen => _panel != null && _panel.IsOpen;

        /// <summary>The focus panel opened (an instrument or a selector): other panels in the free column close.</summary>
        public event Action PanelOpened;

        /// <summary>Per frame, after the telemetry is known.</summary>
        public void Update(in FlightTelemetry telemetry, in RunwayRelation runway, ViewMode view)
        {
            _telemetry = telemetry;
            _onGround = telemetry.WeightOnWheels;
            if (!_hasView || view != _view)
            {
                _hasView = true;
                _view = view;
                bool cockpit = view == ViewMode.Cockpit;
                if (_rig != null && _rig.gameObject.activeSelf != cockpit) _rig.gameObject.SetActive(cockpit); // nothing drawn or updated in chase view
                if (_interaction != null) _interaction.SetActive(cockpit);
                Close();
            }

            bool needInstruments = _view == ViewMode.Cockpit || (_panel != null && _panel.ActiveFace != null);
            if (needInstruments)
            {
                var readings = InstrumentReadings.From(telemetry, runway);
                if (_view == ViewMode.Cockpit && _rig != null)
                    for (int i = 0; i < _rig.instruments.Length; i++)
                        if (_rig.instruments[i] != null) _rig.instruments[i].Render(readings);
                if (_panel != null) _panel.Render(readings);
            }
            RefreshSystemPanel();
            RefreshSelection();
            if (_viewControls != null)
                _viewControls.ShowLookForward(_view == ViewMode.Cockpit && _camera != null && _camera.IsLookingAway);
        }

        /// <summary>Turns the cockpit view smoothly back to rest (LOOK FWD). Never touches flight input.</summary>
        public void LookForward()
        {
            if (_camera != null) _camera.RecentreCockpitLook();
        }

        /// <summary>The cockpit item the open panel refers to, or -1. Derived from the panel, so every way of closing it clears the frame.</summary>
        int SelectedItem()
        {
            if (_panel == null) return NoSelection;
            if (_panel.IsShowingInstrument(out var kind)) return (int)kind; // instruments share their values with CockpitItem
            if (_panel.IsShowingSystem(out var system)) return (int)CockpitItems.ControlFor(system);
            return NoSelection;
        }

        void RefreshSelection()
        {
            var view = _rig != null ? _rig.selection : null;
            if (view == null) return;
            int selected = SelectedItem();
            if (selected == _shownSelection) return;
            _shownSelection = selected;
            if (selected == NoSelection) view.Hide();
            else view.Show(_rig.Find((CockpitItem)selected));
        }

        /// <summary>Opens the enlarged view of an instrument.</summary>
        public void OpenInstrument(InstrumentKind kind)
        {
            if (_panel == null) return;
            var item = (CockpitItem)(int)kind;
            _gate.Clear();
            _panel.ShowInstrument(kind, CockpitItems.DisplayName(item), CockpitItems.AccessibilityHint(item));
            PanelOpened?.Invoke();
        }

        /// <summary>Opens the selector for a system (from the cockpit or from the contextual HUD).</summary>
        public void OpenSystem(AircraftSystem system)
        {
            if (_panel == null) return;
            var item = CockpitItems.ControlFor(system);
            _gate.Clear();
            _shownConfirmation = -1;
            _panel.ShowSystem(system, CockpitItems.DisplayName(item), CockpitItems.AccessibilityHint(item), Options(system), _systems.PositionCount(system));
            RefreshSystemPanel();
            PanelOpened?.Invoke();
        }

        public void Close()
        {
            _gate.Clear();
            _shownConfirmation = -1;
            if (_panel != null) _panel.Close();
        }

        /// <summary>A new flight: panels closed, pending confirmations dropped. The view direction is kept.</summary>
        public void OnReset() => Close();

        void OnInteractableTapped(CockpitInteractable target)
        {
            if (target == null || _panel == null) return;
            // Tapping the item that is already open closes it again.
            if (target.IsInstrument)
            {
                var kind = (InstrumentKind)(int)target.item;
                if (_panel.IsShowingInstrument(out var shown) && shown == kind) Close();
                else OpenInstrument(kind);
                return;
            }
            if (!CockpitItems.TryGetSystem(target.item, out var system)) return;
            if (_panel.IsShowingSystem(out var current) && current == system) Close();
            else OpenSystem(system);
        }

        void OnOptionChosen(int option)
        {
            if (_panel == null || !_panel.IsShowingSystem(out var system)) return;
            _gate.Request(new SystemChange(system, option), _systems, _onGround, Time.unscaledTime);
            RefreshSystemPanel();
        }

        void RefreshSystemPanel()
        {
            if (_panel == null || !_panel.IsShowingSystem(out var system)) return;
            int positions = _systems.PositionCount(system);
            int enabled = 0;
            for (int i = 0; i < positions; i++)
                if (AircraftSystemsPolicy.Evaluate(new SystemChange(system, i), _systems, _onGround) != AircraftSystemsPolicy.Verdict.Refused)
                    enabled |= 1 << i;
            _panel.UpdateSystem(_systems.Get(system), enabled, State(system));

            int pending = _gate.HasPending(Time.unscaledTime, out var change) && change.System == system ? change.Value : -1;
            if (pending == _shownConfirmation) return;
            _shownConfirmation = pending;
            if (pending < 0) _panel.ClearConfirmation();
            else _panel.SetConfirmation(pending, system == AircraftSystem.Ignition ? EngineOffConfirmation : ConfirmGeneric);
        }

        string[] Options(AircraftSystem system)
        {
            switch (system)
            {
                case AircraftSystem.Flaps: return _flapOptions;
                case AircraftSystem.ParkingBrake: return ParkingBrakeOptions;
                default: return OnOff;
            }
        }

        /// <summary>What the system is actually doing (cached strings: no allocation once seen).</summary>
        string State(AircraftSystem system)
        {
            switch (system)
            {
                case AircraftSystem.Flaps:
                {
                    int selected = _systems.FlapDetent;
                    float max = Mathf.Max(1f, _systems.MaxFlapDegrees);
                    int actual = Mathf.Clamp(Mathf.RoundToInt(_telemetry.FlapDegrees), 0, Mathf.RoundToInt(max));
                    var row = _flapStates[selected] ?? (_flapStates[selected] = new string[Mathf.RoundToInt(max) + 1]);
                    return row[actual] ?? (row[actual] = "SET " + _flapOptions[selected] + "    FLAPS NOW " + actual + "°");
                }
                case AircraftSystem.Ignition:
                    return EngineStates[(int)_telemetry.EngineState];
                case AircraftSystem.ParkingBrake:
                    return _systems.ParkingBrakeSet ? "BRAKE SET" : "BRAKE RELEASED";
                case AircraftSystem.LandingLight:
                    return _systems.LandingLightOn ? "LANDING LIGHT ON" : "LANDING LIGHT OFF";
                default:
                    return _systems.NavigationLightsOn ? "NAV LIGHTS ON" : "NAV LIGHTS OFF";
            }
        }
    }
}
