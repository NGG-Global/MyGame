using System;
using System.Collections.Generic;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Scripted telemetry for awareness tests: a kinematic point that moves by
    /// its groundspeed, heading and vertical speed at the physics rate (50 Hz)
    /// and feeds the phase estimator, warning monitor and disclosure controller
    /// with consistent telemetry and runway geometry. It is not a flight model;
    /// tests set speeds and rates directly. Fully deterministic.
    /// </summary>
    sealed class FlightScript
    {
        public const float Dt = 0.02f;
        public const float MetresPerNm = 1852f;

        /// <summary>The prototype runway: 36/18, 1500 × 30 m, centred on the origin.</summary>
        public static readonly RunwayInfo Runway = new RunwayInfo(Vector3.zero, 0f, 1500f, 30f);
        /// <summary>South threshold (runway 36) and the prototype start point 40 m beyond it.</summary>
        public const float Threshold36Z = -750f;
        public static readonly Vector3 RunwayStart = new Vector3(0f, 0f, Threshold36Z + 40f);

        public readonly FlightPhaseEstimator Estimator;
        public readonly FlightWarningMonitor Warnings;
        public readonly DisclosureController Disclosure;
        public readonly DisclosureSettings DisclosureSettings;

        /// <summary>Committed phases in order, starting with the initial estimate.</summary>
        public readonly List<FlightPhase> Phases = new List<FlightPhase>();
        public readonly List<PhaseTransitionReason> Reasons = new List<PhaseTransitionReason>();
        public readonly List<float> Times = new List<float>();

        // Aircraft state, set by the test.
        public Vector3 Position;
        public float Heading;
        public float Groundspeed;      // kt; IAS = TAS = GS (no wind)
        public float VerticalSpeed;    // fpm, used only in the air
        public float Throttle;
        public float Brake;
        public float Bank;
        public bool WeightOnWheels;
        public bool StallWarning;

        // HUD state, set by the test.
        public HudProfile Profile = HudProfile.Smart;
        public ViewMode View = ViewMode.External;
        public bool ParkingBrakeSet = false;
        public bool HasFlaps = false;

        public float Time { get; private set; }
        public FlightTelemetry LastTelemetry { get; private set; }
        public RunwayRelation LastRunway { get; private set; }

        /// <summary>Called after every step, e.g. to record HUD visibility.</summary>
        public Action<FlightScript> AfterStep;

        Func<FlightTelemetry, FlightTelemetry> _corruptNext;
        int _recordedTransitions;

        public FlightScript(bool onGround = true, FlightPhaseSettings phaseSettings = null, DisclosureSettings disclosureSettings = null)
        {
            Estimator = new FlightPhaseEstimator(phaseSettings);
            Estimator.Reset(onGround);
            Warnings = new FlightWarningMonitor();
            DisclosureSettings = disclosureSettings ?? new DisclosureSettings();
            Disclosure = new DisclosureController(DisclosureSettings);
            WeightOnWheels = onGround;
            Position = RunwayStart;
        }

        /// <summary>In the air on the extended centreline of runway 36, <paramref name="nmBeforeThreshold"/> out.</summary>
        public static FlightScript Airborne(float nmBeforeThreshold, float altitudeFeet, float knots, float verticalSpeed, float throttle,
            float heading = 0f)
        {
            var script = new FlightScript(onGround: false)
            {
                Heading = heading, Groundspeed = knots, VerticalSpeed = verticalSpeed, Throttle = throttle,
            };
            script.Position = new Vector3(0f, 0f, Threshold36Z - nmBeforeThreshold * MetresPerNm);
            script.AltitudeFeet = altitudeFeet;
            return script;
        }

        public FlightPhase Phase => Estimator.Phase;
        public PhaseTransitionReason Reason => Estimator.Current.Reason;

        public float AltitudeFeet
        {
            get => Position.y * FlightUnits.FeetPerMetre;
            set => Position.y = value / FlightUnits.FeetPerMetre;
        }

        /// <summary>Replace the next telemetry sample (glitch injection).</summary>
        public void CorruptNextSample(Func<FlightTelemetry, FlightTelemetry> corrupt) => _corruptNext = corrupt;

        public void Run(float seconds, Action<FlightScript> control = null)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                control?.Invoke(this);
                Step();
            }
        }

        public void RunUntil(Func<FlightScript, bool> done, float maxSeconds, Action<FlightScript> control = null)
        {
            int steps = Mathf.RoundToInt(maxSeconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                if (done(this)) return;
                control?.Invoke(this);
                Step();
            }
            if (!done(this)) throw new TimeoutException($"Condition not met within {maxSeconds} s (phase {Phase}, alt {AltitudeFeet:F0} ft, gs {Groundspeed:F1} kt)");
        }

        public void Step()
        {
            float speed = FlightUnits.KnotsToMetresPerSecond(Groundspeed);
            Position += RunwayGeometry.HorizontalDirection(Heading) * (speed * Dt);
            if (!WeightOnWheels) Position.y += VerticalSpeed / FlightUnits.FeetPerMinutePerMetrePerSecond * Dt;
            if (Position.y < 0f) Position.y = 0f;

            var telemetry = BuildTelemetry();
            if (_corruptNext != null)
            {
                telemetry = _corruptNext(telemetry);
                _corruptNext = null;
            }
            LastTelemetry = telemetry;
            LastRunway = RunwayGeometry.Evaluate(Runway, Position, Heading, DisclosureSettings.aimPointMetres);

            Estimator.Update(telemetry, LastRunway, Dt);
            Warnings.Update(telemetry, Dt);
            Disclosure.Update(BuildDisclosureInput(), Dt);
            Time += Dt;

            if (Estimator.TransitionTotal != _recordedTransitions)
            {
                _recordedTransitions = Estimator.TransitionTotal;
                Phases.Add(Estimator.Phase);
                Reasons.Add(Estimator.Current.Reason);
                Times.Add(Time);
            }
            AfterStep?.Invoke(this);
        }

        FlightTelemetry BuildTelemetry() => new FlightTelemetry
        {
            IndicatedAirspeedKnots = Groundspeed,
            TrueAirspeedKnots = Groundspeed,
            GroundspeedKnots = Groundspeed,
            AltitudeFeet = AltitudeFeet,
            HeightAboveGroundFeet = AltitudeFeet, // flat world at sea level
            VerticalSpeedFeetPerMinute = WeightOnWheels ? 0f : VerticalSpeed,
            HeadingDegrees = Heading,
            BankDegrees = Bank,
            Throttle = Throttle,
            EnginePower = Throttle,
            Brake = Brake,
            WeightOnWheels = WeightOnWheels,
            StallWarning = StallWarning,
        };

        public DisclosureInput BuildDisclosureInput() => new DisclosureInput
        {
            Profile = Profile,
            View = View,
            Phase = Estimator.Current,
            WarningMask = Warnings.ActiveMask,
            OnGround = WeightOnWheels,
            BankDegrees = Bank,
            HasRunway = LastRunway.Valid,
            DistanceToRunwayNm = LastRunway.DistanceToRunwayMetres / MetresPerNm,
            Systems = new AircraftSystemsState { HasParkingBrake = true, ParkingBrakeSet = ParkingBrakeSet, HasFlaps = HasFlaps },
        };

        // ---- Control helpers (call from a control callback) -------------------------

        public void ChangeSpeed(float knotsPerSecond, float limit)
        {
            Groundspeed = knotsPerSecond >= 0f
                ? Mathf.Min(limit, Groundspeed + knotsPerSecond * Dt)
                : Mathf.Max(limit, Groundspeed + knotsPerSecond * Dt);
        }

        public void TurnTowards(float heading, float degreesPerSecond)
        {
            float delta = Mathf.DeltaAngle(Heading, heading);
            float step = degreesPerSecond * Dt;
            Heading = RunwayGeometry.Normalize360(Heading + Mathf.Clamp(delta, -step, step));
            Bank = Mathf.Abs(delta) > 0.01f ? Mathf.Sign(delta) * 25f : 0f;
        }

        /// <summary>Turn through a full angle in one direction (negative = left), at a constant rate.</summary>
        public void Turn(float totalDegrees, float degreesPerSecond)
        {
            float remaining = totalDegrees;
            Run(Mathf.Abs(totalDegrees) / degreesPerSecond, s =>
            {
                float step = Mathf.Sign(totalDegrees) * Mathf.Min(degreesPerSecond * Dt, Mathf.Abs(remaining));
                remaining -= step;
                s.Heading = RunwayGeometry.Normalize360(s.Heading + step);
                s.Bank = Mathf.Sign(totalDegrees) * 25f;
            });
            Bank = 0f;
        }

        public void ApproachVerticalSpeed(float target, float fpmPerSecond)
        {
            VerticalSpeed = Mathf.MoveTowards(VerticalSpeed, target, fpmPerSecond * Dt);
        }

        /// <summary>Vertical speed that holds a glide path of the given angle at the current groundspeed, fpm (negative).</summary>
        public float GlidepathVerticalSpeed(float degrees) =>
            -FlightUnits.KnotsToMetresPerSecond(Groundspeed) * Mathf.Tan(degrees * Mathf.Deg2Rad) * FlightUnits.FeetPerMinutePerMetrePerSecond;
    }
}
