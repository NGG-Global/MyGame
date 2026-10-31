using System;

namespace OpenSkies.FlightState
{
    /// <summary>
    /// Engine state reported by the simulation. Running is the zero value, so a
    /// default (not yet sampled) telemetry never reads as an engine failure.
    /// </summary>
    public enum EngineState
    {
        Running = 0,
        /// <summary>Ignition switched on, the starter is cranking.</summary>
        Starting = 1,
        /// <summary>Not running: no power.</summary>
        Off = 2,
    }

    /// <summary>Aircraft systems the pilot sets with switches and levers. Values are stable.</summary>
    public enum AircraftSystem
    {
        Flaps = 0,
        ParkingBrake = 1,
        LandingLight = 2,
        NavigationLights = 3,
        Ignition = 4,
    }

    /// <summary>A requested switch or lever position: a flap detent index, or 0/1 for the others.</summary>
    public readonly struct SystemChange
    {
        public readonly AircraftSystem System;
        public readonly int Value;

        public SystemChange(AircraftSystem system, int value)
        {
            System = system;
            Value = value;
        }
    }

    /// <summary>
    /// The one authoritative state of the aircraft's switches and levers. The
    /// cockpit visuals, the flight simulation (through the input router) and the
    /// contextual HUD all read this object, and every change from any of them goes
    /// through <see cref="Apply"/>, so they can never disagree. It holds selector
    /// positions only; what the aircraft actually does (flap travel, engine start)
    /// is simulated and reported in <see cref="FlightTelemetry"/>.
    /// Plain C#, no allocations after construction.
    /// </summary>
    public sealed class AircraftSystems
    {
        public const int SystemCount = 5;

        readonly float[] _flapDetentDegrees;

        public AircraftSystems(float[] flapDetentDegrees = null)
        {
            _flapDetentDegrees = flapDetentDegrees != null && flapDetentDegrees.Length >= 2
                ? (float[])flapDetentDegrees.Clone()
                : new[] { 0f, 10f, 20f, 30f };
            ResetToStartState();
        }

        /// <summary>Raised after any change, with the system that changed.</summary>
        public event Action<AircraftSystem> Changed;

        /// <summary>Increments on every change: a cheap way for views to notice one.</summary>
        public int Version { get; private set; }

        public int FlapDetent { get; private set; }
        public int FlapDetentCount => _flapDetentDegrees.Length;
        public bool ParkingBrakeSet { get; private set; }
        public bool LandingLightOn { get; private set; }
        public bool NavigationLightsOn { get; private set; }
        public bool IgnitionOn { get; private set; }

        public float FlapDetentDegrees(int detent) => _flapDetentDegrees[Clamp(detent, 0, _flapDetentDegrees.Length - 1)];
        public float MaxFlapDegrees => _flapDetentDegrees[_flapDetentDegrees.Length - 1];

        /// <summary>Selected flap position as a fraction of full travel, 0-1.</summary>
        public float FlapSelection => MaxFlapDegrees > 0f ? FlapDetentDegrees(FlapDetent) / MaxFlapDegrees : 0f;

        /// <summary>Number of positions a system's selector has.</summary>
        public int PositionCount(AircraftSystem system) => system == AircraftSystem.Flaps ? _flapDetentDegrees.Length : 2;

        /// <summary>Current position: flap detent index, or 1 = on/set, 0 = off/released.</summary>
        public int Get(AircraftSystem system)
        {
            switch (system)
            {
                case AircraftSystem.Flaps: return FlapDetent;
                case AircraftSystem.ParkingBrake: return ParkingBrakeSet ? 1 : 0;
                case AircraftSystem.LandingLight: return LandingLightOn ? 1 : 0;
                case AircraftSystem.NavigationLights: return NavigationLightsOn ? 1 : 0;
                case AircraftSystem.Ignition: return IgnitionOn ? 1 : 0;
                default: return 0;
            }
        }

        /// <summary>Sets a selector. Out-of-range values are clamped. Returns whether anything changed.</summary>
        public bool Apply(in SystemChange change)
        {
            int value = Clamp(change.Value, 0, PositionCount(change.System) - 1);
            if (value == Get(change.System)) return false;
            switch (change.System)
            {
                case AircraftSystem.Flaps: FlapDetent = value; break;
                case AircraftSystem.ParkingBrake: ParkingBrakeSet = value == 1; break;
                case AircraftSystem.LandingLight: LandingLightOn = value == 1; break;
                case AircraftSystem.NavigationLights: NavigationLightsOn = value == 1; break;
                case AircraftSystem.Ignition: IgnitionOn = value == 1; break;
                default: return false;
            }
            Version++;
            Changed?.Invoke(change.System);
            return true;
        }

        public bool Set(AircraftSystem system, int value) => Apply(new SystemChange(system, value));

        /// <summary>
        /// The pilot's commands with the systems applied: full wheel brakes while the
        /// parking brake is set (or the pilot's own braking if stronger), the selected
        /// flap position and the ignition. Whatever produced the commands (touch,
        /// keyboard, a test script, a future autopilot), the aircraft always does
        /// what the switches show.
        /// </summary>
        public FlightControls ApplyTo(in FlightControls pilot) =>
            new FlightControls(pilot.Pitch, pilot.Roll, pilot.Yaw, pilot.Throttle,
                ParkingBrakeSet ? 1f : pilot.Brake, FlapSelection, IgnitionOn);
        public bool Set(AircraftSystem system, bool on) => Apply(new SystemChange(system, on ? 1 : 0));

        /// <summary>
        /// Ready on the runway: engine running (ignition on), navigation lights on,
        /// landing light off, flaps up, parking brake released.
        /// </summary>
        public void ResetToStartState()
        {
            FlapDetent = 0;
            ParkingBrakeSet = false;
            LandingLightOn = false;
            NavigationLightsOn = true;
            IgnitionOn = true;
            Version++;
            Changed?.Invoke(AircraftSystem.Flaps);
        }

        static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
    }

    /// <summary>
    /// Rules for changing systems, the same for every way of changing them
    /// (cockpit, contextual HUD, keyboard). Pure functions.
    /// </summary>
    public static class AircraftSystemsPolicy
    {
        public enum Verdict
        {
            Allowed = 0,
            /// <summary>Allowed after an explicit second confirmation (hazardous in this situation).</summary>
            NeedsConfirmation = 1,
            /// <summary>Not possible now (the reason is given by <see cref="ExplainRefusal"/>).</summary>
            Refused = 2,
        }

        public static Verdict Evaluate(in SystemChange change, AircraftSystems systems, bool onGround)
        {
            switch (change.System)
            {
                case AircraftSystem.ParkingBrake:
                    // Setting needs weight on wheels; releasing is always possible.
                    return change.Value == 1 && !systems.ParkingBrakeSet && !onGround ? Verdict.Refused : Verdict.Allowed;
                case AircraftSystem.Ignition:
                    // Switching the engine off in the air is never a casual tap.
                    return change.Value == 0 && systems.IgnitionOn && !onGround ? Verdict.NeedsConfirmation : Verdict.Allowed;
                default:
                    return Verdict.Allowed;
            }
        }

        public static string ExplainRefusal(in SystemChange change)
        {
            return change.System == AircraftSystem.ParkingBrake ? "Ground only" : "Not available";
        }
    }
}

namespace OpenSkies.FlightState
{
    /// <summary>
    /// The single way to change <see cref="AircraftSystems"/>, used by the cockpit,
    /// the contextual HUD and the keyboard alike. It applies
    /// <see cref="AircraftSystemsPolicy"/> and holds a hazardous change until the
    /// same change is requested again within the confirmation window.
    /// </summary>
    public sealed class SystemChangeGate
    {
        public enum Outcome
        {
            Applied = 0,
            /// <summary>Allowed, but the system was already in that position.</summary>
            Unchanged = 1,
            AwaitingConfirmation = 2,
            Refused = 3,
        }

        public float ConfirmationWindowSeconds = 4f;

        SystemChange _pending;
        bool _hasPending;
        float _pendingSince;

        public Outcome Request(in SystemChange change, AircraftSystems systems, bool onGround, float now)
        {
            var verdict = AircraftSystemsPolicy.Evaluate(change, systems, onGround);
            if (verdict == AircraftSystemsPolicy.Verdict.Refused)
            {
                Clear();
                return Outcome.Refused;
            }
            if (verdict == AircraftSystemsPolicy.Verdict.NeedsConfirmation)
            {
                bool confirmed = HasPending(now, out var pending) && pending.System == change.System && pending.Value == change.Value;
                if (!confirmed)
                {
                    _pending = change;
                    _hasPending = true;
                    _pendingSince = now;
                    return Outcome.AwaitingConfirmation;
                }
            }
            Clear();
            return systems.Apply(change) ? Outcome.Applied : Outcome.Unchanged;
        }

        /// <summary>A change waiting for its confirmation, if it has not expired.</summary>
        public bool HasPending(float now, out SystemChange change)
        {
            change = _pending;
            if (_hasPending && now - _pendingSince > ConfirmationWindowSeconds) _hasPending = false;
            return _hasPending;
        }

        public void Clear() => _hasPending = false;
    }
}
