using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Draws the systems arc: segments that unfold from the handle as the thumb
    /// pulls, labels that fade in with them, the segment under the finger lifted
    /// a little, the armed segment filled cyan with dark text, engaged systems
    /// marked, and a smooth fold after release. It only draws what
    /// <see cref="SystemsArcModel"/> says; it never decides anything. Segments are
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
        public Image[] outerSegments = new Image[SystemsArcModel.MaxItems];
        public Text[] outerLabels = new Text[SystemsArcModel.MaxItems];
        [Tooltip("A short line under the arc: what releasing would do.")]
        public Text caption;
        public SystemsArcSettings settings = new SystemsArcSettings();

        [Header("Colours")]
        public Color segmentColor = HudStyle.ControlSurface;
        public Color hoverColor = new Color(0.16f, 0.2f, 0.24f, 0.7f);
        public Color armedColor = HudStyle.Selected;
        public Color engagedColor = new Color(0.33f, 0.8f, 0.95f, 0.45f);
        public Color labelColor = HudStyle.TextPrimary;
        public Color quietLabelColor = HudStyle.TextSecondary;
        public Color armedLabelColor = HudStyle.TextOnAccent;
        [Range(0f, 1f)] public float quietAlpha = 0.55f;
        [Range(0f, 1f)] public float disabledAlpha = 0.35f;
        [Tooltip("The hovered segment grows this much (a lift, not a bounce).")]
        public float hoverScale = 1.04f;

        float _shownUnfold;       // smoothed, 0 folded to 1 unfolded
        float _targetUnfold;
        float _outerUnfold;       // the outer ring's own unfold (it appears after a rest)
        bool _outerWasShown;
        float _fold;              // 1 while folding after a release (dims towards the end)
        float _hintUntil = float.NegativeInfinity;
        bool _reducedMotion, _initialized;
        float _opacity = 1f;
        readonly float[] _innerHover = new float[SystemsArcModel.MaxItems];
        readonly float[] _outerHover = new float[SystemsArcModel.MaxItems];

        public float ShownUnfold => _shownUnfold;
        public bool IsShown => _shownUnfold > 0.001f;
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

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            float innerRatio = settings.innerRadius / Mathf.Max(1f, settings.outerRadius);
            float outerRatio = settings.OuterRingInner / Mathf.Max(1f, settings.OuterRingOuter);
            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                Prepare(innerSegments[i], HudSprites.Annulus(innerRatio), settings.outerRadius * 2f);
                Prepare(outerSegments[i], HudSprites.Annulus(outerRatio), settings.OuterRingOuter * 2f);
                if (innerMarks[i] != null && innerMarks[i].sprite == null) innerMarks[i].sprite = HudSprites.Disc;
            }
            ApplyAlpha();
            Present(null, false);
        }

        void Prepare(Image segment, Sprite sprite, float size)
        {
            if (segment == null) return;
            if (segment.sprite == null) segment.sprite = sprite;
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

        /// <summary>A tap on the handle: show the arc briefly so the player sees what a pull reveals.</summary>
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
                    Show(innerSegments[i], innerLabels[i], innerMarks[i], false);
                    Show(outerSegments[i], outerLabels[i], null, false);
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

            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                bool present = i < model.InnerCount && model.InnerItem(i).Available;
                Show(innerSegments[i], innerLabels[i], innerMarks[i], present && _shownUnfold > 0.001f);
                if (!present) continue;
                ref var item = ref model.InnerItem(i);
                bool hovered = model.HasHovered && !model.HoveredIsOuter && model.HoveredIndex == i;
                _innerHover[i] = Approach(_innerHover[i], hovered ? 1f : 0f, settings.hoverSeconds);
                float angle = model.UnfoldedAngle(i, unfold);
                Place(innerSegments[i], innerLabels[i], innerMarks[i], item, angle, sweep, labelRadius, settings.outerRadius,
                    _innerHover[i], hovered && armed, unfold);
            }

            bool outerShown = model.OuterRevealed && _shownUnfold > 0.5f;
            if (outerShown != _outerWasShown) { _outerWasShown = outerShown; if (!outerShown) _outerUnfold = 0f; }
            float outerUnfold = _reducedMotion ? (outerShown ? 1f : 0f) : HudEasing.SoftSpring(_outerUnfold);
            float outerLabelRadius = (settings.OuterRingInner + settings.OuterRingOuter) * 0.5f;
            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                bool present = outerShown && i < model.OuterCount && model.OuterItem(i).Available;
                Show(outerSegments[i], outerLabels[i], null, present);
                if (!present) continue;
                ref var item = ref model.OuterItem(i);
                bool hovered = model.HasHovered && model.HoveredIsOuter && model.HoveredIndex == i;
                _outerHover[i] = Approach(_outerHover[i], hovered ? 1f : 0f, settings.hoverSeconds);
                Place(outerSegments[i], outerLabels[i], null, item, model.UnfoldedOuterAngle(i, outerUnfold), sweep, outerLabelRadius,
                    settings.OuterRingOuter, _outerHover[i], hovered && armed, outerUnfold);
            }

            if (caption != null)
            {
                string text = CaptionFor(model, pulling, open);
                if (!ReferenceEquals(caption.text, text)) caption.text = text;
            }
        }

        const string OpenCaption = "Tap to choose · tap outside to close";
        const string SubArcCaption = "Tap an option · tap the action again to go back";

        static string CaptionFor(SystemsArcModel model, bool pulling, bool open)
        {
            if (open && !pulling) return model.OuterRevealed && model.OuterParent != SystemsArcModel.NoItem ? SubArcCaption : OpenCaption;
            if (!pulling) return string.Empty;
            if (!model.Selectable) return "Pull to open";
            if (!model.HasHovered) return model.OuterRevealed ? "Release here: nothing changes" : "Release here: nothing changes · rest for more";
            var item = model.Item(model.HoveredIsOuter, model.HoveredIndex);
            if (item.Emphasis == ArcEmphasis.Disabled) return "Not available now";
            return model.OuterRevealed && !model.HoveredIsOuter && model.OuterParent == model.HoveredIndex ? "Release to choose · or slide out to an option" : "Release to choose";
        }

        void Place(Image segment, Text label, Image mark, in ArcItem item, float angle, float sweep, float labelRadius, float markRadius,
            float hover, bool armed, float unfold)
        {
            if (segment == null) return;
            var rect = segment.rectTransform;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle - sweep * 0.5f);
            segment.fillAmount = sweep / 360f;
            float lift = 1f + (hoverScale - 1f) * hover;
            float grow = Mathf.Lerp(0.94f, 1f, Mathf.Clamp01(unfold));
            float scale = lift * grow;
            if (rect.localScale.x != scale) rect.localScale = new Vector3(scale, scale, 1f);

            float emphasisAlpha = item.Emphasis == ArcEmphasis.Quiet ? quietAlpha : (item.Emphasis == ArcEmphasis.Disabled ? disabledAlpha : 1f);
            Color fill = item.Emphasis == ArcEmphasis.Engaged ? engagedColor : segmentColor;
            fill = Color.Lerp(fill, hoverColor, hover);
            if (armed) fill = armedColor;
            fill.a *= armed ? 1f : emphasisAlpha;
            segment.color = fill;

            if (label != null)
            {
                var centre = GestureMath.FromAngle(angle) * labelRadius;
                label.rectTransform.anchoredPosition = centre;
                string text = item.CurrentLabel;
                if (!ReferenceEquals(label.text, text)) label.text = text;
                Color c = armed ? armedLabelColor : (item.Emphasis == ArcEmphasis.Full || item.Emphasis == ArcEmphasis.Engaged ? labelColor : quietLabelColor);
                c.a *= armed ? 1f : Mathf.Max(emphasisAlpha, 0.6f);
                label.color = c;
            }
            if (mark != null)
            {
                bool engaged = item.Emphasis == ArcEmphasis.Engaged;
                if (mark.enabled != engaged) mark.enabled = engaged;
                if (engaged) mark.rectTransform.anchoredPosition = GestureMath.FromAngle(angle) * (markRadius - 16f);
            }
        }

        static void Show(Image segment, Text label, Image mark, bool shown)
        {
            if (segment != null && segment.enabled != shown) segment.enabled = shown;
            if (label != null && label.enabled != shown) label.enabled = shown;
            if (mark != null && !shown && mark.enabled) mark.enabled = false;
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
