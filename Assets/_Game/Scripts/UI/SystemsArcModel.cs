using System;
using OpenSkies.Gestures;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>How strongly an arc item is drawn and whether it can be chosen.</summary>
    public enum ArcEmphasis
    {
        /// <summary>Available but not relevant now (manual access): drawn quietly, still selectable.</summary>
        Quiet = 0,
        /// <summary>Relevant to the phase of flight: drawn in full.</summary>
        Full = 1,
        /// <summary>The system is on (brake set, flaps out, a guide shown): cyan, so it can be undone.</summary>
        Engaged = 2,
        /// <summary>Not possible now (parking brake in the air): dimmed, not selectable.</summary>
        Disabled = 3,
    }

    /// <summary>One segment of the arc. Labels are fixed strings so presenting allocates nothing.</summary>
    public struct ArcItem
    {
        public string Label;
        /// <summary>Shown instead of the label while engaged (BRAKE SET); null keeps the label.</summary>
        public string EngagedLabel;
        public ArcEmphasis Emphasis;
        /// <summary>The aircraft or the airfield has it at all. Unavailable items keep their slot but are never drawn.</summary>
        public bool Available;

        public string CurrentLabel => Emphasis == ArcEmphasis.Engaged && EngagedLabel != null ? EngagedLabel : Label;
        public bool Selectable => Available && Emphasis != ArcEmphasis.Disabled;
    }

    /// <summary>Geometry and timing of the systems arc, canvas units and seconds. Tune on a device.</summary>
    [Serializable]
    public sealed class SystemsArcSettings
    {
        [Header("Geometry (canvas units, from the handle centre)")]
        [Tooltip("Inner radius of the segment band.")]
        public float innerRadius = 170f;
        [Tooltip("Outer radius of the segment band (the band is at least a touch target thick).")]
        public float outerRadius = 290f;
        [Tooltip("The outer ring (dwell) lies between the band's outer radius plus this gap and its own thickness.")]
        public float outerRingGap = 18f;
        public float outerRingThickness = 104f;
        [Tooltip("A finger closer to the handle than this is on nothing (the cancel zone).")]
        public float selectionInnerRadius = 110f;
        [Tooltip("Angle of the first slot, degrees (0 = right, 90 = straight up). Slots continue anticlockwise, away from the throttle.")]
        public float firstSlotAngle = 98f;
        [Tooltip("Angle between neighbouring slots, degrees.")]
        public float slotStep = 25f;
        [Tooltip("Angular gap between segments, degrees.")]
        public float segmentGap = 2.5f;
        [Tooltip("The first and last wedges extend this much beyond their slot, so the ends are easy to hit, degrees.")]
        public float endTolerance = 10f;
        [Tooltip("Where segments sit before unfolding, degrees (straight up from the handle).")]
        public float foldedAngle = 90f;
        [Tooltip("A sub-arc (the options of one segment, revealed by resting on it) is kept within this much of the arc's own span, degrees.")]
        public float subArcMargin = 12f;

        [Header("Behaviour")]
        [Tooltip("Pull progress from which a segment can be selected (before that the arc is only a preview).")]
        [Range(0f, 1f)] public float selectableFromProgress = 0.55f;
        [Tooltip("A tap on the handle shows the arc this long as a hint, then it folds away, s.")]
        public float hintSeconds = 1.1f;

        [Header("Motion (seconds)")]
        public float unfoldSeconds = 0.22f;
        public float foldSeconds = 0.16f;
        public float hoverSeconds = 0.08f;

        public float OuterRingInner => outerRadius + outerRingGap;
        public float OuterRingOuter => OuterRingInner + outerRingThickness;
    }

    /// <summary>
    /// The systems arc as data: fixed slots around the handle, an unfold driven by
    /// the pull progress, the item under the finger, and arming. Pure, so the
    /// ergonomics (which wedge a thumb position lands in) are testable. Slots never
    /// move between phases: an item keeps its angle for the whole session, so the
    /// thumb learns where things are.
    /// </summary>
    public sealed class SystemsArcModel
    {
        public const int MaxItems = 8;
        public const int NoItem = -1;

        readonly SystemsArcSettings _s;
        readonly ArcItem[] _inner = new ArcItem[MaxItems];
        readonly ArcItem[] _outer = new ArcItem[MaxItems];
        readonly float[] _outerAngles = new float[MaxItems];
        int _innerCount, _outerCount;
        float _progress;
        int _hovered = NoItem;
        bool _hoveredOuter;
        bool _outerRevealed;
        int _outerParent = NoItem;

        public SystemsArcModel(SystemsArcSettings settings = null)
        {
            _s = settings ?? new SystemsArcSettings();
        }

        public SystemsArcSettings Settings => _s;
        public int InnerCount => _innerCount;
        public int OuterCount => _outerCount;
        /// <summary>Pull progress, 0 (folded) to 1 (unfolded).</summary>
        public float Progress => _progress;
        public int HoveredIndex => _hovered;
        public bool HoveredIsOuter => _hoveredOuter;
        public bool HasHovered => _hovered != NoItem;
        public bool OuterRevealed => _outerRevealed;
        /// <summary>The inner segment the outer ring fans out from (its sub-options), or -1 for the free ring.</summary>
        public int OuterParent => _outerParent;
        /// <summary>Releasing now would choose the hovered item.</summary>
        public bool IsArmed => _hovered != NoItem && Item(_hoveredOuter, _hovered).Selectable;
        public bool Selectable => _progress >= _s.selectableFromProgress;

        public ArcItem Item(bool outer, int index) => outer ? _outer[index] : _inner[index];
        public ref ArcItem InnerItem(int index) => ref _inner[index];
        public ref ArcItem OuterItem(int index) => ref _outer[index];

        public void SetInnerCount(int count) => _innerCount = Mathf.Clamp(count, 0, MaxItems);

        /// <summary>The free outer ring's items (no parent): they sit at the inner slots' angles.</summary>
        public void SetOuterCount(int count)
        {
            _outerCount = Mathf.Clamp(count, 0, MaxItems);
            for (int i = 0; i < _outerCount; i++) _outerAngles[i] = SlotAngle(i);
        }

        /// <summary>A new pull: folded, nothing hovered, outer ring hidden.</summary>
        public void Begin()
        {
            _progress = 0f;
            _hovered = NoItem;
            _hoveredOuter = false;
            _outerRevealed = false;
            _outerParent = NoItem;
        }

        /// <summary>The finger rested on nothing: the free outer ring unfolds (its items become selectable).</summary>
        public void RevealOuter(bool revealed)
        {
            _outerRevealed = revealed && _outerCount > 0;
            _outerParent = NoItem;
            for (int i = 0; i < _outerCount; i++) _outerAngles[i] = SlotAngle(i);
        }

        /// <summary>
        /// The finger rested on an inner segment that has options: they fan out from it on
        /// the outer ring, centred on its angle and kept inside the arc's span. The items
        /// themselves are set by the caller through <see cref="OuterItem"/> before this.
        /// </summary>
        public void RevealSubArc(int parentIndex, int count)
        {
            _outerCount = Mathf.Clamp(count, 0, MaxItems);
            _outerParent = parentIndex;
            _outerRevealed = _outerCount > 0 && parentIndex >= 0 && parentIndex < _innerCount;
            if (!_outerRevealed) return;
            float centre = SlotAngle(parentIndex);
            float first = centre - (_outerCount - 1) * 0.5f * _s.slotStep;
            float min = _s.firstSlotAngle - _s.subArcMargin;
            float max = SlotAngle(Mathf.Max(0, _innerCount - 1)) + _s.subArcMargin;
            float last = first + (_outerCount - 1) * _s.slotStep;
            if (first < min) first = min;
            else if (last > max) first = Mathf.Max(min, max - (_outerCount - 1) * _s.slotStep);
            for (int i = 0; i < _outerCount; i++) _outerAngles[i] = first + i * _s.slotStep;
        }

        /// <summary>Resting angle of an outer item, degrees.</summary>
        public float OuterAngle(int index) => _outerAngles[Mathf.Clamp(index, 0, MaxItems - 1)];

        /// <summary>
        /// Updates the unfold and the item under the finger.
        /// <paramref name="fingerFromAnchor"/> is the finger relative to the handle
        /// centre in canvas units; <paramref name="pullProgress"/> comes from the recognizer.
        /// </summary>
        public void Update(Vector2 fingerFromAnchor, float pullProgress)
        {
            _progress = Mathf.Clamp01(GestureMath.Finite(pullProgress));
            _hovered = NoItem;
            _hoveredOuter = false;
            if (!Selectable) return;

            float radius = fingerFromAnchor.magnitude;
            if (radius < _s.selectionInnerRadius) return;
            float angle = GestureMath.AngleDegrees(fingerFromAnchor);

            bool outer = _outerRevealed && radius >= _s.OuterRingInner - _s.outerRingGap * 0.5f;
            int count = outer ? _outerCount : _innerCount;
            if (count == 0) return;
            int index = outer ? OuterAt(angle) : SlotAt(angle, count);
            if (index == NoItem) return;
            if (!Item(outer, index).Available) return;
            _hovered = index;
            _hoveredOuter = outer;
        }

        /// <summary>The outer item whose wedge (half a step either side of its angle) contains an angle, or -1.</summary>
        public int OuterAt(float angleDegrees)
        {
            float half = _s.slotStep * 0.5f;
            for (int i = 0; i < _outerCount; i++)
            {
                float d = Mathf.DeltaAngle(_outerAngles[i], angleDegrees);
                float below = half + (i == 0 ? _s.endTolerance : 0f);
                float above = half + (i == _outerCount - 1 ? _s.endTolerance : 0f);
                if (d >= -below && d <= above) return i;
            }
            return NoItem;
        }

        /// <summary>The slot whose wedge contains an angle (the end wedges are widened by the end tolerance), or -1.</summary>
        public int SlotAt(float angleDegrees, int count)
        {
            if (count <= 0) return NoItem;
            float half = _s.slotStep * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float d = Mathf.DeltaAngle(SlotAngle(i), angleDegrees); // signed, -180 to 180
                float below = half + (i == 0 ? _s.endTolerance : 0f);
                float above = half + (i == count - 1 ? _s.endTolerance : 0f);
                if (d >= -below && d <= above) return i;
            }
            return NoItem;
        }

        /// <summary>Resting angle of a slot, degrees (0 = right, 90 = up).</summary>
        public float SlotAngle(int index) => _s.firstSlotAngle + index * _s.slotStep;

        /// <summary>Where a slot is while unfolding: from the folded angle towards its resting angle.</summary>
        public float UnfoldedAngle(int index, float unfold) => Mathf.LerpUnclamped(_s.foldedAngle, SlotAngle(index), unfold);

        /// <summary>Where an outer item is while unfolding: from its parent's angle (or the folded angle) towards its resting angle.</summary>
        public float UnfoldedOuterAngle(int index, float unfold)
        {
            float from = _outerParent >= 0 ? SlotAngle(_outerParent) : _s.foldedAngle;
            return Mathf.LerpUnclamped(from, OuterAngle(index), unfold);
        }

        /// <summary>Centre of a segment's label, canvas units from the handle centre.</summary>
        public Vector2 SlotCentre(bool outer, int index, float unfold)
        {
            float radius = outer ? (_s.OuterRingInner + _s.OuterRingOuter) * 0.5f : (_s.innerRadius + _s.outerRadius) * 0.5f;
            float angle = outer ? UnfoldedOuterAngle(index, unfold) : UnfoldedAngle(index, unfold);
            return GestureMath.FromAngle(angle) * radius;
        }

        /// <summary>Angular size of one segment (the slot step less the gap), degrees.</summary>
        public float SegmentSweep => Mathf.Max(1f, _s.slotStep - _s.segmentGap);
    }
}
