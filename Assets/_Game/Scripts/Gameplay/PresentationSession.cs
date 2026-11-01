using OpenSkies.Audio;
using OpenSkies.Awareness;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Options;
using OpenSkies.Platform;
using OpenSkies.UI;
using OpenSkies.World;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Presentation around the flight: the player's options (interface scale, HUD
    /// opacity, motion, haptics, volumes, graphics, frame rate) and the settings
    /// screen, the sound of the flight and its alerts, haptic pulses, and pause and
    /// resume of all of it. It reads the flight session and the game modes; it never
    /// produces flight controls, never changes the physics step and never changes the
    /// aircraft. Graphics and frame-rate choices change rendering only.
    ///
    /// Runs after the flight session, the game modes and the telemetry sample, so it
    /// sees this step's state and this frame's pause.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class PresentationSession : MonoBehaviour
    {
        public FlightSession flight;
        public GameModeSession modes;
        public AudioManager audioManager;
        public SettingsScreen settings;
        [Tooltip("The HUD canvas scaler: the interface scale is applied here.")]
        public CanvasScaler canvasScaler;
        [Tooltip("The scene's sun: its shadows follow the graphics preset.")]
        public Light sun;

        [Header("Session")]
        [Tooltip("Keep the options between sessions (PlayerPrefs). Tests keep them in memory.")]
        public bool rememberOptions = true;
        [Tooltip("Apply the graphics preset (URP settings, LOD bias, shadows). Tests that do not need it may turn it off.")]
        public bool applyGraphics = true;
        [Tooltip("Apply the frame-rate preference (Application.targetFrameRate).")]
        public bool applyFrameRate = true;
        [Tooltip("Within this distance of the shoreline the surf is fully in the ambience, m.")]
        public float coastAmbienceDistance = 1500f;

        IOptionsStore _store;
        IHapticDevice _hapticDevice;
        GameOptions _options;
        GraphicsQuality _graphics;
        HapticFeedback _haptics;
        UiScaleMath.Layout _layout;
        readonly TouchdownDetector _touchdown = new TouchdownDetector();
        GroundAudioSettings _ground;
        int _stallBit, _redBits, _cautionBits, _previousRed;
        float _effectiveUiScale = 1f;
        Rect _lastSafeArea;
        Vector2Int _lastScreen;
        bool _initialized, _started, _appPaused, _dirty, _subscribed;

        public GameOptions Options => _options;
        public float EffectiveUiScale => _effectiveUiScale;
        public HapticFeedback Haptics => _haptics;
        public GraphicsQuality Graphics => _graphics;
        public bool OutputPaused => audioManager != null && audioManager.Paused;

        /// <summary>Use another options store (tests). Call before the component wakes, or call <see cref="ReloadOptions"/>.</summary>
        public void UseOptionsStore(IOptionsStore store) => _store = store;

        /// <summary>Use another haptic device (tests). Call before the component wakes.</summary>
        public void UseHapticDevice(IHapticDevice device) => _hapticDevice = device;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (_store == null) _store = rememberOptions ? (IOptionsStore)new PlayerPrefsOptionsStore() : new MemoryOptionsStore();
            _options = GameOptionsRules.Sanitize(_store.Load(GameOptionsRules.Defaults));
            _haptics = new HapticFeedback(_hapticDevice ?? HapticFeedback.CreateDevice());
            var camera = flight != null && flight.cameraRig != null ? flight.cameraRig.ViewCamera : null;
            _graphics = new GraphicsQuality(sun, camera);
            _layout = UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop);
            if (audioManager != null) audioManager.EnsureInitialized();
            _ground = audioManager != null ? audioManager.Configuration.ground : new GroundAudioSettings();

            _stallBit = 1 << (int)FlightWarning.Stall;
            for (int w = 0; w < FlightWarnings.Count; w++)
            {
                var warning = (FlightWarning)w;
                if (FlightWarnings.Severity(warning) == WarningSeverity.Warning) _redBits |= 1 << w;
                else _cautionBits |= 1 << w;
            }
        }

        /// <summary>After every Awake and the game modes' Start: everything the options touch exists.</summary>
        void Start()
        {
            EnsureInitialized();
            Subscribe();
            ApplyAll();
            _started = true;
        }

        void OnEnable()
        {
            HudButton.AnyClicked += OnUiClicked;
            if (_started) Subscribe();
        }

        void OnDisable()
        {
            HudButton.AnyClicked -= OnUiClicked;
            Unsubscribe();
            SaveIfDirty();
        }

        void OnDestroy()
        {
            _graphics?.Dispose(); // the project's pipeline asset and the sun's shadows come back
        }

        void OnApplicationPause(bool paused)
        {
            _appPaused = paused;
            if (paused)
            {
                if (audioManager != null) audioManager.SetPaused(true);
                SaveIfDirty();
            }
        }

        void OnApplicationQuit() => SaveIfDirty();

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            if (flight != null)
            {
                flight.FlightRestarted += OnFlightRestarted;
                flight.SystemChanged += OnSystemChanged;
            }
            if (modes != null)
            {
                modes.SettingsRequested += OpenSettings;
                modes.BackRequested += CloseSettings;
            }
            if (settings != null)
            {
                settings.OptionChosen += OnOptionChosen;
                settings.Stepped += OnStepped;
                settings.CloseRequested += CloseSettings;
            }
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            if (flight != null)
            {
                flight.FlightRestarted -= OnFlightRestarted;
                flight.SystemChanged -= OnSystemChanged;
            }
            if (modes != null)
            {
                modes.SettingsRequested -= OpenSettings;
                modes.BackRequested -= CloseSettings;
            }
            if (settings != null)
            {
                settings.OptionChosen -= OnOptionChosen;
                settings.Stepped -= OnStepped;
                settings.CloseRequested -= CloseSettings;
            }
        }

        // ---- Options ------------------------------------------------------------------------------

        /// <summary>Applies every option (start-up, or after the store changed).</summary>
        public void ApplyAll()
        {
            ApplyMotion();
            ApplyOpacity();
            ApplyUiScale(force: true);
            if (audioManager != null) audioManager.SetVolumes(_options);
            _haptics.Enabled = _options.Haptics;
            if (applyGraphics) _graphics.Apply(_options.Graphics);
            if (applyFrameRate) FrameRatePolicy.Apply(_options.FrameRate);
        }

        /// <summary>Reads the options again from the store and applies them (tests).</summary>
        public void ReloadOptions()
        {
            EnsureInitialized();
            _options = GameOptionsRules.Sanitize(_store.Load(GameOptionsRules.Defaults));
            ApplyAll();
        }

        /// <summary>Changes the options as a whole (tests, tools); applied at once and saved when the settings close.</summary>
        public void SetOptions(in GameOptions options)
        {
            EnsureInitialized();
            _options = GameOptionsRules.Sanitize(options);
            _dirty = true;
            ApplyAll();
        }

        void ApplyMotion()
        {
            var preference = (MotionPreference)(int)_options.Motion;
            bool reduced;
            if (flight != null && flight.contextualHud != null)
            {
                flight.ApplyMotionPreference(preference);
                reduced = flight.contextualHud.ReducedMotion;
            }
            else reduced = SystemMotion.Resolve(preference, preference == MotionPreference.FollowSystem && SystemMotion.PrefersReducedMotion());
            if (modes != null) modes.SetReducedMotion(reduced);
            if (settings != null) settings.SetReducedMotion(reduced);
        }

        void ApplyOpacity()
        {
            if (flight != null) flight.ApplyHudOpacity(_options.HudOpacity);
            if (modes != null) modes.SetHudOpacity(_options.HudOpacity);
        }

        /// <summary>The interface scale the screen allows; recomputed when the screen or safe area changes.</summary>
        void ApplyUiScale(bool force)
        {
            var safe = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == _lastSafeArea && size == _lastScreen) return;
            _lastSafeArea = safe;
            _lastScreen = size;
            float aspect = safe.height > 0f ? safe.width / safe.height : (float)size.x / Mathf.Max(1, size.y);
            float heightShare = size.y > 0 && safe.height > 0f ? Mathf.Clamp01(safe.height / size.y) : 1f;
            _effectiveUiScale = UiScaleMath.Effective(_options.UiScale, aspect, _layout, heightShare);
            if (canvasScaler != null) canvasScaler.referenceResolution = UiScaleMath.ReferenceResolution(_effectiveUiScale);
        }

        void MarkDirty() => _dirty = true;

        void SaveIfDirty()
        {
            if (!_dirty || _store == null) return;
            _dirty = false;
            _store.Save(_options);
        }

        // ---- Settings screen ------------------------------------------------------------------------

        public void OpenSettings()
        {
            if (settings == null) return;
            settings.Open(View());
            if (modes != null) modes.OverlayOpen = true;
        }

        public void CloseSettings()
        {
            if (settings != null) settings.Close();
            if (modes != null) modes.OverlayOpen = false;
            SaveIfDirty();
        }

        public SettingsView View() => new SettingsView
        {
            Options = _options,
            EffectiveUiScale = _effectiveUiScale,
            HudProfile = flight != null ? flight.ActiveHudProfile : HudProfile.Smart,
            AssistProfile = flight != null ? flight.AssistProfile : FlightAssistProfile.Assisted,
            Sensitivity = flight != null ? flight.Sensitivity : ControlSensitivity.Medium,
            HapticsSupported = _haptics.Supported,
            Fps60Supported = FrameRatePolicy.Supports60(FrameRatePolicy.DisplayRefreshRate()),
        };

        public void OnOptionChosen(SettingId id, int index)
        {
            switch (id)
            {
                case SettingId.HudProfile:
                    if (flight != null) flight.SetHudProfile(SettingsScreen.HudProfileAt(index));
                    return; // stored by the flight session
                case SettingId.AssistProfile:
                    if (flight != null && FlightAssistProfiles.IsValid(index)) flight.SetAssistProfile((FlightAssistProfile)index);
                    return;
                case SettingId.Sensitivity:
                    if (flight != null && index >= 0 && index <= (int)ControlSensitivity.High) flight.SetSensitivity((ControlSensitivity)index);
                    return;
                case SettingId.Motion:
                    _options.Motion = (MotionOption)Mathf.Clamp(index, 0, GameOptionsRules.MotionOptionCount - 1);
                    ApplyMotion();
                    break;
                case SettingId.Haptics:
                    _options.Haptics = index == 0;
                    _haptics.Enabled = _options.Haptics;
                    if (_options.Haptics) _haptics.Play(HapticEvent.Confirm); // a sample of what it feels like
                    break;
                case SettingId.Graphics:
                    _options.Graphics = (GraphicsPreset)Mathf.Clamp(index, 0, GameOptionsRules.GraphicsPresetCount - 1);
                    if (applyGraphics) _graphics.Apply(_options.Graphics);
                    break;
                case SettingId.FrameRate:
                    _options.FrameRate = index == 1 ? FrameRatePreference.Fps60 : FrameRatePreference.Fps30;
                    if (applyFrameRate) FrameRatePolicy.Apply(_options.FrameRate);
                    break;
                default:
                    return;
            }
            MarkDirty();
        }

        public void OnStepped(SettingId id, int direction)
        {
            switch (id)
            {
                case SettingId.UiScale:
                    _options.UiScale = GameOptionsRules.StepUiScale(_options.UiScale, direction);
                    ApplyUiScale(force: true);
                    break;
                case SettingId.HudOpacity:
                    _options.HudOpacity = GameOptionsRules.StepHudOpacity(_options.HudOpacity, direction);
                    ApplyOpacity();
                    break;
                case SettingId.MasterVolume:
                case SettingId.AircraftVolume:
                case SettingId.EnvironmentVolume:
                case SettingId.InterfaceVolume:
                case SettingId.WarningVolume:
                    var channel = SettingsScreen.VolumeChannelOf(id);
                    _options.SetVolume(channel, GameOptionsRules.StepVolume(channel, _options.Volume(channel), direction));
                    if (audioManager != null) audioManager.SetVolumes(_options);
                    break;
                default:
                    return;
            }
            MarkDirty();
        }

        // ---- Per step and per frame -------------------------------------------------------------------

        void FixedUpdate()
        {
            if (flight == null || flight.telemetry == null) return;
            var t = flight.Telemetry;
            if (_touchdown.Update(t.WeightOnWheels, t.VerticalSpeedFeetPerMinute, t.GroundspeedKnots, Time.fixedDeltaTime, _ground, out float intensity))
            {
                if (audioManager != null) audioManager.PlayTouchdown(intensity);
                _haptics.Play(intensity >= 0.5f ? HapticEvent.TouchdownFirm : HapticEvent.TouchdownSoft);
            }
        }

        void Update()
        {
            ApplyUiScale(force: false);
            if (settings != null && settings.IsOpen)
            {
                // Settings live over a paused world; if the flight resumed some other way, they close.
                if (modes != null && !modes.IsPaused && !modes.ModeSelectOpen) CloseSettings();
                else settings.Present(View());
            }
            if (flight == null) return;

            bool paused = _appPaused || (modes != null && (modes.IsPaused || modes.ModeSelectOpen));
            if (audioManager != null)
            {
                audioManager.SetPaused(paused);
                if (!paused)
                {
                    var t = flight.Telemetry;
                    bool cockpit = flight.CurrentView == ViewMode.Cockpit;
                    audioManager.Present(FlightAudioInput.From(t, cockpit, CoastProximity(flight.AircraftPosition)), Time.deltaTime);
                }
            }

            int active = flight.Warnings != null ? flight.Warnings.ActiveMask : 0;
            if (paused) active = 0;
            if (audioManager != null)
                audioManager.PresentAlerts((active & _stallBit) != 0, active & _redBits & ~_stallBit, active & _cautionBits, Time.unscaledDeltaTime);
            int red = active & _redBits;
            if ((red & ~_previousRed) != 0) _haptics.Play(HapticEvent.Warning); // once when a red warning appears, never continuously
            _previousRed = red;
        }

        /// <summary>1 at the shoreline, fading to 0 at <see cref="coastAmbienceDistance"/> inland or out at sea.</summary>
        float CoastProximity(Vector3 position)
        {
            float distance = Mathf.Abs(position.x - PrototypeWorld.ShorelineX);
            return 1f - Mathf.Clamp01(distance / Mathf.Max(1f, coastAmbienceDistance));
        }

        // ---- Events ------------------------------------------------------------------------------------

        void OnFlightRestarted()
        {
            _touchdown.Reset(); // placing the aircraft is never a touchdown
            _previousRed = 0;
            if (audioManager != null) audioManager.Silence();
        }

        void OnSystemChanged(SystemChange change)
        {
            if (audioManager != null) audioManager.PlayUi(UiSound.Switch);
            _haptics.Play(HapticEvent.Selection);
        }

        void OnUiClicked(UiFeedback feedback)
        {
            if (feedback == UiFeedback.Confirm)
            {
                if (audioManager != null) audioManager.PlayUi(UiSound.Confirm);
                _haptics.Play(HapticEvent.Confirm);
            }
            else if (feedback == UiFeedback.Tap && audioManager != null) audioManager.PlayUi(UiSound.Tap);
        }
    }
}
