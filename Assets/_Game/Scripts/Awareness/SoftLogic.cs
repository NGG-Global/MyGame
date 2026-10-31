using UnityEngine;

namespace OpenSkies.Awareness
{
    /// <summary>
    /// Soft conditions: instead of a hard threshold, a condition is a score that
    /// ramps between two values. Scores combine by multiplication (and) and
    /// <see cref="Mathf.Max(float, float)"/> (or).
    /// </summary>
    public static class SoftLogic
    {
        /// <summary>
        /// 0 at <paramref name="zeroAt"/>, 1 at <paramref name="oneAt"/>, linear in
        /// between and clamped outside. Works in both directions (zeroAt may be
        /// larger than oneAt). If both are equal it is a step up at that value.
        /// </summary>
        public static float Ramp(float value, float zeroAt, float oneAt)
        {
            if (float.IsNaN(value)) return 0f;
            float span = oneAt - zeroAt;
            if (Mathf.Abs(span) < 1e-6f) return value >= oneAt ? 1f : 0f;
            return Mathf.Clamp01((value - zeroAt) / span);
        }

        /// <summary>Blend factor of a first-order low-pass filter for one step.</summary>
        public static float Smoothing(float deltaTime, float timeConstant) =>
            timeConstant > 1e-4f ? 1f - Mathf.Exp(-deltaTime / timeConstant) : 1f;

        /// <summary>The value, or the fallback if it is NaN or infinite (a bad sample must not poison filters).</summary>
        public static float Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
