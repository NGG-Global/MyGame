using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>Result of one aerodynamic evaluation, in the aircraft's body axes.</summary>
    public struct AerodynamicForces
    {
        /// <summary>Total aerodynamic force (lift + drag + side force), N, body axes.</summary>
        public Vector3 Force;
        /// <summary>Total aerodynamic moment about the centre of mass, N·m, body axes.</summary>
        public Vector3 Torque;
        public Vector3 Lift;
        public Vector3 Drag;
        public Vector3 SideForce;
        public float Airspeed;           // m/s
        public float AngleOfAttack;      // rad
        public float Sideslip;           // rad
        public float LiftCoefficient;
        public float DragCoefficient;
        /// <summary>0 = attached flow, 1 = fully stalled.</summary>
        public float StallDepth;
    }
}
