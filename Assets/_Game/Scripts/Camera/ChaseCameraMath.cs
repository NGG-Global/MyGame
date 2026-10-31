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

        /// <summary>Frame-rate independent smoothing factor for Lerp: 1 - e^(-sharpness·dt).</summary>
        public static float SmoothingFactor(float sharpness, float deltaTime)
        {
            if (deltaTime <= 0f) return 0f;
            if (sharpness <= 0f) return 1f; // no smoothing: follow exactly
            return 1f - Mathf.Exp(-sharpness * deltaTime);
        }
    }
}
