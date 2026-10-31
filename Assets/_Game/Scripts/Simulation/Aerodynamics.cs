using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Coefficient-based aerodynamics of a conventional light aircraft. Pure
    /// functions: no Unity objects, fully unit-testable.
    ///
    /// Body axes are Unity's: +X right wing, +Y roof, +Z nose (left-handed).
    /// A positive torque about +X lowers the nose, about +Y yaws the nose right,
    /// about +Z rolls left (raises the right wing).
    /// </summary>
    public static class Aerodynamics
    {
        const float MinAirspeed = 0.5f; // below this the airflow is treated as zero

        /// <summary>
        /// Lift coefficient: linear up to the critical angle, then a smooth fall
        /// towards flat-plate lift k·sin(2α). Symmetric treatment for negative stall.
        /// </summary>
        public static float LiftCoefficient(float alpha, AircraftConfiguration c, out float stallDepth)
        {
            float positiveStall = c.stallAngle * Mathf.Deg2Rad;
            float negativeStall = c.negativeStallAngle * Mathf.Deg2Rad;
            float transition = Mathf.Max(c.stallTransition * Mathf.Deg2Rad, 1e-4f);
            float flatPlate = c.flatPlateLift * Mathf.Sin(2f * alpha);

            if (alpha > positiveStall)
            {
                float peak = c.liftCoefficientAtZeroAlpha + c.liftCurveSlope * positiveStall;
                stallDepth = SmoothStep01((alpha - positiveStall) / transition);
                return Mathf.Lerp(peak, flatPlate, stallDepth);
            }
            if (alpha < negativeStall)
            {
                float peak = c.liftCoefficientAtZeroAlpha + c.liftCurveSlope * negativeStall;
                stallDepth = SmoothStep01((negativeStall - alpha) / transition);
                return Mathf.Lerp(peak, flatPlate, stallDepth);
            }
            stallDepth = 0f;
            return c.liftCoefficientAtZeroAlpha + c.liftCurveSlope * alpha;
        }

        /// <summary>Parasitic + induced drag CL²/(π·e·AR), plus flat-plate drag when stalled.</summary>
        public static float DragCoefficient(float liftCoefficient, float alpha, float stallDepth, AircraftConfiguration c)
        {
            float induced = liftCoefficient * liftCoefficient / (Mathf.PI * c.oswaldEfficiency * c.AspectRatio);
            float sinAlpha = Mathf.Sin(alpha);
            return c.parasiticDrag + induced + c.flatPlateDrag * sinAlpha * sinAlpha * stallDepth;
        }

        /// <param name="bodyVelocity">Velocity of the air relative to the aircraft is −bodyVelocity; m/s, body axes.</param>
        /// <param name="bodyAngularVelocity">rad/s, body axes.</param>
        /// <param name="surfaces">Current control surface positions, −1..1 (same directions as FlightControls).</param>
        /// <param name="density">Air density, kg/m³.</param>
        public static AerodynamicForces Compute(Vector3 bodyVelocity, Vector3 bodyAngularVelocity,
            in FlightControls surfaces, float density, AircraftConfiguration c)
        {
            var result = new AerodynamicForces();
            float airspeed = bodyVelocity.magnitude;
            result.Airspeed = airspeed;
            if (airspeed < MinAirspeed || density <= 0f) return result;

            float alpha = Mathf.Atan2(-bodyVelocity.y, bodyVelocity.z);
            float beta = Mathf.Atan2(bodyVelocity.x, bodyVelocity.z);
            float dynamicPressure = 0.5f * density * airspeed * airspeed;
            float qS = dynamicPressure * c.wingArea;

            float cl = LiftCoefficient(alpha, c, out float stallDepth);
            float cd = DragCoefficient(cl, alpha, stallDepth, c);

            // Lift is perpendicular to the airflow in the aircraft's plane of symmetry;
            // drag opposes the motion through the air.
            Vector3 flowDirection = bodyVelocity / airspeed;
            Vector3 liftDirection = Vector3.Cross(flowDirection, Vector3.right);
            liftDirection = liftDirection.sqrMagnitude > 1e-8f ? liftDirection.normalized : Vector3.up;

            result.Lift = liftDirection * (qS * cl);
            result.Drag = -flowDirection * (qS * cd);
            result.SideForce = Vector3.right * (qS * c.sideForcePerSideslip * beta);
            result.Force = result.Lift + result.Drag + result.SideForce;

            // Moments in pilot convention (nose up, roll right, yaw right positive), then mapped to Unity axes.
            float chord = c.meanChord, span = c.wingSpan;
            float pitchUp = qS * chord * (c.pitchMomentAtZeroAlpha + c.pitchStability * alpha
                                          + c.stallPitchBreak * stallDepth + c.elevatorAuthority * surfaces.Pitch);
            float rollRight = qS * span * (c.aileronAuthority * surfaces.Roll + c.dihedralEffect * beta);
            float yawRight = qS * span * (c.rudderAuthority * surfaces.Yaw + c.directionalStability * beta);

            // Damping: M = ½ρV·S·L²/2 · C_damp · ω  (from q·S·L·C_damp·ωL/2V), opposing rotation on each axis.
            float dampingScale = 0.25f * density * airspeed * c.wingArea;
            Vector3 damping = new Vector3(
                dampingScale * chord * chord * c.pitchDamping * bodyAngularVelocity.x,
                dampingScale * span * span * c.yawDamping * bodyAngularVelocity.y,
                dampingScale * span * span * c.rollDamping * bodyAngularVelocity.z);

            result.Torque = new Vector3(-pitchUp, yawRight, -rollRight) + damping;
            result.AngleOfAttack = alpha;
            result.Sideslip = beta;
            result.LiftCoefficient = cl;
            result.DragCoefficient = cd;
            result.StallDepth = stallDepth;
            return result;
        }

        static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
