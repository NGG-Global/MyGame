using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Force from one wheel in contact with the ground: spring-damper suspension
    /// along the ground normal, tyre side friction, rolling resistance and brakes.
    /// Pure function; the controller supplies the contact from a raycast.
    /// </summary>
    public static class LandingGear
    {
        /// <param name="compression">How far the wheel is pushed up from full extension, m (≥ 0).</param>
        /// <param name="contactVelocity">World velocity of the aircraft at the contact point, m/s.</param>
        /// <param name="groundNormal">Unit normal of the ground.</param>
        /// <param name="wheelForward">Direction the wheel rolls (includes nosewheel steering), world.</param>
        /// <param name="brake">0-1, applied only if the wheel is braked.</param>
        /// <returns>World-space force to apply at the contact point, N.</returns>
        public static Vector3 ContactForce(float compression, Vector3 contactVelocity, Vector3 groundNormal,
            Vector3 wheelForward, float brake, in WheelDefinition wheel, AircraftConfiguration c)
        {
            if (compression <= 0f) return Vector3.zero;

            // Suspension: never pulls the aircraft down into the ground.
            float compressionSpeed = -Vector3.Dot(contactVelocity, groundNormal);
            float normalForce = Mathf.Max(0f, wheel.springRate * compression + wheel.damperRate * compressionSpeed);
            if (normalForce <= 0f) return Vector3.zero;

            // Tyre frame on the ground plane.
            Vector3 forward = Vector3.ProjectOnPlane(wheelForward, groundNormal);
            if (forward.sqrMagnitude < 1e-8f) return groundNormal * normalForce;
            forward.Normalize();
            Vector3 right = Vector3.Cross(groundNormal, forward);

            float forwardSpeed = Vector3.Dot(contactVelocity, forward);
            float sideSpeed = Vector3.Dot(contactVelocity, right);

            // Side force grows with slip and saturates at μ·N.
            float slip = Mathf.Clamp(sideSpeed / Mathf.Max(c.lateralSlipSpeed, 1e-3f), -1f, 1f);
            float sideForce = -slip * c.lateralFriction * normalForce;

            // Rolling resistance and brakes oppose rolling; tanh fades them in near
            // zero speed so the aircraft rests without jitter.
            float friction = c.rollingResistance + (wheel.braked ? c.brakeFriction * Mathf.Clamp01(brake) : 0f);
            float rollingForce = -Tanh(forwardSpeed / Mathf.Max(c.frictionSmoothingSpeed, 1e-3f)) * friction * normalForce;

            return groundNormal * normalForce + right * sideForce + forward * rollingForce;
        }

        static float Tanh(float x)
        {
            if (x > 10f) return 1f;
            if (x < -10f) return -1f;
            float e = Mathf.Exp(2f * x);
            return (e - 1f) / (e + 1f);
        }
    }
}
