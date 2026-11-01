using System;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Awareness
{
    /// <summary>Flying tips for the Assisted profile. Values are stable; lower values win (safety first).</summary>
    public enum GuidanceHint
    {
        None = -1,
        StallRecovery = 0,
        SinkRate = 1,
        EngineOff = 2,
        Overspeed = 3,
        SteepBank = 4,
        LowSpeed = 5,
        ReleaseParkingBrake = 6,
        TakeOffPower = 7,
        Rotate = 8,
        ClimbSpeed = 9,
        ApproachSetup = 10,
        Flare = 11,
        Stop = 12,
    }

    [Serializable]
    public sealed class GuidanceSettings
    {
        [Tooltip("A tip stays at least this long, unless a safety tip replaces it, s.")]
        public float minimumSeconds = 2.5f;
        [Tooltip("A tip stays this long after its reason has gone, s.")]
        public float lingerSeconds = 1f;
        [Tooltip("Advice for a phase of flight is shown this long, then not again until the phase changes, s. Safety tips stay while needed.")]
        public float adviceSeconds = 8f;
        [Tooltip("Bank beyond which the steep-bank tip appears, degrees.")]
        public float steepBankDegrees = 45f;
        [Tooltip("Low-speed tip below this multiple of the stall speed for the current flaps.")]
        public float lowSpeedFactor = 1.2f;
        [Tooltip("Rotate tip from this multiple of the clean stall speed.")]
        public float rotateFactor = 1.1f;
        [Tooltip("Climb speed in the tip, multiple of the clean stall speed.")]
        public float climbSpeedFactor = 1.35f;
        [Tooltip("Approach speed in the tip, multiple of the full-flap stall speed.")]
        public float approachSpeedFactor = 1.4f;
        [Tooltip("Flare tip below this height above ground on the landing, ft.")]
        public float flareHeightFeet = 40f;
    }

    /// <summary>
    /// Chooses one short flying tip at a time from the phase of flight, the warnings
    /// and the telemetry, for players who chose the Assisted profile. Safety tips
    /// (stall, sink rate, engine, overspeed) come first and stay while needed;
    /// phase advice (take-off, climb, approach, landing) shows once per phase for a
    /// few seconds, so it never nags. A tip never flickers: it stays a minimum time
    /// and lingers briefly after its reason has gone. Speeds in the tips come from
    /// the aircraft's envelope. Plain C#; texts are built once.
    /// </summary>
    public sealed class FlightGuidanceAdvisor
    {
        public struct Input
        {
            public FlightTelemetry Telemetry;
            public FlightPhase Phase;
            public int WarningMask;
            public bool OnRunway;
            public bool ParkingBrakeSet;
        }

        const int HintCount = 13;

        readonly GuidanceSettings _settings;
        readonly string[] _texts = new string[HintCount];
        AircraftEnvelope _envelope;
        GuidanceHint _current = GuidanceHint.None;
        float _shownFor, _reasonGoneFor;
        FlightPhase _advicePhase = (FlightPhase)(-1);
        float _adviceTime;
        bool _adviceUsed;

        public FlightGuidanceAdvisor(AircraftEnvelope envelope, GuidanceSettings settings = null)
        {
            _settings = settings ?? new GuidanceSettings();
            SetEnvelope(envelope);
        }

        public GuidanceHint Current => _current;
        public string CurrentText => _current == GuidanceHint.None ? string.Empty : _texts[(int)_current];
        public GuidanceSettings Settings => _settings;

        /// <summary>Warnings: shown at once, over anything else, for as long as they last.</summary>
        public static bool IsSafety(GuidanceHint hint) => hint >= GuidanceHint.StallRecovery && hint <= GuidanceHint.Overspeed;

        /// <summary>Phase advice: shown once per phase of flight for a few seconds.</summary>
        public static bool IsAdvice(GuidanceHint hint) => hint >= GuidanceHint.ReleaseParkingBrake;

        public string Text(GuidanceHint hint) => hint == GuidanceHint.None ? string.Empty : _texts[(int)hint];

        public void SetEnvelope(in AircraftEnvelope envelope)
        {
            _envelope = envelope.IsDefined ? envelope : AircraftEnvelope.PrototypeTrainer;
            var s = _settings;
            int rotate = Mathf.RoundToInt(_envelope.StallSpeedCleanKnots * s.rotateFactor / 5f) * 5;
            int climb = Mathf.RoundToInt(_envelope.StallSpeedCleanKnots * s.climbSpeedFactor / 5f) * 5;
            int approach = Mathf.RoundToInt(_envelope.StallSpeedFullFlapKnots * s.approachSpeedFactor / 5f) * 5;
            _texts[(int)GuidanceHint.StallRecovery] = "Stall: lower the nose and add power";
            _texts[(int)GuidanceHint.SinkRate] = "Sinking fast: add power, raise the nose gently";
            _texts[(int)GuidanceHint.EngineOff] = "Engine off: switch the ignition on to restart";
            _texts[(int)GuidanceHint.Overspeed] = "Too fast: reduce power, raise the nose gently";
            _texts[(int)GuidanceHint.SteepBank] = "Steep bank: roll the wings back towards level";
            _texts[(int)GuidanceHint.LowSpeed] = "Slow: lower the nose a little or add power";
            _texts[(int)GuidanceHint.ReleaseParkingBrake] = "Release the parking brake to roll";
            _texts[(int)GuidanceHint.TakeOffPower] = "Take-off: full power, keep the stick centred";
            _texts[(int)GuidanceHint.Rotate] = "At " + rotate + " KT, ease the stick back to lift off";
            _texts[(int)GuidanceHint.ClimbSpeed] = "Climb at about " + climb + " KT; let go and the trim holds it";
            _texts[(int)GuidanceHint.ApproachSetup] = "Approach: flaps down, about " + approach + " KT";
            _texts[(int)GuidanceHint.Flare] = "Idle power, raise the nose gently to touch down";
            _texts[(int)GuidanceHint.Stop] = "Hold BRAKE to stop";
        }

        public void Reset()
        {
            _current = GuidanceHint.None;
            _shownFor = _reasonGoneFor = 0f;
            _advicePhase = (FlightPhase)(-1);
            _adviceTime = 0f;
            _adviceUsed = false;
        }

        public GuidanceHint Update(in Input input, float deltaTime)
        {
            if (input.Phase != _advicePhase)
            {
                _advicePhase = input.Phase;
                _adviceTime = 0f;
                _adviceUsed = false;
            }

            var wanted = Choose(input);
            if (IsAdvice(wanted))
            {
                // Phase advice shows once per phase, then stays quiet (cautions and safety tips do not expire).
                if (_adviceUsed && _adviceTime >= _settings.adviceSeconds) wanted = GuidanceHint.None;
                else
                {
                    _adviceUsed = true;
                    _adviceTime += deltaTime;
                }
            }

            _shownFor += deltaTime;
            if (wanted == _current)
            {
                _reasonGoneFor = 0f;
                return _current;
            }

            bool preempt = IsSafety(wanted) && (_current == GuidanceHint.None || wanted < _current);
            if (_current == GuidanceHint.None || preempt)
            {
                Show(wanted);
                return _current;
            }

            // The current tip's reason is gone (or something else is more relevant): keep it a little longer.
            _reasonGoneFor += deltaTime;
            if (_shownFor >= _settings.minimumSeconds && _reasonGoneFor >= _settings.lingerSeconds) Show(wanted);
            return _current;
        }

        void Show(GuidanceHint hint)
        {
            _current = hint;
            _shownFor = 0f;
            _reasonGoneFor = 0f;
        }

        GuidanceHint Choose(in Input input)
        {
            var safety = Safety(input);
            if (safety != GuidanceHint.None) return safety;

            var t = input.Telemetry;
            var s = _settings;
            bool airborne = !t.WeightOnWheels;
            if (airborne && Mathf.Abs(t.BankDegrees) > s.steepBankDegrees) return GuidanceHint.SteepBank;
            if (airborne && input.Phase != FlightPhase.Landing
                && t.IndicatedAirspeedKnots < _envelope.StallSpeedKnots(t.FlapPosition) * s.lowSpeedFactor)
                return GuidanceHint.LowSpeed;

            switch (input.Phase)
            {
                case FlightPhase.Parked:
                case FlightPhase.Taxi:
                    if (input.ParkingBrakeSet && t.Throttle > 0.3f) return GuidanceHint.ReleaseParkingBrake;
                    return input.OnRunway && !input.ParkingBrakeSet && t.Throttle < 0.5f ? GuidanceHint.TakeOffPower : GuidanceHint.None;
                case FlightPhase.TakeoffRoll:
                    return t.IndicatedAirspeedKnots >= _envelope.StallSpeedCleanKnots * s.rotateFactor - 5f ? GuidanceHint.Rotate : GuidanceHint.TakeOffPower;
                case FlightPhase.InitialClimb:
                    return GuidanceHint.ClimbSpeed;
                case FlightPhase.Approach:
                    return GuidanceHint.ApproachSetup;
                case FlightPhase.Landing:
                    return airborne && t.HeightAboveGroundFeet < s.flareHeightFeet ? GuidanceHint.Flare : GuidanceHint.None;
                case FlightPhase.Rollout:
                    return GuidanceHint.Stop;
                default:
                    return GuidanceHint.None;
            }
        }

        static GuidanceHint Safety(in Input input)
        {
            int w = input.WarningMask;
            if ((w & FlightWarnings.Bit(FlightWarning.Stall)) != 0) return GuidanceHint.StallRecovery;
            if ((w & FlightWarnings.Bit(FlightWarning.SinkRate)) != 0) return GuidanceHint.SinkRate;
            if ((w & FlightWarnings.Bit(FlightWarning.EngineOff)) != 0) return GuidanceHint.EngineOff;
            if ((w & FlightWarnings.Bit(FlightWarning.Overspeed)) != 0) return GuidanceHint.Overspeed;
            return GuidanceHint.None;
        }
    }
}
