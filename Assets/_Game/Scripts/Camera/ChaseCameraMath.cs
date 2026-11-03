using UnityEngine;

namespace OpenSkies.CameraSystems
{
    /// <summary>Pure functions behind the chase camera, so they can be unit-tested.</summary>
    public static class ChaseCameraMath
    {
        /// <summary>
        /// Camera position behind and above the target. Uses only the target's
        /// heading, so the horizon stays level when the aircraft banks.
        /// </summary>
        public static Vector3 DesiredPosition(Vector3 targetPosition, Quaternion targetRotation, float distance, float height)
        {
            Vector3 forward = targetRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward; // nose straight up or down
            forward.Normalize();
            return targetPosition - forward * distance + Vector3.up * height;
        }

        /// <summary>
        /// Camera position for a temporary free look around the target: the chase
        /// position turned by <paramref name="yawDegrees"/> about the target (positive =
        /// the camera moves to the right of the tail, looking from the right) and raised
        /// by <paramref name="pitchDegrees"/> (positive = looking down from higher up).
        /// With zero offsets it is <see cref="DesiredPosition"/>.
        /// </summary>
        public static Vector3 OrbitPosition(Vector3 targetPosition, Quaternion targetRotation, float distance, float height,
            float yawDegrees, float pitchDegrees)
        {
            Vector3 forward = targetRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 back = Quaternion.AngleAxis(yawDegrees, Vector3.up) * -forward;
            float pitch = Mathf.Clamp(pitchDegrees, -89f, 89f) * Mathf.Deg2Rad;
            Vector3 offset = back * (distance * Mathf.Cos(pitch)) + Vector3.up * (height + distance * Mathf.Sin(pitch));
            return targetPosition + offset;
        }

        /// <summary>Frame-rate independent smoothing factor for Lerp: 1 - e^(-sharpness·dt).</summary>
        public static float SmoothingFactor(float sharpness, float deltaTime)
        {
            if (deltaTime <= 0f) return 0f;
            if (sharpness <= 0f) return 1f; // no smoothing: follow exactly
            return 1f - Mathf.Exp(-sharpness * deltaTime);
        }
    }
}
