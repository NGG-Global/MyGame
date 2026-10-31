using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>Attitude angles from a rotation, in pilot conventions.</summary>
    public static class AttitudeMath
    {
        /// <summary>Compass heading of the nose: 0 = +Z (north), 90 = +X (east).</summary>
        public static float HeadingDegrees(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            if (forward.x * forward.x + forward.z * forward.z < 1e-8f) return 0f; // nose straight up or down
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return heading < 0f ? heading + 360f : heading;
        }

        /// <summary>Nose up positive.</summary>
        public static float PitchDegrees(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>Right wing down positive.</summary>
        public static float BankDegrees(Quaternion rotation)
        {
            Vector3 right = rotation * Vector3.right;
            return Mathf.Asin(Mathf.Clamp(-right.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
