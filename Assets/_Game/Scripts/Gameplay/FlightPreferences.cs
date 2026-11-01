using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>The player's flight settings, kept between sessions (PlayerPrefs): assistance profile, sensitivity and options.</summary>
    public static class FlightPreferences
    {
        const string ProfileKey = "OpenSkies.Flight.AssistProfile";
        const string SensitivityKey = "OpenSkies.Flight.Sensitivity";
        const string StandardAutoTrimKey = "OpenSkies.Flight.StandardAutoTrim";
        const string AssistedStallPreventionKey = "OpenSkies.Flight.AssistedStallPrevention";
        const string GuidanceKey = "OpenSkies.Flight.Guidance";

        public struct Settings
        {
            public FlightAssistProfile Profile;
            public ControlSensitivity Sensitivity;
            public bool StandardAutoTrim;
            public bool AssistedStallPrevention;
            public bool Guidance;
        }

        public static Settings Load(in Settings fallback)
        {
            int profile = PlayerPrefs.GetInt(ProfileKey, (int)fallback.Profile);
            int sensitivity = PlayerPrefs.GetInt(SensitivityKey, (int)fallback.Sensitivity);
            return new Settings
            {
                Profile = FlightAssistProfiles.IsValid(profile) ? (FlightAssistProfile)profile : fallback.Profile,
                Sensitivity = sensitivity >= 0 && sensitivity <= (int)ControlSensitivity.High ? (ControlSensitivity)sensitivity : fallback.Sensitivity,
                StandardAutoTrim = PlayerPrefs.GetInt(StandardAutoTrimKey, fallback.StandardAutoTrim ? 1 : 0) != 0,
                AssistedStallPrevention = PlayerPrefs.GetInt(AssistedStallPreventionKey, fallback.AssistedStallPrevention ? 1 : 0) != 0,
                Guidance = PlayerPrefs.GetInt(GuidanceKey, fallback.Guidance ? 1 : 0) != 0,
            };
        }

        public static void Save(in Settings settings)
        {
            PlayerPrefs.SetInt(ProfileKey, (int)settings.Profile);
            PlayerPrefs.SetInt(SensitivityKey, (int)settings.Sensitivity);
            PlayerPrefs.SetInt(StandardAutoTrimKey, settings.StandardAutoTrim ? 1 : 0);
            PlayerPrefs.SetInt(AssistedStallPreventionKey, settings.AssistedStallPrevention ? 1 : 0);
            PlayerPrefs.SetInt(GuidanceKey, settings.Guidance ? 1 : 0);
            PlayerPrefs.Save(); // mobile apps can be killed without a clean quit
        }
    }
}
