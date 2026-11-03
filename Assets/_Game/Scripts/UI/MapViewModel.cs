using System;
using UnityEngine;

namespace OpenSkies.UI
{
    [Serializable]
    public sealed class MapViewSettings
    {
        [Tooltip("The map can be panned this far from the aircraft, in map radii.")]
        public float maxPanRadii = 1.5f;
        [Tooltip("Pinch: one screen height of spread changes the range by this many steps.")]
        public float zoomStepsPerScreenHeight = 6f;
        [Tooltip("Zoom hysteresis: the range changes only past this fraction of a step.")]
        public float zoomHysteresis = 0.3f;
    }

    /// <summary>
    /// The GPS map's view while the player manipulates it: a pan offset from the
    /// aircraft (drag), a chosen range (pinch) and a way back to the automatic view
    /// (double tap or release-near-the-aircraft). The flight is never affected;
    /// the map only draws differently. Pure.
    /// </summary>
    public sealed class MapViewModel
    {
        /// <summary>The ranges the map can show, NM (the automatic view picks among the same).</summary>
        public static readonly float[] RangesNm = { 2f, 5f, 10f, 20f, 50f };

        readonly MapViewSettings _s;
        Vector2 _pan;
        int _rangeIndex = -1;   // -1 = automatic
        float _zoomAccumulator;
        float _radius = 190f;

        public MapViewModel(MapViewSettings settings = null)
        {
            _s = settings ?? new MapViewSettings();
        }

        public MapViewSettings Settings => _s;
        /// <summary>Pan offset of the map under the aircraft symbol, in map units (the aircraft moves the other way).</summary>
        public Vector2 Pan => _pan;
        public bool IsPanned => _pan.sqrMagnitude > 1e-4f;
        /// <summary>The chosen range, or 0 for automatic.</summary>
        public float RangeNm => _rangeIndex < 0 ? 0f : RangesNm[_rangeIndex];
        public bool IsCustom => IsPanned || _rangeIndex >= 0;

        /// <summary>The map radius in the units the pan is measured in (the face's map radius).</summary>
        public void SetRadius(float radius) => _radius = Mathf.Max(1f, radius);

        /// <summary>Drag: the map follows the finger (the finger's movement in map units).</summary>
        public void Drag(Vector2 stepInMapUnits)
        {
            _pan = Vector2.ClampMagnitude(_pan + stepInMapUnits, _s.maxPanRadii * _radius);
        }

        /// <summary>Pinch: spreading the fingers zooms in (a smaller range), as a fraction of the screen height.</summary>
        public void Pinch(float spreadScreenHeights, float automaticRangeNm)
        {
            if (_rangeIndex < 0) _rangeIndex = IndexOf(automaticRangeNm);
            _zoomAccumulator += spreadScreenHeights * _s.zoomStepsPerScreenHeight;
            float threshold = 1f - Mathf.Clamp01(_s.zoomHysteresis) + 0.5f;
            while (_zoomAccumulator >= threshold && _rangeIndex > 0) { _rangeIndex--; _zoomAccumulator -= 1f; }
            while (_zoomAccumulator <= -threshold && _rangeIndex < RangesNm.Length - 1) { _rangeIndex++; _zoomAccumulator += 1f; }
            _zoomAccumulator = Mathf.Clamp(_zoomAccumulator, -1f, 1f);
        }

        /// <summary>Back to the aircraft and the automatic range.</summary>
        public void Recenter()
        {
            _pan = Vector2.zero;
            _rangeIndex = -1;
            _zoomAccumulator = 0f;
        }

        public static int IndexOf(float rangeNm)
        {
            for (int i = 0; i < RangesNm.Length; i++)
                if (Mathf.Approximately(RangesNm[i], rangeNm)) return i;
            return 1;
        }
    }
}
