using UnityEngine;

namespace OpenSkies.CameraSystems
{
    /// <summary>Pure math for the cockpit look: limits, recentring and field of view.</summary>
    public static class CockpitLookMath
    {
        /// <summary>Look offset (x = yaw right, y = pitch up, degrees) kept inside comfortable limits.</summary>
        public static Vector2 Clamp(Vector2 look, Vector2 yawLimits, Vector2 pitchLimits) =>
            new Vector2(Mathf.Clamp(Finite(look.x), yawLimits.x, yawLimits.y), Mathf.Clamp(Finite(look.y), pitchLimits.x, pitchLimits.y));

        /// <summary>Look offset during a smooth return to the resting direction; progress 0 → 1 with ease in and out.</summary>
        public static Vector2 Recentre(Vector2 from, float progress)
        {
            float t = Mathf.Clamp01(progress);
            float remaining = 1f - t * t * (3f - 2f * t);
            return new Vector2(from.x * remaining, from.y * remaining);
        }

        public static float ClampFieldOfView(float degrees, float min, float max) => Mathf.Clamp(Finite(degrees, 0.5f * (min + max)), min, max);

        /// <summary>Screen drag in pixels to look degrees, independent of resolution (a full screen height turns <paramref name="degreesPerScreenHeight"/>).</summary>
        public static Vector2 DragToLook(Vector2 dragPixels, float screenHeightPixels, float degreesPerScreenHeight, bool followFinger)
        {
            if (screenHeightPixels <= 0f) return Vector2.zero;
            float scale = degreesPerScreenHeight / screenHeightPixels * (followFinger ? 1f : -1f);
            return new Vector2(dragPixels.x * scale, dragPixels.y * scale);
        }

        static float Finite(float value, float fallback = 0f) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
