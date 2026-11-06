using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Draws the systems wheel: segments that unfold from the handle, each with its symbol,
    /// a short label and a state line (FLAPS 10°, BRAKE SET); engaged systems marked; the
    /// segment under a sliding finger lifted; and the focused state, where the primary
    /// segments fold into the tapped action and its options unfold from it across the same
    /// band (the flap detents, the switches, the destinations, the profiles). A hazardous
    /// option waiting for its second tap is drawn red with the word CONFIRM. The outer ring
    /// is drawn only for the pull comparison (docs/GESTURE_INTERACTION.md). It only draws
    /// what <see cref="SystemsArcModel"/> says; it never decides anything. Segments are
    /// radial fills of an annulus sprite, so no imported art is needed.
    /// </summary>
    public sealed class SystemsArcView : MonoBehaviour
    {
        [Tooltip("Centred on the handle; segments are its children.")]
        public RectTransform root;
        public CanvasGroup group;
        public Image[] innerSegments = new Image[SystemsArcModel.MaxItems];
        public Text[] innerLabels = new Text[SystemsArcModel.MaxItems];
        public Image[] innerMarks = new Image[SystemsArcModel.MaxItems];
        [Tooltip("The symbol above each primary segment's label.")]
        public Image[] innerIcons = new Image[SystemsArcModel.MaxItems];
        [Tooltip("The state line under each primary segment's label (10°, SET, ON).")]
        public Text[] innerStates = new Text[SystemsArcModel.MaxItems];
        [Tooltip("A focused action's options, drawn on the band.")]
        public Image[] optionSegments = new Image[SystemsArcModel.MaxItems];
        public Text[] optionLabels = new Text[SystemsArcModel.MaxItems];
        public Text[] optionStates = new Text[SystemsArcModel.MaxItems];
        public Image[] optionMarks = new Image[SystemsArcModel.MaxItems];
        public Image[] outerSegments = new Image[SystemsArcModel.MaxItems];
        public Text[] outerLabels = new Text[SystemsArcModel.MaxItems];
        [Tooltip("A short line beside the wheel: what a tap does here.")]
        public Text caption;
        public SystemsArcSettings settings = new SystemsArcSettings();

        [Header("Colours")]
        public Color segmentColor = HudStyle.ControlSurface;
        public Color hoverColor = new Color(0.16f, 0.2f, 0.24f, 0.7f);
        public Color armedColor = HudStyle.Selected;
        public Color engagedColor = new Color(0.33f, 0.8f, 0.95f, 0.45f);
        [Tooltip("A hazardous option waiting for its second tap (the same red as the cockpit selector's confirmation).")]
        public Color confirmColor = new Color(0.85f, 0.2f, 0.12f, 0.95f);
        public Color labelColor = HudStyle.TextPrimary;
        public Color quietLabelColor = HudStyle.TextSecondary;
        public Color armedLabelColor = HudStyle.TextOnAccent;
        [Range(0f, 1f)] public float quietAlpha = 0.55f;
        [Range(0f, 1f)] public float disabledAlpha = 0.35f;
        [Tooltip("The hovered segment grows this much (a lift, not a bounce).")]
        public float hoverScale = 1.04f;

        [Header("Segment content (canvas units from the segment's centre)")]
        public float iconOffset = 22f;
        public float labelOffsetWithIcon = -6f;
        public float stateOffsetWithIcon = -28f;
        public float optionLabelOffset = 9f;
        public float optionStateOffset = -15f;

        float _shownUnfold;       // smoothed, 0 folded to 1 unfolded
        float _targetUnfold;
        float _outerUnfold;       // the outer ring's own unfold (pull comparison)
        bool _outerWasShown;
        float _focusShown;        // 0 primary segments to 1 a focused action's options
        int _lastFocusParent = SystemsArcModel.NoItem;
        float _fold;              // 1 while folding after a release (dims towards the end)
        float _hintUntil = float.NegativeInfinity;
        bool _reducedMotion, _initialized;
        float _opacity = 1f;
        readonly float[] _innerHover = new float[SystemsArcModel.MaxItems];
        readonly float[] _outerHover = new float[SystemsArcModel.MaxItems];
        readonly float[] _optionHover = new float[SystemsArcModel.MaxItems];
        readonly HudIcon[] _iconShown = new HudIcon[SystemsArcModel.MaxItems];

        public float ShownUnfold => _shownUnfold;
        public bool IsShown => _shownUnfold > 0.001f;
        /// <summary>0 showing the primary segments, 1 showing a focused action's options.</summary>
        public float ShownFocus => _focusShown;
        /// <summary>The arc's hit area takes presses (only while the arc is open as a menu).</summary>
        public bool TakesTouches => group != null && group.blocksRaycasts;

        /// <summary>
        /// Open as a tappable menu: the band takes presses (through the hotspot shape over it);
        /// folded or pulled, it takes none, so a hidden arc never intercepts a touch.
        /// </summary>
        public void SetTakesTouches(bool on)
        {
            if (group == null) return;
            group.blocksRaycasts = on;
            group.interactable = on;
        }

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        public void SetOpacity(float opacity)
        {
            _opacity = Mathf.Clamp01(opacity);
            ApplyAlpha();
        }

        /// <summary>New geometry (fitted to the screen): the segments are re-cut for the new radii.</summary>
        public void ApplySettings(SystemsArcSettings fitted)
        {
            if (fitted == null) return;
            settings = fitted;
            if (_initialized) PrepareGeometry();
        }

        /// <summary>
        /// Places the caption beside the wheel: right-aligned at <paramref name="rightCentre"/>
        /// (canvas units from the handle centre), wrapping within <paramref name="width"/>.
        /// A width too small for a few words hides it (the wheel reads without it).
        /// </summary>
        public void PlaceCaption(Vector2 rightCentre, float width)
        {
            if (caption == null) return;
            bool shown = width >= 140f;
            caption.enabled = shown;
            if (!shown) return;
            var rect = caption.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = rightCentre;
            rect.sizeDelta = new Vector2(width, 84f);
            caption.alignment = TextAnchor.MiddleRight;
            caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            caption.verticalOverflow = VerticalWrapMode.Overflow;
        }

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            PrepareGeometry();
            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                if (innerMarks[i] != null && innerMarks[i].sprite == null) innerMarks[i].sprite = HudSprites.Disc;
                if (optionMarks[i] != null && optionMarks[i].sprite == null) optionMarks[i].sprite = HudSprites.Disc;
            }
            ApplyAlpha();
            Present(null, false);
        }

        void PrepareGeometry()
        {
            var bandSprite = HudSprites.Annulus(settings.innerRadius / Mathf.Max(1f, settings.outerRadius));
            var ringSprite = HudSprites.Annulus(settings.OuterRingInner / Mathf.Max(1f, settings.OuterRingOuter));
            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                Prepare(innerSegments[i], bandSprite, settings.outerRadius * 2f);
                Prepare(optionSegments[i], bandSprite, settings.outerRadius * 2f);
                Prepare(outerSegments[i], ringSprite, settings.OuterRingOuter * 2f);
            }
        }

        static void Prepare(Image segment, Sprite sprite, float size)
        {
            if (segment == null) return;
            segment.sprite = sprite;
            segment.type = Image.Type.Filled;
            segment.fillMethod = Image.FillMethod.Radial360;
            segment.fillOrigin = (int)Image.Origin360.Right;
            segment.fillClockwise = false; // the fill runs anticlockwise from the right: increasing angle
            segment.raycastTarget = false;
            segment.rectTransform.sizeDelta = new Vector2(size, size);
        }

        void ApplyAlpha()
        {
            if (group != null) group.alpha = _opacity * Mathf.Clamp01(_shownUnfold * 1.6f) * (1f - 0.35f * _fold);
        }

        /// <summary>Unfold to the pull progress (0 folds away).</summary>
        public void SetUnfold(float target)
        {
            _targetUnfold = Mathf.Clamp01(target);
            _fold = 0f;
        }

        /// <summary>The arc folds away (after a commit or a cancel).</summary>
        public void Fold()
        {
            _targetUnfold = 0f;
            _fold = 1f;
            _hintUntil = float.NegativeInfinity;
        }

        /// <summary>Show the wheel briefly so the player sees what a tap on the handle opens.</summary>
        public void ShowHint() => _hintUntil = Time.unscaledTime + settings.hintSeconds;

        /// <summary>Per frame from the session: the model's items, emphasis and hover.</summary>
        public void Present(SystemsArcModel model, bool pulling) => Present(model, pulling, false);

        /// <summary>
        /// Per frame from the session. <paramref name="pulling"/>: a finger is pulling or sliding;
        /// <paramref name="open"/>: the arc stands open as a tappable menu (fully unfolded, no finger needed).
        /// </summary>
        public void Present(SystemsArcModel model, bool pulling, bool open)
        {
            EnsureInitialized();
            if (model == null)
            {
                for (int i = 0; i < SystemsArcModel.MaxItems; i++)
                {
                    Show(innerSegments[i], innerLabels[i], innerMarks[i], innerStates[i], innerIcons[i], false);
                    Show(optionSegments[i], optionLabels[i], optionMarks[i], optionStates[i], null, false);
                    Show(outerSegments[i], outerLabels[i], null, null, null, false);
                }
                if (caption != null) caption.text = string.Empty;
                return;
            }

            bool hinting = Time.unscaledTime < _hintUntil;
            if (open) SetUnfold(1f);
            else if (pulling) SetUnfold(Mathf.Max(model.Progress, hinting ? 1f : 0f));
            else if (hinting) { _targetUnfold = 1f; _fold = 0f; }
            else if (_targetUnfold > 0f && _fold == 0f) Fold();

            float unfold = _reducedMotion ? (_targetUnfold > 0.5f ? 1f : 0f) : HudEasing.SoftSpring(_shownUnfold);
            float sweep = model.SegmentSweep;
            float labelRadius = (settings.innerRadius + settings.outerRadius) * 0.5f;
            bool armed = model.IsArmed;
            bool shownAtAll = _shownUnfold > 0.001f;

            // The focus: the primary segments fold into the tapped action, its options unfold from it.
            float focusTarget = model.Focused ? 1f : 0f;
            _focusShown = Approach(_focusShown, focusTarget, settings.unfoldSeconds);
            if (model.Focused) _lastFocusParent = model.FocusParent;
            float focus = Mathf.SmoothStep(0f, 1f, _focusShown);
            int parent = model.Focused ? model.FocusParent : _lastFocusParent;
            if (focus <= 0.001f && !model.Focused) _lastFocusParent = SystemsArcModel.NoItem;
            float parentAngle = parent >= 0 && parent < model.InnerCount ? model.SlotAngle(parent) : 0f;

            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                bool present = i < model.InnerCount && model.InnerItem(i).Available && shownAtAll && focus < 0.999f;
                Show(innerSegments[i], innerLabels[i], innerMarks[i], innerStates[i], innerIcons[i], present);
                if (!present) continue;
                ref var item = ref model.InnerItem(i);
                bool hovered = model.HasHovered && !model.HoveredIsOuter && !model.HoveredIsOption && model.HoveredIndex == i;
                _innerHover[i] = Approach(_innerHover[i], hovered ? 1f : 0f, settings.hoverSeconds);
                float angle = model.UnfoldedAngle(i, unfold);
                if (parent >= 0) angle = Mathf.LerpUnclamped(angle, parentAngle, focus);
                Place(innerSegments[i], innerLabels[i], innerMarks[i], innerStates[i], innerIcons[i], ref _iconShown[i], item, angle, sweep,
                    labelRadius, settings.outerRadius, _innerHover[i], hovered && armed, unfold, 1f - focus);
            }

            float optionSweep = model.OptionSweep;
            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                bool present = model.Focused && i < model.OptionCount && model.OptionItem(i).Available && shownAtAll;
                Show(optionSegments[i], optionLabels[i], optionMarks[i], optionStates[i], null, present);
                if (!present) continue;
                ref var item = ref model.OptionItem(i);
                bool hovered = model.HasHovered && model.HoveredIsOption && model.HoveredIndex == i;
                _optionHover[i] = Approach(_optionHover[i], hovered ? 1f : 0f, settings.hoverSeconds);
                HudIcon none = HudIcon.None;
                Place(optionSegments[i], optionLabels[i], optionMarks[i], optionStates[i], null, ref none, item, model.UnfoldedOptionAngle(i, focus),
                    optionSweep, labelRadius, settings.outerRadius, _optionHover[i], hovered && armed, unfold, focus);
            }

            bool outerShown = model.OuterRevealed && _shownUnfold > 0.5f;
            if (outerShown != _outerWasShown) { _outerWasShown = outerShown; if (!outerShown) _outerUnfold = 0f; }
            float outerUnfold = _reducedMotion ? (outerShown ? 1f : 0f) : HudEasing.SoftSpring(_outerUnfold);
            float outerLabelRadius = (settings.OuterRingInner + settings.OuterRingOuter) * 0.5f;
            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                bool present = outerShown && i < model.OuterCount && model.OuterItem(i).Available;
                Show(outerSegments[i], outerLabels[i], null, null, null, present);
                if (!present) continue;
                ref var item = ref model.OuterItem(i);
                bool hovered = model.HasHovered && model.HoveredIsOuter && model.HoveredIndex == i;
                _outerHover[i] = Approach(_outerHover[i], hovered ? 1f : 0f, settings.hoverSeconds);
                HudIcon none = HudIcon.None;
                Place(outerSegments[i], outerLabels[i], null, null, null, ref none, item, model.UnfoldedOuterAngle(i, outerUnfold), sweep,
                    outerLabelRadius, settings.OuterRingOuter, _outerHover[i], hovered && armed, outerUnfold, 1f);
            }

            if (caption != null && caption.enabled)
            {
                string text = CaptionFor(model, pulling, open);
                if (!ReferenceEquals(caption.text, text)) caption.text = text;
            }
        }

        public const string OpenCaption = "Tap to choose · tap outside to close";
        public const string SubArcCaption = "Tap an option · tap the action again to go back";
        public const string FocusCaptionDefault = "Tap an option · tap the handle to go back";
        public const string ConfirmCaption = "Hazardous: tap CONFIRM again, or tap outside to cancel";

        static string CaptionFor(SystemsArcModel model, bool pulling, bool open)
        {
            if (open && !pulling)
            {
                if (model.Focused) return AnyConfirm(model) ? ConfirmCaption : (model.FocusCaption ?? FocusCaptionDefault);
                return model.OuterRevealed && model.OuterParent != SystemsArcModel.NoItem ? SubArcCaption : OpenCaption;
            }
            if (!pulling) return string.Empty;
            if (!model.Selectable) return "Pull to open";
            if (!model.HasHovered)
            {
                if (open) return model.Focused ? (model.FocusCaption ?? FocusCaptionDefault) : OpenCaption;
                return model.OuterRevealed ? "Release here: nothing changes" : "Release here: nothing changes · rest for more";
            }
            var item = model.HoveredItem;
            if (item.Emphasis == ArcEmphasis.Disabled) return "Not available now";
            return model.OuterRevealed && !model.HoveredIsOuter && !model.HoveredIsOption && model.OuterParent == model.HoveredIndex
                ? "Release to choose · or slide out to an option" : "Release to choose";
        }

        static bool AnyConfirm(SystemsArcModel model)
        {
            for (int i = 0; i < model.OptionCount; i++)
                if (model.OptionItem(i).Emphasis == ArcEmphasis.Confirm) return true;
            return false;
        }

        void Place(Image segment, Text label, Image mark, Text state, Image icon, ref HudIcon iconShown, in ArcItem item, float angle, float sweep,
            float labelRadius, float markRadius, float hover, bool armed, float unfold, float visibility)
        {
            if (segment == null) return;
            var rect = segment.rectTransform;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle - sweep * 0.5f);
            segment.fillAmount = sweep / 360f;
            float lift = 1f + (hoverScale - 1f) * hover;
            float grow = Mathf.Lerp(0.94f, 1f, Mathf.Clamp01(unfold));
            float scale = lift * grow;
            if (rect.localScale.x != scale) rect.localScale = new Vector3(scale, scale, 1f);

            bool confirm = item.Emphasis == ArcEmphasis.Confirm;
            float emphasisAlpha = item.Emphasis == ArcEmphasis.Quiet ? quietAlpha : (item.Emphasis == ArcEmphasis.Disabled ? disabledAlpha : 1f);
            Color fill = confirm ? confirmColor : (item.Emphasis == ArcEmphasis.Engaged ? engagedColor : segmentColor);
            if (!confirm) fill = Color.Lerp(fill, hoverColor, hover);
            if (armed && !confirm) fill = armedColor;
            fill.a *= (armed || confirm ? 1f : emphasisAlpha) * visibility;
            segment.color = fill;

            Color text = armed && !confirm ? armedLabelColor
                : (confirm || item.Emphasis == ArcEmphasis.Full || item.Emphasis == ArcEmphasis.Engaged ? labelColor : quietLabelColor);
            text.a *= (armed || confirm ? 1f : Mathf.Max(emphasisAlpha, 0.6f)) * visibility;

            var centre = GestureMath.FromAngle(angle) * labelRadius;
            bool hasIcon = icon != null && item.Icon != HudIcon.None;
            bool hasState = state != null && !string.IsNullOrEmpty(item.State);
            if (icon != null)
            {
                if (icon.enabled != hasIcon) icon.enabled = hasIcon;
                if (hasIcon)
                {
                    if (iconShown != item.Icon)
                    {
                        iconShown = item.Icon;
                        icon.sprite = HudSprites.Icon(item.Icon);
                    }
                    icon.rectTransform.anchoredPosition = centre + new Vector2(0f, iconOffset);
                    icon.color = text;
                }
            }
            if (label != null)
            {
                float offset = hasIcon ? labelOffsetWithIcon : (hasState ? optionLabelOffset : 0f);
                label.rectTransform.anchoredPosition = centre + new Vector2(0f, offset);
                string current = item.CurrentLabel;
                if (!ReferenceEquals(label.text, current)) label.text = current;
                label.color = text;
            }
            if (state != null)
            {
                if (state.enabled != hasState) state.enabled = hasState;
                if (hasState)
                {
                    state.rectTransform.anchoredPosition = centre + new Vector2(0f, hasIcon ? stateOffsetWithIcon : optionStateOffset);
                    if (!ReferenceEquals(state.text, item.State)) state.text = item.State;
                    var c = text;
                    c.a *= 0.85f;
                    state.color = c;
                }
            }
            if (mark != null)
            {
                bool engaged = item.Emphasis == ArcEmphasis.Engaged && visibility > 0.5f;
                if (mark.enabled != engaged) mark.enabled = engaged;
                if (engaged) mark.rectTransform.anchoredPosition = GestureMath.FromAngle(angle) * (markRadius - 16f);
            }
        }

        static void Show(Image segment, Text label, Image mark, Text state, Image icon, bool shown)
        {
            if (segment != null && segment.enabled != shown) segment.enabled = shown;
            if (label != null && label.enabled != shown) label.enabled = shown;
            if (!shown)
            {
                if (mark != null && mark.enabled) mark.enabled = false;
                if (state != null && state.enabled) state.enabled = false;
                if (icon != null && icon.enabled) icon.enabled = false;
            }
        }

        float Approach(float current, float target, float seconds)
        {
            if (_reducedMotion || seconds <= 0f) return target;
            return Mathf.MoveTowards(current, target, Time.unscaledDeltaTime / seconds);
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            float dt = Time.unscaledDeltaTime;
            float seconds = _targetUnfold > _shownUnfold ? settings.unfoldSeconds : settings.foldSeconds;
            if (_reducedMotion || seconds <= 0f) _shownUnfold = _targetUnfold;
            else _shownUnfold = Mathf.MoveTowards(_shownUnfold, _targetUnfold, dt / seconds);
            if (_shownUnfold <= 0.001f && _fold > 0f) _fold = 0f;
            if (_outerWasShown && _outerUnfold < 1f)
                _outerUnfold = _reducedMotion || settings.unfoldSeconds <= 0f ? 1f : Mathf.Min(1f, _outerUnfold + dt / settings.unfoldSeconds);
            ApplyAlpha();
        }
    }
}
