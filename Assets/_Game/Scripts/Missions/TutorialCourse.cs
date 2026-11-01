using OpenSkies.Awareness;
using OpenSkies.World;
using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>
    /// The First Flight as data: eight steps from the runway, round the
    /// lighthouse and the harbour, and back to land on runway 36. Every speed in
    /// the texts comes from <see cref="TrainingSpeeds"/> (the aircraft data), and
    /// every threshold from <see cref="TutorialSettings"/>. Texts are built once.
    /// </summary>
    public static class TutorialCourse
    {
        /// <summary>Waypoint id of the start of the final approach (not a landmark).</summary>
        public const string FinalEntryId = "final";
        /// <summary>Waypoint id of the landing threshold (not a landmark).</summary>
        public const string ThresholdId = "threshold";
        public const int StepCount = 8;

        public static TutorialStep[] Build(TutorialSettings settings, in TrainingSpeeds trainingSpeeds)
        {
            var s = settings ?? new TutorialSettings();
            var speeds = trainingSpeeds; // lambdas cannot capture an in parameter
            string rotate = MissionText.Number(speeds.RotateKnots);
            string climb = MissionText.Number(speeds.ClimbKnots);
            string approachClean = MissionText.Number(speeds.ApproachCleanKnots);
            string approachFlaps = MissionText.Number(speeds.ApproachFlapsKnots);
            string minimumCruise = MissionText.Number(Mathf.Round(s.minimumCruiseFeet / 100f) * 100f + 100f);
            float correction = s.correctionSustainSeconds;
            int nav = HudElements.Bit(HudElement.NavigationGuide);
            int approach = HudElements.Bit(HudElement.ApproachGuide);

            var steps = new TutorialStep[StepCount];

            steps[0] = new TutorialStep
            {
                Id = TutorialStepId.Preparation,
                Title = "PREPARATION",
                Detail = "The stick tilts the aircraft: back raises the nose, forward lowers it, sideways banks the wings. Let go and it re-centres.",
                Success = "Good. That is all the stick needs.",
                Callouts = new[]
                {
                    new TutorialCallout
                    {
                        Text = "Left side: your stick. Slide your thumb, then let go",
                        KeyboardText = "W and S tilt the nose, A and D bank. Try them",
                        Highlights = CoachHighlight.Stick,
                    },
                },
                Hints = new[]
                {
                    new TutorialHint
                    {
                        Text = "Place your left thumb anywhere on the left half and slide it",
                        KeyboardText = "Hold S, then W, to feel the controls",
                        Highlights = CoachHighlight.Stick, AfterSeconds = 12f, SustainSeconds = 0.5f,
                        When = c => c.StickActivity < 0.2f,
                    },
                },
                Complete = c => (c.StickActivity >= 0.5f && Mathf.Abs(c.Input.Pilot.Pitch) < 0.15f && Mathf.Abs(c.Input.Pilot.Roll) < 0.15f)
                                || c.Throttle >= 0.5f,
                CompleteSustainSeconds = 0.3f,
                Checkpoint = TutorialCheckpoint.Runway,
            };

            steps[1] = new TutorialStep
            {
                Id = TutorialStepId.Takeoff,
                Title = "TAKE-OFF",
                Detail = "Full power accelerates the aircraft down the runway. Keep the stick centred and let it roll straight.",
                Success = "Full power. Accelerating",
                Callouts = new[]
                {
                    new TutorialCallout
                    {
                        Text = "Release the parking brake: tap BRAKE SET",
                        KeyboardText = "Release the parking brake: press P",
                        When = c => c.Input.ParkingBrakeSet,
                    },
                    new TutorialCallout
                    {
                        Text = "Right side: slide the throttle all the way up",
                        KeyboardText = "Hold Left Shift for full power",
                        Highlights = CoachHighlight.Throttle,
                    },
                },
                Hints = new[]
                {
                    new TutorialHint
                    {
                        Text = "Push the throttle to the top with your right thumb",
                        KeyboardText = "Keep Left Shift held until the throttle is full",
                        Highlights = CoachHighlight.Throttle, AfterSeconds = 12f, SustainSeconds = 1f,
                        When = c => c.Throttle < 0.5f,
                    },
                },
                OnTrack = c => c.Throttle >= s.takeoffThrottle,
                QuietAfterSeconds = 0f,
                Complete = c => c.Throttle >= s.takeoffThrottle && c.OnGround && c.Groundspeed >= 15f,
                CompleteSustainSeconds = 0.3f,
                Checkpoint = TutorialCheckpoint.Runway,
            };

            steps[2] = new TutorialStep
            {
                Id = TutorialStepId.Rotation,
                Title = "ROTATION",
                Detail = "Once it is fast enough, the wing can lift the aircraft. Easing the stick back raises the nose, so the wing meets the air at a higher angle.",
                Success = "Airborne!",
                Callouts = new[]
                {
                    new TutorialCallout
                    {
                        Text = "Stick centred while the speed builds",
                        When = c => c.OnGround && c.Ias < speeds.RotateKnots - 8f,
                    },
                    new TutorialCallout
                    {
                        Text = rotate + " KT: ease the stick back gently",
                        KeyboardText = rotate + " KT: hold S gently to lift the nose",
                        Highlights = CoachHighlight.Stick,
                    },
                },
                Hints = new[]
                {
                    new TutorialHint
                    {
                        Text = "A little more back pressure: raise the nose",
                        KeyboardText = "Hold S a little longer: raise the nose",
                        Highlights = CoachHighlight.Stick, SustainSeconds = 2f,
                        When = c => c.OnGround && c.Ias >= speeds.RotateKnots + 12f,
                    },
                    new TutorialHint
                    {
                        Text = "Runway ending: idle the power and brake",
                        KeyboardText = "Runway ending: Left Ctrl to idle, Space to brake",
                        Highlights = CoachHighlight.Throttle | CoachHighlight.Brake, SustainSeconds = 0.3f,
                        When = c => c.OnGround && c.Input.Runway.OnRunway && c.RunwayRemainingMetres < 300f && c.Ias < speeds.RotateKnots,
                    },
                },
                Complete = c => c.Airborne && c.HeightFeet >= s.liftOffHeightFeet,
                CompleteSustainSeconds = 0.5f,
                Checkpoint = TutorialCheckpoint.Runway,
            };

            steps[3] = new TutorialStep
            {
                Id = TutorialStepId.InitialClimb,
                Title = "CLIMB",
                Detail = "Too much nose-up costs speed; too little and the climb stops. Make small corrections, then hold the attitude.",
                Success = "Nice, steady climb",
                Callouts = new[]
                {
                    new TutorialCallout
                    {
                        Text = "Lower the nose a little to keep your speed",
                        KeyboardText = "Tap W to lower the nose a little and keep your speed",
                        Highlights = CoachHighlight.Stick, SustainSeconds = 0.5f,
                        When = c => c.Airborne && c.Ias < c.SlowKnots,
                    },
                    new TutorialCallout
                    {
                        Text = "Ease the stick back a little to climb",
                        KeyboardText = "Hold S a little to climb",
                        Highlights = CoachHighlight.Stick, SustainSeconds = correction,
                        When = c => c.Airborne && c.VerticalSpeed < -100f,
                    },
                    new TutorialCallout
                    {
                        Text = "Keep the wings level for now",
                        Highlights = CoachHighlight.Stick, SustainSeconds = correction,
                        When = c => c.Bank > 20f,
                    },
                    new TutorialCallout
                    {
                        Text = "Climb at about " + climb + " KT, wings level",
                        AssistedText = "Climb at about " + climb + " KT. Let go: the trim holds it",
                    },
                },
                Hints = new[]
                {
                    new TutorialHint
                    {
                        Text = "Full power, nose a little above the horizon",
                        KeyboardText = "Full power (Left Shift), nose a little above the horizon",
                        Highlights = CoachHighlight.Throttle | CoachHighlight.Stick, AfterSeconds = 40f, SustainSeconds = 2f,
                        When = c => c.Airborne && c.HeightFeet < 200f,
                    },
                },
                OnTrack = c => c.SteadyClimb,
                Complete = c => c.Airborne && c.HeightFeet >= s.climbTargetFeet && c.Bank < 20f && c.Ias >= c.SlowKnots,
                CompleteSustainSeconds = 1.5f,
                Checkpoint = TutorialCheckpoint.Runway,
            };

            steps[4] = new TutorialStep
            {
                Id = TutorialStepId.Turn,
                Title = "TURN",
                Detail = "Bank to turn: 15 to 25 degrees is plenty. The arrow on the guide points to the lighthouse, a white tower on the coast.",
                Success = "On course for the lighthouse",
                Waypoints = new[] { PrototypeWorld.LighthouseId },
                Guides = nav,
                Callouts = new[]
                {
                    new TutorialCallout
                    {
                        Text = "Gentler: keep the bank under 30°",
                        Highlights = CoachHighlight.Stick, SustainSeconds = 1f,
                        When = c => c.Bank > s.steepBankDegrees,
                    },
                    new TutorialCallout
                    {
                        Text = "Now level the wings and fly straight",
                        KeyboardText = "Now level the wings with A or D and fly straight",
                        Highlights = CoachHighlight.Stick,
                        When = c => c.OnCourse,
                    },
                    new TutorialCallout
                    {
                        Text = "Turn right to the lighthouse: tilt the stick right gently",
                        AssistedText = "Turn right to the lighthouse: bank gently, then let go",
                        KeyboardText = "Turn right to the lighthouse: hold D gently",
                        Highlights = CoachHighlight.Stick,
                        When = c => c.RelativeBearing > 0f,
                    },
                    new TutorialCallout
                    {
                        Text = "Turn left to the lighthouse: tilt the stick left gently",
                        AssistedText = "Turn left to the lighthouse: bank gently, then let go",
                        KeyboardText = "Turn left to the lighthouse: hold A gently",
                        Highlights = CoachHighlight.Stick,
                    },
                },
                Hints = new[]
                {
                    new TutorialHint
                    {
                        Text = "Follow the arrow on the guide, top left",
                        AfterSeconds = 30f, SustainSeconds = 3f,
                        When = c => c.OffCourse,
                    },
                },
                OnTrack = c => c.OnCourse && c.WingsLevel,
                Complete = c => c.OnCourse && c.WingsLevel,
                CompleteSustainSeconds = 2f,
                Checkpoint = TutorialCheckpoint.Airborne,
            };

            steps[5] = new TutorialStep
            {
                Id = TutorialStepId.Cruise,
                Title = "CRUISE",
                Detail = "Small stick movements are enough. Power sets your height: more to climb, less to descend.",
                Success = "Harbour reached. Time to head home",
                Waypoints = new[] { PrototypeWorld.LighthouseId, PrototypeWorld.HarbourId },
                WaypointReached = new[] { "Lighthouse reached. Follow the coast south" },
                Guides = nav,
                Callouts = new[]
                {
                    new TutorialCallout
                    {
                        Text = "Climb a little: stay above " + minimumCruise + " FT",
                        Highlights = CoachHighlight.Stick | CoachHighlight.Throttle, SustainSeconds = correction,
                        When = c => c.Airborne && c.HeightFeet < s.minimumCruiseFeet,
                    },
                    new TutorialCallout
                    {
                        Text = "Turn towards the arrow",
                        Highlights = CoachHighlight.Stick, SustainSeconds = correction,
                        When = c => c.OffCourse,
                    },
                    new TutorialCallout { Text = "Fly over the lighthouse", When = c => c.WaypointIndex == 0 },
                    new TutorialCallout { Text = "Follow the coast south to the harbour" },
                },
                Hints = new[]
                {
                    new TutorialHint
                    {
                        Text = "Enjoy the view: tap CAM for the cockpit",
                        KeyboardText = "Enjoy the view: press C for the cockpit",
                        Highlights = CoachHighlight.Camera, AfterSeconds = 20f, SustainSeconds = 3f,
                        When = c => c.Input.View == ViewMode.External && c.OnCourse && c.HeightFeet >= s.minimumCruiseFeet,
                    },
                },
                OnTrack = c => c.HasTarget && Mathf.Abs(c.RelativeBearing) < 25f && c.HeightFeet >= s.minimumCruiseFeet,
                Checkpoint = TutorialCheckpoint.Airborne,
            };

            steps[6] = new TutorialStep
            {
                Id = TutorialStepId.Return,
                Title = "RETURN",
                Detail = "Line up a few kilometres out so there is time to settle. Less power to descend; the approach lights appear once you are lined up.",
                Success = "Lined up with the runway",
                Waypoints = new[] { FinalEntryId, ThresholdId },
                WaypointReached = new[] { "Now turn to line up with the runway" },
                CompleteOnLastWaypoint = false,
                Guides = nav,
                Callouts = new[]
                {
                    new TutorialCallout
                    {
                        Text = "Reduce power a little: descend to about 800 FT",
                        KeyboardText = "Left Ctrl reduces power: descend to about 800 FT",
                        Highlights = CoachHighlight.Throttle, SustainSeconds = correction,
                        When = c => c.HeightFeet > s.returnDescentFeet && c.TargetDistance < 4500f,
                    },
                    new TutorialCallout
                    {
                        Text = "Turn towards the arrow to line up",
                        Highlights = CoachHighlight.Stick, SustainSeconds = correction,
                        When = c => c.OffCourse,
                    },
                    new TutorialCallout { Text = "Head back: follow the arrow to the runway" },
                },
                Hints = new[]
                {
                    new TutorialHint
                    {
                        Text = "Follow the arrow back towards the airfield",
                        AfterSeconds = 45f, SustainSeconds = 1f,
                        When = c => c.MovingAwaySeconds > 15f,
                    },
                },
                OnTrack = c => c.HasTarget && Mathf.Abs(c.RelativeBearing) < 25f,
                Complete = c => c.Airborne && c.OnApproachPhase && c.LinedUp,
                CompleteSustainSeconds = 1f,
                Checkpoint = TutorialCheckpoint.Airborne,
            };

            steps[7] = new TutorialStep
            {
                Id = TutorialStepId.ApproachAndLanding,
                Title = "LANDING",
                Detail = "Arrive at about " + approachFlaps + "-" + approachClean + " KT on the light path. Over the runway, idle the power and raise the nose gently so the main wheels touch first.",
                Success = "Landed. Welcome back!",
                Guides = approach,
                Actions = 1 << (int)ContextAction.Flaps,
                Callouts = new[]
                {
                    new TutorialCallout
                    {
                        Text = "Idle power and hold BRAKE to stop",
                        KeyboardText = "Left Ctrl to idle, hold Space to stop",
                        Highlights = CoachHighlight.Throttle | CoachHighlight.Brake,
                        When = c => c.TouchedDown && c.OnGround,
                    },
                    new TutorialCallout
                    {
                        Text = "Idle power, raise the nose gently to touch down",
                        KeyboardText = "Left Ctrl to idle, hold S gently to touch down",
                        Highlights = CoachHighlight.Throttle | CoachHighlight.Stick,
                        When = c => c.Airborne && c.HeightFeet < s.flareHeightFeet,
                    },
                    new TutorialCallout
                    {
                        Text = "Too slow: lower the nose, add a little power",
                        Highlights = CoachHighlight.Throttle | CoachHighlight.Stick, SustainSeconds = 0.5f,
                        When = c => c.Airborne && c.Ias < c.SlowKnots,
                    },
                    new TutorialCallout
                    {
                        Text = "Too fast: reduce power, nose a little up",
                        Highlights = CoachHighlight.Throttle, SustainSeconds = correction,
                        When = c => c.Airborne && c.Ias > c.ApproachKnots + s.approachFastMarginKnots,
                    },
                    new TutorialCallout
                    {
                        Text = "High: reduce power to descend",
                        KeyboardText = "High: Left Ctrl to reduce power and descend",
                        Highlights = CoachHighlight.Throttle, SustainSeconds = correction,
                        When = c => c.Airborne && c.High,
                    },
                    new TutorialCallout
                    {
                        Text = "Low: add a little power",
                        KeyboardText = "Low: Left Shift to add a little power",
                        Highlights = CoachHighlight.Throttle, SustainSeconds = correction,
                        When = c => c.Airborne && c.Low,
                    },
                    new TutorialCallout
                    {
                        Text = "Steer towards the yellow marker to line up",
                        Highlights = CoachHighlight.Stick, SustainSeconds = correction,
                        When = c => c.Airborne && c.Input.Runway.Valid && Mathf.Abs(c.Input.Runway.CrossTrackMetres) > s.centrelineCueMetres,
                    },
                    new TutorialCallout
                    {
                        Text = "Two white, two red lights: on the path. About " + approachFlaps + " KT",
                        When = c => c.Input.FlapSelection > 0.05f,
                    },
                    new TutorialCallout { Text = "Two white, two red lights: on the path. About " + approachClean + " KT" },
                },
                Hints = new[]
                {
                    new TutorialHint
                    {
                        Text = "Optional: FLAPS let you fly the approach slower",
                        KeyboardText = "Optional: ] lowers the flaps so you can fly slower",
                        Highlights = CoachHighlight.Flaps, AfterSeconds = 3f, SustainSeconds = 1f,
                        When = c => c.Airborne && c.Input.FlapSelection < 0.05f && c.Ias < 85f
                                    && c.Input.Runway.Valid && c.Input.Runway.DistanceToThresholdMetres > 1500f,
                    },
                },
                OnTrack = c => c.Airborne && !c.High && !c.Low && Mathf.Abs(c.Ias - c.ApproachKnots) < 10f
                               && c.Input.Runway.Valid && Mathf.Abs(c.Input.Runway.CrossTrackMetres) <= s.centrelineCueMetres
                               && c.HeightFeet >= s.flareHeightFeet,
                QuietAfterSeconds = 6f,
                Complete = c => c.TouchedDown && !c.LandingCrashed && c.OnGround && c.Groundspeed <= s.stoppedKnots,
                CompleteSustainSeconds = 1f,
                Checkpoint = TutorialCheckpoint.FinalApproach,
            };

            for (int i = 0; i < steps.Length; i++)
                steps[i].Label = (i + 1) + "/" + StepCount + "  " + steps[i].Title;
            return steps;
        }
    }
}
