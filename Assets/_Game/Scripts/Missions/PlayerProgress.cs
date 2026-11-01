using System.Collections.Generic;

namespace OpenSkies.Missions
{
    /// <summary>Where the player is with the First Flight. Values are stable (saved).</summary>
    public enum TutorialStatus
    {
        NotStarted = 0,
        Skipped = 1,
        Completed = 2,
    }

    /// <summary>Local key-value storage for progress (PlayerPrefs in the game, memory in tests).</summary>
    public interface IProgressStore
    {
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        /// <summary>Writes to disk now: mobile apps can be killed without a clean quit.</summary>
        void Save();
    }

    public sealed class MemoryProgressStore : IProgressStore
    {
        readonly Dictionary<string, int> _values = new Dictionary<string, int>();
        public int Saves { get; private set; }
        public int GetInt(string key, int fallback) => _values.TryGetValue(key, out int value) ? value : fallback;
        public void SetInt(string key, int value) => _values[key] = value;
        public void Save() => Saves++;
    }

    /// <summary>
    /// The player's progress, kept on the device only (no account, no cloud):
    /// First Flight status and the best Landing Challenge score. Flight settings,
    /// the HUD profile and the camera view are remembered by their own preference
    /// classes in the Gameplay layer.
    /// </summary>
    public sealed class PlayerProgress
    {
        public const string TutorialKey = "OpenSkies.Progress.FirstFlight";
        public const string BestLandingKey = "OpenSkies.Progress.BestLanding";
        public const string LandingsKey = "OpenSkies.Progress.Landings";
        public const int NoScore = -1;

        readonly IProgressStore _store;

        public PlayerProgress(IProgressStore store)
        {
            _store = store ?? new MemoryProgressStore();
            int tutorial = _store.GetInt(TutorialKey, (int)TutorialStatus.NotStarted);
            Tutorial = tutorial >= (int)TutorialStatus.NotStarted && tutorial <= (int)TutorialStatus.Completed
                ? (TutorialStatus)tutorial : TutorialStatus.NotStarted;
            int best = _store.GetInt(BestLandingKey, NoScore);
            BestLandingScore = best >= 0 && best <= 100 ? best : NoScore;
            Landings = System.Math.Max(0, _store.GetInt(LandingsKey, 0));
        }

        public TutorialStatus Tutorial { get; private set; }
        public int BestLandingScore { get; private set; }
        public int Landings { get; private set; }
        public bool HasBestLanding => BestLandingScore != NoScore;
        /// <summary>The entry screen recommends the First Flight until it has been flown or skipped.</summary>
        public bool RecommendFirstFlight => Tutorial == TutorialStatus.NotStarted;

        public void MarkTutorialCompleted()
        {
            if (Tutorial == TutorialStatus.Completed) return;
            Tutorial = TutorialStatus.Completed;
            _store.SetInt(TutorialKey, (int)Tutorial);
            _store.Save();
        }

        /// <summary>Skipping never downgrades a completed First Flight.</summary>
        public void MarkTutorialSkipped()
        {
            if (Tutorial != TutorialStatus.NotStarted) return;
            Tutorial = TutorialStatus.Skipped;
            _store.SetInt(TutorialKey, (int)Tutorial);
            _store.Save();
        }

        /// <summary>Records a scored landing; returns true for a new best.</summary>
        public bool RecordLanding(int score)
        {
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            Landings++;
            _store.SetInt(LandingsKey, Landings);
            bool best = score > BestLandingScore;
            if (best)
            {
                BestLandingScore = score;
                _store.SetInt(BestLandingKey, score);
            }
            _store.Save();
            return best;
        }
    }
}
