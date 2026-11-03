using OpenSkies.CameraSystems;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Poses the camera for the entry stations: three framings of the parked
    /// aircraft (near the runway for the First Flight, a wide coastal view for Free
    /// Flight, down the runway for the Landing Challenge), blended continuously by
    /// the stations' position so a drag moves the view and a release settles it. It
    /// reads the aircraft's transform and the world layout; it never moves anything
    /// but the camera.
    /// </summary>
    public sealed class EntryShowcase
    {
        struct Pose
        {
            public Vector3 Offset;    // from the aircraft, in its own frame (x right, y up, z forward)
            public Vector3 LookAt;    // from the aircraft, in its own frame
            public float FieldOfView;
        }

        static readonly Pose[] Poses =
        {
            new Pose { Offset = new Vector3(-9f, 2.4f, -8f), LookAt = new Vector3(0f, 1.2f, 1.5f), FieldOfView = 50f },    // First Flight: close, front-left, low
            new Pose { Offset = new Vector3(-26f, 14f, -30f), LookAt = new Vector3(40f, -2f, 60f), FieldOfView = 58f },   // Free Flight: high and wide, over the aircraft towards the coast
            new Pose { Offset = new Vector3(0f, 5f, -34f), LookAt = new Vector3(0f, 1f, 220f), FieldOfView = 46f },       // Landing Challenge: behind, down the runway
        };

        readonly CameraRig _rig;
        readonly Transform _aircraft;

        public EntryShowcase(CameraRig rig, Transform aircraft)
        {
            _rig = rig;
            _aircraft = aircraft;
        }

        /// <summary>Poses the camera for a continuous station position (0 to 2). Call per frame while the entry screen is open.</summary>
        public void Present(float position)
        {
            if (_rig == null || _aircraft == null) return;
            position = Mathf.Clamp(position, 0f, EntryStationModel.Count - 1);
            int a = Mathf.Clamp(Mathf.FloorToInt(position), 0, Poses.Length - 2);
            float t = HudEasing.EaseInOut(position - a);
            var from = Poses[a];
            var to = Poses[a + 1];
            Vector3 offset = Vector3.Lerp(from.Offset, to.Offset, t);
            Vector3 lookAt = Vector3.Lerp(from.LookAt, to.LookAt, t);
            float fov = Mathf.Lerp(from.FieldOfView, to.FieldOfView, t);
            // The aircraft's heading only: the framing stays level however the aircraft sits.
            Vector3 forward = _aircraft.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            var frame = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Vector3 position3 = _aircraft.position + frame * offset;
            Vector3 target = _aircraft.position + frame * lookAt;
            _rig.SetShowcase(position3, target, fov);
        }
    }
}
