using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the contextual HUD hierarchy in code (used by the scene builder and
    /// by tests), so the layout is reviewable and identical everywhere. Canvas
    /// units for a 1920×1080 reference with height matching, inside the safe area.
    ///
    /// Layout (never changes at runtime; disclosure only fades elements):
    /// - Top left: nothing of its own. The flight information cluster (<see cref="FlightInfoClusterFactory"/>)
    ///   holds the readings and the approach and navigation guides.
    /// - Upper middle, slightly right of centre (between the joystick region and the
    ///   action rail, above the aircraft): warning banners, most urgent first.
    /// - Top right: nothing of its own. The command cluster (<see cref="CommandClusterFactory"/>)
    ///   holds the camera, the HUD profile and the session actions; the development inspector
    ///   sits left of the cluster's column, under the top bar.
    /// - Bottom right, left of the throttle and above BRAKE: SYSTEMS, with the
    ///   contextual action rail stacked above it. Nothing overlaps the joystick
    ///   region, the throttle or the brake button.
    /// </summary>
    public static class ContextualHudFactory
    {
        public const float Margin = 24f;
        public const float TopMargin = 16f;
        public static readonly Vector2 RailButtonSize = new Vector2(168f, 96f);
        /// <summary>SYSTEMS sits here (bottom-right anchor); the rail continues upwards in steps of 112.</summary>
        public static readonly Vector2 RailBase = new Vector2(-268f, 184f);
        public const float RailStep = 112f;
        /// <summary>Distance from the right safe-area edge to the left edge of SYSTEMS and the action rail, canvas units.</summary>
        public static float RailLeftInset => -(RailBase.x - RailButtonSize.x);
        /// <summary>Horizontal anchor of the warning banners, fraction of the safe-area width.</summary>
        public const float WarningColumn = 0.56f;
        public static readonly Vector2 WarningSize = new Vector2(380f, 72f);

        static readonly Color ShadowColor = HudStyle.TextShadow;

        public static ContextualHud Build(RectTransform safeArea, Font font)
        {
            var root = NewRect("ContextualHud", safeArea);
            // Build inactive so Awake runs after every reference is wired.
            root.gameObject.SetActive(false);
            Stretch(root);
            var hud = root.gameObject.AddComponent<ContextualHud>();

            BuildWarnings(root, font, hud);
            // The development inspector is built before the rail: where the top rail slot (ASSIST) reaches
            // under it, the rail button draws on top and takes the touch.
            hud.inspector = BuildInspector(root, font);
            BuildRail(root, font, hud);

            root.gameObject.SetActive(true);
            return hud;
        }

        static void BuildWarnings(RectTransform parent, Font font, ContextualHud hud)
        {
            for (int k = 0; k < ContextualHud.WarningSlotCount; k++)
            {
                var slot = NewRect("Warning" + k, parent);
                // In the free column between the joystick region (left 42 %) and the action rail,
                // above the aircraft: clear of every flight control from 4:3 tablets to 20:9 phones.
                slot.anchorMin = slot.anchorMax = new Vector2(WarningColumn, 1f);
                slot.pivot = new Vector2(0.5f, 1f);
                slot.anchoredPosition = new Vector2(0f, -250f - k * 84f);
                slot.sizeDelta = WarningSize;
                hud.warningSlots[k] = Hidden(slot);
                hud.warningBackgrounds[k] = AddImage(slot, hud.warningColor);

                // Severity symbol on the left; the word is centred in the rest of the banner.
                var icon = NewRect("Icon", slot);
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0f, 0.5f);
                icon.anchoredPosition = new Vector2(16f, 0f);
                icon.sizeDelta = new Vector2(44f, 44f);
                hud.warningIcons[k] = AddImage(icon, Color.white);

                var label = NewText("Label", slot, font, 34, TextAnchor.MiddleCenter, Color.white, shadow: false);
                label.fontStyle = FontStyle.Bold;
                Stretch(label.rectTransform);
                label.rectTransform.offsetMin = new Vector2(60f, 0f);
                label.rectTransform.offsetMax = new Vector2(-16f, 0f);
                hud.warningLabels[k] = label;
            }
        }

        static void BuildRail(RectTransform parent, Font font, ContextualHud hud)
        {
            var corner = new Vector2(1f, 0f);
            hud.systemsButton = BuildButton("SystemsButton", parent, font, HudStyle.ButtonSize, corner, RailBase, RailButtonSize, "SYSTEMS");
            hud.systemsGroup = hud.systemsButton.gameObject.AddComponent<CanvasGroup>();
            for (int i = 0; i < ContextualHud.RailSlotCount; i++)
            {
                var position = RailBase + new Vector2(0f, (i + 1) * RailStep);
                var button = BuildButton("RailSlot" + i, parent, font, HudStyle.ButtonSize, corner, position, RailButtonSize, string.Empty);
                button.feedback = UiFeedback.Confirm; // rail actions change a system or a guide
                Hidden((RectTransform)button.transform);
                hud.railSlots[i] = button;
            }
        }

        /// <summary>Development inspector: under the top bar, left of the command cluster's column (LOOK FWD sits under the camera).</summary>
        public static readonly Vector2 InspectorPosition = new Vector2(-152f, -128f);
        public static readonly Vector2 InspectorSize = new Vector2(488f, 216f);

        static DisclosureInspector BuildInspector(RectTransform parent, Font font)
        {
            var text = NewText("DisclosureInspector", parent, font, 18, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.85f));
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = InspectorPosition;
            rect.sizeDelta = InspectorSize;
            text.raycastTarget = true; // tap to collapse; rail buttons drawn over it keep their touches
            var inspector = text.gameObject.AddComponent<DisclosureInspector>();
            inspector.text = text;
            return inspector;
        }

        static HudButton BuildButton(string name, RectTransform parent, Font font, int fontSize, Vector2 anchor, Vector2 position,
            Vector2 size, string caption)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = AddImage(rect, HudStyle.ControlSurface);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;

            var label = NewText("Label", rect, font, fontSize, TextAnchor.MiddleCenter, Color.white, shadow: false);
            Stretch(label.rectTransform);
            label.text = caption;
            button.label = label;
            return button;
        }

        /// <summary>Adds a CanvasGroup that starts invisible and ignores touches.</summary>
        static CanvasGroup Hidden(RectTransform rect)
        {
            var group = rect.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            return group;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>
        /// Sprites are generated at runtime and cannot be saved in a scene, so the
        /// factory leaves them empty; ContextualHud and HudButton assign them in Awake.
        /// </summary>
        static Image AddImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor, Color color, bool shadow = true)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            if (shadow) AddShadow(text);
            return text;
        }

        /// <summary>A soft drop shadow keeps white text and lights legible against bright sky without a panel.</summary>
        static void AddShadow(Graphic graphic)
        {
            var effect = graphic.gameObject.AddComponent<Shadow>();
            effect.effectColor = ShadowColor;
            effect.effectDistance = new Vector2(1.5f, -1.5f);
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
