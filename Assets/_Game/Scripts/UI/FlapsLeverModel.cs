using System;
using OpenSkies.Gestures;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>Feel of the flap lever, canvas units. Tune on a device.</summary>
    [Serializable]
    public sealed class FlapsLeverSettings
    {
        [Tooltip("Travel between two detents.")]
        public float detentSpacing = 76f;
        [Tooltip("The preview changes detent only this far past the midpoint between two detents (no flicker).")]
        public float detentHysteresis = 14f;
        [Tooltip("The knob can be pushed this far beyond the end detents, compressed (it feels held by its stop).")]
        public float overTravel = 40f;
        [Tooltip("Moving this far sideways from the lever cancels (the lever greys out; release changes nothing).")]
        public float cancelDistance = 200f;
        [Tooltip("Moving back within this distance un-cancels.")]
        public float uncancelDistance = 150f;
        [Tooltip("Time the knob takes to settle on a detent after the finger stops, s.")]
        public float settleSeconds = 0.12f;
    }

    /// <summary>
    /// The flap lever as data: the current detent sits under the thumb when the lever
    /// appears, dragging down extends (more flaps), dragging up retracts, with detent
    /// snapping and hysteresis. The preview is what the lever shows; the aircraft is
    /// not touched until the gesture completes with <see cref="Changed"/> true. Pure.
    /// </summary>
    public sealed class FlapsLeverModel
    {
        readonly FlapsLeverSettings _s;
        int _count, _current, _origin, _preview;
        Vector2 _start;
        float _travel;
        bool _cancelled, _active;

        public FlapsLeverModel(FlapsLeverSettings settings = null)
        {
            _s = settings ?? new FlapsLeverSettings();
        }

        public FlapsLeverSettings Settings => _s;
        public bool IsActive => _active;
        public int DetentCount => _count;
        /// <summary>The detent the aircraft is set to (committed).</summary>
        public int Current => _current;
        /// <summary>The detent that was under the thumb when the lever appeared (the lever's zero).</summary>
        public int Origin => _origin;
        /// <summary>The detent the lever shows (what releasing would set).</summary>
        public int Preview => _preview;
        public bool Cancelled => _cancelled;
        /// <summary>Releasing now would change the flaps.</summary>
        public bool Changed => _active && !_cancelled && _preview != _current;
        /// <summary>Where the lever's zero point is: the finger when the lever appeared.</summary>
        public Vector2 Start => _start;
        /// <summary>Finger travel along the lever: positive = pulled down (extend), canvas units.</summary>
        public float Travel => _travel;

        /// <summary>The lever appears with the current detent under the thumb.</summary>
        public void Begin(int detentCount, int currentDetent, Vector2 finger)
        {
            _count = Mathf.Max(2, detentCount);
            _current = Mathf.Clamp(currentDetent, 0, _count - 1);
            _origin = _current;
            _preview = _current;
            _start = finger;
            _travel = 0f;
            _cancelled = false;
            _active = true;
        }

        public void End() => _active = false;

        /// <summary>The committed detent changed under the lever (keyboard, cockpit): keep the preview honest.</summary>
        public void SetCurrent(int detent) => _current = Mathf.Clamp(detent, 0, Mathf.Max(1, _count) - 1);

        public void Update(Vector2 finger)
        {
            if (!_active) return;
            _travel = _start.y - finger.y; // down = extend
            float lateral = Mathf.Abs(finger.x - _start.x);
            if (_cancelled)
            {
                if (lateral <= _s.uncancelDistance) _cancelled = false;
            }
            else if (lateral > _s.cancelDistance) _cancelled = true;
            if (_cancelled) return;

            // Travel is measured from the lever's zero (the origin detent); the preview can move both ways.
            _preview = GestureMath.SteppedLevel(_travel, _s.detentSpacing, _s.detentHysteresis, _preview - _origin, -_origin, _count - 1 - _origin) + _origin;
        }

        /// <summary>Knob position relative to the lever's zero (positive = down), compressed beyond the end detents.</summary>
        public float KnobOffset
        {
            get
            {
                if (!_active) return 0f;
                float min = -_origin * _s.detentSpacing;
                float max = (_count - 1 - _origin) * _s.detentSpacing;
                float t = Mathf.Clamp(_travel, min, max);
                float beyond = _travel - t;
                if (beyond == 0f) return t;
                // Beyond the end detents the knob moves a little, then less and less, up to the over-travel.
                float x = Mathf.Abs(beyond) * 0.3f;
                float compressed = _s.overTravel * x / (x + Mathf.Max(1f, _s.overTravel));
                return t + Mathf.Sign(beyond) * compressed;
            }
        }

        /// <summary>Position of a detent mark relative to the lever's zero (positive = down).</summary>
        public float DetentOffset(int detent) => (detent - _origin) * _s.detentSpacing;
    }
}
