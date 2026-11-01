using System;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>What the player chose on the entry screen. Values are stable.</summary>
    public enum GameMode
    {
        FreeFlight = 0,
        FirstFlight = 1,
        LandingChallenge = 2,
    }

    /// <summary>Where a free flight begins. Values are stable.</summary>
    public enum FreeFlightStart
    {
        Runway = 0,
        Airborne = 1,
    }

    /// <summary>
    /// Everything a mission looks at for one step: measured telemetry, the phase
    /// of flight the awareness layer believes, warnings, the runway geometry,
    /// the aircraft's position, the pilot's own commands (before assistance),
    /// and the player's choices. Missions only read it; they never change the flight.
    /// </summary>
    public struct MissionInput
    {
        public FlightTelemetry Telemetry;
        public FlightPhaseEstimate Phase;
        /// <summary>One bit per active <see cref="FlightWarning"/>.</summary>
        public int WarningMask;
        public RunwayRelation Runway;
        /// <summary>Aircraft root position, world, m (y is the altitude).</summary>
        public Vector3 Position;
        /// <summary>The pilot's commands after sensitivity, before assistance.</summary>
        public FlightControls Pilot;
        public FlightAssistProfile Profile;
        public ViewMode View;
        /// <summary>The touch controls are on screen (otherwise keyboard or gamepad texts are used).</summary>
        public bool TouchControls;
        public bool ParkingBrakeSet;
        /// <summary>Selected flap position, 0-1.</summary>
        public float FlapSelection;

        public bool Airborne => !Phase.OnGround;
    }

    /// <summary>The kind of message a coaching cue carries. Values are stable.</summary>
    public enum CoachTone
    {
        Instruction = 0,
        /// <summary>Extra help when the player seems to struggle.</summary>
        Hint = 1,
        /// <summary>Short confirmation that an action worked.</summary>
        Success = 2,
        /// <summary>What to do about an active warning. The warning banner itself is the alert.</summary>
        Safety = 3,
    }

    /// <summary>Controls a coaching cue can point at with a subtle highlight.</summary>
    [Flags]
    public enum CoachHighlight
    {
        None = 0,
        Stick = 1,
        Throttle = 2,
        Brake = 4,
        Flaps = 8,
        Camera = 16,
        Reset = 32,
    }

    /// <summary>
    /// One coaching message for the HUD. Strings are cached by the mission, so
    /// presenting a cue never allocates; <see cref="Version"/> changes only when
    /// the content does.
    /// </summary>
    public struct CoachingCue
    {
        public bool Visible;
        /// <summary>Small label, e.g. "3/8  ROTATION".</summary>
        public string Label;
        public string Text;
        public CoachTone Tone;
        public CoachHighlight Highlights;
        /// <summary>A short explanation is available on request (tap the message).</summary>
        public bool HasDetail;
        /// <summary>The explanation is showing instead of the instruction.</summary>
        public bool ShowingDetail;
        public int Version;
    }

    /// <summary>Where a mission (re)starts the aircraft. Values are stable.</summary>
    public enum MissionStartKind
    {
        /// <summary>At rest at the start of the runway (the prototype's reset position).</summary>
        Runway = 0,
        /// <summary>In level flight, clean, at the stick-free trimmed speed.</summary>
        Airborne = 1,
        /// <summary>Established on final approach, configured for landing.</summary>
        FinalApproach = 2,
    }

    /// <summary>
    /// A start or checkpoint request. The composition root turns it into a
    /// physical aircraft state from the aircraft data, so missions need no
    /// knowledge of the flight model.
    /// </summary>
    public struct MissionStart
    {
        public MissionStartKind Kind;
        /// <summary>Airborne: aircraft position (y = altitude), m.</summary>
        public Vector3 Position;
        /// <summary>Airborne: direction of flight, degrees true.</summary>
        public float HeadingDegrees;
        /// <summary>Final approach: distance before the landing threshold, m.</summary>
        public float DistanceBeforeThresholdMetres;
        /// <summary>Final approach: flap detent index (-1 = the last, full flap).</summary>
        public int FlapDetent;
        /// <summary>Final approach: glide path angle, degrees.</summary>
        public float GlidepathDegrees;
        /// <summary>Final approach: runway landing direction, degrees true.</summary>
        public float LandingHeadingDegrees;

        public static MissionStart Runway => new MissionStart { Kind = MissionStartKind.Runway };

        public static MissionStart Airborne(Vector3 position, float headingDegrees) =>
            new MissionStart { Kind = MissionStartKind.Airborne, Position = position, HeadingDegrees = headingDegrees };
    }

    /// <summary>
    /// Reference speeds and distances for coaching, derived from the aircraft's
    /// envelope (stall speeds from the flight model) and the take-off run
    /// estimate, never typed in by hand. Displayed speeds are rounded to 5 kt.
    /// </summary>
    public struct TrainingSpeeds
    {
        public float StallCleanKnots;
        public float StallFullFlapKnots;
        public float RotateKnots;
        public float ClimbKnots;
        public float ApproachCleanKnots;
        public float ApproachFlapsKnots;
        /// <summary>Estimated ground run to lift-off at full power, m.</summary>
        public float TakeoffRunMetres;

        public float StallKnots(float flaps) => Mathf.Lerp(StallCleanKnots, StallFullFlapKnots, Mathf.Clamp01(flaps));

        public float ApproachKnots(float flaps) => flaps > 0.05f ? ApproachFlapsKnots : ApproachCleanKnots;

        public static TrainingSpeeds From(in AircraftEnvelope envelope, float takeoffRunMetres, TutorialSettings settings)
        {
            var e = envelope.IsDefined ? envelope : AircraftEnvelope.PrototypeTrainer;
            var s = settings ?? new TutorialSettings();
            return new TrainingSpeeds
            {
                StallCleanKnots = e.StallSpeedCleanKnots,
                StallFullFlapKnots = e.StallSpeedFullFlapKnots,
                RotateKnots = RoundToFive(e.StallSpeedCleanKnots * s.rotateFactor),
                ClimbKnots = RoundToFive(e.StallSpeedCleanKnots * s.climbFactor),
                ApproachCleanKnots = RoundToFive(e.StallSpeedCleanKnots * s.approachFactor),
                ApproachFlapsKnots = RoundToFive(e.StallSpeedFullFlapKnots * s.approachFactor),
                TakeoffRunMetres = float.IsNaN(takeoffRunMetres) || takeoffRunMetres <= 0f ? s.fallbackTakeoffRunMetres : takeoffRunMetres,
            };
        }

        public static float RoundToFive(float knots) => Mathf.Round(knots / 5f) * 5f;
    }

    public static class MissionText
    {
        /// <summary>Whole number as text, for strings built once (never per frame).</summary>
        public static string Number(float value) => Mathf.RoundToInt(value).ToString();

        public static string ModeLabel(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.FirstFlight: return "FIRST FLIGHT";
                case GameMode.LandingChallenge: return "LANDING CHALLENGE";
                default: return "FREE FLIGHT";
            }
        }
    }
}
