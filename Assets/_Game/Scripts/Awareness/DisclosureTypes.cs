using System;
using UnityEngine;

namespace OpenSkies.Awareness
{
    /// <summary>How much the HUD shows. The player chooses; the default is Smart.</summary>
    public enum HudProfile
    {
        /// <summary>Routine information hidden in the external view. Flight controls and warnings stay.</summary>
        Cinematic = 0,
        /// <summary>Essential information and contextual controls by phase of flight; quieter in stable flight.</summary>
        Smart = 1,
        /// <summary>All flight information and every available system action, always in the same place.</summary>
        Detailed = 2,
    }

    public enum ViewMode
    {
        External = 0,
        Cockpit = 1,
    }

    /// <summary>Information elements whose visibility the disclosure controller decides. Values are array indices.</summary>
    public enum HudElement
    {
        Airspeed = 0,
        Altitude = 1,
        VerticalSpeed = 2,
        Heading = 3,
        Groundspeed = 4,
        ApproachGuide = 5,
        NavigationGuide = 6,
    }

    /// <summary>Actions offered in the contextual action rail. Values are array indices and the rail order (bottom up).</summary>
    public enum ContextAction
    {
        ParkingBrake = 0,
        Flaps = 1,
        ApproachGuide = 2,
        Navigation = 3,
        /// <summary>Flight assistance and control settings (opens the selector). Offered before take-off.</summary>
        Assistance = 4,
    }

    /// <summary>How strongly an element is shown.</summary>
    public enum Prominence
    {
        Hidden = 0,
        /// <summary>Visible but receded (stable flight).</summary>
        Quiet = 1,
        Full = 2,
    }

    /// <summary>Why an element or action has its current visibility, for the inspector and tests.</summary>
    public enum DisclosureReason
    {
        None = 0,
        Profile,
        Phase,
        UncertainPhase,
        StableFlight,
        Warning,
        PlayerShown,
        PlayerHidden,
        RailExpanded,
        Engaged,
        InstrumentsInView,
        NotAvailable,
        /// <summary>A mission or tutorial asked for the guide (the player can still hide it).</summary>
        Mission,
    }

    /// <summary>A player's explicit choice about a guide, overriding the automatic rules.</summary>
    public enum PlayerOverride
    {
        None = 0,
        Show = 1,
        Hide = 2,
    }

    /// <summary>Aircraft systems the action rail can control, and their state.</summary>
    public struct AircraftSystemsState
    {
        public bool HasParkingBrake;
        public bool ParkingBrakeSet;
        public bool HasFlaps;
        public bool FlapsExtended;
    }

    /// <summary>Everything the disclosure controller needs for one update.</summary>
    public struct DisclosureInput
    {
        public HudProfile Profile;
        public ViewMode View;
        public FlightPhaseEstimate Phase;
        /// <summary>Active warnings, one bit per <see cref="FlightWarning"/>.</summary>
        public int WarningMask;
        /// <summary>Physical weight on wheels (not the phase estimate).</summary>
        public bool OnGround;
        public float BankDegrees;
        public bool HasRunway;
        public float DistanceToRunwayNm;
        public AircraftSystemsState Systems;
        /// <summary>The session can show the flight assistance and control settings.</summary>
        public bool HasAssistanceSettings;
        /// <summary>The navigation guide has a destination (the airfield or a mission checkpoint).</summary>
        public bool HasNavigationTarget;
        /// <summary>
        /// Guides a mission or tutorial needs on screen, one bit per <see cref="HudElement"/>
        /// (<see cref="HudElements.Bit"/>). They show in every profile and view; the player can
        /// still hide them, and warnings still win.
        /// </summary>
        public int MissionGuides;
        /// <summary>Rail actions a mission or tutorial points at, one bit per <see cref="ContextAction"/>: offered in every profile.</summary>
        public int MissionActions;
    }

    public static class HudElements
    {
        public const int Count = 7;

        public static bool IsGuide(HudElement element) => element == HudElement.ApproachGuide || element == HudElement.NavigationGuide;

        public static int Bit(HudElement element) => 1 << (int)element;

        public static string ShortLabel(HudElement element)
        {
            switch (element)
            {
                case HudElement.Airspeed: return "IAS";
                case HudElement.Altitude: return "ALT";
                case HudElement.VerticalSpeed: return "VS";
                case HudElement.Heading: return "HDG";
                case HudElement.Groundspeed: return "GS";
                case HudElement.ApproachGuide: return "APPR";
                case HudElement.NavigationGuide: return "NAV";
                default: return "?";
            }
        }
    }

    public static class ContextActions
    {
        public const int Count = 5;

        public static string ShortLabel(ContextAction action)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: return "PARK";
                case ContextAction.Flaps: return "FLAPS";
                case ContextAction.ApproachGuide: return "APPR";
                case ContextAction.Navigation: return "NAV";
                case ContextAction.Assistance: return "ASSIST";
                default: return "?";
            }
        }
    }

    public static class DisclosureReasons
    {
        public static string Describe(DisclosureReason reason)
        {
            switch (reason)
            {
                case DisclosureReason.Profile: return "profile";
                case DisclosureReason.Phase: return "phase";
                case DisclosureReason.UncertainPhase: return "phase uncertain";
                case DisclosureReason.StableFlight: return "stable";
                case DisclosureReason.Warning: return "warning";
                case DisclosureReason.PlayerShown: return "player";
                case DisclosureReason.PlayerHidden: return "player hid";
                case DisclosureReason.RailExpanded: return "systems open";
                case DisclosureReason.Engaged: return "engaged";
                case DisclosureReason.InstrumentsInView: return "instruments";
                case DisclosureReason.NotAvailable: return "n/a";
                case DisclosureReason.Mission: return "mission";
                default: return "-";
            }
        }
    }

    /// <summary>
    /// Disclosure rules that are tuning rather than design. The per-phase tables
    /// are in <see cref="DisclosureController"/> and docs/CONTEXTUAL_UI.md.
    /// </summary>
    [Serializable]
    public sealed class DisclosureSettings
    {
        public HudProfile defaultProfile = HudProfile.Smart;

        [Header("Anti-flicker")]
        [Tooltip("Elements appear at once but disappear only after their reason has been gone this long, s.")]
        public float hideDelaySeconds = 1.5f;
        [Tooltip("Once shown, an element stays at least this long, s.")]
        public float minimumVisibleSeconds = 2f;

        [Header("Smart profile")]
        [Tooltip("Below this phase confidence, Smart falls back to a safe set of information instead of trusting the phase.")]
        [Range(0f, 1f)] public float uncertainConfidence = 0.35f;
        [Tooltip("Cruise held this long (with the conditions below) counts as stable flight, s.")]
        public float stableFlightSeconds = 8f;
        [Range(0f, 1f)] public float stableMinimumConfidence = 0.7f;
        [Tooltip("Stable flight needs wings within this bank angle, degrees.")]
        public float stableMaxBankDegrees = 10f;
        [Tooltip("The approach guide is offered or shown within this distance of the runway, NM.")]
        public float approachGuideRangeNm = 8f;

        [Header("Cockpit")]
        [Tooltip("The cockpit has readable instruments (and a GPS): the HUD hides flight data and the navigation guide in cockpit view (design pillar 4).")]
        public bool cockpitHasInstruments = true;

        [Header("Approach guide")]
        [Tooltip("Glide path angle, degrees.")]
        public float glidepathDegrees = 3f;
        [Tooltip("Touchdown aim point beyond the threshold, m.")]
        public float aimPointMetres = 150f;
        [Tooltip("PAPI-style lights: the inner pair are set this far either side of the glide path, degrees.")]
        public float papiInnerOffsetDegrees = 0.167f;
        [Tooltip("The outer pair are set this far either side of the glide path, degrees.")]
        public float papiOuterOffsetDegrees = 0.5f;
        [Tooltip("Centreline offset shown as full deflection, m.")]
        public float lateralFullScaleMetres = 150f;
    }

    /// <summary>Approach guide values, pure functions.</summary>
    public static class ApproachGuidance
    {
        public const int PapiLights = 4;

        /// <summary>
        /// Number of white lights out of four, PAPI style: 2 = on the glide path,
        /// 3-4 = high, 0-1 = low. Each light turns white when the aircraft is above its setting angle.
        /// </summary>
        public static int PapiWhiteLights(float glidepathAngleDegrees, DisclosureSettings s)
        {
            float g = s.glidepathDegrees;
            int white = 0;
            if (glidepathAngleDegrees > g - s.papiOuterOffsetDegrees) white++;
            if (glidepathAngleDegrees > g - s.papiInnerOffsetDegrees) white++;
            if (glidepathAngleDegrees > g + s.papiInnerOffsetDegrees) white++;
            if (glidepathAngleDegrees > g + s.papiOuterOffsetDegrees) white++;
            return white;
        }

        /// <summary>−1 (full left of the centreline) to +1 (full right).</summary>
        public static float LateralDeviation(float crossTrackMetres, DisclosureSettings s) =>
            Mathf.Clamp(crossTrackMetres / Mathf.Max(s.lateralFullScaleMetres, 1f), -1f, 1f);
    }
}
