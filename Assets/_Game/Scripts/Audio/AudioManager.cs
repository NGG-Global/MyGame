using OpenSkies.Options;
using UnityEngine;
using UnityEngine.Audio;

namespace OpenSkies.Audio
{
    /// <summary>Interface sounds.</summary>
    public enum UiSound
    {
        Tap = 0,
        Confirm = 1,
        Switch = 2,
    }

    /// <summary>
    /// Plays the aircraft, its surroundings, the interface and the alerts through
    /// Unity's Audio Mixer (Master with Aircraft, Environment, Interface and
    /// Warnings groups). The session presents the flight state each frame; this
    /// component only smooths towards the targets from <see cref="FlightAudioMath"/>,
    /// so all tuning is in <see cref="GameAudioConfiguration"/>.
    ///
    /// Without a mixer (or with a parameter missing) the same volumes and the
    /// cockpit filter are applied on the sources themselves, so the game is never
    /// silent because of a missing asset. Sounds are 2D: one aircraft, heard from
    /// the camera; the cockpit sounds enclosed (lowpass, less airflow) and the
    /// external views open. Never reads input or changes the flight.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        [Tooltip("Tuning and optional clips. The defaults in code are used when empty.")]
        public GameAudioConfiguration configuration;

        enum Channel
        {
            Aircraft = 0,
            Environment = 1,
            Interface = 2,
            Warnings = 3,
        }

        enum Loop
        {
            EngineLow = 0,
            EngineHigh,
            Airflow,
            Rolling,
            Ambience,
            StallHorn,
        }

        const int LoopCount = 6;
        const float SilentStopSeconds = 0.5f;

        GameAudioConfiguration _config;
        AudioMixer _mixer;
        readonly AudioSource[] _loops = new AudioSource[LoopCount];
        readonly AudioLowPassFilter[] _loopFilters = new AudioLowPassFilter[LoopCount];
        readonly float[] _loopVolume = new float[LoopCount];
        readonly float[] _loopPitch = new float[LoopCount];
        readonly float[] _loopSilentFor = new float[LoopCount];
        AudioSource _aircraftOneShot, _warningOneShot, _uiOneShot;
        AudioLowPassFilter _aircraftOneShotFilter;
        readonly float[] _channelVolume = { 1f, 1f, 1f, 1f };
        float _masterVolume = 1f;
        float _aircraftCutoff, _environmentCutoff;
        float _appliedAircraftCutoff = -1f, _appliedEnvironmentCutoff = -1f;
        bool _mixerVolumes, _mixerCutoff;
        bool _initialized, _paused, _alertsActive;
        float _lastUiSound = float.NegativeInfinity;
        FlightAudioTargets _targets;
        readonly AlertSoundScheduler _alerts = new AlertSoundScheduler();

        public bool UsesMixer => _mixerVolumes;
        public bool UsesMixerFilter => _mixerCutoff;
        public bool Paused => _paused;
        public FlightAudioTargets Targets => _targets;
        public GameAudioConfiguration Configuration => _config;
        public float CurrentAircraftCutoff => _aircraftCutoff;
        public bool StallHornOn => _loops[(int)Loop.StallHorn] != null && _loopVolume[(int)Loop.StallHorn] > 0.01f;

        /// <summary>Current smoothed level of the engine (both layers), for tests and diagnostics.</summary>
        public float EngineLevel => _loopVolume[(int)Loop.EngineLow] + _loopVolume[(int)Loop.EngineHigh];
        public float EnginePitch => _loopPitch[(int)Loop.EngineLow];
        public float AirflowLevel => _loopVolume[(int)Loop.Airflow];

        void Awake() => EnsureInitialized();

        void OnDisable()
        {
            // Never leave the whole game muted (scene change, tests).
            if (_paused) AudioListener.pause = false;
            _paused = false;
        }

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _config = configuration != null ? configuration : ScriptableObject.CreateInstance<GameAudioConfiguration>();
            _mixer = _config.mixer;
            var p = _config.parameters;
            var clips = _config.clips;

            _loops[(int)Loop.EngineLow] = CreateSource("EngineLow", Channel.Aircraft, Pick(clips.engineLow, PlaceholderSound.EngineLow), true);
            _loops[(int)Loop.EngineHigh] = CreateSource("EngineHigh", Channel.Aircraft, Pick(clips.engineHigh, PlaceholderSound.EngineHigh), true);
            _loops[(int)Loop.Airflow] = CreateSource("Airflow", Channel.Environment, Pick(clips.airflow, PlaceholderSound.Airflow), true);
            _loops[(int)Loop.Rolling] = CreateSource("Rolling", Channel.Aircraft, Pick(clips.rolling, PlaceholderSound.Rolling), true);
            _loops[(int)Loop.Ambience] = CreateSource("Ambience", Channel.Environment, Pick(clips.ambience, PlaceholderSound.Ambience), true);
            _loops[(int)Loop.StallHorn] = CreateSource("StallHorn", Channel.Warnings, Pick(clips.stallHorn, PlaceholderSound.StallHorn), true);
            _aircraftOneShot = CreateSource("AircraftOneShots", Channel.Aircraft, null, false);
            _warningOneShot = CreateSource("WarningOneShots", Channel.Warnings, null, false);
            _uiOneShot = CreateSource("InterfaceOneShots", Channel.Interface, null, false);
            // Menu taps stay audible while the game (and the listener) is paused.
            _uiOneShot.ignoreListenerPause = true;
            for (int i = 0; i < LoopCount; i++) _loopPitch[i] = 1f;

            // The mixer is used for what it provides; anything missing falls back to the sources.
            _mixerVolumes = _mixer != null && HasParameter(p.masterVolume) && HasParameter(p.aircraftVolume) && HasParameter(p.environmentVolume)
                            && HasParameter(p.interfaceVolume) && HasParameter(p.warningsVolume);
            _mixerCutoff = _mixer != null && HasParameter(p.aircraftCutoff) && HasParameter(p.environmentCutoff);
            if (!_mixerCutoff)
            {
                for (int i = 0; i < LoopCount; i++)
                {
                    var channel = ChannelOf((Loop)i);
                    if (channel == Channel.Aircraft || channel == Channel.Environment) _loopFilters[i] = AddFilter(_loops[i]);
                }
                _aircraftOneShotFilter = AddFilter(_aircraftOneShot);
            }
            _aircraftCutoff = _environmentCutoff = _config.view.externalCutoff;
            ApplyCutoffs(force: true);
            ApplyVolumes();
        }

        AudioClip Pick(AudioClip assigned, PlaceholderSound placeholder) => assigned != null ? assigned : PlaceholderAudio.Clip(placeholder);

        AudioSource CreateSource(string name, Channel channel, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.clip = clip;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.priority = channel == Channel.Warnings ? 0 : (channel == Channel.Interface ? 64 : 128);
            source.outputAudioMixerGroup = FindGroup(channel);
            return source;
        }

        static AudioLowPassFilter AddFilter(AudioSource source)
        {
            var filter = source.gameObject.AddComponent<AudioLowPassFilter>();
            filter.lowpassResonanceQ = 1f;
            return filter;
        }

        AudioMixerGroup FindGroup(Channel channel)
        {
            if (_mixer == null) return null;
            string name = GroupName(channel);
            var groups = _mixer.FindMatchingGroups("Master/" + name);
            for (int i = 0; i < groups.Length; i++)
                if (groups[i] != null && groups[i].name == name) return groups[i];
            return null;
        }

        string GroupName(Channel channel)
        {
            var p = _config.parameters;
            switch (channel)
            {
                case Channel.Environment: return p.environmentGroup;
                case Channel.Interface: return p.interfaceGroup;
                case Channel.Warnings: return p.warningsGroup;
                default: return p.aircraftGroup;
            }
        }

        bool HasParameter(string name) => !string.IsNullOrEmpty(name) && _mixer.GetFloat(name, out _);

        static Channel ChannelOf(Loop loop)
        {
            switch (loop)
            {
                case Loop.Airflow:
                case Loop.Ambience: return Channel.Environment;
                case Loop.StallHorn: return Channel.Warnings;
                default: return Channel.Aircraft;
            }
        }

        // ---- Presented by the session -------------------------------------------------------------

        /// <summary>The flight's sound for this frame. Call once per frame; cheap and allocation-free.</summary>
        public void Present(in FlightAudioInput input, float deltaTime)
        {
            EnsureInitialized();
            _targets = FlightAudioMath.Compute(input, _config);
            var e = _config.engine;
            float engineSmoothing = e.smoothingSeconds, airSmoothing = _config.airflow.smoothingSeconds;
            Approach(Loop.EngineLow, _targets.EngineLowVolume, _targets.EnginePitch, engineSmoothing, deltaTime);
            Approach(Loop.EngineHigh, _targets.EngineHighVolume, _targets.EnginePitch, engineSmoothing, deltaTime);
            Approach(Loop.Airflow, _targets.AirflowVolume, _targets.AirflowPitch, airSmoothing, deltaTime);
            float rollingSmoothing = input.WeightOnWheels ? airSmoothing : _config.ground.releaseSeconds;
            Approach(Loop.Rolling, _targets.RollingVolume, _targets.RollingPitch, rollingSmoothing, deltaTime);
            Approach(Loop.Ambience, _targets.AmbienceVolume, 1f, 0.5f, deltaTime);

            float viewSeconds = _config.view.transitionSeconds;
            _aircraftCutoff = SmoothCutoff(_aircraftCutoff, _targets.AircraftCutoffHz, viewSeconds, deltaTime);
            _environmentCutoff = SmoothCutoff(_environmentCutoff, _targets.EnvironmentCutoffHz, viewSeconds, deltaTime);
            ApplyCutoffs(force: false);
        }

        /// <summary>
        /// Alert sounds from the active warnings: stall horn, repeating warning tone and
        /// caution chime (see <see cref="AlertSoundScheduler"/>). Call once per frame.
        /// </summary>
        public void PresentAlerts(bool stall, int redWarningMask, int cautionMask, float deltaTime)
        {
            EnsureInitialized();
            if (_paused) return; // the menu is up: alerts resume with the flight
            _alerts.Update(stall, redWarningMask, cautionMask, Time.unscaledTime, _config.alerts);
            _alertsActive = stall || redWarningMask != 0;
            Approach(Loop.StallHorn, _alerts.StallHorn ? _config.alerts.stallHornVolume : 0f, 1f, 0.03f, deltaTime);
            if (_alerts.PlayWarningTone) PlayOneShot(_warningOneShot, Pick(_config.clips.warningTone, PlaceholderSound.WarningTone),
                _config.alerts.warningVolume, Channel.Warnings);
            if (_alerts.PlayCautionChime) PlayOneShot(_warningOneShot, Pick(_config.clips.cautionChime, PlaceholderSound.CautionChime),
                _config.alerts.cautionVolume, Channel.Warnings);
        }

        /// <summary>Wheels on the runway: louder and lower for a harder touchdown (intensity 0-1).</summary>
        public void PlayTouchdown(float intensity)
        {
            EnsureInitialized();
            if (_paused) return;
            var g = _config.ground;
            _aircraftOneShot.pitch = Mathf.Lerp(1.08f, 0.85f, Mathf.Clamp01(intensity));
            PlayOneShot(_aircraftOneShot, Pick(_config.clips.touchdown, PlaceholderSound.Touchdown),
                FlightAudioMath.TouchdownVolume(intensity, g), Channel.Aircraft);
        }

        public void PlayUi(UiSound sound)
        {
            EnsureInitialized();
            var ui = _config.ui;
            if (Time.unscaledTime - _lastUiSound < ui.minimumInterval) return;
            _lastUiSound = Time.unscaledTime;
            switch (sound)
            {
                case UiSound.Confirm:
                    PlayOneShot(_uiOneShot, Pick(_config.clips.uiConfirm, PlaceholderSound.UiConfirm), ui.confirmVolume, Channel.Interface);
                    break;
                case UiSound.Switch:
                    PlayOneShot(_uiOneShot, Pick(_config.clips.switchClick, PlaceholderSound.SwitchClick), ui.switchVolume, Channel.Interface);
                    break;
                default:
                    PlayOneShot(_uiOneShot, Pick(_config.clips.uiTap, PlaceholderSound.UiTap), ui.tapVolume, Channel.Interface);
                    break;
            }
        }

        /// <summary>
        /// Pauses the flight's sound (menu, entry screen, app in the background).
        /// Interface sounds keep playing so the menu still answers taps.
        /// </summary>
        public void SetPaused(bool paused)
        {
            EnsureInitialized();
            if (paused == _paused) return;
            _paused = paused;
            AudioListener.pause = paused;
            if (paused)
            {
                _alerts.Reset();
                _alertsActive = false;
            }
        }

        /// <summary>Applies the player's volumes (mixer groups, or the sources without a mixer).</summary>
        public void SetVolumes(in GameOptions options)
        {
            EnsureInitialized();
            _masterVolume = Mathf.Clamp01(options.MasterVolume);
            _channelVolume[(int)Channel.Aircraft] = Mathf.Clamp01(options.AircraftVolume);
            _channelVolume[(int)Channel.Environment] = Mathf.Clamp01(options.EnvironmentVolume);
            _channelVolume[(int)Channel.Interface] = Mathf.Clamp01(options.InterfaceVolume);
            _channelVolume[(int)Channel.Warnings] = Mathf.Clamp01(options.WarningVolume);
            ApplyVolumes();
        }

        /// <summary>Stops every flight sound at once (reset, new mode): nothing carries over from the previous flight.</summary>
        public void Silence()
        {
            EnsureInitialized();
            for (int i = 0; i < LoopCount; i++)
            {
                _loopVolume[i] = 0f;
                if (_loops[i] != null)
                {
                    _loops[i].volume = 0f;
                    _loops[i].Stop();
                }
            }
            if (_aircraftOneShot != null) _aircraftOneShot.Stop();
            if (_warningOneShot != null) _warningOneShot.Stop();
            _alerts.Reset();
        }

        public bool AlertsActive => _alertsActive;

        // ---- Internals ------------------------------------------------------------------------------

        void Approach(Loop loop, float targetVolume, float targetPitch, float seconds, float deltaTime)
        {
            int i = (int)loop;
            var source = _loops[i];
            if (source == null) return;
            _loopVolume[i] = FlightAudioMath.Smooth(_loopVolume[i], Mathf.Clamp01(targetVolume), seconds, deltaTime);
            _loopPitch[i] = FlightAudioMath.Smooth(_loopPitch[i], targetPitch, seconds, deltaTime);
            source.pitch = _loopPitch[i];
            source.volume = _loopVolume[i] * SourceGain(ChannelOf(loop));

            // Silent loops stop (no mixing cost) and start again, at a random point, when needed.
            if (_loopVolume[i] < 0.002f && targetVolume < 0.002f)
            {
                _loopSilentFor[i] += deltaTime;
                if (_loopSilentFor[i] > SilentStopSeconds && source.isPlaying && !_paused) source.Stop();
            }
            else
            {
                _loopSilentFor[i] = 0f;
                if (!source.isPlaying && !_paused && source.clip != null)
                {
                    source.timeSamples = Random.Range(0, Mathf.Max(1, source.clip.samples));
                    source.Play();
                }
            }
        }

        void PlayOneShot(AudioSource source, AudioClip clip, float volume, Channel channel)
        {
            if (source == null || clip == null) return;
            source.PlayOneShot(clip, Mathf.Clamp01(volume) * SourceGain(channel)); // one-shot sources stay at volume 1
        }

        /// <summary>Volume applied on the source itself: 1 with the mixer (the groups attenuate), the player's levels without.</summary>
        float SourceGain(Channel channel) => _mixerVolumes ? 1f : _masterVolume * _channelVolume[(int)channel];

        void ApplyVolumes()
        {
            // One-shot sources keep volume 1: PlayOneShot scales each sound.
            if (_aircraftOneShot != null) _aircraftOneShot.volume = 1f;
            if (_warningOneShot != null) _warningOneShot.volume = 1f;
            if (_uiOneShot != null) _uiOneShot.volume = 1f;
            if (!_mixerVolumes) return;
            var p = _config.parameters;
            _mixer.SetFloat(p.masterVolume, FlightAudioMath.LinearToDecibels(_masterVolume));
            _mixer.SetFloat(p.aircraftVolume, FlightAudioMath.LinearToDecibels(_channelVolume[(int)Channel.Aircraft]));
            _mixer.SetFloat(p.environmentVolume, FlightAudioMath.LinearToDecibels(_channelVolume[(int)Channel.Environment]));
            _mixer.SetFloat(p.interfaceVolume, FlightAudioMath.LinearToDecibels(_channelVolume[(int)Channel.Interface]));
            _mixer.SetFloat(p.warningsVolume, FlightAudioMath.LinearToDecibels(_channelVolume[(int)Channel.Warnings]));
        }

        static float SmoothCutoff(float current, float target, float seconds, float deltaTime)
        {
            // Smooth in the log domain: the ear hears cutoff changes as ratios.
            float from = Mathf.Log(Mathf.Max(10f, current)), to = Mathf.Log(Mathf.Max(10f, target));
            return Mathf.Exp(FlightAudioMath.Smooth(from, to, seconds, deltaTime));
        }

        void ApplyCutoffs(bool force)
        {
            // Skip inaudible changes (under 1 %), so a settled view costs nothing.
            bool aircraft = force || Mathf.Abs(_aircraftCutoff - _appliedAircraftCutoff) > _appliedAircraftCutoff * 0.01f;
            bool environment = force || Mathf.Abs(_environmentCutoff - _appliedEnvironmentCutoff) > _appliedEnvironmentCutoff * 0.01f;
            if (!aircraft && !environment) return;
            if (aircraft) _appliedAircraftCutoff = _aircraftCutoff;
            if (environment) _appliedEnvironmentCutoff = _environmentCutoff;
            if (_mixerCutoff)
            {
                var p = _config.parameters;
                if (aircraft) _mixer.SetFloat(p.aircraftCutoff, _aircraftCutoff);
                if (environment) _mixer.SetFloat(p.environmentCutoff, _environmentCutoff);
                return;
            }
            for (int i = 0; i < LoopCount; i++)
            {
                if (_loopFilters[i] == null) continue;
                bool isAircraft = ChannelOf((Loop)i) == Channel.Aircraft;
                if (isAircraft ? aircraft : environment) _loopFilters[i].cutoffFrequency = isAircraft ? _aircraftCutoff : _environmentCutoff;
            }
            if (aircraft && _aircraftOneShotFilter != null) _aircraftOneShotFilter.cutoffFrequency = _aircraftCutoff;
        }
    }
}
