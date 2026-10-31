namespace OpenSkies.FlightState
{
    public enum TouchControlsVisibility
    {
        /// <summary>Shown on mobile platforms, when a touchscreen is present, and in the Editor.</summary>
        Auto,
        Always,
        Never,
    }

    /// <summary>
    /// Progressive-disclosure rules for what the screen shows, kept in one place
    /// so they are consistent and unit-testable. UI and input code ask these
    /// functions instead of deciding on their own.
    /// </summary>
    public static class DisclosurePolicy
    {
        /// <summary>Development overlays (telemetry line, touch diagnostics) never appear in release builds.</summary>
        public static bool ShowDevelopmentOverlay(bool enabledInSettings, bool isDevelopmentBuild) =>
            enabledInSettings && isDevelopmentBuild;

        /// <summary>Wheel brakes are only offered with weight on wheels.</summary>
        public static bool ShowBrakeControl(in FlightTelemetry telemetry) => telemetry.WeightOnWheels;

        public static bool ShowTouchControls(TouchControlsVisibility mode, bool isMobilePlatform, bool isEditor, bool hasTouchscreen)
        {
            switch (mode)
            {
                case TouchControlsVisibility.Always: return true;
                case TouchControlsVisibility.Never: return false;
                default: return isMobilePlatform || isEditor || hasTouchscreen;
            }
        }
    }
}
