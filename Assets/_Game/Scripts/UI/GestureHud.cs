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
        bool _bandEnabled;
        bool _held, _open, _back;
        float _opacity = 1f;

        public const string SystemsLabel = "SYSTEMS";

        public bool GestureFirst => _gestureFirst;
        /// <summary>The HUD band takes touches (gesture-first with the pull comparison on; docs/TAP_DISCLOSURE_DESIGN.md, milestone 4).</summary>
        public bool BandActive => _gestureFirst && _bandEnabled;
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

        /// <summary>Gesture-first on: the handle (and the band, when enabled) take touches. Off (legacy): they are hidden and inert.</summary>
        public void SetGestureFirst(bool on)
        {
            _gestureFirst = on;
            ApplyMode();
        }

        /// <summary>
        /// The HUD band's pull (density preview) is a development comparison like the wheel's pull:
        /// in the tap-first interface the top left belongs to the flight information cluster, whose
        /// taps open it, and the HUD profile is chosen in the command cluster.
        /// </summary>
        public void SetBandEnabled(bool on)
        {
            _bandEnabled = on;
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
                bandGroup.blocksRaycasts = BandActive;
                bandGroup.interactable = BandActive;
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
            _held = held;
            _open = open;
            ApplyHandle();
        }

        /// <summary>The handle is the wheel's hub, so it says where the player is.</summary>
        public bool ShowsBack => _back;

        /// <summary>
        /// The hub: while an action's options are shown the handle carries that action's name
        /// and a chevron pointing back (a tap goes back to the wheel); otherwise SYSTEMS.
        /// <paramref name="label"/> is a fixed string (null: SYSTEMS).
        /// </summary>
        public void SetHub(string label, bool back)
        {
            _back = back;
            if (handleLabel != null)
            {
                string text = back && label != null ? label : SystemsLabel;
                if (!ReferenceEquals(handleLabel.text, text)) handleLabel.text = text;
            }
            ApplyHandle();
        }

        void ApplyHandle()
        {
            if (handleGrip != null) handleGrip.color = _held ? gripHeld : (_open ? gripOpen : gripIdle);
            if (handleChevron == null) return;
            var c = handleChevron.color;
            c.a = _back ? 0.9f : (_open ? 0f : (_held ? 0.9f : 0.5f));
            handleChevron.color = c;
            var rect = handleChevron.rectTransform;
            if (_back)
            {
                // Pointing left, at the left end of the grip: back.
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(20f, 4f);
                rect.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -4f);
                rect.localRotation = Quaternion.identity;
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
