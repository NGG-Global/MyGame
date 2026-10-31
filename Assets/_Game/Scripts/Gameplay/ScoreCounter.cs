using System;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Tracks the current score and the best score of the session.
    /// Plain C# so it can be unit-tested without the Editor.
    /// </summary>
    public sealed class ScoreCounter
    {
        public int Value { get; private set; }
        public int Best { get; private set; }

        /// <summary>Raised with the new value whenever <see cref="Value"/> changes.</summary>
        public event Action<int> Changed;

        public ScoreCounter(int best = 0)
        {
            if (best < 0) throw new ArgumentOutOfRangeException(nameof(best), "Best score cannot be negative.");
            Best = best;
        }

        public void Add(int points)
        {
            if (points < 0) throw new ArgumentOutOfRangeException(nameof(points), "Use Reset to lower the score.");
            if (points == 0) return;

            // Saturate instead of overflowing into a negative score.
            Value = Value > int.MaxValue - points ? int.MaxValue : Value + points;
            if (Value > Best) Best = Value;
            Changed?.Invoke(Value);
        }

        public void Reset()
        {
            if (Value == 0) return;
            Value = 0;
            Changed?.Invoke(Value);
        }
    }
}
