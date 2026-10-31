using UnityEngine;

namespace OpenSkies.CameraSystems
{
    /// <summary>
    /// Positions the camera for the active view. It only reads the aircraft's
    /// transform, so switching views can never interrupt flight control.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Pilot eye point, a child of the aircraft.")]
        public Transform cockpitAnchor;
        public CameraMode mode = CameraMode.Chase;

        [Header("Chase view")]
        public float chaseDistance = 14f;
        public float chaseHeight = 4f;
        public float lookAheadHeight = 1.5f;
        [Tooltip("Higher is stiffer. Frame-rate independent.")]
        public float followSharpness = 6f;

        public void SetMode(CameraMode newMode)
        {
            mode = newMode;
            SnapToTarget();
        }

        public void ToggleMode() => SetMode(mode == CameraMode.Chase ? CameraMode.Cockpit : CameraMode.Chase);

        void OnEnable() => SnapToTarget();

        void LateUpdate() => Place(Time.deltaTime, snap: false);

        public void SnapToTarget() => Place(0f, snap: true);

        void Place(float deltaTime, bool snap)
        {
            if (target == null) return;

            if (mode == CameraMode.Cockpit && cockpitAnchor != null)
            {
                transform.SetPositionAndRotation(cockpitAnchor.position, cockpitAnchor.rotation);
                return;
            }

            Vector3 desired = ChaseCameraMath.DesiredPosition(target.position, target.rotation, chaseDistance, chaseHeight);
            float t = snap ? 1f : ChaseCameraMath.SmoothingFactor(followSharpness, deltaTime);
            Vector3 position = Vector3.Lerp(transform.position, desired, t);
            Vector3 toTarget = target.position + Vector3.up * lookAheadHeight - position;
            Quaternion rotation = toTarget.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toTarget, Vector3.up) : transform.rotation;
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
