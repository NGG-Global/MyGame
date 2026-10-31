using UnityEngine;

namespace OpenSkies.CameraSystems
{
    /// <summary>
    /// Positions the camera for the active view. It only reads the aircraft's
    /// transform, so switching views can never interrupt flight control.
    ///
    /// Cockpit view: the camera sits at the pilot's eye anchor and looks along it
    /// (the anchor sets the resting direction, slightly down at the panel), plus a
    /// look offset the player drags. The offset is kept within comfortable limits,
    /// survives camera switches and resets, and can return to rest smoothly. The
    /// field of view is adjustable within limits.
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
        public float chaseFieldOfView = 60f;

        [Header("Cockpit view")]
        [Tooltip("Vertical field of view, degrees. Players adjust it within the limits below.")]
        public float cockpitFieldOfView = 62f;
        public float minCockpitFieldOfView = 50f;
        public float maxCockpitFieldOfView = 80f;
        [Tooltip("Look offset limits from the resting direction, degrees (left/right).")]
        public Vector2 cockpitYawLimits = new Vector2(-135f, 135f);
        [Tooltip("Look offset limits from the resting direction, degrees (down/up).")]
        public Vector2 cockpitPitchLimits = new Vector2(-40f, 45f);
        [Tooltip("Duration of the smooth return to the resting direction, s.")]
        public float recentreSeconds = 0.35f;
        [Tooltip("Return to the resting direction by itself after this long without looking around, s. 0 = never.")]
        public float autoRecentreSeconds = 0f;

        Camera _camera;
        Vector2 _look;               // x = yaw right, y = pitch up, degrees from the anchor direction
        Vector2 _recentreFrom;
        float _recentreProgress = 1f;
        float _idleSeconds;

        /// <summary>Current cockpit look offset (x yaw right, y pitch up), degrees.</summary>
        public Vector2 CockpitLook => _look;
        public bool IsRecentring => _recentreProgress < 1f;
        public Camera ViewCamera => _camera != null ? _camera : _camera = GetComponent<Camera>();

        /// <summary>Turns the cockpit view; cancels a return to rest.</summary>
        public void AddCockpitLook(float yawDegrees, float pitchDegrees)
        {
            _look = CockpitLookMath.Clamp(_look + new Vector2(yawDegrees, pitchDegrees), cockpitYawLimits, cockpitPitchLimits);
            _recentreProgress = 1f;
            _idleSeconds = 0f;
        }

        public void SetCockpitLook(Vector2 look)
        {
            _look = CockpitLookMath.Clamp(look, cockpitYawLimits, cockpitPitchLimits);
            _recentreProgress = 1f;
            _idleSeconds = 0f;
        }

        /// <summary>Smoothly back to the resting direction.</summary>
        public void RecentreCockpitLook()
        {
            if (_look.x * _look.x + _look.y * _look.y < 1e-6f) return;
            _recentreFrom = _look;
            _recentreProgress = recentreSeconds > 0f ? 0f : 1f;
            if (_recentreProgress >= 1f) _look = Vector2.zero;
        }

        public void SetCockpitFieldOfView(float degrees) =>
            cockpitFieldOfView = CockpitLookMath.ClampFieldOfView(degrees, minCockpitFieldOfView, maxCockpitFieldOfView);

        public void SetMode(CameraMode newMode)
        {
            mode = newMode;
            SnapToTarget();
        }

        public void ToggleMode() => SetMode(mode == CameraMode.Chase ? CameraMode.Cockpit : CameraMode.Chase);

        void Awake() => _camera = GetComponent<Camera>();

        void OnEnable() => SnapToTarget();

        void LateUpdate() => Place(Time.deltaTime, snap: false);

        public void SnapToTarget() => Place(0f, snap: true);

        void Place(float deltaTime, bool snap)
        {
            if (target == null) return;

            if (mode == CameraMode.Cockpit && cockpitAnchor != null)
            {
                AdvanceCockpitLook(deltaTime);
                // Unity: positive X rotation pitches down, positive Y yaws right.
                transform.SetPositionAndRotation(cockpitAnchor.position, cockpitAnchor.rotation * Quaternion.Euler(-_look.y, _look.x, 0f));
                SetFieldOfView(CockpitLookMath.ClampFieldOfView(cockpitFieldOfView, minCockpitFieldOfView, maxCockpitFieldOfView));
                return;
            }
            SetFieldOfView(chaseFieldOfView);

            Vector3 desired = ChaseCameraMath.DesiredPosition(target.position, target.rotation, chaseDistance, chaseHeight);
            float t = snap ? 1f : ChaseCameraMath.SmoothingFactor(followSharpness, deltaTime);
            Vector3 position = Vector3.Lerp(transform.position, desired, t);
            Vector3 toTarget = target.position + Vector3.up * lookAheadHeight - position;
            Quaternion rotation = toTarget.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toTarget, Vector3.up) : transform.rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        void AdvanceCockpitLook(float deltaTime)
        {
            if (_recentreProgress < 1f)
            {
                _recentreProgress = Mathf.Min(1f, _recentreProgress + deltaTime / Mathf.Max(recentreSeconds, 1e-3f));
                _look = CockpitLookMath.Recentre(_recentreFrom, _recentreProgress);
                return;
            }
            if (autoRecentreSeconds <= 0f || _look.x * _look.x + _look.y * _look.y < 1e-6f) return;
            _idleSeconds += deltaTime;
            if (_idleSeconds >= autoRecentreSeconds) RecentreCockpitLook();
        }

        void SetFieldOfView(float degrees)
        {
            var viewCamera = ViewCamera;
            if (viewCamera != null && !Mathf.Approximately(viewCamera.fieldOfView, degrees)) viewCamera.fieldOfView = degrees;
        }
    }
}
