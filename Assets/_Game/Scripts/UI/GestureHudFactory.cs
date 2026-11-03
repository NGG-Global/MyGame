using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the gesture-first HUD in code (scene builder and tests), in canvas
    /// units for the 1920 × 1080 reference inside the safe area:
    /// - The systems handle where SYSTEMS used to be (bottom right, left of the
    ///   throttle, above BRAKE): a 168 × 96 touch area with a quiet grip.
    /// - The HUD band over the flight data strip (top left).
    /// - The arc centred on the handle; the flap lever, the density ladder and the
    ///   explanation plate, hidden until a gesture calls for them.
    /// - The development inspector, top right under the disclosure inspector.
    /// Everything is built inactive and wired before Awake runs.
    /// </summary>
    public static class GestureHudFactory
    {
        public static readonly Vector2 HandleSize = ContextualHudFactory.RailButtonSize;
        public static readonly Vector2 HandlePosition = ContextualHudFactory.RailBase;
        /// <summary>The HUD band covers the five flight data slots and a little below them.</summary>
        public static readonly Rect BandArea = new Rect(ContextualHudFactory.Margin, -ContextualHudFactory.TopMargin - 104f,
            ContextualHud.DataItemCount * ContextualHudFactory.DataItemSpacing - (ContextualHudFactory.DataItemSpacing - ContextualHudFactory.DataItemSize.x), 104f);
        public static readonly Vector2 InspectorPosition = new Vector2(-ContextualHudFactory.Margin, -352f);
        public static readonly Vector2 InspectorSize = new Vector2(520f, 128f);

        public static GestureHud Build(RectTransform safeArea, Font font, InteractionSurface surface)
        {
            var rootGo = new GameObject("GestureHud", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(safeArea, false);
            rootGo.SetActive(false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = Vector2.zero; // local units from the bottom-left corner of the safe area
            root.offsetMin = root.offsetMax = Vector2.zero;
            var hud = rootGo.AddComponent<GestureHud>();
            hud.root = root;

            hud.arc = BuildArc(root, font);
            hud.lever = BuildLever(root, font);
            hud.ladder = BuildLadder(root, font);
            BuildHandle(root, font, surface, hud);
            BuildBand(root, surface, hud);
            hud.plate = BuildPlate(root, font);
            hud.hint = BuildHint(root, font);
            hud.inspector = BuildInspector(root, font);

            rootGo.SetActive(true);
            return hud;
        }

        // ---- Handle and band ----------------------------------------------------------------------

        static void BuildHandle(RectTransform root, Font font, InteractionSurface surface, GestureHud hud)
        {
            var rect = NewRect("SystemsHandle", root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = HandlePosition;
            rect.sizeDelta = HandleSize;
            hud.handleGroup = rect.gameObject.AddComponent<CanvasGroup>();
            var hit = rect.gameObject.AddComponent<RaycastOnlyHudGraphic>(); // the touch area draws nothing
            hit.raycastTarget = true;
            var hotspot = rect.gameObject.AddComponent<GestureHotspot>();
            hotspot.surface = surface;
            hotspot.route = InteractionRoute.SystemsHandle;
            hud.handleHotspot = hotspot;

            // The grip: a slim pill with the caption and a small chevron that says "pull".
            var grip = NewRect("Grip", rect);
            grip.anchorMin = grip.anchorMax = grip.pivot = new Vector2(0.5f, 0.5f);
            grip.sizeDelta = new Vector2(HandleSize.x, 60f);
            hud.handleGrip = AddImage(grip, HudStyle.ControlSurface);

            var chevron = NewRect("Chevron", grip);
            chevron.anchorMin = chevron.anchorMax = new Vector2(0.5f, 1f);
            chevron.pivot = new Vector2(0.5f, 1f);
            chevron.anchoredPosition = new Vector2(0f, -4f);
            chevron.sizeDelta = new Vector2(18f, 14f);
            hud.handleChevron = AddImage(chevron, new Color(1f, 1f, 1f, 0.5f));

            var label = NewText("Label", grip, font, HudStyle.LabelSize, TextAnchor.LowerCenter, HudStyle.TextSecondary);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(0f, 8f);
            label.text = "SYSTEMS";
            hud.handleLabel = label;
        }

        static void BuildBand(RectTransform root, InteractionSurface surface, GestureHud hud)
        {
            var rect = NewRect("HudBand", root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(BandArea.x, BandArea.yMax);
            rect.sizeDelta = BandArea.size;
            hud.bandGroup = rect.gameObject.AddComponent<CanvasGroup>();
            var hit = rect.gameObject.AddComponent<RaycastOnlyHudGraphic>();
            hit.raycastTarget = true;
            var hotspot = rect.gameObject.AddComponent<GestureHotspot>();
            hotspot.surface = surface;
            hotspot.route = InteractionRoute.HudBand;
            hud.bandHotspot = hotspot;
        }

        // ---- Arc ----------------------------------------------------------------------------------

        static SystemsArcView BuildArc(RectTransform root, Font font)
        {
            var rect = NewRect("SystemsArc", root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = HandlePosition + new Vector2(-HandleSize.x * 0.5f, HandleSize.y * 0.5f); // the handle centre
            rect.sizeDelta = Vector2.zero;
            var view = rect.gameObject.AddComponent<SystemsArcView>();
            view.root = rect;
            view.group = rect.gameObject.AddComponent<CanvasGroup>();
            view.group.alpha = 0f;
            view.group.blocksRaycasts = false;
            view.group.interactable = false;

            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                view.innerSegments[i] = Segment("Inner" + i, rect);
                view.innerLabels[i] = SegmentLabel("InnerLabel" + i, rect, font);
                var mark = NewRect("Mark" + i, rect);
                mark.sizeDelta = new Vector2(10f, 10f);
                view.innerMarks[i] = AddImage(mark, HudStyle.Selected);
                view.innerMarks[i].enabled = false;
            }
            for (int i = 0; i < SystemsArcModel.MaxItems; i++)
            {
                view.outerSegments[i] = Segment("Outer" + i, rect);
                view.outerLabels[i] = SegmentLabel("OuterLabel" + i, rect, font);
            }
            var caption = NewText("Caption", rect, font, HudStyle.CaptionSize, TextAnchor.MiddleRight, HudStyle.TextQuiet);
            caption.rectTransform.anchoredPosition = new Vector2(-60f, -72f);
            caption.rectTransform.sizeDelta = new Vector2(420f, 28f);
            caption.rectTransform.pivot = new Vector2(1f, 0.5f);
            view.caption = caption;
            return view;
        }

        static Image Segment(string name, RectTransform parent)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            var image = AddImage(rect, HudStyle.ControlSurface);
            image.enabled = false;
            return image;
        }

        static Text SegmentLabel(string name, RectTransform parent, Font font)
        {
            var text = NewText(name, parent, font, HudStyle.LabelSize, TextAnchor.MiddleCenter, HudStyle.TextPrimary);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(120f, 48f);
            text.fontStyle = FontStyle.Bold;
            text.enabled = false;
            return text;
        }

        // ---- Lever --------------------------------------------------------------------------------

        static FlapsLeverView BuildLever(RectTransform root, Font font)
        {
            var rect = NewRect("FlapsLever", root);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            var view = rect.gameObject.AddComponent<FlapsLeverView>();
            view.root = rect;
            view.group = Hidden(rect);

            var plate = NewRect("Plate", rect);
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(0.5f, 0.5f);
            view.plate = AddImage(plate, HudStyle.PanelSurface);

            var title = NewText("Title", rect, font, HudStyle.LabelSize, TextAnchor.MiddleCenter, HudStyle.TextSecondary);
            title.rectTransform.anchoredPosition = new Vector2(0f, 0f); // placed by the view relative to the plate
            title.rectTransform.sizeDelta = new Vector2(200f, 28f);
            title.fontStyle = FontStyle.Bold;
            view.title = title;

            var track = NewRect("Track", rect);
            track.anchorMin = track.anchorMax = track.pivot = new Vector2(0.5f, 0.5f);
            view.track = AddImage(track, HudStyle.Hairline);

            for (int i = 0; i < FlapsLeverView.MaxDetents; i++)
            {
                var mark = NewRect("Mark" + i, rect);
                mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(0.5f, 0.5f);
                view.marks[i] = AddImage(mark, HudStyle.TextSecondary);
                var label = NewText("Label" + i, rect, font, HudStyle.BodySize, TextAnchor.MiddleRight, HudStyle.TextSecondary);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.zero;
                label.rectTransform.pivot = new Vector2(1f, 0.5f);
                label.rectTransform.sizeDelta = new Vector2(90f, 36f);
                view.labels[i] = label;
            }

            var knob = NewRect("Knob", rect);
            knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0.5f, 0.5f);
            knob.sizeDelta = new Vector2(112f, 48f);
            view.knob = AddImage(knob, HudStyle.Selected);
            var knobLabel = NewText("KnobLabel", knob, font, HudStyle.ButtonSize, TextAnchor.MiddleCenter, HudStyle.TextOnAccent, shadow: false);
            Stretch(knobLabel.rectTransform);
            knobLabel.fontStyle = FontStyle.Bold;
            view.knobLabel = knobLabel;

            var note = NewText("Note", rect, font, HudStyle.CaptionSize, TextAnchor.MiddleCenter, HudStyle.TextSecondary);
            note.rectTransform.sizeDelta = new Vector2(280f, 26f);
            view.note = note;
            return view;
        }

        // ---- Ladder -------------------------------------------------------------------------------

        static HudDensityLadder BuildLadder(RectTransform root, Font font)
        {
            var rect = NewRect("HudDensityLadder", root);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            var view = rect.gameObject.AddComponent<HudDensityLadder>();
            view.root = rect;
            view.group = Hidden(rect);

            var plate = NewRect("Plate", rect);
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(0.5f, 0.5f);
            view.plate = AddImage(plate, HudStyle.PanelSurface);
            plate.anchoredPosition = new Vector2(70f, 0f); // the labels sit right of the rail

            var rail = NewRect("Rail", rect);
            rail.anchorMin = rail.anchorMax = rail.pivot = new Vector2(0.5f, 0.5f);
            view.rail = AddImage(rail, HudStyle.Hairline);

            for (int i = 0; i < HudDensityModel.LevelCount; i++)
            {
                var stop = NewRect("Stop" + i, rect);
                stop.anchorMin = stop.anchorMax = stop.pivot = new Vector2(0.5f, 0.5f);
                stop.sizeDelta = new Vector2(14f, 14f);
                view.stops[i] = AddImage(stop, HudStyle.TextSecondary);
                var label = NewText("Label" + i, rect, font, HudStyle.LabelSize, TextAnchor.MiddleLeft, HudStyle.TextSecondary);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.zero;
                label.rectTransform.pivot = new Vector2(0f, 0.5f);
                label.rectTransform.sizeDelta = new Vector2(200f, 32f);
                label.fontStyle = FontStyle.Bold;
                view.labels[i] = label;
            }
            var ring = NewRect("CommittedRing", rect);
            ring.anchorMin = ring.anchorMax = ring.pivot = new Vector2(0.5f, 0.5f);
            ring.sizeDelta = new Vector2(30f, 30f);
            view.committedRing = AddImage(ring, HudStyle.TextPrimary);
            var marker = NewRect("Marker", rect);
            marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(22f, 22f);
            view.marker = AddImage(marker, HudStyle.Selected);
            var note = NewText("Note", rect, font, HudStyle.CaptionSize, TextAnchor.MiddleLeft, HudStyle.TextSecondary);
            note.rectTransform.anchorMin = note.rectTransform.anchorMax = Vector2.zero;
            note.rectTransform.pivot = new Vector2(0f, 0.5f);
            note.rectTransform.sizeDelta = new Vector2(240f, 26f);
            view.note = note;
            return view;
        }

        // ---- Plate --------------------------------------------------------------------------------

        static ExplanationPlate BuildPlate(RectTransform root, Font font)
        {
            var holder = NewRect("Explanation", root);
            Stretch(holder);
            var view = holder.gameObject.AddComponent<ExplanationPlate>();

            var ring = NewRect("HoldRing", holder);
            ring.anchorMin = ring.anchorMax = Vector2.zero;
            ring.pivot = new Vector2(0.5f, 0.5f);
            ring.sizeDelta = new Vector2(64f, 64f);
            view.holdRing = AddImage(ring, HudStyle.TextSecondary);

            var rect = NewRect("Plate", holder);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(440f, 96f);
            view.root = rect;
            view.group = Hidden(rect);
            view.plate = AddImage(rect, HudStyle.PanelSurface);

            var title = NewText("Title", rect, font, HudStyle.LabelSize, TextAnchor.UpperLeft, HudStyle.TextPrimary, shadow: false);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(14f, -12f);
            title.rectTransform.sizeDelta = new Vector2(-28f, 26f);
            title.fontStyle = FontStyle.Bold;
            view.title = title;

            var body = NewText("Body", rect, font, HudStyle.BodySize - 2, TextAnchor.UpperLeft, HudStyle.TextSecondary, shadow: false);
            body.rectTransform.anchorMin = new Vector2(0f, 1f);
            body.rectTransform.anchorMax = new Vector2(1f, 1f);
            body.rectTransform.pivot = new Vector2(0f, 1f);
            body.rectTransform.anchoredPosition = new Vector2(14f, -40f);
            body.rectTransform.sizeDelta = new Vector2(-28f, 60f);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            view.body = body;
            return view;
        }

        // ---- Hint ---------------------------------------------------------------------------------

        static GestureHintView BuildHint(RectTransform root, Font font)
        {
            var rect = NewRect("GestureHint", root);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            var view = rect.gameObject.AddComponent<GestureHintView>();
            view.root = rect;
            view.group = Hidden(rect);

            var trail = NewRect("Trail", rect);
            trail.anchorMin = trail.anchorMax = trail.pivot = new Vector2(0.5f, 0.5f);
            view.trail = AddImage(trail, new Color(1f, 1f, 1f, 0.18f));

            var dot = NewRect("Dot", rect);
            dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(26f, 26f);
            view.dot = AddImage(dot, HudStyle.Selected);

            var plate = NewRect("Caption", rect);
            plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 0.5f);
            view.captionPlate = AddImage(plate, HudStyle.DataBacking);
            var caption = NewText("Text", plate, font, HudStyle.LabelSize, TextAnchor.MiddleCenter, HudStyle.TextPrimary, shadow: false);
            Stretch(caption.rectTransform);
            caption.rectTransform.offsetMin = new Vector2(12f, 0f);
            caption.rectTransform.offsetMax = new Vector2(-12f, 0f);
            view.caption = caption;
            return view;
        }

        // ---- Inspector ----------------------------------------------------------------------------

        static GestureInspector BuildInspector(RectTransform root, Font font)
        {
            var text = NewText("GestureInspector", root, font, 18, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.85f));
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = InspectorPosition;
            rect.sizeDelta = InspectorSize;
            text.raycastTarget = true; // tap to switch interface mode (development only)
            var inspector = text.gameObject.AddComponent<GestureInspector>();
            inspector.text = text;
            return inspector;
        }

        // ---- Helpers ------------------------------------------------------------------------------

        static CanvasGroup Hidden(RectTransform rect)
        {
            var group = rect.gameObject.AddComponent<CanvasGroup>();
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
            if (shadow) HudStyle.Shadow(text);
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

    /// <summary>A raycast target that draws nothing (the UI layer's own; layers do not share code).</summary>
    public sealed class RaycastOnlyHudGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
