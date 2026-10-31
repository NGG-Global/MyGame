using System;
using UnityEngine;

namespace OpenSkies.FlightInput.TouchControls
{
    [Serializable]
    public sealed class JoystickSettings
    {
        [Tooltip("Knob travel from centre to full deflection, in canvas units (1080 p reference height).")]
        public float radius = 130f;
        [Tooltip("Fraction of the radius that produces no output.")]
        [Range(0f, 0.5f)] public float deadZone = 0.08f;
        [Tooltip("Output gain after the dead zone. Above 1 reaches full deflection before the edge.")]
        [Range(0.25f, 3f)] public float sensitivity = 1f;
        [Tooltip("Response curve exponent per axis. 1 = linear; higher = finer control near centre.")]
        [Range(1f, 3f)] public float responseExponent = 1.6f;
        [Tooltip("Smoothing time constant, seconds. Keep small: it adds latency.")]
        [Range(0f, 0.2f)] public float smoothingTime = 0.04f;
    }

    /// <summary>Pure functions behind the floating joystick.</summary>
    public static class JoystickMath
    {
        /// <summary>Finger offset (canvas units) to a deflection inside the unit circle.</summary>
        public static Vector2 Deflection(Vector2 fingerOffset, float radius)
        {
            if (radius <= 0f) return Vector2.zero;
            Vector2 v = fingerOffset / radius;
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        /// <summary>
        /// Radial dead zone, rescaled so output starts at 0 just outside it, then
        /// per-axis response curve and sensitivity. Result is clamped to -1..1.
        /// </summary>
        public static Vector2 Shape(Vector2 deflection, JoystickSettings s)
        {
            float magnitude = deflection.magnitude;
            if (magnitude <= s.deadZone || magnitude <= 0f) return Vector2.zero;

            float rescaled = Mathf.Clamp01((magnitude - s.deadZone) / Mathf.Max(1f - s.deadZone, 1e-4f));
            Vector2 v = deflection / magnitude * rescaled;
            return new Vector2(Curve(v.x, s), Curve(v.y, s));
        }

        /// <summary>
        /// Screen to flight axes. Dragging down (−y on screen) pulls the stick back:
        /// nose up (+pitch). Dragging right rolls right.
        /// </summary>
        public static void ToFlightAxes(Vector2 shaped, out float pitch, out float roll)
        {
            pitch = -shaped.y;
            roll = shaped.x;
        }

        /// <summary>Frame-rate independent exponential smoothing towards a target.</summary>
        public static Vector2 Smooth(Vector2 current, Vector2 target, float timeConstant, float deltaTime)
        {
            if (deltaTime <= 0f) return current;
            if (timeConstant <= 0f) return target;
            float t = 1f - Mathf.Exp(-deltaTime / timeConstant);
            return Vector2.Lerp(current, target, t);
        }

        /// <summary>Keeps the joystick base fully inside its region.</summary>
        public static Vector2 ClampBase(Vector2 position, Rect region, float radius)
        {
            float minX = region.xMin + radius, maxX = region.xMax - radius;
            float minY = region.yMin + radius, maxY = region.yMax - radius;
            // Region smaller than the joystick: centre it on that axis.
            float x = minX <= maxX ? Mathf.Clamp(position.x, minX, maxX) : region.center.x;
            float y = minY <= maxY ? Mathf.Clamp(position.y, minY, maxY) : region.center.y;
            return new Vector2(x, y);
        }

        static float Curve(float axis, JoystickSettings s)
        {
            float shaped = Mathf.Sign(axis) * Mathf.Pow(Mathf.Abs(axis), s.responseExponent) * s.sensitivity;
            return Mathf.Clamp(shaped, -1f, 1f);
        }
    }

    /// <summary>Pure function behind the throttle slider: relative drag, so touching never makes the throttle jump.</summary>
    public static class ThrottleDragMath
    {
        /// <param name="previousY">Last finger height, canvas units.</param>
        /// <param name="currentY">Current finger height, canvas units.</param>
        /// <param name="trackLength">Finger travel for 0 → 100 %, canvas units.</param>
        /// <param name="gain">1 = full track for full range; lower for finer control.</param>
        public static float Delta(float previousY, float currentY, float trackLength, float gain = 1f)
        {
            if (trackLength <= 0f) return 0f;
            return (currentY - previousY) / trackLength * gain;
        }
    }
}
