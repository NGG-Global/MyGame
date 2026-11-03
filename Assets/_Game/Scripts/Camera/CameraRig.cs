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

        [Header("Chase free look (temporary, springs back)")]
        [Tooltip("Free-look limits about the chase position, degrees (left/right).")]
        public Vector2 chaseYawLimits = new Vector2(-150f, 150f);
        [Tooltip("Free-look limits, degrees (down/up).")]
        public Vector2 chasePitchLimits = new Vector2(-20f, 60f);
        [Tooltip("Duration of the return to the chase position after the finger lifts, s.")]
        public float chaseReturnSeconds = 0.45f;
        [Tooltip("Pinch zoom limits for the chase distance, m.")]
        public float minChaseDistance = 8f;
        public float maxChaseDistance = 30f;

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
        [Tooltip("The look-forward control is offered once the view is turned further than this from rest (yaw or pitch), degrees.")]
        public float lookAwayDegrees = 10f;

        Camera _camera;
        Vector2 _look;               // x = yaw right, y = pitch up, degrees from the anchor direction
        Vector2 _recentreFrom;
        float _recentreProgress = 1f;
        float _idleSeconds;
        Vector3 _showcasePosition, _showcaseLookAt;
        float _showcaseFieldOfView = 55f;
        Vector2 _chaseLook;          // x = yaw, y = pitch, degrees about the chase position
        Vector2 _chaseReturnFrom;
        float _chaseReturnProgress = 1f;
        bool _chaseHeld;

        /// <summary>Current cockpit look offset (x yaw right, y pitch up), degrees.</summary>
        public Vector2 CockpitLook => _look;
        /// <summary>Current chase free-look offset (x yaw, y pitch), degrees; zero at the chase position.</summary>
        public Vector2 ChaseLook => _chaseLook;
        /// <summary>A finger is holding the chase view away from its position.</summary>
        public bool IsChaseLookHeld => _chaseHeld;
        public bool IsChaseReturning => _chaseReturnProgress < 1f;
        public bool IsRecentring => _recentreProgress < 1f;
        /// <summary>Cockpit view turned away from rest (beyond <see cref="lookAwayDegrees"/>) and not already returning.</summary>
        public bool IsLookingAway => !IsRecentring && CockpitLookMath.IsLookingAway(_look, lookAwayDegrees);
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

        /// <summary>Turns the chase view about the aircraft while a finger drags; it springs back on <see cref="ReleaseChaseLook"/>.</summary>
        public void AddChaseLook(float yawDegrees, float pitchDegrees)
        {
            _chaseLook = CockpitLookMath.Clamp(_chaseLook + new Vector2(yawDegrees, pitchDegrees), chaseYawLimits, chasePitchLimits);
            _chaseReturnProgress = 1f;
            _chaseHeld = true;
        }

        /// <summary>The finger lifted: back to the chase position, smoothly or at once (reduced motion).</summary>
        public void ReleaseChaseLook(bool instant = false)
        {
            _chaseHeld = false;
            if (_chaseLook.sqrMagnitude < 1e-6f) return;
            if (instant || chaseReturnSeconds <= 0f)
            {
                _chaseLook = Vector2.zero;
                _chaseReturnProgress = 1f;
                return;
            }
            _chaseReturnFrom = _chaseLook;
            _chaseReturnProgress = 0f;
        }

        /// <summary>Pinch zoom in the chase view, within limits. Presentation only.</summary>
        public void SetChaseDistance(float metres) =>
            chaseDistance = Mathf.Clamp(float.IsNaN(metres) ? chaseDistance : metres, minChaseDistance, maxChaseDistance);

        public void SetMode(CameraMode newMode)
        {
            mode = newMode;
            SnapToTarget();
        }

        public void ToggleMode() => SetMode(mode == CameraMode.Chase ? CameraMode.Cockpit : CameraMode.Chase);

        /// <summary>The entry screen's framing: where the camera is and what it looks at, applied while the mode is Showcase.</summary>
        public void SetShowcase(Vector3 position, Vector3 lookAt, float fieldOfView)
        {
            _showcasePosition = position;
            _showcaseLookAt = lookAt;
            _showcaseFieldOfView = Mathf.Clamp(fieldOfView, 20f, 100f);
            if (mode == CameraMode.Showcase) PlaceShowcase();
        }

        void PlaceShowcase()
        {
            Vector3 to = _showcaseLookAt - _showcasePosition;
            Quaternion rotation = to.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(to, Vector3.up) : transform.rotation;
            transform.SetPositionAndRotation(_showcasePosition, rotation);
            SetFieldOfView(_showcaseFieldOfView);
        }

        void Awake() => _camera = GetComponent<Camera>();

        void OnEnable() => SnapToTarget();

        void LateUpdate() => Place(Time.deltaTime, snap: false);

        public void SnapToTarget() => Place(0f, snap: true);

        void Place(float deltaTime, bool snap)
        {
            if (mode == CameraMode.Showcase)
            {
                PlaceShowcase();
                return;
            }
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

            AdvanceChaseLook(deltaTime);
            Vector3 desired = ChaseCameraMath.OrbitPosition(target.position, target.rotation, chaseDistance, chaseHeight, _chaseLook.x, _chaseLook.y);
            float t = snap ? 1f : ChaseCameraMath.SmoothingFactor(followSharpness, deltaTime);
            Vector3 position = Vector3.Lerp(transform.position, desired, t);
            Vector3 toTarget = target.position + Vector3.up * lookAheadHeight - position;
            Quaternion rotation = toTarget.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toTarget, Vector3.up) : transform.rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        void AdvanceChaseLook(float deltaTime)
        {
            if (_chaseReturnProgress >= 1f) return;
            _chaseReturnProgress = Mathf.Min(1f, _chaseReturnProgress + deltaTime / Mathf.Max(chaseReturnSeconds, 1e-3f));
            _chaseLook = CockpitLookMath.Recentre(_chaseReturnFrom, _chaseReturnProgress);
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
