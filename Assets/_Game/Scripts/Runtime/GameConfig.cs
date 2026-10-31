using UnityEngine;

namespace Game
{
    /// <summary>
    /// Tunable settings. Bootstrap loads "GameConfig" from a Resources folder if one
    /// exists, otherwise it uses an in-memory instance with the defaults below.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        public const string ResourcePath = "GameConfig";

        [Min(1)] public int targetFrameRate = 60;
        [Min(1)] public int pointsPerTap = 1;
        public Color backgroundColor = new Color(0.12f, 0.14f, 0.2f);
        public Vector2 referenceResolution = new Vector2(1080f, 1920f);

        public static GameConfig Load()
        {
            var config = Resources.Load<GameConfig>(ResourcePath);
            return config != null ? config : CreateInstance<GameConfig>();
        }
    }
}
