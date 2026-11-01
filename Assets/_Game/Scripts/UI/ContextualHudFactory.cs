using OpenSkies.Awareness;
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
    /// - Top left: flight data strip (fixed slot per item), guides below it.
    /// - Upper middle, slightly right of centre (between the joystick region and the
    ///   action rail, above the aircraft): warning banners, most urgent first.
    /// - Top right: HUD profile button, left of RESET and CAM; inspector below (development only).
    /// - Bottom right, left of the throttle and above BRAKE: SYSTEMS, with the
    ///   contextual action rail stacked above it. Nothing overlaps the joystick
    ///   region, the throttle or the brake button.
    /// </summary>
    public static class ContextualHudFactory
    {
        public const float Margin = 24f;
        public const float TopMargin = 16f;
        public static readonly Vector2 DataItemSize = new Vector2(148f, 84f);
        public const float DataItemSpacing = 156f;
        public static readonly Vector2 RailButtonSize = new Vector2(168f, 96f);
        /// <summary>SYSTEMS sits here (bottom-right anchor); the rail continues upwards in steps of 112.</summary>
        public static readonly Vector2 RailBase = new Vector2(-268f, 184f);
        public const float RailStep = 112f;
        /// <summary>Distance from the right safe-area edge to the left edge of SYSTEMS and the action rail, canvas units.</summary>
        public static float RailLeftInset => -(RailBase.x - RailButtonSize.x);
        /// <summary>Horizontal anchor of the warning banners, fraction of the safe-area width.</summary>
        public const float WarningColumn = 0.56f;

        static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.7f);
        static readonly Color ValueColor = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.55f);

        public static ContextualHud Build(RectTransform safeArea, Font font)
        {
            var root = NewRect("ContextualHud", safeArea);
            // Build inactive so Awake runs after every reference is wired.
            root.gameObject.SetActive(false);
            Stretch(root);
            var hud = root.gameObject.AddComponent<ContextualHud>();

            for (int i = 0; i < ContextualHud.DataItemCount; i++)
                BuildDataItem(root, font, (HudElement)i, i, out hud.dataItems[i], out hud.dataValues[i]);
            BuildApproachGuide(root, font, hud);
            BuildNavigationGuide(root, font, hud);
            BuildWarnings(root, font, hud);
            // The development inspector is built before the rail: where the top rail slot (ASSIST) reaches
            // under it, the rail button draws on top and takes the touch.
            hud.inspector = BuildInspector(root, font);
            BuildRail(root, font, hud);
            hud.profileButton = BuildButton("HudProfileButton", root, font, 22, new Vector2(1f, 1f), new Vector2(-312f, -TopMargin),
                new Vector2(128f, 96f), ContextualHud.ProfileLabel(HudProfile.Smart));

            root.gameObject.SetActive(true);
            return hud;
        }

        static void BuildDataItem(RectTransform parent, Font font, HudElement element, int slot, out CanvasGroup group, out Text value)
        {
            var item = NewRect(element + "Readout", parent);
            item.anchorMin = item.anchorMax = item.pivot = new Vector2(0f, 1f);
            item.anchoredPosition = new Vector2(Margin + slot * DataItemSpacing, -TopMargin);
            item.sizeDelta = DataItemSize;
            group = Hidden(item);

            var label = NewText("Label", item, font, 20, TextAnchor.UpperLeft, LabelColor);
            label.rectTransform.anchorMin = new Vector2(0f, 1f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = new Vector2(0f, 26f);
            label.text = DataLabel(element);

            value = NewText("Value", item, font, 40, TextAnchor.LowerLeft, ValueColor);
            Stretch(value.rectTransform);
            value.rectTransform.offsetMax = new Vector2(0f, -24f);
            value.text = "-";
        }

        static string DataLabel(HudElement element)
        {
            switch (element)
            {
                case HudElement.Airspeed: return "IAS KT";
                case HudElement.Altitude: return "ALT FT";
                case HudElement.VerticalSpeed: return "VS FPM";
                case HudElement.Heading: return "HDG";
                default: return "GS KT";
            }
        }

        static void BuildApproachGuide(RectTransform parent, Font font, ContextualHud hud)
        {
            var panel = NewRect("ApproachGuide", parent);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(Margin, -112f);
            panel.sizeDelta = new Vector2(430f, 96f);
            hud.approachGuide = Hidden(panel);

            hud.approachRunway = TopRowText("Runway", panel, font, 0f, 120f);
            hud.approachDistance = TopRowText("Distance", panel, font, 130f, 140f);
            hud.approachHeight = TopRowText("Height", panel, font, 280f, 150f);

            // PAPI-style lights, bottom left: more white = higher.
            for (int k = 0; k < ApproachGuidance.PapiLights; k++)
            {
                var light = NewRect("Papi" + k, panel);
                light.anchorMin = light.anchorMax = light.pivot = new Vector2(0f, 0f);
                light.anchoredPosition = new Vector2(k * 30f, 8f);
                light.sizeDelta = new Vector2(22f, 22f);
                var image = AddImage(light, Color.white);
                AddShadow(image);
                hud.papiLights[k] = image;
            }

            // Centreline cue: a track with a centre tick and a marker that shows where the centreline is.
            var track = NewRect("LateralTrack", panel);
            track.anchorMin = track.anchorMax = track.pivot = new Vector2(0f, 0f);
            track.anchoredPosition = new Vector2(150f, 16f);
            track.sizeDelta = new Vector2(180f, 6f);
            AddShadow(AddImage(track, new Color(1f, 1f, 1f, 0.5f)));

            var tick = NewRect("CentreTick", track);
            tick.anchorMin = tick.anchorMax = tick.pivot = new Vector2(0.5f, 0.5f);
            tick.sizeDelta = new Vector2(3f, 18f);
            AddImage(tick, new Color(1f, 1f, 1f, 0.6f));

            var marker = NewRect("CentrelineMarker", track);
            marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(12f, 26f);
            AddShadow(AddImage(marker, new Color(1f, 0.85f, 0.3f, 1f)));
            hud.lateralMarker = marker;
            hud.lateralHalfTravel = 80f;
        }

        static void BuildNavigationGuide(RectTransform parent, Font font, ContextualHud hud)
        {
            var panel = NewRect("NavigationGuide", parent);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(474f, -112f);
            panel.sizeDelta = new Vector2(300f, 96f);
            hud.navigationGuide = Hidden(panel);

            var arrow = NewRect("Arrow", panel);
            arrow.anchorMin = arrow.anchorMax = new Vector2(0f, 0.5f);
            arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.anchoredPosition = new Vector2(32f, 0f);
            arrow.sizeDelta = new Vector2(56f, 56f);
            AddShadow(AddImage(arrow, new Color(1f, 1f, 1f, 0.95f)));
            hud.navigationArrow = arrow;

            var label = NewText("Label", panel, font, 20, TextAnchor.UpperLeft, LabelColor);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(76f, -12f);
            label.rectTransform.sizeDelta = new Vector2(220f, 26f);
            label.text = "AIRFIELD";
            hud.navigationLabel = label;

            var distance = NewText("Distance", panel, font, 32, TextAnchor.UpperLeft, ValueColor);
            distance.rectTransform.anchorMin = distance.rectTransform.anchorMax = distance.rectTransform.pivot = new Vector2(0f, 1f);
            distance.rectTransform.anchoredPosition = new Vector2(76f, -40f);
            distance.rectTransform.sizeDelta = new Vector2(124f, 44f);
            distance.text = "-";
            hud.navigationDistance = distance;

            // Course to steer, quieter than the distance: optional heading guidance.
            var bearing = NewText("Bearing", panel, font, 24, TextAnchor.UpperLeft, LabelColor);
            bearing.rectTransform.anchorMin = bearing.rectTransform.anchorMax = bearing.rectTransform.pivot = new Vector2(0f, 1f);
            bearing.rectTransform.anchoredPosition = new Vector2(204f, -46f);
            bearing.rectTransform.sizeDelta = new Vector2(92f, 36f);
            bearing.text = string.Empty;
            hud.navigationBearing = bearing;
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
                slot.sizeDelta = new Vector2(380f, 72f);
                hud.warningSlots[k] = Hidden(slot);
                hud.warningBackgrounds[k] = AddImage(slot, hud.warningColor);

                var label = NewText("Label", slot, font, 34, TextAnchor.MiddleCenter, Color.white, shadow: false);
                label.fontStyle = FontStyle.Bold;
                Stretch(label.rectTransform);
                hud.warningLabels[k] = label;
            }
        }

        static void BuildRail(RectTransform parent, Font font, ContextualHud hud)
        {
            var corner = new Vector2(1f, 0f);
            hud.systemsButton = BuildButton("SystemsButton", parent, font, 24, corner, RailBase, RailButtonSize, "SYSTEMS");
            for (int i = 0; i < ContextualHud.RailSlotCount; i++)
            {
                var position = RailBase + new Vector2(0f, (i + 1) * RailStep);
                var button = BuildButton("RailSlot" + i, parent, font, 24, corner, position, RailButtonSize, string.Empty);
                Hidden((RectTransform)button.transform);
                hud.railSlots[i] = button;
            }
        }

        static DisclosureInspector BuildInspector(RectTransform parent, Font font)
        {
            var text = NewText("DisclosureInspector", parent, font, 18, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.85f));
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-Margin, -128f);
            rect.sizeDelta = new Vector2(520f, 216f);
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
            var image = AddImage(rect, new Color(0f, 0f, 0f, 0.35f));
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;

            var label = NewText("Label", rect, font, fontSize, TextAnchor.MiddleCenter, Color.white, shadow: false);
            Stretch(label.rectTransform);
            label.text = caption;
            button.label = label;
            return button;
        }

        static Text TopRowText(string name, RectTransform panel, Font font, float x, float width)
        {
            var text = NewText(name, panel, font, 26, TextAnchor.UpperLeft, ValueColor);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, 40f);
            text.text = "-";
            return text;
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
