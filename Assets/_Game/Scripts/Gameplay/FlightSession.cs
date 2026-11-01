using OpenSkies.Assistance;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.Cockpit;
using OpenSkies.FlightInput;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Composition root for one flight. Before each physics step it reads the
    /// control source (by default a <see cref="FlightInputRouter"/> merging
    /// keyboard/gamepad and touch), shapes it with the player's sensitivity,
    /// applies the aircraft systems (parking brake, flaps, ignition), passes the
    /// commands through the flight assistance layer (<see cref="FlightAssistController"/>,
    /// the player's profile) and hands them to the aircraft, then
    /// updates flight awareness (phase of flight, warnings) from the latest
    /// telemetry. Each frame it handles session commands (reset, camera, systems,
    /// HUD profile), decides HUD disclosure, runs the cockpit and feeds the HUD
    /// and touch controls. Layers only meet here.
    ///
    /// <see cref="Systems"/> is the one authoritative state of the aircraft's
    /// switches and levers; every change (cockpit, contextual HUD, keyboard) goes
    /// through <see cref="RequestSystemChange"/>.
    /// </summary>
    [DefaultExecutionOrder(-50)] // before AircraftController.FixedUpdate
    public sealed class FlightSession : MonoBehaviour
    {
        public AircraftController aircraft;
        public AircraftTelemetry telemetry;
        public CameraRig cameraRig;
        public HudPresenter hud;
        public TouchFlightControls touchControls;

        [Header("Contextual HUD")]
        public ContextualHud contextualHud;
        [Tooltip("Phase, warning and disclosure tuning. The defaults in code are used when empty.")]
        public FlightAwarenessConfiguration awarenessConfiguration;
        [Tooltip("Home runway: approach detection, approach guide and navigation.")]
        public RunwayInfo homeRunway;
        [Tooltip("Remember the HUD profile, motion preference, cockpit field of view and flight settings between sessions (PlayerPrefs).")]
        public bool rememberHudPreferences = true;

        [Header("Flight assistance")]
        [Tooltip("Assistance tuning and first-launch defaults. The defaults in code are used when empty.")]
        public FlightAssistConfiguration assistConfiguration;
        [Tooltip("Assistance profile, sensitivity and options (opened from ASSIST on the action rail).")]
        public FlightSettingsPanel flightSettings;
        [Tooltip("Flying tips for the Assisted profile.")]
        public GuidanceLine guidanceLine;

        [Header("Cockpit")]
        public CockpitRig cockpit;
        public CockpitInteractionManager cockpitInteraction;
        public CockpitFocusPanel focusPanel;
        [Tooltip("LOOK FWD and other cockpit view controls in the top bar.")]
        public CockpitViewControls cockpitViewControls;
        public AircraftLightsView aircraftLights;
        [Tooltip("Cockpit field of view change per second while the keyboard zoom keys are held, degrees.")]
        public float fieldOfViewKeyRate = 20f;

        const float TrackFromVelocityMinSpeed = 2.5f; // m/s; slower than this, the heading stands in for the track
        const float MetresPerNauticalMile = 1852f;

        IFlightControlSource _controls;
        IFlightAssist _assist = NoFlightAssist.Instance;
        bool _assistOverridden;
        FlightAssistController _assistance;
        FlightGuidanceAdvisor _guidance;
        FlightPreferences.Settings _flight;
        KeyboardGamepadInputProvider _devices;
        FlightInputRouter _router;
        AircraftSystems _systems;
        readonly SystemChangeGate _gate = new SystemChangeGate();
        CockpitSession _cockpit;
        float _savedFieldOfView;
        FlightPhaseEstimator _phase;
        FlightWarningMonitor _warnings;
        DisclosureController _disclosure;
        RunwayRelation _runway;
        HudProfile _hudProfile = HudProfile.Smart;

        public FlightTelemetry Telemetry => telemetry != null ? telemetry.Current : default;

        /// <summary>Replace the control source, e.g. with touch controls or a test script.</summary>
        public void SetControlSource(IFlightControlSource source) => _controls = source;

        /// <summary>Replace the assistance layer (tests, experiments). Null gives direct control.</summary>
        public void SetFlightAssist(IFlightAssist assist)
        {
            _assist = assist ?? NoFlightAssist.Instance;
            _assistOverridden = true;
        }

        /// <summary>The flight assistance layer (profile, trim, state).</summary>
        public FlightAssistController Assistance => _assistance;
        public FlightGuidanceAdvisor Guidance => _guidance;
        public FlightAssistProfile AssistProfile => _flight.Profile;
        public ControlSensitivity Sensitivity => _flight.Sensitivity;
        public bool GuidanceEnabled => _flight.Guidance;
        public bool FlightSettingsOpen => flightSettings != null && flightSettings.IsOpen;

        public FlightSettingsState CurrentFlightSettings => new FlightSettingsState
        {
            Profile = _flight.Profile,
            Sensitivity = _flight.Sensitivity,
            StallPrevention = _flight.AssistedStallPrevention,
            Guidance = _flight.Guidance,
            AutoTrim = _flight.StandardAutoTrim,
        };

        public FlightInputRouter Router => _router;
        /// <summary>The authoritative switch and lever positions of the aircraft.</summary>
        public AircraftSystems Systems => _systems;
        public CockpitSession Cockpit => _cockpit;
        public FlightPhaseEstimator PhaseEstimator => _phase;
        public FlightWarningMonitor Warnings => _warnings;
        public DisclosureController Disclosure => _disclosure;
        public RunwayRelation Runway => _runway;
        public HudProfile ActiveHudProfile => _hudProfile;
        public ViewMode CurrentView => cameraRig != null && cameraRig.mode == CameraMode.Cockpit ? ViewMode.Cockpit : ViewMode.External;

        /// <summary>Contextual actions this aircraft and airfield support, one bit per <see cref="ContextAction"/>.</summary>
        public int AvailableActions
        {
            get
            {
                int mask = ContextualHud.ActionBit(ContextAction.ParkingBrake) | ContextualHud.ActionBit(ContextAction.Flaps);
                if (flightSettings != null) mask |= ContextualHud.ActionBit(ContextAction.Assistance);
                if (homeRunway.IsDefined)
                    mask |= ContextualHud.ActionBit(ContextAction.ApproachGuide) | ContextualHud.ActionBit(ContextAction.Navigation);
                return mask;
            }
        }

        void Awake()
        {
            _devices = new KeyboardGamepadInputProvider();
            _router = new FlightInputRouter();
            _router.AddProvider(_devices);
            if (touchControls != null) _router.AddProvider(touchControls);
            var aircraftConfiguration = aircraft != null ? aircraft.configuration : null;
            _systems = new AircraftSystems(aircraftConfiguration != null ? aircraftConfiguration.flapDetentDegrees : null);
            if (_controls == null) _controls = _router;

            var config = awarenessConfiguration != null ? awarenessConfiguration : ScriptableObject.CreateInstance<FlightAwarenessConfiguration>();
            _phase = new FlightPhaseEstimator(config.phase);
            _warnings = new FlightWarningMonitor(config.warnings);
            _disclosure = new DisclosureController(config.disclosure);
            _hudProfile = rememberHudPreferences ? HudPreferences.LoadProfile(config.disclosure.defaultProfile) : config.disclosure.defaultProfile;

            if (contextualHud != null)
            {
                if (rememberHudPreferences) contextualHud.SetMotionPreference(HudPreferences.LoadMotion(contextualHud.motionPreference));
                contextualHud.ConfigureActions(AvailableActions);
            }
            if (focusPanel != null && contextualHud != null) focusPanel.SetReducedMotion(contextualHud.ReducedMotion);
            if (cockpitViewControls != null && contextualHud != null) cockpitViewControls.SetReducedMotion(contextualHud.ReducedMotion);
            if (cameraRig != null && rememberHudPreferences)
                cameraRig.SetCockpitFieldOfView(HudPreferences.LoadCockpitFieldOfView(cameraRig.cockpitFieldOfView));
            _savedFieldOfView = cameraRig != null ? cameraRig.cockpitFieldOfView : 0f;

            if (cockpit != null || cockpitInteraction != null || focusPanel != null || cockpitViewControls != null || aircraftLights != null)
            {
                _cockpit = new CockpitSession(_systems, _gate, cockpit, cockpitInteraction, focusPanel, cockpitViewControls, aircraftLights, cameraRig,
                    touchControls != null ? touchControls.Ownership : null, AirspeedMarkingsFor(aircraftConfiguration, config.warnings));
                _cockpit.PanelOpened += CloseFlightSettings; // one panel at a time in the free column
            }

            // Flight assistance: the same aircraft data as the simulation, the player's saved choices.
            var defaults = new FlightPreferences.Settings
            {
                Profile = assistConfiguration != null ? assistConfiguration.defaultProfile : FlightAssistProfile.Assisted,
                Sensitivity = assistConfiguration != null ? assistConfiguration.defaultSensitivity : ControlSensitivity.Medium,
                StandardAutoTrim = true,
                AssistedStallPrevention = true,
                Guidance = true,
            };
            _flight = rememberHudPreferences ? FlightPreferences.Load(defaults) : defaults;
            var envelope = aircraftConfiguration != null
                ? TelemetryMath.Envelope(aircraftConfiguration, config.warnings.neverExceedSpeedKnots)
                : AircraftEnvelope.PrototypeTrainer;
            _assistance = new FlightAssistController(assistConfiguration != null ? assistConfiguration.tuning : null, _flight.Profile);
            _assistance.SetEnvelope(envelope);
            _assistance.SetOptions(_flight.StandardAutoTrim, _flight.AssistedStallPrevention);
            _assistance.SetProfile(_flight.Profile, immediate: true);
            _guidance = new FlightGuidanceAdvisor(envelope);
            if (!_assistOverridden) _assist = _assistance;
            bool reduced = contextualHud != null && contextualHud.ReducedMotion;
            if (flightSettings != null) flightSettings.SetReducedMotion(reduced);
            if (guidanceLine != null) guidanceLine.SetReducedMotion(reduced);
        }

        /// <summary>Airspeed indicator markings from the aircraft (stall speeds from the flight model) and the warning limits.</summary>
        public static AirspeedMarkings AirspeedMarkingsFor(AircraftConfiguration aircraftConfiguration, FlightWarningSettings warnings)
        {
            var markings = AirspeedMarkings.Default;
            if (aircraftConfiguration != null)
            {
                markings.CleanStall = FlightUnits.MetresPerSecondToKnots(Aerodynamics.StallSpeed(aircraftConfiguration, 0f));
                markings.FlapStall = FlightUnits.MetresPerSecondToKnots(Aerodynamics.StallSpeed(aircraftConfiguration, 1f));
            }
            if (warnings != null)
            {
                markings.FlapExtended = warnings.flapExtendedSpeedKnots;
                markings.NormalOperating = warnings.normalOperatingSpeedKnots;
                markings.NeverExceed = warnings.neverExceedSpeedKnots;
            }
            return markings;
        }

        void OnDestroy()
        {
            _cockpit?.Dispose();
            SaveViewPreferences();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveViewPreferences();
        }

        void SaveViewPreferences()
        {
            if (!rememberHudPreferences || cameraRig == null || Mathf.Approximately(cameraRig.cockpitFieldOfView, _savedFieldOfView)) return;
            _savedFieldOfView = cameraRig.cockpitFieldOfView;
            HudPreferences.SaveCockpitFieldOfView(_savedFieldOfView);
        }

        void OnEnable()
        {
            if (hud != null)
            {
                hud.CameraToggleRequested += ToggleCamera;
                hud.ResetRequested += ResetFlight;
            }
            if (contextualHud != null)
            {
                contextualHud.ActionRequested += OnContextAction;
                contextualHud.SystemsToggleRequested += ToggleSystemsRail;
                contextualHud.ProfileCycleRequested += CycleHudProfile;
            }
            if (flightSettings != null)
            {
                flightSettings.ProfileChosen += SetAssistProfile;
                flightSettings.SensitivityChosen += SetSensitivity;
                flightSettings.OptionToggled += ToggleFlightOption;
                flightSettings.CloseRequested += CloseFlightSettings;
            }
        }

        void OnDisable()
        {
            if (hud != null)
            {
                hud.CameraToggleRequested -= ToggleCamera;
                hud.ResetRequested -= ResetFlight;
            }
            if (contextualHud != null)
            {
                contextualHud.ActionRequested -= OnContextAction;
                contextualHud.SystemsToggleRequested -= ToggleSystemsRail;
                contextualHud.ProfileCycleRequested -= CycleHudProfile;
            }
            if (flightSettings != null)
            {
                flightSettings.ProfileChosen -= SetAssistProfile;
                flightSettings.SensitivityChosen -= SetSensitivity;
                flightSettings.OptionToggled -= ToggleFlightOption;
                flightSettings.CloseRequested -= CloseFlightSettings;
            }
        }

        void FixedUpdate()
        {
            if (aircraft == null) return;
            float dt = Time.fixedDeltaTime;
            // The player's sensitivity, then the switches and levers (they apply to any control source), then assistance.
            var pilot = _systems.ApplyTo(ControlResponse.Apply(_controls.Read(dt), _flight.Sensitivity));
            _assistance.SetTrimInput(_devices.PitchTrimAxis());
            var commands = _assist.Apply(pilot, Telemetry, dt);
            aircraft.SetControls(commands);
            if (touchControls != null) touchControls.ReportFlightControls(commands);

            // Awareness runs at the physics rate on every telemetry sample, so it is frame-rate independent.
            var sample = Telemetry;
            _runway = EvaluateRunway(sample);
            _phase.Update(sample, _runway, dt);
            _warnings.Update(sample, dt);
        }

        void Update()
        {
            if (_devices.ResetPressedThisFrame()) ResetFlight();
            if (_devices.CameraTogglePressedThisFrame()) ToggleCamera();
            if (_devices.ParkingBrakeTogglePressedThisFrame()) ToggleParkingBrake();
            if (_devices.HudProfileCyclePressedThisFrame()) CycleHudProfile();
            if (_devices.AssistProfileCyclePressedThisFrame()) SetAssistProfile(FlightAssistProfiles.Next(_flight.Profile));
            if (_devices.InspectorTogglePressedThisFrame() && contextualHud != null && contextualHud.inspector != null)
                contextualHud.inspector.Toggle();
            HandleSystemKeys();

            var t = Telemetry;
            if (hud != null) hud.Present(t);
            if (touchControls != null)
            {
                touchControls.ShowThrottle(aircraft != null ? aircraft.Controls.Throttle : _router.Throttle);
                // Brakes follow weight on wheels (a physical fact), never the estimated phase.
                touchControls.SetOnGround(DisclosurePolicy.ShowBrakeControl(t));
            }
            UpdateDisclosure(t, Time.deltaTime);
            _cockpit?.Update(t, _runway, CurrentView);
            UpdateGuidance(t, Time.deltaTime);
            if (flightSettings != null && flightSettings.IsOpen) flightSettings.Present(CurrentFlightSettings);
        }

        /// <summary>Flying tips, only for the Assisted profile with tips on.</summary>
        void UpdateGuidance(in FlightTelemetry t, float deltaTime)
        {
            string tip = string.Empty;
            if (_flight.Profile == FlightAssistProfile.Assisted && _flight.Guidance)
            {
                _guidance.Update(new FlightGuidanceAdvisor.Input
                {
                    Telemetry = t,
                    Phase = _phase.Current.Phase,
                    WarningMask = _warnings.ActiveMask,
                    OnRunway = _runway.Valid && _runway.OnRunway,
                    ParkingBrakeSet = _systems.ParkingBrakeSet,
                }, deltaTime);
                tip = _guidance.CurrentText;
            }
            if (guidanceLine != null) guidanceLine.Present(tip);
        }

        /// <summary>
        /// Changes the assistance profile. In flight the behaviour blends over a couple of
        /// seconds and the trim is kept, so the aircraft never jolts. Remembered.
        /// </summary>
        public void SetAssistProfile(FlightAssistProfile profile)
        {
            if (profile == _flight.Profile) return;
            _flight.Profile = profile;
            _assistance.SetProfile(profile);
            SaveFlightSettings();
        }

        public void SetSensitivity(ControlSensitivity sensitivity)
        {
            if (sensitivity == _flight.Sensitivity) return;
            _flight.Sensitivity = sensitivity;
            SaveFlightSettings();
        }

        public void ToggleFlightOption(FlightSettingOption option)
        {
            switch (option)
            {
                case FlightSettingOption.StallPrevention: _flight.AssistedStallPrevention = !_flight.AssistedStallPrevention; break;
                case FlightSettingOption.Guidance: _flight.Guidance = !_flight.Guidance; break;
                case FlightSettingOption.AutoTrim: _flight.StandardAutoTrim = !_flight.StandardAutoTrim; break;
            }
            _assistance.SetOptions(_flight.StandardAutoTrim, _flight.AssistedStallPrevention);
            SaveFlightSettings();
        }

        /// <summary>Opens or closes the flight settings (ASSIST on the rail). Opening closes the cockpit panel.</summary>
        public void ToggleFlightSettings()
        {
            if (flightSettings == null) return;
            if (flightSettings.IsOpen)
            {
                flightSettings.Close();
                return;
            }
            _cockpit?.Close();
            flightSettings.Open(CurrentFlightSettings);
        }

        public void CloseFlightSettings()
        {
            if (flightSettings != null) flightSettings.Close();
        }

        void SaveFlightSettings()
        {
            if (rememberHudPreferences) FlightPreferences.Save(_flight);
        }

        void HandleSystemKeys()
        {
            int flapStep = _devices.FlapStepPressedThisFrame();
            if (flapStep != 0) RequestSystemChange(new SystemChange(AircraftSystem.Flaps, _systems.FlapDetent + flapStep));
            if (_devices.LandingLightTogglePressedThisFrame())
                RequestSystemChange(new SystemChange(AircraftSystem.LandingLight, _systems.LandingLightOn ? 0 : 1));
            if (_devices.NavigationLightsTogglePressedThisFrame())
                RequestSystemChange(new SystemChange(AircraftSystem.NavigationLights, _systems.NavigationLightsOn ? 0 : 1));
            if (cameraRig == null) return;
            if (_devices.RecentreViewPressedThisFrame()) cameraRig.RecentreCockpitLook();
            float zoom = _devices.FieldOfViewAxis();
            if (zoom != 0f) cameraRig.SetCockpitFieldOfView(cameraRig.cockpitFieldOfView + zoom * fieldOfViewKeyRate * Time.unscaledDeltaTime);
        }

        /// <summary>
        /// The one way to change an aircraft system, whatever asked for it. Applies
        /// the shared rules (parking brake only set on the ground; engine off in the
        /// air needs a confirmation, which only the cockpit selector can give).
        /// </summary>
        public SystemChangeGate.Outcome RequestSystemChange(in SystemChange change) =>
            _gate.Request(change, _systems, Telemetry.WeightOnWheels, Time.unscaledTime);

        void UpdateDisclosure(in FlightTelemetry t, float deltaTime)
        {
            // No estimate yet (first frame, or just after a reset): keep the HUD as it is rather
            // than flash the low-confidence fallback for a frame.
            if (_phase.TransitionTotal == 0) return;
            var view = CurrentView;
            var input = new DisclosureInput
            {
                Profile = _hudProfile,
                View = view,
                Phase = _phase.Current,
                WarningMask = _warnings.ActiveMask,
                OnGround = t.WeightOnWheels,
                BankDegrees = t.BankDegrees,
                HasRunway = _runway.Valid,
                DistanceToRunwayNm = _runway.DistanceToRunwayMetres / MetresPerNauticalMile,
                Systems = new AircraftSystemsState
                {
                    HasParkingBrake = true,
                    ParkingBrakeSet = _systems.ParkingBrakeSet,
                    HasFlaps = true,
                    FlapsExtended = _systems.FlapDetent > 0,
                },
                HasAssistanceSettings = flightSettings != null,
            };
            _disclosure.Update(input, deltaTime);

            if (contextualHud == null) return;
            contextualHud.Present(t, _runway, _disclosure, _warnings, _hudProfile);
            if (contextualHud.inspector != null) contextualHud.inspector.Present(_phase, _warnings, _disclosure, _hudProfile, view);
        }

        RunwayRelation EvaluateRunway(in FlightTelemetry t)
        {
            if (!homeRunway.IsDefined || aircraft == null || aircraft.Body == null) return default;
            var body = aircraft.Body;
            Vector3 horizontal = body.linearVelocity;
            horizontal.y = 0f;
            float track = horizontal.magnitude >= TrackFromVelocityMinSpeed ? RunwayGeometry.HeadingOf(horizontal) : t.HeadingDegrees;
            return RunwayGeometry.Evaluate(homeRunway, body.position, track, _disclosure.Settings.aimPointMetres);
        }

        /// <summary>
        /// Aircraft back to the runway start at rest; throttle idle; systems in their
        /// start state (engine running, flaps up, brake off); awareness, HUD choices
        /// and cockpit panels for the flight cleared. The cockpit view direction is kept.
        /// </summary>
        public void ResetFlight()
        {
            if (aircraft != null) aircraft.ResetToStart();
            _controls.ResetControls();
            _systems.ResetToStartState();
            _gate.Clear();
            _cockpit?.OnReset();
            _assistance.Reset(); // trim neutral, nothing held; the player's profile is kept
            _guidance.Reset();
            if (telemetry != null) telemetry.Refresh(); // nobody reads the pre-reset state
            _phase.Reset(assumeOnGround: true);
            _warnings.Reset();
            _disclosure.Reset();
            _runway = default;
            if (cameraRig != null) cameraRig.SnapToTarget();
        }

        /// <summary>Changes only what the HUD shows; flight controls are never affected.</summary>
        public void SetHudProfile(HudProfile profile)
        {
            if (profile == _hudProfile) return;
            _hudProfile = profile;
            if (rememberHudPreferences) HudPreferences.SaveProfile(profile);
        }

        /// <summary>Smart, then Detailed, then Cinematic.</summary>
        public void CycleHudProfile() => SetHudProfile(NextProfile(_hudProfile));

        public static HudProfile NextProfile(HudProfile profile) =>
            profile == HudProfile.Smart ? HudProfile.Detailed : (profile == HudProfile.Detailed ? HudProfile.Cinematic : HudProfile.Smart);

        /// <summary>Sets the parking brake on the ground, or releases it (anywhere).</summary>
        public void ToggleParkingBrake() =>
            RequestSystemChange(new SystemChange(AircraftSystem.ParkingBrake, _systems.ParkingBrakeSet ? 0 : 1));

        public void ToggleSystemsRail() => _disclosure.ToggleRailExpanded();

        void OnContextAction(ContextAction action)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: ToggleParkingBrake(); break;
                case ContextAction.ApproachGuide: _disclosure.ToggleGuide(HudElement.ApproachGuide); break;
                case ContextAction.Navigation: _disclosure.ToggleGuide(HudElement.NavigationGuide); break;
                case ContextAction.Assistance: ToggleFlightSettings(); break;
                case ContextAction.Flaps:
                    // The same touch-friendly selector as the cockpit lever; without one, step down a detent (wrapping to up).
                    if (_cockpit != null && focusPanel != null) _cockpit.OpenSystem(AircraftSystem.Flaps);
                    else RequestSystemChange(new SystemChange(AircraftSystem.Flaps, (_systems.FlapDetent + 1) % _systems.FlapDetentCount));
                    break;
            }
        }

        void ToggleCamera()
        {
            if (cameraRig != null) cameraRig.ToggleMode();
            SaveViewPreferences();
        }
    }
}
