using OpenSkies.Awareness;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// The player's HUD and view choices (HUD profile, motion preference, cockpit
    /// field of view), kept between sessions with PlayerPrefs. A settings menu will
    /// expose the motion preference; until then it is set on the ContextualHud
    /// component. The cockpit look direction is kept for the session only.
    /// </summary>
    public static class HudPreferences
    {
        const string ProfileKey = "OpenSkies.Hud.Profile";
        const string MotionKey = "OpenSkies.Hud.Motion";
        const string CockpitFieldOfViewKey = "OpenSkies.View.CockpitFieldOfView";

        public static HudProfile LoadProfile(HudProfile fallback)
        {
            int value = PlayerPrefs.GetInt(ProfileKey, (int)fallback);
            return value >= (int)HudProfile.Cinematic && value <= (int)HudProfile.Detailed ? (HudProfile)value : fallback;
        }

        public static void SaveProfile(HudProfile profile)
        {
            PlayerPrefs.SetInt(ProfileKey, (int)profile);
            PlayerPrefs.Save(); // mobile apps can be killed without a clean quit
        }

        public static MotionPreference LoadMotion(MotionPreference fallback)
        {
            int value = PlayerPrefs.GetInt(MotionKey, (int)fallback);
            return value >= (int)MotionPreference.FollowSystem && value <= (int)MotionPreference.Full ? (MotionPreference)value : fallback;
        }

        public static void SaveMotion(MotionPreference preference)
        {
            PlayerPrefs.SetInt(MotionKey, (int)preference);
            PlayerPrefs.Save();
        }

        public static float LoadCockpitFieldOfView(float fallback)
        {
            float value = PlayerPrefs.GetFloat(CockpitFieldOfViewKey, fallback);
            return float.IsNaN(value) || value <= 0f ? fallback : value;
        }

        public static void SaveCockpitFieldOfView(float degrees)
        {
            PlayerPrefs.SetFloat(CockpitFieldOfViewKey, degrees);
            PlayerPrefs.Save();
        }
    }
}
