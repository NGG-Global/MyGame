using OpenSkies.FlightState;

namespace OpenSkies.Cockpit
{
    /// <summary>Everything in the cockpit the pilot can tap. Values are stable; instruments come first.</summary>
    public enum CockpitItem
    {
        AirspeedIndicator = 0,
        AttitudeIndicator = 1,
        Altimeter = 2,
        HeadingIndicator = 3,
        VerticalSpeedIndicator = 4,
        NavigationDisplay = 5,
        IgnitionSwitch = 6,
        LandingLightSwitch = 7,
        NavigationLightsSwitch = 8,
        ParkingBrakeHandle = 9,
        FlapSelector = 10,
    }

    /// <summary>Names, accessible labels and system mapping of cockpit items, in one place.</summary>
    public static class CockpitItems
    {
        public const int Count = 11;
        public const int InstrumentCount = 6;

        public static bool IsInstrument(CockpitItem item) => (int)item < InstrumentCount;

        /// <summary>The aircraft system a control sets, if it is a control.</summary>
        public static bool TryGetSystem(CockpitItem item, out AircraftSystem system)
        {
            switch (item)
            {
                case CockpitItem.IgnitionSwitch: system = AircraftSystem.Ignition; return true;
                case CockpitItem.LandingLightSwitch: system = AircraftSystem.LandingLight; return true;
                case CockpitItem.NavigationLightsSwitch: system = AircraftSystem.NavigationLights; return true;
                case CockpitItem.ParkingBrakeHandle: system = AircraftSystem.ParkingBrake; return true;
                case CockpitItem.FlapSelector: system = AircraftSystem.Flaps; return true;
                default: system = default; return false;
            }
        }

        /// <summary>The control for a system.</summary>
        public static CockpitItem ControlFor(AircraftSystem system)
        {
            switch (system)
            {
                case AircraftSystem.Ignition: return CockpitItem.IgnitionSwitch;
                case AircraftSystem.LandingLight: return CockpitItem.LandingLightSwitch;
                case AircraftSystem.NavigationLights: return CockpitItem.NavigationLightsSwitch;
                case AircraftSystem.ParkingBrake: return CockpitItem.ParkingBrakeHandle;
                default: return CockpitItem.FlapSelector;
            }
        }

        /// <summary>Accessible name: what assistive technology reads and what the focus view is titled.</summary>
        public static string DisplayName(CockpitItem item)
        {
            switch (item)
            {
                case CockpitItem.AirspeedIndicator: return "Airspeed indicator";
                case CockpitItem.AttitudeIndicator: return "Attitude indicator";
                case CockpitItem.Altimeter: return "Altimeter";
                case CockpitItem.HeadingIndicator: return "Heading indicator";
                case CockpitItem.VerticalSpeedIndicator: return "Vertical speed indicator";
                case CockpitItem.NavigationDisplay: return "GPS map";
                case CockpitItem.IgnitionSwitch: return "Engine ignition";
                case CockpitItem.LandingLightSwitch: return "Landing light";
                case CockpitItem.NavigationLightsSwitch: return "Navigation lights";
                case CockpitItem.ParkingBrakeHandle: return "Parking brake";
                case CockpitItem.FlapSelector: return "Flaps";
                default: return "?";
            }
        }

        /// <summary>Accessible hint: what the item shows or does and how to use it.</summary>
        public static string AccessibilityHint(CockpitItem item)
        {
            switch (item)
            {
                case CockpitItem.AirspeedIndicator: return "Indicated airspeed in knots. Tap to enlarge.";
                case CockpitItem.AttitudeIndicator: return "Pitch and bank against the horizon. Tap to enlarge.";
                case CockpitItem.Altimeter: return "Altitude in feet. Tap to enlarge.";
                case CockpitItem.HeadingIndicator: return "Magnetic-style heading, degrees. Tap to enlarge.";
                case CockpitItem.VerticalSpeedIndicator: return "Climb or descent rate, hundreds of feet per minute. Tap to enlarge.";
                case CockpitItem.NavigationDisplay: return "Moving map with the home runway, track up. Tap to enlarge.";
                case CockpitItem.IgnitionSwitch: return "Engine on or off. On starts the engine. Tap to choose.";
                case CockpitItem.LandingLightSwitch: return "Landing light on or off. Tap to choose.";
                case CockpitItem.NavigationLightsSwitch: return "Navigation lights on or off. Tap to choose.";
                case CockpitItem.ParkingBrakeHandle: return "Holds the wheels. Can be set on the ground. Tap to choose.";
                case CockpitItem.FlapSelector: return "Flap position: up, 10, 20 or 30 degrees. Tap to choose.";
                default: return string.Empty;
            }
        }

        /// <summary>Panel placard under a control.</summary>
        public static string Placard(CockpitItem item)
        {
            switch (item)
            {
                case CockpitItem.IgnitionSwitch: return "IGN";
                case CockpitItem.LandingLightSwitch: return "LDG LT";
                case CockpitItem.NavigationLightsSwitch: return "NAV LT";
                case CockpitItem.ParkingBrakeHandle: return "PARK BRK";
                case CockpitItem.FlapSelector: return "FLAPS";
                default: return string.Empty;
            }
        }
    }
}
