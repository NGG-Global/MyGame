using System;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.Cockpit;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Missions;
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
        readonly InstrumentSettings _instruments;
        readonly FlapsLeverInteraction _lever;
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

        // A switch being flicked: the control, its system, where the finger started and the switch view that previews it.
        int _flickPointer = NoPointer;
        AircraftSystem _flickSystem;
        float _flickStartY;
        CockpitSwitchView _flickView;
        int _leverPointer = NoPointer;

        const int NoSelection = -1;
        const int NoPointer = int.MinValue;
        /// <summary>A flick must travel this share of the screen height to move the switch.</summary>
        public float flickDistance = 0.05f;

        /// <summary>Two fingers tapped the cockpit together: the pause gesture.</summary>
        public event Action PauseRequested;

        GestureHints _hints;
        public void SetHints(GestureHints hints) => _hints = hints;

        public CockpitSession(AircraftSystems systems, SystemChangeGate gate, CockpitRig rig, CockpitInteractionManager interaction,
            CockpitFocusPanel panel, CockpitViewControls viewControls, AircraftLightsView lights, CameraRig camera, PointerOwnership ownership,
            in AirspeedMarkings markings) : this(systems, gate, rig, interaction, panel, viewControls, lights, camera, ownership, markings, null, null) { }

        public CockpitSession(AircraftSystems systems, SystemChangeGate gate, CockpitRig rig, CockpitInteractionManager interaction,
            CockpitFocusPanel panel, CockpitViewControls viewControls, AircraftLightsView lights, CameraRig camera, PointerOwnership ownership,
            in AirspeedMarkings markings, InstrumentSettings instruments, FlapsLeverInteraction lever)
        {
            _systems = systems ?? throw new ArgumentNullException(nameof(systems));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _instruments = instruments;
            _lever = lever;
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
                _interaction.ControlDragBegan += OnControlDragBegan;
                _interaction.ControlDragMoved += OnControlDragMoved;
                _interaction.ControlDragEnded += OnControlDragEnded;
                _interaction.TwoFingerTapped += OnTwoFingerTapped;
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
                _interaction.ControlDragBegan -= OnControlDragBegan;
                _interaction.ControlDragMoved -= OnControlDragMoved;
                _interaction.ControlDragEnded -= OnControlDragEnded;
                _interaction.TwoFingerTapped -= OnTwoFingerTapped;
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
                if (cockpit) OfferInstrumentHint();
            }

            bool needInstruments = _view == ViewMode.Cockpit || (_panel != null && _panel.ActiveFace != null);
            if (needInstruments)
            {
                float baro = _instruments != null ? InstrumentSettings.BaroCorrectionFeet(_instruments.BaroHectopascals) : 0f;
                var readings = InstrumentReadings.From(telemetry, runway, baro);
                if (_view == ViewMode.Cockpit && _rig != null)
                    for (int i = 0; i < _rig.instruments.Length; i++)
                        if (_rig.instruments[i] != null) RenderFace(_rig.instruments[i], readings);
                if (_panel != null)
                {
                    _panel.Render(readings);
                    var active = _panel.ActiveFace;
                    if (active != null) ShowSettings(active);
                }
            }
            _lever?.Sync();
            RefreshSystemPanel();
            RefreshSelection();
            if (_viewControls != null)
                _viewControls.ShowLookForward(_view == ViewMode.Cockpit && _camera != null && _camera.IsLookingAway);
        }

        /// <summary>The first time the cockpit opens: a cue on the heading indicator says that instruments can be held and adjusted.</summary>
        void OfferInstrumentHint()
        {
            if (_hints == null || !_hints.HasView || _rig == null || _camera == null || _camera.ViewCamera == null) return;
            if (_hints.HasLearned(GestureId.HoldInstrument)) return;
            var heading = _rig.Find(CockpitItem.HeadingIndicator);
            if (heading == null) return;
            Vector3 screen = _camera.ViewCamera.WorldToScreenPoint(heading.transform.position);
            if (screen.z <= 0f) return;
            _hints.OfferAtScreen(GestureId.HoldInstrument, screen, Vector2.up, 60f, "Tap an instrument, then drag around it to adjust · hold to explain", Time.unscaledTime);
        }

        void RenderFace(InstrumentFace face, in InstrumentReadings readings)
        {
            face.Render(readings);
            ShowSettings(face);
        }

        /// <summary>The committed heading bug and subscale on a face (a manipulation in progress draws its preview over them afterwards).</summary>
        void ShowSettings(InstrumentFace face)
        {
            if (_instruments == null) return;
            if (face.kind == InstrumentKind.Heading) face.SetHeadingBug(_instruments.HeadingBugDegrees, false);
            else if (face.kind == InstrumentKind.Altimeter) face.SetBaro(_instruments.BaroHectopascals, false);
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

        /// <summary>Opens the enlarged view of an instrument, growing out of the instrument on the panel when it is in view.</summary>
        public void OpenInstrument(InstrumentKind kind)
        {
            if (_panel == null) return;
            var item = (CockpitItem)(int)kind;
            _gate.Clear();
            var source = _rig != null ? _rig.Find(item) : null;
            var viewCamera = _camera != null ? _camera.ViewCamera : null;
            if (source != null && viewCamera != null && _view == ViewMode.Cockpit)
            {
                Vector3 screen = viewCamera.WorldToScreenPoint(source.transform.position);
                if (screen.z > 0f) _panel.ShowInstrument(kind, CockpitItems.DisplayName(item), CockpitItems.AccessibilityHint(item), screen, 0.25f);
                else _panel.ShowInstrument(kind, CockpitItems.DisplayName(item), CockpitItems.AccessibilityHint(item));
            }
            else _panel.ShowInstrument(kind, CockpitItems.DisplayName(item), CockpitItems.AccessibilityHint(item));
            PanelOpened?.Invoke();
        }

        /// <summary>Opens the selector for a system (from the cockpit or from the contextual HUD).</summary>
        public void OpenSystem(AircraftSystem system) => OpenSystem(system, keepPending: false);

        /// <summary>Opens the selector; <paramref name="keepPending"/> keeps a confirmation the gate is already waiting for.</summary>
        public void OpenSystem(AircraftSystem system, bool keepPending)
        {
            if (_panel == null) return;
            var item = CockpitItems.ControlFor(system);
            if (!keepPending) _gate.Clear();
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
        public void OnReset()
        {
            Close();
            _flickView?.SetPreview(0f, false);
            _flickPointer = _leverPointer = NoPointer;
        }

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

        // ---- Direct manipulation of controls ------------------------------------------------------

        /// <summary>
        /// A finger pressed on a control started moving it: the flap lever hands over to the
        /// shared lever interaction (the same lever as the arc's); a switch or the brake
        /// handle follows the finger as a flick and commits on release through the gate.
        /// </summary>
        void OnControlDragBegan(CockpitInteractable target, int pointerId, Vector2 screenPoint)
        {
            if (target == null || !CockpitItems.TryGetSystem(target.item, out var system)) return;
            Close();
            if (system == AircraftSystem.Flaps)
            {
                if (_lever == null || !_lever.Available) return;
                _leverPointer = pointerId;
                _lever.Begin(screenPoint);
                _hints?.Learned(GestureId.DragLever, Time.unscaledTime);
                return;
            }
            _flickPointer = pointerId;
            _flickSystem = system;
            _flickStartY = screenPoint.y;
            _flickView = FindSwitchView(system);
            _flickView?.SetPreview(_systems.Get(system), true);
        }

        void OnControlDragMoved(int pointerId, Vector2 screenPoint)
        {
            if (pointerId == _leverPointer)
            {
                _lever?.Move(screenPoint);
                return;
            }
            if (pointerId != _flickPointer) return;
            _flickView?.SetPreview(FlickPosition(screenPoint.y), true);
        }

        void OnControlDragEnded(int pointerId, Vector2 screenPoint, bool revoked)
        {
            if (pointerId == _leverPointer)
            {
                _leverPointer = NoPointer;
                if (revoked) _lever?.Cancel();
                else _lever?.Commit();
                return;
            }
            if (pointerId != _flickPointer) return;
            _flickPointer = NoPointer;
            _flickView?.SetPreview(0f, false);
            if (revoked) return;
            float travel = (screenPoint.y - _flickStartY) / Mathf.Max(1f, Screen.height);
            if (Mathf.Abs(travel) < flickDistance) { HudButton.Report(UiFeedback.Tap); return; } // too short: the switch springs back
            int value = travel > 0f ? 1 : 0; // up is on or set, down is off or released
            var outcome = _gate.Request(new SystemChange(_flickSystem, value), _systems, _onGround, Time.unscaledTime);
            switch (outcome)
            {
                case SystemChangeGate.Outcome.Applied:
                    HudButton.Report(UiFeedback.Confirm);
                    SystemChanged?.Invoke(new SystemChange(_flickSystem, value));
                    break;
                case SystemChangeGate.Outcome.AwaitingConfirmation:
                    OpenSystem(_flickSystem, keepPending: true); // a hazardous flick: the selector shows the confirmation
                    HudButton.Report(UiFeedback.Tap);
                    break;
                default:
                    HudButton.Report(UiFeedback.Tap);
                    break;
            }
        }

        /// <summary>A change this session applied directly (a flick), so the flight session can tell the others.</summary>
        public event Action<SystemChange> SystemChanged;

        float FlickPosition(float fingerY)
        {
            float travel = (fingerY - _flickStartY) / Mathf.Max(1f, Screen.height) / Mathf.Max(0.01f, flickDistance);
            return Mathf.Clamp01(_systems.Get(_flickSystem) + travel);
        }

        CockpitSwitchView FindSwitchView(AircraftSystem system)
        {
            if (_rig == null) return null;
            for (int i = 0; i < _rig.switchViews.Length; i++)
                if (_rig.switchViews[i] != null && _rig.switchViews[i].system == system) return _rig.switchViews[i];
            return null;
        }

        void OnTwoFingerTapped()
        {
            _hints?.Learned(GestureId.TwoFingerPause, Time.unscaledTime);
            PauseRequested?.Invoke();
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
