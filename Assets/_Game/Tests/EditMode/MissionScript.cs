using System;
using System.Collections.Generic;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.Missions;
using OpenSkies.World;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Drives the First Flight with <see cref="FlightScript"/> (scripted kinematic
    /// telemetry with the real phase estimator, warning monitor and runway geometry)
    /// and the pilot's stick, at the physics rate. Records the steps entered and every
    /// message the coach shows. Deterministic; not a flight model.
    /// </summary>
    sealed class MissionScript
    {
        public const float Dt = FlightScript.Dt;
        public static readonly TutorialSettings Settings = new TutorialSettings();
        public static readonly TrainingSpeeds Speeds = TrainingSpeeds.From(AircraftEnvelope.PrototypeTrainer, 340f, Settings);

        public readonly FlightScript Flight;
        public readonly FirstFlightTutorial Tutorial;
        public readonly LandmarkRegistry World = PrototypeWorld.CreateRegistry();
        public readonly List<TutorialStepId> Steps = new List<TutorialStepId>();
        /// <summary>Every message as it appears, with its tone (again when it reappears after being hidden or replaced).</summary>
        public readonly List<string> Messages = new List<string>();
        public readonly List<CoachTone> Tones = new List<CoachTone>();

        public float StickPitch, StickRoll;
        public FlightAssistProfile Profile = FlightAssistProfile.Assisted;
        public bool Touch = true;
        public float FlapSelection;
        public MissionInput Input { get; private set; }
        int _version = int.MinValue;

        public MissionScript(bool onGround = true)
        {
            Flight = new FlightScript(onGround);
            Tutorial = new FirstFlightTutorial(Settings, Speeds, World, FlightScript.Runway);
            Tutorial.StepChanged += id => Steps.Add(id);
        }

        public TutorialStepId Step => Tutorial.CurrentStep;
        public CoachingCue Cue => Tutorial.Cue;

        public MissionInput BuildInput() => new MissionInput
        {
            Telemetry = Flight.LastTelemetry,
            Phase = Flight.Estimator.Current,
            WarningMask = Flight.Warnings.ActiveMask,
            Runway = Flight.LastRunway,
            Position = Flight.Position,
            Pilot = new FlightControls(StickPitch, StickRoll, 0f, Flight.Throttle),
            Profile = Profile,
            View = Flight.View,
            TouchControls = Touch,
            ParkingBrakeSet = Flight.ParkingBrakeSet,
            FlapSelection = FlapSelection,
        };

        public void StepOnce()
        {
            Flight.Step();
            Input = BuildInput();
            Tutorial.Update(Input, Dt);
            var cue = Tutorial.Cue;
            if (cue.Version == _version) return;
            _version = cue.Version;
            bool appeared = cue.Visible && (!_lastVisible || _lastText != cue.Text);
            _lastVisible = cue.Visible;
            if (cue.Visible) _lastText = cue.Text;
            if (!appeared) return;
            Messages.Add(cue.Text);
            Tones.Add(cue.Tone);
        }

        bool _lastVisible;
        string _lastText;

        public int TimesShown(string text)
        {
            int count = 0;
            foreach (var message in Messages)
                if (message == text) count++;
            return count;
        }

        public void Run(float seconds, Action<MissionScript> control = null)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                control?.Invoke(this);
                StepOnce();
            }
        }

        public void RunUntil(Func<MissionScript, bool> done, float maxSeconds, Action<MissionScript> control = null)
        {
            int steps = Mathf.RoundToInt(maxSeconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                if (done(this)) return;
                control?.Invoke(this);
                StepOnce();
            }
            if (!done(this))
                throw new TimeoutException($"Condition not met within {maxSeconds} s (step {Step}, phase {Flight.Phase}, alt {Flight.AltitudeFeet:F0} ft, " +
                                           $"gs {Flight.Groundspeed:F1} kt, pos {Flight.Position}, hdg {Flight.Heading:F0}, cue '{Cue.Text}')");
        }

        public bool Saw(string fragment)
        {
            foreach (var message in Messages)
                if (message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // ---- Flying (call from a control callback) ------------------------------------------------

        /// <summary>Banks proportionally into a turn towards a heading (at most 25°, 3°/s) and levels the wings when there.</summary>
        public void SteerTo(float heading, float degreesPerSecond = 3f)
        {
            float delta = Mathf.DeltaAngle(Flight.Heading, heading);
            float step = Mathf.Clamp(delta, -degreesPerSecond * Dt, degreesPerSecond * Dt);
            Flight.Heading = RunwayGeometry.Normalize360(Flight.Heading + step);
            Flight.Bank = Mathf.Clamp(delta * 2f, -25f, 25f);
        }

        public void SteerTowards(Vector3 target)
        {
            Vector3 offset = target - Flight.Position;
            offset.y = 0f;
            if (offset.sqrMagnitude > 1f) SteerTo(RunwayGeometry.HeadingOf(offset));
        }

        public void HoldSpeed(float knots) => Flight.ChangeSpeed(knots > Flight.Groundspeed ? 2f : -2f, knots);

        public void HoldAltitude(float feet, float maxRate = 700f) =>
            Flight.ApproachVerticalSpeed(Mathf.Clamp((feet - Flight.AltitudeFeet) * 3f, -maxRate, maxRate), 600f);

        /// <summary>Cruise towards the tutorial's current checkpoint.</summary>
        public void FlyToTarget(float knots, float altitudeFeet)
        {
            Flight.Throttle = 0.6f;
            if (Tutorial.HasNavigationTarget) SteerTowards(Tutorial.NavigationTarget.Position);
            HoldSpeed(knots);
            HoldAltitude(altitudeFeet);
        }

        /// <summary>A stabilised 3° approach to runway 36, a flare and a braked roll to a stop.</summary>
        public void FlyApproachAndLanding(float knots = 65f)
        {
            if (Flight.WeightOnWheels)
            {
                Flight.Throttle = 0f;
                Flight.Brake = 1f;
                SteerTo(0f);
                Flight.Bank = 0f;
                Flight.ChangeSpeed(-5f, 0f);
                return;
            }
            Flight.Throttle = 0.3f;
            // Intercept the centreline at up to 30°, then hold it.
            float intercept = Mathf.Clamp(-Flight.Position.x * 0.15f, -30f, 30f);
            SteerTo(RunwayGeometry.Normalize360(intercept));
            float toAim = FlightScript.Threshold36Z + 150f - Flight.Position.z;
            float glidepathFeet = Mathf.Max(0f, toAim) * Mathf.Tan(3f * Mathf.Deg2Rad) * FlightUnits.FeetPerMetre;
            float height = Flight.AltitudeFeet;
            if (height < 30f)
            {
                Flight.Throttle = 0f;
                Flight.ApproachVerticalSpeed(-150f, 2000f);
                Flight.ChangeSpeed(-2f, knots - 6f);
            }
            else
            {
                HoldSpeed(knots);
                float target = Flight.GlidepathVerticalSpeed(3f) + (glidepathFeet - height) * 3f;
                Flight.ApproachVerticalSpeed(Mathf.Clamp(target, -1000f, 0f), 1000f);
            }
            if (Flight.Position.y <= 0.05f)
            {
                Flight.WeightOnWheels = true;
                Flight.Bank = 0f;
            }
        }
    }
}
