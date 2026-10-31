using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// App-wide settings. AppBootstrap loads "GameConfig" from a Resources folder
    /// if one exists, otherwise it uses an in-memory instance with these defaults.
    /// </summary>
    [CreateAssetMenu(menuName = "Open Skies/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        public const string ResourcePath = "GameConfig";

        public const int BaselineFrameRate = 30;
        public const int HighFrameRate = 60;

        [Tooltip("30 is the baseline target. Set 60 to test the optional high-frame-rate target on capable devices.")]
        public int targetFrameRate = BaselineFrameRate;
        [Tooltip("Longest frame time the physics will catch up on, seconds.")]
        public float maxPhysicsCatchUpSeconds = 0.1f;

        public static GameConfig Load()
        {
            var config = Resources.Load<GameConfig>(ResourcePath);
            return config != null ? config : CreateInstance<GameConfig>();
        }
    }
}
