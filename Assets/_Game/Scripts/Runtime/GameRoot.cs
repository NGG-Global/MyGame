using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// Thin MonoBehaviour that owns the game's plain C# objects and forwards
    /// Unity callbacks to them. Game logic does not live here.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public ScoreCounter Score { get; private set; }
        public GameConfig Config { get; private set; }

        Text _scoreLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null; // supports Enter Play Mode without domain reload

        public void Init(GameConfig config, Text scoreLabel)
        {
            Instance = this;
            Config = config;
            Score = new ScoreCounter();
            Score.Changed += OnScoreChanged;
            _scoreLabel = scoreLabel;
            OnScoreChanged(Score.Value);
        }

        void Update()
        {
            // Pointer covers touch on device and mouse in the WebGL playtest. No allocations.
            var pointer = Pointer.current;
            if (Score != null && pointer != null && pointer.press.wasPressedThisFrame)
                Score.Add(Config.pointsPerTap);
        }

        void OnScoreChanged(int value)
        {
            // Allocates a string only when the score changes, never per frame.
            if (_scoreLabel != null) _scoreLabel.text = value.ToString();
        }

        void OnDestroy()
        {
            if (Score != null) Score.Changed -= OnScoreChanged;
            if (Instance == this) Instance = null;
        }
    }
}
