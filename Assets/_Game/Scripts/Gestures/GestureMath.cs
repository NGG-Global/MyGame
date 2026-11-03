using UnityEngine;

namespace OpenSkies.Gestures
{
    /// <summary>Pure functions behind gesture recognition and the motion of gesture-driven controls.</summary>
    public static class GestureMath
    {
        /// <summary>
        /// Classifies a movement as up, down, left or right when it lies within
        /// <paramref name="toleranceDegrees"/> of that axis; otherwise None.
        /// </summary>
        public static SwipeDirection Direction(Vector2 delta, float toleranceDegrees)
        {
            if (delta.sqrMagnitude < 1e-8f) return SwipeDirection.None;
            float tolerance = Mathf.Clamp(toleranceDegrees, 0f, 45f);
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg; // 0 = right, 90 = up
            if (Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) <= tolerance) return SwipeDirection.Up;
            if (Mathf.Abs(Mathf.DeltaAngle(angle, -90f)) <= tolerance) return SwipeDirection.Down;
            if (Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) <= tolerance) return SwipeDirection.Right;
            if (Mathf.Abs(Mathf.DeltaAngle(angle, 180f)) <= tolerance) return SwipeDirection.Left;
            return SwipeDirection.None;
        }

        /// <summary>Frame-rate independent first-order smoothing towards a target.</summary>
        public static Vector2 Smooth(Vector2 current, Vector2 target, float timeConstant, float deltaTime)
        {
            if (timeConstant <= 0f || deltaTime <= 0f) return target;
            float t = 1f - Mathf.Exp(-deltaTime / timeConstant);
            return Vector2.Lerp(current, target, t);
        }

        /// <summary>
        /// A level index from a one-dimensional travel with a dead band at each
        /// boundary: the level only changes once the travel has crossed a boundary
        /// by <paramref name="hysteresis"/>, so a finger resting on a boundary never
        /// flickers between two levels.
        /// </summary>
        public static int SteppedLevel(float travel, float stepDistance, float hysteresis, int currentLevel, int minLevel, int maxLevel)
        {
            if (stepDistance <= 0f || float.IsNaN(travel) || float.IsInfinity(travel)) return Mathf.Clamp(currentLevel, minLevel, maxLevel);
            int level = Mathf.Clamp(currentLevel, minLevel, maxLevel);
            float band = Mathf.Clamp(hysteresis, 0f, stepDistance * 0.49f);
            // Travel is measured from level 0; the finger is at level n when travel is around n * step.
            while (level < maxLevel && travel >= (level + 0.5f) * stepDistance + band) level++;
            while (level > minLevel && travel <= (level - 0.5f) * stepDistance - band) level--;
            return level;
        }

        /// <summary>Ease-out cubic: fast start, gentle settle. For controls appearing under the finger.</summary>
        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        /// <summary>Ease-in-out (smoothstep), for returns and collapses.</summary>
        public static float EaseInOut(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// A restrained spring settle: a single soft overshoot of at most a few
        /// percent, used for the arc unfolding and the lever knob. Returns 0-1 (with
        /// the overshoot above 1 briefly) for progress 0-1.
        /// </summary>
        public static float SoftSpring(float t, float overshoot = 0.04f)
        {
            t = Mathf.Clamp01(t);
            float eased = EaseOut(t);
            float bump = Mathf.Sin(t * Mathf.PI) * overshoot * t;
            return eased + bump;
        }

        /// <summary>
        /// Rubber-banding beyond a limit: the first <paramref name="limit"/> units pass
        /// through, further movement is compressed so the control feels held by its stop.
        /// </summary>
        public static float RubberBand(float value, float limit, float resistance = 0.35f)
        {
            float magnitude = Mathf.Abs(value);
            if (magnitude <= limit) return value;
            float beyond = (magnitude - limit) * Mathf.Clamp01(resistance);
            return Mathf.Sign(value) * (limit + beyond);
        }

        /// <summary>Angle in degrees of a vector, 0 = right, 90 = up, in [0, 360).</summary>
        public static float AngleDegrees(Vector2 v)
        {
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            return angle < 0f ? angle + 360f : angle;
        }

        /// <summary>Unit vector at an angle in degrees (0 = right, 90 = up).</summary>
        public static Vector2 FromAngle(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        public static float Finite(float value, float fallback = 0f) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
