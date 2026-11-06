using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The gesture-first HUD parts in one place: the systems handle (a hotspot
    /// with a quiet grip), the HUD band hotspot, the arc, the flap lever, the
    /// density ladder, the explanation plate and the development inspector. It
    /// converts screen points to canvas units and offers the layout facts the
    /// session needs (the handle centre, the free bounds). It decides nothing.
    /// Built in code by <see cref="GestureHudFactory"/>.
    /// </summary>
    public sealed class GestureHud : MonoBehaviour
    {
        [Tooltip("Stretched over the safe area with its pivot at the bottom left: local units are canvas units from the corner.")]
        public RectTransform root;

        [Header("Systems handle")]
        public GestureHotspot handleHotspot;
        public CanvasGroup handleGroup;
        public Image handleGrip;
        public Image handleChevron;
        public Text handleLabel;

        [Header("HUD band")]
        public GestureHotspot bandHotspot;
        public CanvasGroup bandGroup;

        [Tooltip("Over the open arc: a press on its band goes to the surface on the handle's route (taps choose, slides select).")]
        public GestureHotspot arcHotspot;
        [Tooltip("Shapes the arc hotspot to the band and the outer ring, so it never covers the throttle or the brake.")]
        public HotspotShape arcHitArea;

        [Header("Controls")]
        public SystemsArcView arc;
        public FlapsLeverView lever;
        public HudDensityLadder ladder;
        public ExplanationPlate plate;
        [Tooltip("Contextual gesture cues (a travelling dot and a caption).")]
        public GestureHintView hint;

        [Header("Development")]
        public GestureInspector inspector;

        [Header("Look")]
        public Color gripIdle = HudStyle.ControlSurface;
        public Color gripHeld = HudStyle.ControlPressed;
        public Color gripOpen = new Color(0.16f, 0.2f, 0.24f, 0.7f);

        bool _gestureFirst = true, _reducedMotion, _initialized;
        float _opacity = 1f;

        public bool GestureFirst => _gestureFirst;
        public bool ReducedMotion => _reducedMotion;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (handleGrip != null && handleGrip.sprite == null) HudStyle.Round(handleGrip, HudStyle.ControlRadius);
            if (handleChevron != null && handleChevron.sprite == null) handleChevron.sprite = HudSprites.Arrow;
            SetHandleState(false, false);
            ApplyMode();
        }

        /// <summary>Gesture-first on: the handle and the band take touches. Off (legacy): they are hidden and inert.</summary>
        public void SetGestureFirst(bool on)
        {
            _gestureFirst = on;
            ApplyMode();
        }

        void ApplyMode()
        {
            if (handleGroup != null)
            {
                handleGroup.alpha = _gestureFirst ? _opacity : 0f;
                handleGroup.blocksRaycasts = _gestureFirst;
                handleGroup.interactable = _gestureFirst;
            }
            if (bandGroup != null)
            {
                bandGroup.blocksRaycasts = _gestureFirst;
                bandGroup.interactable = _gestureFirst;
            }
        }

        public void SetReducedMotion(bool reduced)
        {
            _reducedMotion = reduced;
            if (arc != null) arc.SetReducedMotion(reduced);
            if (lever != null) lever.SetReducedMotion(reduced);
            if (ladder != null) ladder.SetReducedMotion(reduced);
            if (plate != null) plate.SetReducedMotion(reduced);
            if (hint != null) hint.SetReducedMotion(reduced);
        }

        /// <summary>The player's HUD opacity (routine controls only; the plate keeps its contrast).</summary>
        public void SetOpacity(float opacity)
        {
            _opacity = Mathf.Clamp(opacity, 0.2f, 1f);
            if (handleGroup != null && _gestureFirst) handleGroup.alpha = _opacity;
            if (arc != null) arc.SetOpacity(_opacity);
            if (ladder != null) ladder.SetOpacity(_opacity);
            if (lever != null) lever.SetOpacity(_opacity);
        }

        /// <summary>The grip answers the thumb: brighter while held, lifted while the arc is open.</summary>
        public void SetHandleState(bool held, bool open)
        {
            if (handleGrip != null) handleGrip.color = held ? gripHeld : (open ? gripOpen : gripIdle);
            if (handleChevron != null)
            {
                var c = handleChevron.color;
                c.a = open ? 0f : (held ? 0.9f : 0.5f);
                handleChevron.color = c;
            }
        }

        /// <summary>Screen pixels to canvas units from the safe-area corner.</summary>
        public Vector2 ToLocal(Vector2 screenPoint)
        {
            if (root == null) return screenPoint;
            var canvas = root.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, camera, out var local);
            return local;
        }

        /// <summary>A child rectangle in root-local units (for placing plates next to HUD elements).</summary>
        public Rect LocalRect(RectTransform rect)
        {
            if (root == null || rect == null) return default;
            var r = rect.rect;
            Vector2 min = root.InverseTransformPoint(rect.TransformPoint(new Vector3(r.xMin, r.yMin, 0f)));
            Vector2 max = root.InverseTransformPoint(rect.TransformPoint(new Vector3(r.xMax, r.yMax, 0f)));
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        /// <summary>The whole safe area in local units.</summary>
        public Rect Bounds => root != null ? root.rect : new Rect(0f, 0f, 1920f, 1080f);

        /// <summary>The handle's centre in local units: the arc's anchor.</summary>
        public Vector2 HandleCentre => handleHotspot != null ? LocalRect((RectTransform)handleHotspot.transform).center : Vector2.zero;

        /// <summary>
        /// Where transient controls may be drawn: right of the joystick region, left of
        /// the throttle, above the brake and below the top bar.
        /// </summary>
        public Rect FreeBounds(float joystickRegionRight, float throttleAreaWidth, float bottomClearance, float topClearance)
        {
            var b = Bounds;
            return Rect.MinMaxRect(b.xMin + b.width * joystickRegionRight + HudStyle.Gap, b.yMin + bottomClearance,
                b.xMax - throttleAreaWidth - HudStyle.Gap, b.yMax - topClearance);
        }
    }
}
