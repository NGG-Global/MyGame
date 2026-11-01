using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Audio
{
    /// <summary>What the sound of one frame is made from: telemetry values and the view. All value types.</summary>
    public struct FlightAudioInput
    {
        /// <summary>Engine output, 0-1 (lags the throttle in the flight model).</summary>
        public float EnginePower;
        public EngineState Engine;
        public float IndicatedAirspeedKnots;
        public float GroundspeedKnots;
        public float SideslipDegrees;
        /// <summary>Actual flap position, 0-1.</summary>
        public float FlapPosition;
        public bool StallWarning;
        public bool WeightOnWheels;
        public float HeightAboveGroundFeet;
        /// <summary>0 far inland or far out at sea, 1 at the shoreline.</summary>
        public float CoastProximity;
        /// <summary>Cockpit (enclosed) or an external view.</summary>
        public bool Cockpit;

        public static FlightAudioInput From(in FlightTelemetry t, bool cockpit, float coastProximity) => new FlightAudioInput
        {
            EnginePower = t.EnginePower,
            Engine = t.EngineState,
            IndicatedAirspeedKnots = t.IndicatedAirspeedKnots,
            GroundspeedKnots = t.GroundspeedKnots,
            SideslipDegrees = t.SideslipDegrees,
            FlapPosition = t.FlapPosition,
            StallWarning = t.StallWarning,
            WeightOnWheels = t.WeightOnWheels,
            HeightAboveGroundFeet = t.HeightAboveGroundFeet,
            CoastProximity = coastProximity,
            Cockpit = cockpit,
        };
    }

    /// <summary>Target levels and pitches for the loops; the manager smooths towards them.</summary>
    public struct FlightAudioTargets
    {
        /// <summary>Propeller RPM as a fraction of maximum, 0-1.</summary>
        public float EngineRpm;
        public float EnginePitch;
        public float EngineLowVolume;
        public float EngineHighVolume;
        public float AirflowVolume;
        public float AirflowPitch;
        public float RollingVolume;
        public float RollingPitch;
        public float AmbienceVolume;
        public float AircraftCutoffHz;
        public float EnvironmentCutoffHz;
    }

    /// <summary>
    /// Pure functions from flight state to sound. Approximations chosen for a
    /// convincing impression, not acoustics: a fixed-pitch propeller whose RPM
    /// rises with power and airspeed, loudness and brightness that follow engine
    /// load, airflow noise from airspeed, sideslip, flaps and buffet, and rolling
    /// noise from groundspeed. EditMode-tested.
    /// </summary>
    public static class FlightAudioMath
    {
        public static FlightAudioTargets Compute(in FlightAudioInput input, GameAudioConfiguration c) =>
            Compute(input, c.engine, c.airflow, c.ground, c.ambience, c.view);

        public static FlightAudioTargets Compute(in FlightAudioInput input, EngineAudioSettings engine, AirflowAudioSettings airflow,
            GroundAudioSettings ground, AmbienceAudioSettings ambience, ViewAudioSettings view)
        {
            var targets = new FlightAudioTargets();
            float ias = Finite(input.IndicatedAirspeedKnots);
            float rpm = EngineRpm(input.Engine, Finite(input.EnginePower), ias, engine);
            float loudness = EngineLoudness(input.Engine, Finite(input.EnginePower), engine) * (input.Cockpit ? view.cockpitEngineGain : 1f);
            float blend = EngineBrightness(rpm, Finite(input.EnginePower), engine);
            // Equal-power crossfade between the low and high layers.
            targets.EngineRpm = rpm;
            targets.EnginePitch = Mathf.Lerp(engine.minPitch, engine.maxPitch, rpm);
            targets.EngineLowVolume = loudness * Mathf.Cos(blend * Mathf.PI * 0.5f);
            targets.EngineHighVolume = loudness * Mathf.Sin(blend * Mathf.PI * 0.5f);

            targets.AirflowVolume = AirflowLevel(ias, input.SideslipDegrees, input.FlapPosition, input.StallWarning, airflow)
                                    * (input.Cockpit ? view.cockpitAirflowGain : 1f);
            targets.AirflowPitch = Mathf.Lerp(airflow.minPitch, airflow.maxPitch, Ramp(ias, airflow.startKnots, airflow.fullKnots));

            float gs = Finite(input.GroundspeedKnots);
            float roll = input.WeightOnWheels ? Ramp(gs, ground.startKnots, ground.fullKnots) : 0f;
            targets.RollingVolume = ground.maxVolume * roll;
            targets.RollingPitch = Mathf.Lerp(ground.minPitch, ground.maxPitch, Ramp(gs, 0f, ground.fullKnots * 1.5f));

            targets.AmbienceVolume = AmbienceLevel(input.HeightAboveGroundFeet, input.CoastProximity, ambience);
            targets.AircraftCutoffHz = input.Cockpit ? view.cockpitAircraftCutoff : view.externalCutoff;
            targets.EnvironmentCutoffHz = input.Cockpit ? view.cockpitEnvironmentCutoff : view.externalCutoff;
            return targets;
        }

        /// <summary>
        /// Propeller RPM fraction. Running: idle RPM plus power; a fixed-pitch propeller
        /// only reaches maximum RPM with airspeed (static RPM at rest). Off: the propeller
        /// windmills with airspeed. Starting: the starter turns it slowly.
        /// </summary>
        public static float EngineRpm(EngineState state, float power, float airspeedKnots, EngineAudioSettings s)
        {
            float windmill = s.windmillRpm * Ramp(airspeedKnots, 0f, Mathf.Max(1f, s.windmillAirspeedKnots));
            switch (state)
            {
                case EngineState.Off:
                    return Mathf.Clamp01(windmill);
                case EngineState.Starting:
                    return Mathf.Clamp01(Mathf.Max(s.crankingRpm, windmill));
                default:
                    float speed = Ramp(airspeedKnots, 0f, Mathf.Max(1f, s.fullRpmAirspeedKnots));
                    float reach = Mathf.Lerp(s.staticRpm, 1f, speed);
                    float powered = s.idleRpm + (reach - s.idleRpm) * Mathf.Pow(Mathf.Clamp01(power), 0.6f);
                    return Mathf.Clamp01(Mathf.Max(powered, windmill));
            }
        }

        /// <summary>Combustion loudness: idle plus load. Silent when off; quiet while cranking.</summary>
        public static float EngineLoudness(EngineState state, float power, EngineAudioSettings s)
        {
            switch (state)
            {
                case EngineState.Off: return 0f;
                case EngineState.Starting: return s.crankingVolume;
                default: return Mathf.Clamp01(s.idleVolume + s.loadVolume * Mathf.Clamp01(power));
            }
        }

        /// <summary>0 = all low layer, 1 = all high layer: mostly RPM, partly load (a loaded engine sounds harder at the same RPM).</summary>
        public static float EngineBrightness(float rpm, float power, EngineAudioSettings s) =>
            Mathf.Clamp01((1f - s.loadBrightness) * SmoothRamp(rpm, 0.2f, 0.85f) + s.loadBrightness * Mathf.Clamp01(power));

        public static float AirflowLevel(float airspeedKnots, float sideslipDegrees, float flapPosition, bool stallWarning, AirflowAudioSettings s)
        {
            float speed = SmoothRamp(airspeedKnots, s.startKnots, s.fullKnots);
            if (speed <= 0f) return 0f;
            float extra = Mathf.Min(s.sideslipMaxGain, Mathf.Abs(Finite(sideslipDegrees)) * s.sideslipGainPerDegree)
                          + s.flapGain * Mathf.Clamp01(Finite(flapPosition))
                          + (stallWarning ? s.stallBuffetGain : 0f);
            return Mathf.Clamp01(s.maxVolume * speed * (1f + extra));
        }

        public static float AmbienceLevel(float heightAboveGroundFeet, float coastProximity, AmbienceAudioSettings s)
        {
            float low = 1f - SmoothRamp(Finite(heightAboveGroundFeet), s.fadeStartFeet, s.fadeEndFeet);
            float coast = s.inlandShare + (1f - s.inlandShare) * Mathf.Clamp01(Finite(coastProximity));
            return s.maxVolume * low * coast;
        }

        /// <summary>Touchdown loudness from the sink rate: 0 softest, 1 hardest.</summary>
        public static float TouchdownIntensity(float sinkFeetPerMinute, GroundAudioSettings s) =>
            Ramp(Finite(sinkFeetPerMinute), s.softTouchdownFpm, s.hardTouchdownFpm);

        public static float TouchdownVolume(float intensity, GroundAudioSettings s) =>
            Mathf.Lerp(s.softTouchdownVolume, s.hardTouchdownVolume, Mathf.Clamp01(intensity));

        /// <summary>Exponential approach, frame-rate independent. A zero time constant snaps.</summary>
        public static float Smooth(float current, float target, float seconds, float deltaTime)
        {
            if (seconds <= 0f || deltaTime <= 0f) return target;
            return target + (current - target) * Mathf.Exp(-deltaTime / seconds);
        }

        /// <summary>Linear volume (0-1) to mixer decibels; silence is -80 dB.</summary>
        public static float LinearToDecibels(float linear) => linear <= 0.0001f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(linear));

        /// <summary>0 below <paramref name="from"/>, 1 above <paramref name="to"/>, linear between.</summary>
        public static float Ramp(float value, float from, float to)
        {
            if (to <= from) return value >= to ? 1f : 0f;
            return Mathf.Clamp01((value - from) / (to - from));
        }

        static float SmoothRamp(float value, float from, float to)
        {
            float x = Ramp(value, from, to);
            return x * x * (3f - 2f * x);
        }

        static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    /// <summary>
    /// Detects the moment the wheels touch the ground after flight and how hard,
    /// from the sink rate of the last airborne sample. A reset or a scenario start
    /// must call <see cref="Reset"/> so that placing the aircraft is never a touchdown.
    /// </summary>
    public sealed class TouchdownDetector
    {
        float _airborneSeconds;
        float _lastAirborneSinkFpm;
        bool _wasOnGround;
        bool _primed;

        public float LastSinkRateFpm { get; private set; }

        /// <summary>After a reset the next sample only sets the state.</summary>
        public void Reset()
        {
            _primed = false;
            _airborneSeconds = 0f;
            _lastAirborneSinkFpm = 0f;
        }

        /// <summary>Feed every physics sample. True on the sample the wheels touch down.</summary>
        public bool Update(bool onGround, float verticalSpeedFpm, float groundspeedKnots, float deltaTime, GroundAudioSettings s, out float intensity)
        {
            intensity = 0f;
            if (!_primed)
            {
                _primed = true;
                _wasOnGround = onGround;
                _airborneSeconds = 0f;
                return false;
            }

            bool touchdown = false;
            if (onGround && !_wasOnGround && _airborneSeconds >= s.minimumAirborneSeconds && groundspeedKnots >= s.minimumGroundspeedKnots)
            {
                LastSinkRateFpm = _lastAirborneSinkFpm;
                intensity = FlightAudioMath.TouchdownIntensity(_lastAirborneSinkFpm, s);
                touchdown = true;
            }

            if (onGround) _airborneSeconds = 0f;
            else
            {
                _airborneSeconds += deltaTime;
                float vs = float.IsNaN(verticalSpeedFpm) || float.IsInfinity(verticalSpeedFpm) ? 0f : verticalSpeedFpm;
                _lastAirborneSinkFpm = Mathf.Max(0f, -vs);
            }
            _wasOnGround = onGround;
            return touchdown;
        }
    }

    /// <summary>
    /// When to sound alerts: the stall horn while the stall warning is on, a
    /// repeating tone while any other red warning is on, and one chime when a
    /// caution appears (not again for the same caution within a few seconds).
    /// Masks use one bit per warning; the session maps its warnings to them.
    /// </summary>
    public sealed class AlertSoundScheduler
    {
        const int MaxBits = 16;
        readonly float[] _lastChime = new float[MaxBits];
        int _previousCautions;
        float _nextWarningTone;
        bool _warningWasOn;

        public bool StallHorn { get; private set; }
        public bool PlayWarningTone { get; private set; }
        public bool PlayCautionChime { get; private set; }

        public AlertSoundScheduler() => Reset();

        public void Reset()
        {
            for (int i = 0; i < MaxBits; i++) _lastChime[i] = float.NegativeInfinity;
            _previousCautions = 0;
            _warningWasOn = false;
            StallHorn = PlayWarningTone = PlayCautionChime = false;
        }

        /// <param name="stall">Stall warning on.</param>
        /// <param name="redMask">Active red warnings other than the stall.</param>
        /// <param name="cautionMask">Active cautions.</param>
        /// <param name="time">Unscaled time, s.</param>
        public void Update(bool stall, int redMask, int cautionMask, float time, AlertAudioSettings s)
        {
            StallHorn = stall;

            bool warning = redMask != 0;
            PlayWarningTone = false;
            if (warning && (!_warningWasOn || time >= _nextWarningTone))
            {
                PlayWarningTone = true;
                _nextWarningTone = time + Mathf.Max(0.2f, s.warningRepeatSeconds);
            }
            _warningWasOn = warning;

            PlayCautionChime = false;
            int appeared = cautionMask & ~_previousCautions;
            for (int bit = 0; bit < MaxBits && appeared != 0; bit++)
            {
                if ((appeared & (1 << bit)) == 0) continue;
                if (time - _lastChime[bit] >= s.cautionRepeatSeconds) PlayCautionChime = true;
                _lastChime[bit] = time;
            }
            _previousCautions = cautionMask;
        }
    }
}
