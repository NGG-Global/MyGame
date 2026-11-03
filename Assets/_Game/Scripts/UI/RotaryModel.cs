using System;
using OpenSkies.Gestures;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>Feel of a rotary adjustment (heading bug, altimeter subscale), in the instrument's 400-unit design space.</summary>
    [Serializable]
    public sealed class RotarySettings
    {
        [Tooltip("Inside this radius of the dial centre the gesture is in its cancel zone: releasing there changes nothing.")]
        public float cancelRadius = 46f;
        [Tooltip("Beyond this radius the adjustment is fine: the same angle moves the value less.")]
        public float fineRadius = 150f;
        [Tooltip("Value change for one full turn of the finger at the coarse radius.")]
        public float unitsPerTurn = 360f;
        [Tooltip("Fine gain beyond the fine radius (0.5 = half as fast).")]
        [Range(0.05f, 1f)] public float fineGain = 0.5f;
        [Tooltip("The value snaps to multiples of this (1° for a heading bug, 1 hPa for the subscale).")]
        public float step = 1f;
        [Tooltip("A tick (sound, haptic) every this many units.")]
        public float tickEvery = 5f;
        [Tooltip("Leaving the cancel zone again needs this much extra radius (no flicker at the edge).")]
        public float cancelHysteresis = 10f;
    }

    /// <summary>
    /// A continuous rotary adjustment: the finger circles the dial centre, the
    /// value follows the accumulated angle (unwrapped, so many turns are fine),
    /// coarse near the centre and fine further out, snapped to a step. Moving into
    /// the centre cancels; releasing commits <see cref="Preview"/>. Nothing is
    /// applied here: the consumer acts on release. Pure.
    /// </summary>
    public sealed class RotaryModel
    {
        readonly RotarySettings _s;
        float _start, _min, _max;
        bool _wrap, _active, _cancelled;
        float _lastAngle, _accumulated;
        float _preview;
        int _ticks;
        int _lastTickBucket;

        public RotaryModel(RotarySettings settings = null)
        {
            _s = settings ?? new RotarySettings();
        }

        public RotarySettings Settings => _s;
        public bool IsActive => _active;
        public float Start => _start;
        /// <summary>The value releasing would set (snapped to the step, wrapped or clamped).</summary>
        public float Preview => _preview;
        public bool Cancelled => _cancelled;
        public bool Changed => _active && !_cancelled && !Mathf.Approximately(_preview, _start);
        /// <summary>Accumulated finger angle since the start, degrees (positive = clockwise on screen).</summary>
        public float AccumulatedDegrees => _accumulated;
        /// <summary>Ticks crossed since the last query (for feedback); reading clears it.</summary>
        public int TakeTicks()
        {
            int t = _ticks;
            _ticks = 0;
            return t;
        }

        /// <summary>
        /// Starts at <paramref name="startValue"/>. For wrapping values (headings) give the
        /// period as min 1 and max 360 with <paramref name="wrap"/>; otherwise the value is clamped.
        /// </summary>
        public void Begin(float startValue, float min, float max, bool wrap, Vector2 fingerFromCentre)
        {
            _start = _preview = startValue;
            _lastTickBucket = Bucket(startValue);
            _min = min;
            _max = max;
            _wrap = wrap;
            _active = true;
            _cancelled = fingerFromCentre.magnitude < _s.cancelRadius;
            _lastAngle = Angle(fingerFromCentre);
            _accumulated = 0f;
            _ticks = 0;
        }

        public void End() => _active = false;

        public void Update(Vector2 fingerFromCentre)
        {
            if (!_active) return;
            float radius = fingerFromCentre.magnitude;
            if (_cancelled)
            {
                if (radius >= _s.cancelRadius + _s.cancelHysteresis) _cancelled = false;
            }
            else if (radius < _s.cancelRadius) _cancelled = true;
            if (_cancelled)
            {
                _lastAngle = Angle(fingerFromCentre); // no jump when the finger comes back out
                return;
            }

            float angle = Angle(fingerFromCentre);
            float delta = Mathf.DeltaAngle(_lastAngle, angle); // short way round: the finger never jumps half a turn
            _lastAngle = angle;
            float gain = radius >= _s.fineRadius ? _s.fineGain : 1f;
            _accumulated += delta * gain;

            float raw = _start + _accumulated / 360f * _s.unitsPerTurn;
            float step = Mathf.Max(1e-3f, _s.step);
            float snapped = Mathf.Round(raw / step) * step;
            _preview = _wrap ? Wrap(snapped) : Mathf.Clamp(snapped, _min, _max);

            // Ticks count the tick boundaries crossed by the unwrapped value (clamped values stop ticking at the ends).
            int bucket = Bucket(_wrap ? snapped : _preview);
            _ticks += Mathf.Abs(bucket - _lastTickBucket);
            _lastTickBucket = bucket;
        }

        int Bucket(float value) => _s.tickEvery > 0f ? Mathf.FloorToInt(value / _s.tickEvery + 1e-4f) : 0;

        float Wrap(float value)
        {
            float period = _max - _min + 1f; // 1..360 has period 360
            float v = Mathf.Repeat(value - _min, period) + _min;
            return v;
        }

        /// <summary>Clockwise angle on screen, degrees: up is 0, right is 90 (a dial's sense).</summary>
        static float Angle(Vector2 v) => GestureMath.AngleDegrees(new Vector2(v.y, v.x)); // swap axes: atan2(x, y)
    }
}
