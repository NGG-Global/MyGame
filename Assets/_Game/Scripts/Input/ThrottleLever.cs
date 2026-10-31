using UnityEngine;

namespace OpenSkies.FlightInput
{
    /// <summary>
    /// A throttle stays where the pilot leaves it, unlike a spring-centred stick.
    /// Buttons or triggers move the lever at a fixed rate; sliders nudge it.
    /// </summary>
    public sealed class ThrottleLever
    {
        public float Position { get; private set; }
        public float TravelPerSecond { get; }

        public ThrottleLever(float travelPerSecond = 0.5f, float initialPosition = 0f)
        {
            TravelPerSecond = Mathf.Max(0f, travelPerSecond);
            Position = Mathf.Clamp01(initialPosition);
        }

        /// <param name="direction">-1 to close, +1 to open, 0 to hold.</param>
        public float Move(float direction, float deltaTime)
        {
            if (float.IsNaN(direction) || deltaTime <= 0f) return Position;
            Position = Mathf.Clamp01(Position + Mathf.Clamp(direction, -1f, 1f) * TravelPerSecond * deltaTime);
            return Position;
        }

        /// <summary>Moves the lever by a fraction of its travel (e.g. from a slider drag).</summary>
        public float Nudge(float delta)
        {
            if (float.IsNaN(delta)) return Position;
            Position = Mathf.Clamp01(Position + delta);
            return Position;
        }

        public void Set(float position) => Position = Mathf.Clamp01(position);
    }
}
