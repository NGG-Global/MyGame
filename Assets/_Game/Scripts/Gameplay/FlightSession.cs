using System;
using OpenSkies.Assistance;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.Cockpit;
using OpenSkies.FlightInput;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Gestures;
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
        [Tooltip("Remember the chase or cockpit view between sessions (PlayerPrefs). The prototype scene turns it on.")]
        public bool rememberCameraMode;

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
        [Tooltip("The airframe's moving parts (propeller, control surfaces, flaps, nosewheel); optional.")]
        public AirframeView airframeView;
        [Tooltip("Cockpit field of view change per second while the keyboard zoom keys are held, degrees.")]
        public float fieldOfViewKeyRate = 20f;

        [Header("Gesture-first interface")]
        [Tooltip("The single entry point for touches no control took: routes them to gestures or the cockpit.")]
        public InteractionSurface interactionSurface;
        [Tooltip("The systems handle and arc, the flap lever, the HUD band, the explanation plate.")]
        public GestureHud gestureHud;
        [Tooltip("The feel of the gesture language (thresholds, arc, lever, sheet, stations). The defaults in code are used when empty.")]
        public InteractionConfiguration interactionConfiguration;
        [Tooltip("Development: the interface at start. F4 or a tap on the gesture inspector switches; remembered with the HUD preferences.")]
        public InteractionMode interactionMode = InteractionMode.GestureFirst;
        [Tooltip("Development comparison: the M13/M14 pull gestures on the systems handle instead of the tap-first wheel (F4 cycles: tap wheel, pull, legacy rail).")]
        public bool pullGestureComparison;
        [Tooltip("Development: append each completed or cancelled gesture to interaction-diary.csv in the app's data folder (development builds only).")]
        public bool recordInteractionDiary;
        public GestureSession.Settings gestureSettings = new GestureSession.Settings();

        const float TrackFromVelocityMinSpeed = 2.5f; // m/s; slower than this, the heading stands in for the track
        const float MetresPerNauticalMile = 1852f;

        IFlightControlSource _controls;
        DisclosureCoordinator _coordinator;
        int _flightSettingsLayer = DisclosureCoordinator.None;
        GestureSession _gestures;
        GestureHints _hints;
        bool _hintsEnabled = true;
        InteractionDiary _diary;
        InstrumentManipulation _manipulation;
        FlapsLeverInteraction _leverInteraction;
        readonly InstrumentSettings _instruments = new InstrumentSettings();
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
        AircraftEnvelope _envelope = AircraftEnvelope.PrototypeTrainer;
        FlightControls _pilot = FlightControls.Neutral;

        // Navigation: the airfield by default, or a mission's checkpoint.
        NavigationTarget _navigationTarget;
        bool _hasMissionTarget;
        NavigationRelation _navigation;
        int _missionGuides, _missionActions;

        // Where RESET puts the aircraft: the runway start unless a mode sets a checkpoint.
        bool _hasResetStart;
        AircraftStartState _resetStart;
        int _resetFlapDetent;

        /// <summary>Raised after the player's RESET has put the aircraft back (runway start or the mode's checkpoint).</summary>
        public event Action ResetPerformed;
        /// <summary>Raised after every restart of the flight (RESET, a mode's start): the aircraft was placed, not flown there.</summary>
        public event Action FlightRestarted;
        /// <summary>A switch or lever actually moved (applied through the gate), whatever asked for it.</summary>
        public event Action<SystemChange> SystemChanged;

        public FlightTelemetry Telemetry => telemetry != null ? telemetry.Current : default;

        /// <summary>The pilot's commands of the last physics step: after sensitivity and the systems, before assistance.</summary>
        public FlightControls PilotControls => _pilot;
        /// <summary>The aircraft's limits from its data (stall speeds for coaching and scoring).</summary>
        public AircraftEnvelope Envelope => _envelope;
        public KeyboardGamepadInputProvider Devices => _devices;
        /// <summary>Where the navigation guide points now, and how far.</summary>
        public NavigationRelation Navigation => _navigation;
        /// <summary>A mission's coaching replaces the Assisted flying tips while it is on screen.</summary>
        public bool SuppressFlyingTips { get; set; }
        /// <summary>Aircraft world position (physics pose), m.</summary>
        public Vector3 AircraftPosition => aircraft != null && aircraft.Body != null ? aircraft.Body.position : Vector3.zero;

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
        /// <summary>
        /// The one coordinator of expanded transient interfaces (the arc, the panels, the pause
        /// sheet, the settings): who is open, which tap collapses what. The surface consumes the
        /// collapsing tap. Eligibility stays with <see cref="Disclosure"/> (the flight context).
        /// </summary>
        public DisclosureCoordinator Coordinator => _coordinator;
        /// <summary>The authoritative switch and lever positions of the aircraft.</summary>
        public AircraftSystems Systems => _systems;
        public CockpitSession Cockpit => _cockpit;
        /// <summary>The gesture-first interaction (null without an interaction surface).</summary>
        public GestureSession Gestures => _gestures;
        /// <summary>Direct manipulation of the enlarged cockpit instrument (null without a surface and a focus panel).</summary>
        public InstrumentManipulation Manipulation => _manipulation;
        /// <summary>The one flap lever interaction shared by the arc and the cockpit lever.</summary>
        public FlapsLeverInteraction LeverInteraction => _leverInteraction;
        /// <summary>Contextual gesture cues (set by the game modes, which own the player's progress).</summary>
        public GestureHints Hints => _hints;
        /// <summary>Development counts of gestures for comparing interaction models (null without a surface).</summary>
        public InteractionDiary Diary => _diary;

        /// <summary>The game modes give the session the hint policy (it needs the player's progress); the sessions offer cues through it.</summary>
        public void SetGestureHints(GestureHints hints)
        {
            _hints = hints;
            _hints?.SetEnabled(_hintsEnabled); // the options may have been applied before the modes handed the policy over
            _gestures?.SetHints(hints);
            _cockpit?.SetHints(hints);
            _manipulation?.SetHints(hints);
        }
        /// <summary>The pilot's instrument settings (heading bug, altimeter subscale): one source of truth, never flight input.</summary>
        public InstrumentSettings Instruments => _instruments;
        /// <summary>Altitude as the altimeter shows it with its subscale, ft (the HUD shows the same, so the two never disagree).</summary>
        public float IndicatedAltitudeFeet => Telemetry.AltitudeFeet + InstrumentSettings.BaroCorrectionFeet(_instruments.BaroHectopascals);

        /// <summary>A deliberate two-finger tap on the world or the cockpit: the player wants the pause menu.</summary>
        public event Action PauseRequested;
        /// <summary>An instrument setting changed (heading bug, subscale).</summary>
        public event Action<InstrumentSetting> InstrumentSettingChanged;

        /// <summary>The one way to change an instrument setting, whatever asked for it. Returns whether it changed.</summary>
        public bool RequestInstrumentSetting(InstrumentSetting setting, int value)
        {
            bool changed = _instruments.Set(setting, value);
            if (changed) InstrumentSettingChanged?.Invoke(setting);
            return changed;
        }

        void RaisePauseRequested() => PauseRequested?.Invoke();
        public InteractionMode CurrentInteractionMode =>
            _gestures == null ? InteractionMode.Legacy : (_gestures.GestureFirst ? InteractionMode.GestureFirst : InteractionMode.Legacy);
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
            if (cameraRig != null && rememberCameraMode) cameraRig.SetMode(HudPreferences.LoadCameraMode(cameraRig.mode));
            _savedFieldOfView = cameraRig != null ? cameraRig.cockpitFieldOfView : 0f;

            // One coordinator for everything the player expands; the surface owns it when there is one, so it can consume outside taps.
            _coordinator = interactionSurface != null ? interactionSurface.Disclosure : new DisclosureCoordinator();
            _coordinator.TransitionSeconds = contextualHud != null && contextualHud.ReducedMotion ? 0f : HudStyle.FadeSeconds;
            if (flightSettings != null)
                _flightSettingsLayer = _coordinator.Register("Flight settings", IsOnFlightSettings, CollapseFlightSettings);

            // The flap lever interaction is shared by the systems arc and the cockpit's lever: one model, one view, one commit path.
            _leverInteraction = new FlapsLeverInteraction(this, gestureHud != null ? gestureHud.lever : null, gestureHud);
            if (cockpit != null || cockpitInteraction != null || focusPanel != null || cockpitViewControls != null || aircraftLights != null)
            {
                _cockpit = new CockpitSession(_systems, _gate, cockpit, cockpitInteraction, focusPanel, cockpitViewControls, aircraftLights, cameraRig,
                    touchControls != null ? touchControls.Ownership : null, AirspeedMarkingsFor(aircraftConfiguration, config.warnings), _instruments, _leverInteraction);
                _cockpit.SetDisclosure(_coordinator);
                _cockpit.screenHeightOverride = interactionSurface != null ? interactionSurface.screenHeightOverride : 0f;
                _cockpit.PanelOpened += CloseFlightSettings; // one panel at a time in the free column
                _cockpit.PauseRequested += RaisePauseRequested;
                _cockpit.SystemChanged += OnCockpitSystemChanged;
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
            _envelope = envelope;
            _assistance = new FlightAssistController(assistConfiguration != null ? assistConfiguration.tuning : null, _flight.Profile);
            _assistance.SetEnvelope(envelope);
            _assistance.SetOptions(_flight.StandardAutoTrim, _flight.AssistedStallPrevention);
            _assistance.SetProfile(_flight.Profile, immediate: true);
            _guidance = new FlightGuidanceAdvisor(envelope);
            if (!_assistOverridden) _assist = _assistance;
            bool reduced = contextualHud != null && contextualHud.ReducedMotion;
            if (flightSettings != null) flightSettings.SetReducedMotion(reduced);
            if (guidanceLine != null) guidanceLine.SetReducedMotion(reduced);

            ApplyInteractionConfiguration();

            // Gesture-first interaction: one surface for every touch nothing else took, the cockpit routed through it.
            if (interactionSurface != null)
            {
                var ownership = touchControls != null ? touchControls.Ownership : new PointerOwnership();
                interactionSurface.Initialize(ownership);
                if (cockpitInteraction != null) interactionSurface.SetCockpitTarget(cockpitInteraction);
                var explanations = new HudExplanations(envelope, config.warnings.flapExtendedSpeedKnots, _systems.MaxFlapDegrees);
                _gestures = new GestureSession(this, interactionSurface, gestureHud, contextualHud, cameraRig, ownership, explanations, _systems, gestureSettings,
                    _leverInteraction);
                _gestures.SetReducedMotion(reduced);
                if (rememberHudPreferences) interactionMode = HudPreferences.LoadInteractionMode(interactionMode);
                _gestures.SetGestureFirst(interactionMode == InteractionMode.GestureFirst);
                // The pull gestures remain only as a development comparison: never in a release build.
                _gestures.SetPullComparisonAllowed(Debug.isDebugBuild);
                if (rememberHudPreferences) pullGestureComparison = HudPreferences.LoadPullComparison(pullGestureComparison);
                _gestures.SetPullGestures(pullGestureComparison);
                _gestures.ModeChanged += OnInteractionModeChanged;
                _gestures.PullGesturesChanged += OnPullComparisonChanged;
                _gestures.PauseRequested += RaisePauseRequested;
                _diary = new InteractionDiary { RecordToFile = recordInteractionDiary };
                interactionSurface.GestureRaised += OnDiaryGesture;
                _gestures.SetDiary(_diary);
                if (flightSettings != null)
                {
                    // ASSIST's options: the settings card itself first (the action), then the profiles.
                    var profileLabels = new string[FlightAssistProfiles.Count + 1];
                    profileLabels[0] = "SETTINGS";
                    for (int i = 0; i < FlightAssistProfiles.Count; i++) profileLabels[i + 1] = FlightAssistProfiles.Label((FlightAssistProfile)i);
                    _gestures.RegisterSubOptions(ContextAction.Assistance, profileLabels, () => (int)_flight.Profile + 1,
                        i => { if (i == 0) ToggleFlightSettings(); else if (FlightAssistProfiles.IsValid(i - 1)) SetAssistProfile((FlightAssistProfile)(i - 1)); });
                }
                if (focusPanel != null)
                {
                    _manipulation = new InstrumentManipulation(this, interactionSurface, _cockpit, focusPanel, _instruments, gestureHud, explanations);
                    _manipulation.SetReducedMotion(reduced);
                }
            }
            else if (contextualHud != null) contextualHud.SetLegacyRailEnabled(true);
        }

        /// <summary>A flick in the cockpit applied a change through the shared gate: tell the listeners as for any other change.</summary>
        void OnCockpitSystemChanged(SystemChange change) => SystemChanged?.Invoke(change);

        void OnDiaryGesture(InteractionRoute route, GestureEvent e, int target) => _diary?.Record(route, e);

        /// <summary>The asset's settings onto the surface and the views (before the sessions read them). Views keep their own values without an asset.</summary>
        void ApplyInteractionConfiguration()
        {
            var c = interactionConfiguration;
            if (c == null) return;
            if (interactionSurface != null) interactionSurface.settings = c.gestures;
            if (gestureHud != null)
            {
                if (gestureHud.arc != null) gestureHud.arc.settings = c.arc;
                if (gestureHud.lever != null) gestureHud.lever.settings = c.lever;
                if (gestureHud.ladder != null) gestureHud.ladder.settings = c.density;
            }
            if (focusPanel != null)
            {
                focusPanel.rotary = c.rotary;
                focusPanel.mapView = c.mapView;
            }
        }

        void OnInteractionModeChanged(InteractionMode mode)
        {
            interactionMode = mode;
            if (rememberHudPreferences) HudPreferences.SaveInteractionMode(mode);
        }

        /// <summary>Development: the gesture-first interface or the legacy rail. Presentation only; flight input is unaffected.</summary>
        public void SetInteractionMode(InteractionMode mode) => _gestures?.SetGestureFirst(mode == InteractionMode.GestureFirst);

        void OnPullComparisonChanged(bool on)
        {
            pullGestureComparison = on;
            if (rememberHudPreferences) HudPreferences.SavePullComparison(on);
        }

        /// <summary>Development comparison: the M13/M14 pull gestures on the handle instead of the tap-first wheel. Presentation only.</summary>
        public void SetPullGestureComparison(bool on)
        {
            pullGestureComparison = on;
            _gestures?.SetPullGestures(on);
        }

        /// <summary>
        /// The player's gesture options: speed scales the holds, rests and pulls of the
        /// tuning asset (never the asset itself), the look-around sensitivity scales the
        /// chase free look, and the hints switch the contextual cues. Presentation only.
        /// </summary>
        public void ApplyGestureOptions(OpenSkies.Options.GestureSpeed speed, OpenSkies.Options.CameraGestureSensitivity camera, bool hints)
        {
            if (interactionSurface != null)
            {
                var baseSettings = interactionConfiguration != null ? interactionConfiguration.gestures : interactionSurface.settings;
                var tuned = (baseSettings ?? new GestureSettings()).Sanitized();
                float time = OpenSkies.Options.GameOptionsRules.GestureTimeScale(speed);
                float distance = OpenSkies.Options.GameOptionsRules.GestureDistanceScale(speed);
                tuned.longPressSeconds *= time;
                tuned.dwellSeconds *= time;
                tuned.pullPeekDistance *= distance;
                tuned.pullRevealDistance *= distance;
                interactionSurface.ApplySettings(tuned.Sanitized());
            }
            _gestures?.SetLookScale(OpenSkies.Options.GameOptionsRules.CameraGestureScale(camera));
            _hintsEnabled = hints;
            _hints?.SetEnabled(hints);
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
            if (_cockpit != null)
            {
                _cockpit.PauseRequested -= RaisePauseRequested;
                _cockpit.SystemChanged -= OnCockpitSystemChanged;
                _cockpit.Dispose();
            }
            _manipulation?.Dispose();
            if (interactionSurface != null) interactionSurface.GestureRaised -= OnDiaryGesture;
            _diary?.Flush();
            if (_gestures != null)
            {
                _gestures.ModeChanged -= OnInteractionModeChanged;
                _gestures.PullGesturesChanged -= OnPullComparisonChanged;
                _gestures.PauseRequested -= RaisePauseRequested;
                _gestures.Dispose();
            }
            SaveViewPreferences();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveViewPreferences();
            if (paused) _diary?.Flush();
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
            _pilot = pilot;
            _assistance.SetTrimInput(_devices.PitchTrimAxis());
            var commands = _assist.Apply(pilot, Telemetry, dt);
            aircraft.SetControls(commands);
            if (touchControls != null) touchControls.ReportFlightControls(commands);

            // Awareness runs at the physics rate on every telemetry sample, so it is frame-rate independent.
            var sample = Telemetry;
            _runway = EvaluateRunway(sample);
            _navigation = EvaluateNavigation(sample);
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
            if (_devices.InteractionModeTogglePressedThisFrame()) _gestures?.ToggleMode();
            HandleSystemKeys();

            var t = Telemetry;
            if (airframeView != null && aircraft != null) airframeView.Present(t, aircraft.Controls, Time.deltaTime);
            if (hud != null)
            {
                hud.Present(t);
                hud.ShowView(CurrentView == ViewMode.Cockpit);
            }
            if (touchControls != null)
            {
                touchControls.ShowThrottle(aircraft != null ? aircraft.Controls.Throttle : _router.Throttle);
                // Brakes follow weight on wheels (a physical fact), never the estimated phase.
                touchControls.SetOnGround(DisclosurePolicy.ShowBrakeControl(t));
            }
            // Gestures first: they may preview a HUD profile for this frame's disclosure.
            _gestures?.Update(CurrentView, Time.timeScale <= 0f, _hudProfile, _disclosure, _systems);
            _hints?.Tick(Time.unscaledTime);
            UpdateDisclosure(t, Time.deltaTime);
            _cockpit?.Update(t, _runway, CurrentView);
            _manipulation?.Update(); // after the cockpit drew the committed values: the preview goes over them
            UpdateGuidance(t, Time.deltaTime);
            if (flightSettings != null && flightSettings.IsOpen) flightSettings.Present(CurrentFlightSettings);
        }

        /// <summary>Flying tips, only for the Assisted profile with tips on.</summary>
        void UpdateGuidance(in FlightTelemetry t, float deltaTime)
        {
            string tip = string.Empty;
            if (_flight.Profile == FlightAssistProfile.Assisted && _flight.Guidance && !SuppressFlyingTips)
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

        /// <summary>
        /// Opens or closes the flight settings (ASSIST on the rail or the arc: the trigger again
        /// closes). Opening closes the cockpit panel. A tap outside the card closes it too, through
        /// the coordinator; there is no close button.
        /// </summary>
        public void ToggleFlightSettings()
        {
            if (flightSettings == null) return;
            if (flightSettings.IsOpen)
            {
                CloseFlightSettings();
                return;
            }
            _cockpit?.Close();
            flightSettings.Open(CurrentFlightSettings);
            _coordinator.Open(_flightSettingsLayer);
        }

        public void CloseFlightSettings()
        {
            if (flightSettings == null) return;
            flightSettings.Close();
            _coordinator?.NotifyClosed(_flightSettingsLayer);
        }

        bool IsOnFlightSettings(Vector2 screenPoint) => flightSettings != null && flightSettings.IsOpen && ScreenRects.Contains(flightSettings.card, screenPoint);

        void CollapseFlightSettings(DisclosureCollapseReason reason) => CloseFlightSettings();

        /// <summary>Every expanded interface of the flight folds (a modal takes over, a mode ends). Nothing is applied.</summary>
        public void CollapseTransients(DisclosureCollapseReason reason) => _coordinator?.CollapseAll(reason);

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
        public SystemChangeGate.Outcome RequestSystemChange(in SystemChange change)
        {
            var outcome = _gate.Request(change, _systems, Telemetry.WeightOnWheels, Time.unscaledTime);
            if (outcome == SystemChangeGate.Outcome.Applied) SystemChanged?.Invoke(change);
            return outcome;
        }

        /// <summary>A hazardous change waiting for its confirmation (within the gate's window), if any.</summary>
        public bool TryGetPendingSystemChange(out SystemChange change) => _gate.HasPending(Time.unscaledTime, out change);

        /// <summary>Drops a change waiting for its confirmation (the selector or the wheel that asked for it closed). Nothing is applied.</summary>
        public void ClearPendingSystemChange() => _gate.Clear();

        /// <summary>
        /// The player's motion preference for every panel this session owns (HUD fades,
        /// cockpit panel, view controls, flight settings, flying tips). Presentation only.
        /// </summary>
        public void ApplyMotionPreference(MotionPreference preference)
        {
            if (contextualHud == null) return;
            contextualHud.SetMotionPreference(preference);
            bool reduced = contextualHud.ReducedMotion;
            if (focusPanel != null) focusPanel.SetReducedMotion(reduced);
            if (cockpitViewControls != null) cockpitViewControls.SetReducedMotion(reduced);
            if (flightSettings != null) flightSettings.SetReducedMotion(reduced);
            if (guidanceLine != null) guidanceLine.SetReducedMotion(reduced);
            _gestures?.SetReducedMotion(reduced);
            _manipulation?.SetReducedMotion(reduced);
            if (_coordinator != null) _coordinator.TransitionSeconds = reduced ? 0f : HudStyle.FadeSeconds;
        }

        /// <summary>The player's HUD opacity on the HUD this session owns. Flight controls stay findable (see TouchFlightControls).</summary>
        public void ApplyHudOpacity(float opacity)
        {
            if (contextualHud != null) contextualHud.SetOpacity(opacity);
            if (gestureHud != null) gestureHud.SetOpacity(opacity);
            if (hud != null) hud.SetOpacity(opacity);
            if (touchControls != null) touchControls.SetOpacityScale(opacity);
            if (cockpitViewControls != null) cockpitViewControls.SetOpacity(opacity);
        }

        void UpdateDisclosure(in FlightTelemetry t, float deltaTime)
        {
            // No estimate yet (first frame, or just after a reset): keep the HUD as it is rather
            // than flash the low-confidence fallback for a frame.
            if (_phase.TransitionTotal == 0) return;
            var view = CurrentView;
            // While the HUD band is pulled, disclosure shows the previewed profile; the committed one is untouched.
            var profile = _gestures != null && _gestures.HasPreviewProfile ? _gestures.PreviewProfile : _hudProfile;
            var input = new DisclosureInput
            {
                Profile = profile,
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
                HasNavigationTarget = _navigation.Valid,
                MissionGuides = _missionGuides,
                MissionActions = _missionActions,
            };
            _disclosure.Update(input, deltaTime);

            if (contextualHud == null) return;
            // The HUD shows the altitude the altimeter shows (subscale applied): one reading everywhere.
            var shown = t;
            shown.AltitudeFeet = SoftLogic.Finite(t.AltitudeFeet, 0f) + InstrumentSettings.BaroCorrectionFeet(_instruments.BaroHectopascals);
            contextualHud.Present(shown, _runway, _navigation, _disclosure, _warnings, _hudProfile);
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

        NavigationRelation EvaluateNavigation(in FlightTelemetry t)
        {
            if (_hasMissionTarget) return NavigationMath.Evaluate(_navigationTarget, AircraftPosition, t.HeadingDegrees);
            return NavigationMath.FromRunway(_runway, t.HeadingDegrees);
        }

        /// <summary>Points the navigation guide at a mission checkpoint instead of the airfield.</summary>
        public void SetNavigationTarget(in NavigationTarget target)
        {
            _navigationTarget = target;
            _hasMissionTarget = target.IsDefined;
        }

        /// <summary>The navigation guide points at the airfield again.</summary>
        public void ClearNavigationTarget() => _hasMissionTarget = false;

        /// <summary>
        /// Guides (HudElement bits) and rail actions (ContextAction bits) a mission needs on
        /// screen. They go through the same disclosure rules: the player can still hide a
        /// guide, warnings still win, flight controls are never affected.
        /// </summary>
        public void SetMissionGuidance(int guides, int actions)
        {
            _missionGuides = guides;
            _missionActions = actions;
        }

        /// <summary>Where RESET puts the aircraft from now on (a mode's start or checkpoint).</summary>
        public void SetResetStart(in AircraftStartState state, int flapDetent)
        {
            _resetStart = state;
            _resetFlapDetent = flapDetent;
            _hasResetStart = true;
        }

        /// <summary>RESET goes back to the runway start.</summary>
        public void ClearResetStart() => _hasResetStart = false;

        /// <summary>
        /// The player's RESET: back to the runway start, or to the mode's checkpoint, with
        /// everything for the flight cleared (see <see cref="Restart"/>). Then
        /// <see cref="ResetPerformed"/> tells the mode.
        /// </summary>
        public void ResetFlight()
        {
            if (_hasResetStart) Restart(false, _resetStart, _resetFlapDetent);
            else Restart(true, default, 0);
            ResetPerformed?.Invoke();
        }

        /// <summary>A mode begins on the runway (no <see cref="ResetPerformed"/>).</summary>
        public void StartOnRunway() => Restart(true, default, 0);

        /// <summary>A mode begins in the air or on final approach (no <see cref="ResetPerformed"/>).</summary>
        public void StartFlight(in AircraftStartState state, int flapDetent)
        {
            if (state.Airborne) Restart(false, state, flapDetent);
            else Restart(true, default, 0);
        }

        /// <summary>
        /// Aircraft at the runway start at rest with the throttle idle, or in the given
        /// airborne state with its power and flaps; systems in their start state (engine
        /// running, brake off, flaps up or as given); awareness, HUD choices and cockpit
        /// panels for the flight cleared. The cockpit view direction is kept.
        /// </summary>
        void Restart(bool onRunway, in AircraftStartState state, int flapDetent)
        {
            if (aircraft != null)
            {
                if (onRunway) aircraft.ResetToStart();
                else aircraft.ResetTo(state);
            }
            _controls.ResetControls();
            if (!onRunway) _router.SetThrottle(state.Throttle); // the lever where the engine is set
            _pilot = FlightControls.Neutral;
            _systems.ResetToStartState(onRunway ? 0 : flapDetent);
            _gate.Clear();
            _cockpit?.OnReset();
            _assistance.Reset(); // trim neutral, nothing held; the player's profile is kept
            _guidance.Reset();
            if (telemetry != null) telemetry.Refresh(); // nobody reads the pre-reset state
            _phase.Reset(assumeOnGround: onRunway);
            _warnings.Reset();
            _disclosure.Reset();
            _runway = default;
            _navigation = default;
            _gestures?.OnReset();
            _manipulation?.OnReset();
            _leverInteraction?.Cancel();
            _coordinator?.Reset(); // whatever was expanded folds; nothing waits for a confirmation
            if (cameraRig != null) cameraRig.SnapToTarget();
            FlightRestarted?.Invoke();
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

        void OnContextAction(ContextAction action) => PerformContextAction(action);

        /// <summary>A contextual action, from the rail or the arc: a system change through the gate, a guide, or the settings.</summary>
        public void PerformContextAction(ContextAction action)
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

        public void ToggleCamera()
        {
            if (cameraRig != null) cameraRig.ToggleMode();
            SaveViewPreferences();
            if (cameraRig != null && rememberCameraMode) HudPreferences.SaveCameraMode(cameraRig.mode);
        }
    }
}
