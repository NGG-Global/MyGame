using System;
using UnityEngine;
using UnityEngine.Audio;

namespace OpenSkies.Audio
{
    /// <summary>
    /// Every audio tuning value in one asset, so sound is tuned without code.
    /// Clips are optional: an empty slot uses a generated PLACEHOLDER sound
    /// (<see cref="PlaceholderAudio"/>), never a third-party recording.
    /// The defaults in code are used when the session has no asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Open Skies/Audio Configuration", fileName = "AudioConfiguration")]
    public sealed class GameAudioConfiguration : ScriptableObject
    {
        [Tooltip("Mixer with the Master, Aircraft, Environment, Interface and Warnings groups and the exposed parameters below. " +
                 "Without one, volumes and the cockpit filter are applied on the sources.")]
        public AudioMixer mixer;
        public MixerParameterNames parameters = new MixerParameterNames();
        public AudioClipSet clips = new AudioClipSet();
        public EngineAudioSettings engine = new EngineAudioSettings();
        public AirflowAudioSettings airflow = new AirflowAudioSettings();
        public GroundAudioSettings ground = new GroundAudioSettings();
        public AmbienceAudioSettings ambience = new AmbienceAudioSettings();
        public AlertAudioSettings alerts = new AlertAudioSettings();
        public ViewAudioSettings view = new ViewAudioSettings();
        public InterfaceAudioSettings ui = new InterfaceAudioSettings();
    }

    /// <summary>Mixer group paths and exposed parameter names (the Editor mixer builder uses the same names).</summary>
    [Serializable]
    public sealed class MixerParameterNames
    {
        public string aircraftGroup = "Aircraft";
        public string environmentGroup = "Environment";
        public string interfaceGroup = "Interface";
        public string warningsGroup = "Warnings";
        public string masterVolume = "MasterVolume";
        public string aircraftVolume = "AircraftVolume";
        public string environmentVolume = "EnvironmentVolume";
        public string interfaceVolume = "InterfaceVolume";
        public string warningsVolume = "WarningsVolume";
        [Tooltip("Lowpass cutoff on the Aircraft group, Hz: closes in the cockpit.")]
        public string aircraftCutoff = "AircraftCutoff";
        [Tooltip("Lowpass cutoff on the Environment group, Hz: closes in the cockpit.")]
        public string environmentCutoff = "EnvironmentCutoff";
    }

    /// <summary>Optional recorded or designed clips. Empty slots use generated placeholders.</summary>
    [Serializable]
    public sealed class AudioClipSet
    {
        [Tooltip("Loop at the reference RPM (pitch 1), low-power character.")]
        public AudioClip engineLow;
        [Tooltip("Loop at the reference RPM (pitch 1), high-power character (brighter, more propeller).")]
        public AudioClip engineHigh;
        public AudioClip airflow;
        public AudioClip rolling;
        public AudioClip touchdown;
        public AudioClip ambience;
        public AudioClip stallHorn;
        public AudioClip warningTone;
        public AudioClip cautionChime;
        public AudioClip uiTap;
        public AudioClip uiConfirm;
        public AudioClip switchClick;
    }

    [Serializable]
    public sealed class EngineAudioSettings
    {
        [Tooltip("RPM fraction at idle (0-1 of maximum RPM).")]
        [Range(0f, 1f)] public float idleRpm = 0.3f;
        [Tooltip("Full-power RPM fraction at rest (a fixed-pitch propeller cannot reach maximum RPM without airspeed).")]
        [Range(0f, 1f)] public float staticRpm = 0.86f;
        [Tooltip("Airspeed at which full power reaches maximum RPM, knots.")]
        public float fullRpmAirspeedKnots = 110f;
        [Tooltip("A windmilling propeller (engine off) turns at this RPM fraction at the airspeed below.")]
        [Range(0f, 1f)] public float windmillRpm = 0.35f;
        public float windmillAirspeedKnots = 100f;
        [Tooltip("Clip pitch at zero and at maximum RPM (the clips are recorded or generated at a reference RPM).")]
        public float minPitch = 0.55f;
        public float maxPitch = 1.45f;
        [Tooltip("Loudness at idle and the extra loudness from engine load (power).")]
        [Range(0f, 1f)] public float idleVolume = 0.45f;
        [Range(0f, 1f)] public float loadVolume = 0.45f;
        [Tooltip("How much engine load (not only RPM) brightens the sound: blend towards the high layer.")]
        [Range(0f, 1f)] public float loadBrightness = 0.35f;
        [Tooltip("Smoothing of RPM and loudness, s. Engine power already lags the throttle.")]
        public float smoothingSeconds = 0.08f;
        [Tooltip("Starter cranking: RPM fraction and loudness while the engine starts.")]
        [Range(0f, 1f)] public float crankingRpm = 0.18f;
        [Range(0f, 1f)] public float crankingVolume = 0.3f;
    }

    [Serializable]
    public sealed class AirflowAudioSettings
    {
        [Tooltip("Airflow noise starts at this airspeed and is full at the next, knots.")]
        public float startKnots = 15f;
        public float fullKnots = 150f;
        [Range(0f, 1f)] public float maxVolume = 0.65f;
        public float minPitch = 0.75f;
        public float maxPitch = 1.35f;
        [Tooltip("Extra noise per degree of sideslip, up to the limit.")]
        public float sideslipGainPerDegree = 0.015f;
        public float sideslipMaxGain = 0.2f;
        [Tooltip("Extra noise with full flaps.")]
        public float flapGain = 0.12f;
        [Tooltip("Buffet near the stall: extra noise while the stall warning is on.")]
        public float stallBuffetGain = 0.25f;
        public float smoothingSeconds = 0.15f;
    }

    [Serializable]
    public sealed class GroundAudioSettings
    {
        [Tooltip("Rolling noise starts at this groundspeed and is full at the next, knots.")]
        public float startKnots = 1f;
        public float fullKnots = 45f;
        [Range(0f, 1f)] public float maxVolume = 0.5f;
        public float minPitch = 0.7f;
        public float maxPitch = 1.3f;
        [Tooltip("Fade when the wheels leave the ground, s.")]
        public float releaseSeconds = 0.12f;
        [Tooltip("Touchdown: sink rate for the softest and the hardest thump, ft/min.")]
        public float softTouchdownFpm = 60f;
        public float hardTouchdownFpm = 700f;
        [Range(0f, 1f)] public float softTouchdownVolume = 0.25f;
        [Range(0f, 1f)] public float hardTouchdownVolume = 1f;
        [Tooltip("Airborne this long before a ground contact counts as a touchdown, s.")]
        public float minimumAirborneSeconds = 0.5f;
        [Tooltip("Below this groundspeed a contact is not a touchdown (e.g. settling at rest), knots.")]
        public float minimumGroundspeedKnots = 15f;
    }

    [Serializable]
    public sealed class AmbienceAudioSettings
    {
        [Range(0f, 1f)] public float maxVolume = 0.35f;
        [Tooltip("Ambience fades out between these heights above the ground, ft.")]
        public float fadeStartFeet = 50f;
        public float fadeEndFeet = 1500f;
        [Tooltip("Share of the ambience that is always there; the rest follows the closeness to the coast.")]
        [Range(0f, 1f)] public float inlandShare = 0.35f;
    }

    [Serializable]
    public sealed class AlertAudioSettings
    {
        [Range(0f, 1f)] public float stallHornVolume = 0.8f;
        [Range(0f, 1f)] public float warningVolume = 0.75f;
        [Range(0f, 1f)] public float cautionVolume = 0.6f;
        [Tooltip("A red warning other than the stall repeats its tone at this interval while active, s.")]
        public float warningRepeatSeconds = 1.6f;
        [Tooltip("The same caution does not chime again within this time, s.")]
        public float cautionRepeatSeconds = 6f;
    }

    [Serializable]
    public sealed class ViewAudioSettings
    {
        [Tooltip("Lowpass cutoff in the cockpit (enclosed cabin), Hz.")]
        public float cockpitAircraftCutoff = 2200f;
        public float cockpitEnvironmentCutoff = 1400f;
        [Tooltip("Open (no filtering) in external views, Hz.")]
        public float externalCutoff = 22000f;
        [Tooltip("Airflow loudness in the cockpit relative to outside.")]
        [Range(0f, 2f)] public float cockpitAirflowGain = 0.6f;
        [Tooltip("Engine loudness in the cockpit relative to outside (the engine is a metre in front of you).")]
        [Range(0f, 2f)] public float cockpitEngineGain = 1.1f;
        [Tooltip("Time to settle after a camera change, s.")]
        public float transitionSeconds = 0.15f;
    }

    [Serializable]
    public sealed class InterfaceAudioSettings
    {
        [Range(0f, 1f)] public float tapVolume = 0.35f;
        [Range(0f, 1f)] public float confirmVolume = 0.45f;
        [Range(0f, 1f)] public float switchVolume = 0.6f;
        [Tooltip("Taps closer together than this play one sound, s.")]
        public float minimumInterval = 0.05f;
    }
}
