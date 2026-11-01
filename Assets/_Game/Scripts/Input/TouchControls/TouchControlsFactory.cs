using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// Builds the touch control hierarchy in code (used by the scene builder and
    /// by tests), so the layout is reviewable and never hand-edited in a scene.
    /// Sizes are in canvas units for a 1920×1080 reference with height matching;
    /// everything is anchored to the corners of the safe area.
    /// </summary>
    public static class TouchControlsFactory
    {
        /// <summary>Right edge of the joystick touch region, fraction of the safe-area width. Other layouts keep clear of it.</summary>
        public const float JoystickRegionRight = 0.42f;
        /// <summary>The throttle's touch area: offset from the bottom-right corner and size, canvas units.</summary>
        public static readonly Vector2 ThrottleOffset = new Vector2(-24f, 24f);
        public static readonly Vector2 ThrottleAreaSize = new Vector2(220f, 620f);
        /// <summary>Top of the throttle's touch area above the bottom of the safe area, canvas units.</summary>
        public static float ThrottleAreaTop => ThrottleOffset.y + ThrottleAreaSize.y;

        static readonly Color Tint = new Color(1f, 1f, 1f, 0.3f);
        /// <summary>Throttle reference marks at 25, 50 and 75 %.</summary>
        static readonly float[] ThrottleTicks = { 0.25f, 0.5f, 0.75f };

        public static TouchFlightControls Build(RectTransform safeArea, Font font)
        {
            var root = NewRect("TouchControls", safeArea);
            // Build inactive: components added at runtime would otherwise run Awake
            // before their references are wired.
            root.gameObject.SetActive(false);
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            var controls = root.gameObject.AddComponent<TouchFlightControls>();
            controls.controlsGroup = group;

            controls.joystick = BuildJoystick(root);
            controls.throttle = BuildThrottle(root, font);
            controls.brake = BuildBrake(root, font);
            controls.diagnostics = BuildDiagnostics(root, font);
            root.gameObject.SetActive(true);
            return controls;
        }

        /// <summary>
        /// Lower-left touch region (most of the left half, below the top bar), so
        /// the thumb can land anywhere comfortable on phones and tablets.
        /// </summary>
        static FloatingJoystick BuildJoystick(RectTransform parent)
        {
            var region = NewRect("JoystickRegion", parent);
            region.anchorMin = Vector2.zero;
            region.anchorMax = new Vector2(JoystickRegionRight, 0.78f);
            region.pivot = Vector2.zero;
            region.offsetMin = new Vector2(16f, 16f);
            region.offsetMax = Vector2.zero;
            AddHitArea(region); // catches touches; draws nothing (no overdraw over 40 % of the screen)

            var joystick = region.gameObject.AddComponent<FloatingJoystick>();
            float radius = joystick.settings.radius;

            var visual = NewRect("Base", region);
            visual.sizeDelta = Vector2.one * (radius * 2f);
            joystick.visualRoot = visual;
            joystick.baseFill = AddImage(visual, Tint, raycast: false); // on the base itself, sized 2r

            var ring = NewRect("Ring", visual);
            Stretch(ring);
            joystick.baseRing = AddImage(ring, Tint, raycast: false);
            TouchControlStyle.AddEdge(joystick.baseRing);

            var knob = NewRect("Knob", visual);
            knob.sizeDelta = Vector2.one * (radius * 0.85f);
            joystick.knob = knob;
            joystick.knobImage = AddImage(knob, Tint, raycast: false);
            TouchControlStyle.AddEdge(joystick.knobImage);
            return joystick;
        }

        /// <summary>
        /// Lower-right throttle: a slim visible track inside a much larger touch area.
        /// </summary>
        static ThrottleSlider BuildThrottle(RectTransform parent, Font font)
        {
            var hitArea = NewRect("Throttle", parent);
            hitArea.anchorMin = hitArea.anchorMax = hitArea.pivot = new Vector2(1f, 0f);
            hitArea.anchoredPosition = ThrottleOffset;
            hitArea.sizeDelta = ThrottleAreaSize;
            AddHitArea(hitArea);
            var slider = hitArea.gameObject.AddComponent<ThrottleSlider>();

            var track = NewRect("Track", hitArea);
            track.anchorMin = track.anchorMax = track.pivot = new Vector2(0.5f, 0f);
            track.anchoredPosition = new Vector2(0f, 60f);
            track.sizeDelta = new Vector2(64f, 460f);
            slider.track = track;
            slider.trackImage = AddImage(track, Tint, raycast: false);
            TouchControlStyle.AddEdge(slider.trackImage);

            var fill = NewRect("Fill", track);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(1f, 0f);
            fill.offsetMin = new Vector2(6f, 0f); // zero height at 0 %, never negative
            fill.offsetMax = new Vector2(-6f, 0f);
            slider.fill = fill;
            slider.fillImage = AddImage(fill, Tint, raycast: false);

            var handle = NewRect("Handle", track);
            handle.anchorMin = new Vector2(-0.35f, 0f);
            handle.anchorMax = new Vector2(1.35f, 0f);
            handle.sizeDelta = new Vector2(0f, 14f);
            slider.handle = handle;
            slider.handleImage = AddImage(handle, Color.white, raycast: false);
            TouchControlStyle.AddEdge(slider.handleImage);

            // Reference marks beside the track (not on it, so the fill stays readable).
            slider.ticks = new Image[ThrottleTicks.Length];
            for (int i = 0; i < ThrottleTicks.Length; i++)
            {
                var tick = NewRect("Tick" + Mathf.RoundToInt(ThrottleTicks[i] * 100f), track);
                tick.anchorMin = tick.anchorMax = new Vector2(0f, ThrottleTicks[i]);
                tick.pivot = new Vector2(1f, 0.5f);
                tick.anchoredPosition = new Vector2(-6f, 0f);
                tick.sizeDelta = new Vector2(i == 1 ? 18f : 12f, 3f);
                slider.ticks[i] = AddImage(tick, Tint, raycast: false);
            }

            var label = NewText("Label", hitArea, font, TouchControlStyle.ValueSize, TextAnchor.MiddleCenter);
            AddShadow(label);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 30f);
            label.rectTransform.sizeDelta = new Vector2(200f, 44f);
            label.text = "0%";
            slider.label = label;
            return slider;
        }

        /// <summary>Hold-to-brake, left of the throttle, reachable by the right thumb.</summary>
        static HoldButton BuildBrake(RectTransform parent, Font font)
        {
            var rect = NewRect("Brake", parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-268f, 48f);
            rect.sizeDelta = new Vector2(168f, 112f);
            rect.gameObject.AddComponent<CanvasGroup>();
            var button = rect.gameObject.AddComponent<HoldButton>();
            button.background = AddImage(rect, Tint, raycast: true);

            var label = NewText("Label", rect, font, TouchControlStyle.ButtonSize + 2, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "BRAKE";
            button.label = label;
            return button;
        }

        static Text BuildDiagnostics(RectTransform parent, Font font)
        {
            var text = NewText("TouchDiagnostics", parent, font, 22, TextAnchor.UpperLeft);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0.6f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -264f); // below the contextual HUD's flight data and guides
            rect.sizeDelta = new Vector2(0f, 64f);
            text.color = new Color(1f, 1f, 1f, 0.7f);
            return text;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>A touch area that draws nothing.</summary>
        static void AddHitArea(RectTransform rect)
        {
            var graphic = rect.gameObject.AddComponent<RaycastOnlyGraphic>();
            graphic.raycastTarget = true;
        }

        static void AddShadow(Graphic graphic)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = TouchControlStyle.TextShadow;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        static Image AddImage(RectTransform rect, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
