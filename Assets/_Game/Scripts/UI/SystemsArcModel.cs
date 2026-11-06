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
        /// <summary>A hazardous choice waiting for its second tap (engine off in flight): red, with a word saying so.</summary>
        Confirm = 4,
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
        /// <summary>A short state line under the label (10°, SET, ON); null shows none. A fixed string.</summary>
        public string State;
        /// <summary>The symbol above the label (primary segments); None for options.</summary>
        public HudIcon Icon;

        public string CurrentLabel => Emphasis == ArcEmphasis.Engaged && EngagedLabel != null ? EngagedLabel : Label;
        public bool Selectable => Available && Emphasis != ArcEmphasis.Disabled;
    }

    /// <summary>
    /// Geometry and timing of the systems wheel, canvas units and seconds. Tune on a device.
    /// The radii and angles are the preferred ones: with <see cref="fitToScreen"/> the session
    /// fits a copy of them to the free space around the handle (<see cref="SystemsWheelLayout"/>),
    /// so the asset itself is never changed at runtime.
    /// </summary>
    [Serializable]
    public sealed class SystemsArcSettings
    {
        [Header("Geometry (canvas units, from the handle centre)")]
        [Tooltip("Inner radius of the segment band.")]
        public float innerRadius = 196f;
        [Tooltip("Outer radius of the segment band (the band is at least a touch target thick). The largest the fit may use.")]
        public float outerRadius = 300f;
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
        public float endTolerance = 4f;
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

        [Header("Fitting to the screen")]
        [Tooltip("Fit the radii and angles to the free space around the handle whenever the screen changes.")]
        public bool fitToScreen = true;
        [Tooltip("Kept between the wheel and the flight controls, the handle and the warning banners, canvas units.")]
        public float fitClearance = 8f;
        [Tooltip("Kept from the edges of the safe area, canvas units.")]
        public float fitMargin = 16f;
        [Tooltip("The widest angle between neighbouring segments the fit uses, degrees.")]
        public float maxSlotStep = 26f;
        [Tooltip("The thinnest band the fit may use: a touch target across the band, canvas units.")]
        public float minBandThickness = 96f;
        [Tooltip("The shortest segment along the band's middle: a touch target along the arc, canvas units.")]
        public float minSegmentLength = 96f;

        /// <summary>
        /// Runtime, set by the fit: the largest slot-centre angle the free space allows, degrees
        /// (an action's options may use slots beyond the last primary one up to here). NaN: no fit.
        /// </summary>
        [NonSerialized] public float slotCentreMax = float.NaN;

        public float OuterRingInner => outerRadius + outerRingGap;
        public float OuterRingOuter => OuterRingInner + outerRingThickness;

        /// <summary>A copy (the fit works on copies, never on the asset).</summary>
        public SystemsArcSettings Clone() => (SystemsArcSettings)MemberwiseClone();
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

        SystemsArcSettings _s;
        readonly ArcItem[] _inner = new ArcItem[MaxItems];
        readonly ArcItem[] _outer = new ArcItem[MaxItems];
        readonly ArcItem[] _options = new ArcItem[MaxItems];
        readonly float[] _outerAngles = new float[MaxItems];
        readonly float[] _optionAngles = new float[MaxItems];
        int _innerCount, _outerCount, _optionCount;
        float _progress;
        int _hovered = NoItem;
        bool _hoveredOuter, _hoveredOption;
        bool _outerRevealed;
        int _outerParent = NoItem;
        int _focusParent = NoItem;
        float _optionStep;

        public SystemsArcModel(SystemsArcSettings settings = null)
        {
            _s = settings ?? new SystemsArcSettings();
            _optionStep = _s.slotStep;
        }

        public SystemsArcSettings Settings => _s;

        /// <summary>
        /// New geometry (the fit to the screen). Slots keep their order; an open focus is laid
        /// out again. The session changes it only while nothing is under a finger.
        /// </summary>
        public void SetSettings(SystemsArcSettings settings)
        {
            if (settings == null) return;
            _s = settings;
            for (int i = 0; i < _outerCount; i++) if (_outerParent == NoItem) _outerAngles[i] = SlotAngle(i);
            if (Focused) LayoutOptions();
            else _optionStep = _s.slotStep;
        }
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
        public bool IsArmed => _hovered != NoItem && HoveredItem.Selectable;
        public bool Selectable => _progress >= _s.selectableFromProgress;

        public ArcItem Item(bool outer, int index) => outer ? _outer[index] : _inner[index];
        public ref ArcItem InnerItem(int index) => ref _inner[index];
        public ref ArcItem OuterItem(int index) => ref _outer[index];

        // ---- Focus: one action's options on the band -------------------------------------------------

        /// <summary>The wheel shows one action's options on its band (the secondary state).</summary>
        public bool Focused => _focusParent != NoItem;
        /// <summary>The primary segment whose options are shown, or -1.</summary>
        public int FocusParent => _focusParent;
        public int OptionCount => _optionCount;
        public ref ArcItem OptionItem(int index) => ref _options[index];
        /// <summary>The finger is over one of the focused options (not a primary segment, not the outer ring).</summary>
        public bool HoveredIsOption => _hoveredOption;
        /// <summary>The item under the finger, whichever layer it is on (default when none).</summary>
        public ArcItem HoveredItem => _hovered == NoItem ? default : (_hoveredOption ? _options[_hovered] : (_hoveredOuter ? _outer[_hovered] : _inner[_hovered]));
        /// <summary>One line under the focused wheel (set by the owner with the options; a fixed string).</summary>
        public string FocusCaption;
        /// <summary>Angle between neighbouring options, degrees (the slot step, narrower only when many options must fit).</summary>
        public float OptionStep => _optionStep;

        /// <summary>
        /// The action at <paramref name="parentIndex"/> opens its options: they take the band,
        /// fanned out around the parent's own angle (the primary segments fold into it), with the
        /// slot step, kept between the first slot and the last one the screen allows. The items
        /// are set through <see cref="OptionItem"/> before or after this. Nothing is hovered.
        /// </summary>
        public void Focus(int parentIndex, int count)
        {
            _optionCount = Mathf.Clamp(count, 0, MaxItems);
            bool valid = parentIndex >= 0 && parentIndex < _innerCount && _optionCount > 0;
            _focusParent = valid ? parentIndex : NoItem;
            if (!valid) _optionCount = 0;
            _hovered = NoItem;
            _hoveredOuter = false;
            _hoveredOption = false;
            if (valid) LayoutOptions();
        }

        /// <summary>Back to the primary segments (or folded): the options go.</summary>
        public void Unfocus()
        {
            if (_hoveredOption) _hovered = NoItem;
            _hoveredOption = false;
            _focusParent = NoItem;
            _optionCount = 0;
            _optionStep = _s.slotStep;
        }

        /// <summary>Resting angle of a focused option, degrees.</summary>
        public float OptionAngle(int index) => _optionAngles[Mathf.Clamp(index, 0, MaxItems - 1)];

        /// <summary>Where an option is while the focus unfolds: from its parent's angle towards its resting angle.</summary>
        public float UnfoldedOptionAngle(int index, float unfold)
        {
            float from = _focusParent >= 0 ? SlotAngle(_focusParent) : _s.foldedAngle;
            return Mathf.LerpUnclamped(from, OptionAngle(index), unfold);
        }

        /// <summary>The focused option whose wedge contains an angle (the end wedges widened by the end tolerance), or -1.</summary>
        public int OptionAt(float angleDegrees)
        {
            float half = _optionStep * 0.5f;
            for (int i = 0; i < _optionCount; i++)
            {
                float d = Mathf.DeltaAngle(_optionAngles[i], angleDegrees);
                float below = half + (i == 0 ? _s.endTolerance : 0f);
                float above = half + (i == _optionCount - 1 ? _s.endTolerance : 0f);
                if (d >= -below && d <= above) return i;
            }
            return NoItem;
        }

        /// <summary>Angular size of one option segment, degrees.</summary>
        public float OptionSweep => Mathf.Max(1f, _optionStep - _s.segmentGap);

        /// <summary>The largest slot-centre angle options may use: the fit's limit, or the primary slots' span without a fit.</summary>
        public float SlotCentreMax
        {
            get
            {
                float max = _s.slotCentreMax;
                if (float.IsNaN(max)) return SlotAngle(Mathf.Max(0, Mathf.Max(_innerCount, _optionCount) - 1));
                float primary = SlotAngle(Mathf.Max(0, _innerCount - 1)); // the primary slots are on screen by construction
                return max < primary ? primary : max;
            }
        }

        void LayoutOptions()
        {
            float lo = SlotAngle(0), hi = SlotCentreMax;
            int n = _optionCount;
            _optionStep = n > 1 ? Mathf.Min(_s.slotStep, (hi - lo) / (n - 1)) : _s.slotStep;
            if (!(_optionStep > 0f)) _optionStep = _s.slotStep;
            float first = SlotAngle(_focusParent) - (n - 1) * 0.5f * _optionStep;
            float last = first + (n - 1) * _optionStep;
            if (first < lo) first = lo;
            else if (last > hi) first = Mathf.Max(lo, hi - (n - 1) * _optionStep);
            for (int i = 0; i < n; i++) _optionAngles[i] = first + i * _optionStep;
        }

        public void SetInnerCount(int count) => _innerCount = Mathf.Clamp(count, 0, MaxItems);

        /// <summary>The free outer ring's items (no parent): they sit at the inner slots' angles.</summary>
        public void SetOuterCount(int count)
        {
            _outerCount = Mathf.Clamp(count, 0, MaxItems);
            for (int i = 0; i < _outerCount; i++) _outerAngles[i] = SlotAngle(i);
        }

        /// <summary>A new pull or a new opening: folded, nothing hovered, outer ring hidden, no focus.</summary>
        public void Begin()
        {
            _progress = 0f;
            _hovered = NoItem;
            _hoveredOuter = false;
            _outerRevealed = false;
            _outerParent = NoItem;
            Unfocus();
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
            _hoveredOption = false;
            if (!Selectable) return;

            float radius = fingerFromAnchor.magnitude;
            if (radius < _s.selectionInnerRadius) return;
            float angle = GestureMath.AngleDegrees(fingerFromAnchor);

            if (Focused)
            {
                // The focused wheel: only the options are on the band (the primary segments folded into their parent).
                int option = OptionAt(angle);
                if (option == NoItem || !_options[option].Available) return;
                _hovered = option;
                _hoveredOption = true;
                return;
            }

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
