using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// What each explainable element means, in two short lines, with the speeds
    /// taken from the aircraft's own envelope so nothing is typed in by hand. Built
    /// once per session (strings are cached); shown by a long press. Warnings reuse
    /// the safety texts the flying tips and the coach already use, so the advice
    /// is the same wherever the player meets it.
    /// </summary>
    public sealed class HudExplanations
    {
        public const int WarningBase = 100;
        public const int ActionBase = 200;
        public const int HandleId = 300;

        readonly string[] _elementTitles = new string[HudElements.Count];
        readonly string[] _elementTexts = new string[HudElements.Count];
        readonly string[] _warningTitles = new string[FlightWarnings.Count];
        readonly string[] _warningTexts = new string[FlightWarnings.Count];
        readonly string[] _actionTitles = new string[ContextActions.Count];
        readonly string[] _actionTexts = new string[ContextActions.Count];

        public HudExplanations(in AircraftEnvelope envelope, float flapExtendedSpeedKnots, float maxFlapDegrees)
        {
            var env = envelope.IsDefined ? envelope : AircraftEnvelope.PrototypeTrainer;
            int stallClean = Round5(env.StallSpeedCleanKnots);
            int stallFlap = Round5(env.StallSpeedFullFlapKnots);
            int vne = Mathf.RoundToInt(env.NeverExceedSpeedKnots);
            int vfe = Mathf.RoundToInt(flapExtendedSpeedKnots);
            int flaps = Mathf.RoundToInt(maxFlapDegrees);

            Set(_elementTitles, _elementTexts, (int)HudElement.Airspeed, "AIRSPEED, KT",
                "What the wing feels. Stalls near " + stallClean + " clean, " + stallFlap + " with full flap; never above " + vne + ".");
            Set(_elementTitles, _elementTexts, (int)HudElement.Altitude, "ALTITUDE, FT",
                "Height above sea level. The approach guide shows height above the runway instead.");
            Set(_elementTitles, _elementTexts, (int)HudElement.VerticalSpeed, "VERTICAL SPEED, FT/MIN",
                "Climb or descent rate. Aim for 0 in cruise and about 300 to 500 down on final.");
            Set(_elementTitles, _elementTexts, (int)HudElement.Heading, "HEADING",
                "Where the nose points, 360 is north. The navigation guide gives the course to steer.");
            Set(_elementTitles, _elementTexts, (int)HudElement.Groundspeed, "GROUNDSPEED, KT",
                "Speed over the ground. Keep taxiing slow; it is not the speed the wing feels.");
            Set(_elementTitles, _elementTexts, (int)HudElement.ApproachGuide, "APPROACH GUIDE",
                "Two white and two red lights: on the 3° path. More white: high. The marker shows where the centreline is.");
            Set(_elementTitles, _elementTexts, (int)HudElement.NavigationGuide, "NAVIGATION GUIDE",
                "The arrow points at the destination relative to the nose; distance in nautical miles, course to steer.");

            for (int w = 0; w < FlightWarnings.Count; w++)
            {
                var warning = (FlightWarning)w;
                _warningTitles[w] = FlightWarnings.Label(warning);
                _warningTexts[w] = FlightGuidanceAdvisor.SafetyText(FlightGuidanceAdvisor.SafetyHintFor(FlightWarnings.Bit(warning)));
            }

            Set(_actionTitles, _actionTexts, (int)ContextAction.ParkingBrake, "PARKING BRAKE",
                "Holds the aircraft at rest. Set it only on the ground; release it before rolling.");
            Set(_actionTitles, _actionTexts, (int)ContextAction.Flaps, "FLAPS",
                "More lift at low speed for take-off and landing, up to " + flaps + "°. Extend only below " + vfe + " kt.");
            Set(_actionTitles, _actionTexts, (int)ContextAction.ApproachGuide, "APPROACH GUIDE",
                "Shows or hides the glide path lights and the centreline marker for the home runway.");
            Set(_actionTitles, _actionTexts, (int)ContextAction.Navigation, "NAVIGATION",
                "Shows or hides the arrow and distance to the destination.");
            Set(_actionTitles, _actionTexts, (int)ContextAction.Assistance, "ASSISTANCE",
                "Assisted, Standard or Manual flying, and the stick sensitivity. The flight model is the same in all three.");
        }

        public string HandleTitle => "SYSTEMS";
        public string HandleText => "Tap to open the aircraft systems. Tap one to use it or to see its settings; tap outside to close.";

        public string ElementTitle(HudElement element) => _elementTitles[(int)element];
        public string ElementText(HudElement element) => _elementTexts[(int)element];
        public string WarningTitle(FlightWarning warning) => _warningTitles[(int)warning];
        public string WarningText(FlightWarning warning) => _warningTexts[(int)warning];
        public string ActionTitle(ContextAction action) => _actionTitles[(int)action];
        public string ActionText(ContextAction action) => _actionTexts[(int)action];

        /// <summary>Title and text for a target id (element index, WarningBase + warning, ActionBase + action, or the handle).</summary>
        public bool TryGet(int id, out string title, out string text)
        {
            title = text = null;
            if (id == HandleId) { title = HandleTitle; text = HandleText; return true; }
            if (id >= ActionBase && id < ActionBase + ContextActions.Count) { title = _actionTitles[id - ActionBase]; text = _actionTexts[id - ActionBase]; return true; }
            if (id >= WarningBase && id < WarningBase + FlightWarnings.Count) { title = _warningTitles[id - WarningBase]; text = _warningTexts[id - WarningBase]; return true; }
            if (id >= 0 && id < HudElements.Count) { title = _elementTitles[id]; text = _elementTexts[id]; return true; }
            return false;
        }

        static void Set(string[] titles, string[] texts, int index, string title, string text)
        {
            titles[index] = title;
            texts[index] = text;
        }

        static int Round5(float knots) => Mathf.RoundToInt(knots / 5f) * 5;
    }
}
