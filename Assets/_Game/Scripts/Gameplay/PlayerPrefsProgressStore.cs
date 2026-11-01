using OpenSkies.Missions;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>Progress kept on the device with PlayerPrefs (no account, no cloud).</summary>
    public sealed class PlayerPrefsProgressStore : IProgressStore
    {
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save(); // mobile apps can be killed without a clean quit
    }
}
