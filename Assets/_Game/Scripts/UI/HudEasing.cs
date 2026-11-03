using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// The motion language shared by every gesture-driven control: fast start
    /// and gentle settle for things appearing under the finger, smooth ease for
    /// returns, and a single soft overshoot of a few percent at most for things
    /// that unfold. No bouncing, no flashing. Pure.
    /// </summary>
    public static class HudEasing
    {
        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        public static float EaseInOut(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Ease-out with one soft overshoot (at most about 1 + overshoot), settling exactly at 1.</summary>
        public static float SoftSpring(float t, float overshoot = 0.04f)
        {
            t = Mathf.Clamp01(t);
            return EaseOut(t) + Mathf.Sin(t * Mathf.PI) * overshoot * t;
        }

        /// <summary>Frame-rate independent approach towards a target: 1 - e^(-dt/tau).</summary>
        public static float Approach(float current, float target, float timeConstant, float deltaTime)
        {
            if (timeConstant <= 0f || deltaTime <= 0f) return target;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-deltaTime / timeConstant));
        }
    }
}
