using OpenSkies.Awareness;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the top-left flight information cluster in code (scene builder and tests), from the
    /// rectangles of <see cref="FlightInfoLayout"/>, inside the safe area. Every control is an
    /// invisible 96-unit touch target (<see cref="RaycastOnlyHudGraphic"/>) around a smaller
    /// visual. Built after the gesture HUD (it draws over the HUD band, so the capsule and the
    /// panel take their own touches) and before the command cluster, the coaching highlights,
    /// the mission card, the pause sheet and the entry screen (they draw over it).
    /// </summary>
    public static class FlightInfoClusterFactory
    {
        /// <summary>The capsule rests over the world: the flight-data plate's contrast (large values keep 3:1 over a white sky).</summary>
        static readonly Color CapsuleSurface = HudStyle.DataBacking;
        /// <summary>The trigger inside the open panel: lighter, not cyan (cyan means a selection).</summary>
        static readonly Color OpenTrigger = new Color(1f, 1f, 1f, 0.16f);
        /// <summary>A focused tile: a cyan tint behind white text (the lower row also shows the focus, so it is never colour alone).</summary>
        static readonly Color FocusedTile = new Color(HudStyle.Selected.r, HudStyle.Selected.g, HudStyle.Selected.b, 0.3f);

        public static FlightInfoCluster Build(RectTransform safeArea, Font font)
        {
            var root = NewRect("FlightInfoCluster", safeArea);
            root.gameObject.SetActive(false); // wire everything before Awake
            Stretch(root);
            var info = root.gameObject.AddComponent<FlightInfoCluster>();

            BuildCapsule(root, font, info);
            BuildPanel(root, font, info);

            root.gameObject.SetActive(true);
            return info;
        }

        // ---- Capsule ---------------------------------------------------------------------------------

        static void BuildCapsule(RectTransform root, Font font, FlightInfoCluster info)
        {
            var hit = FlightInfoLayout.CapsuleHit(0, false);
            var rect = Place(NewRect("Capsule", root), hit);
            rect.gameObject.AddComponent<RaycastOnlyHudGraphic>().raycastTarget = true;
            info.capsuleGroup = rect.gameObject.AddComponent<CanvasGroup>(); // starts shown: the capsule is the resting state

            // The pill: left-aligned in the touch target, its width set by the view from the content.
            var pill = NewRect("Pill", rect);
            pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0f, 0.5f);
            pill.anchoredPosition = Vector2.zero;
            pill.sizeDelta = new Vector2(FlightInfoLayout.CapsuleVisualWidth(0, false), FlightInfoLayout.CapsuleVisualHeight);
            var plate = AddImage(pill, CapsuleSurface);

            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = plate;
            button.idleColor = CapsuleSurface;
            button.cornerRadius = HudStyle.ControlRadius;
            button.feedback = UiFeedback.Tap;
            info.capsuleButton = button;

            var pillRect = FlightInfoLayout.CapsuleVisual(0, false);
            var trigger = FlightInfoLayout.CapsuleTrigger;
            info.capsuleIcon = Icon("Trigger", pill, new Vector2(trigger.center.x - pillRect.xMin, 0f), 36f, HudStyle.TextPrimary);

            for (int i = 0; i < FlightInfoCluster.CellCount; i++)
            {
                var cellRect = FlightInfoLayout.CapsuleCell(i);
                var cell = NewRect("Cell" + i, pill);
                cell.anchorMin = cell.anchorMax = cell.pivot = new Vector2(0f, 1f);
                cell.anchoredPosition = new Vector2(cellRect.xMin - pillRect.xMin, 0f);
                cell.sizeDelta = cellRect.size;
                info.cells[i] = cell;
                info.cellGroups[i] = cell.gameObject.AddComponent<CanvasGroup>();
                info.cellLabels[i] = Label(cell, font, HudStyle.CaptionSize, 8f, -6f, cellRect.width - 12f, 22f, HudStyle.TextSecondary, shadow: true);
                info.cellValues[i] = Value(cell, font, HudStyle.TitleSize, 8f, 4f, cellRect.width - 12f, 40f, shadow: true);
                cell.gameObject.SetActive(false);
            }

            // The guide chip: the navigation arrow, name and distance, or the approach runway and lights.
            var chip = NewRect("Chip", pill);
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0f, 1f);
            chip.sizeDelta = new Vector2(FlightInfoLayout.ChipWidth, FlightInfoLayout.CapsuleVisualHeight);
            info.chip = chip;

            var navigation = NewRect("Navigation", chip);
            Stretch(navigation);
            info.chipNavigation = navigation;
            info.chipArrow = Arrow("Arrow", navigation, new Vector2(30f, 0f), 40f);
            info.chipLabel = Label(navigation, font, HudStyle.CaptionSize, 58f, -6f, FlightInfoLayout.ChipWidth - 64f, 22f, HudStyle.TextSecondary, shadow: true);
            info.chipLabel.text = "AIRFIELD";
            info.chipDistance = Value(navigation, font, HudStyle.BodySize, 58f, 8f, FlightInfoLayout.ChipWidth - 64f, 30f, shadow: true);

            var approach = NewRect("Approach", chip);
            Stretch(approach);
            info.chipApproach = approach;
            info.chipRunway = Label(approach, font, HudStyle.CaptionSize, 12f, -6f, 120f, 22f, HudStyle.TextSecondary, shadow: true);
            info.chipRunway.text = "RWY";
            for (int k = 0; k < ApproachGuidance.PapiLights; k++)
            {
                var light = NewRect("Papi" + k, approach);
                light.anchorMin = light.anchorMax = light.pivot = new Vector2(0f, 0f);
                light.anchoredPosition = new Vector2(12f + k * 22f, 12f);
                light.sizeDelta = new Vector2(16f, 16f);
                var image = AddImage(light, Color.white);
                AddShadow(image);
                info.chipPapi[k] = image;
            }
            info.chipApproachDistance = Value(approach, font, HudStyle.CaptionSize, 106f, 10f, FlightInfoLayout.ChipWidth - 112f, 22f, shadow: true);
            approach.gameObject.SetActive(false);
            chip.gameObject.SetActive(false);
        }

        // ---- Panel -----------------------------------------------------------------------------------

        static void BuildPanel(RectTransform root, Font font, FlightInfoCluster info)
        {
            var panelRect = FlightInfoLayout.Panel;
            var panel = Place(NewRect("Panel", root), panelRect);
            info.panel = panel;
            info.panelGroup = Hidden(panel);
            info.panelBackground = AddImage(panel, HudStyle.PanelSurface);
            info.panelBackground.raycastTarget = true; // a press between tiles is the panel's, never the world's

            // The trigger in the capsule's place: the same symbol at the same point.
            var triggerHit = FlightInfoLayout.TriggerHit;
            var trigger = Control("Trigger", panel, FlightInfoLayout.Relative(triggerHit, panelRect), new Vector2(FlightInfoLayout.CapsuleVisualHeight, FlightInfoLayout.CapsuleVisualHeight),
                OpenTrigger);
            info.triggerButton = trigger;
            info.triggerIcon = Icon("Icon", trigger.background.rectTransform, Vector2.zero, 36f, HudStyle.TextPrimary, centred: true);

            // One tile per reading, in the capsule's order.
            for (int i = 0; i < FlightInfoCluster.TileCount; i++)
            {
                var element = (HudElement)i;
                var hit = FlightInfoLayout.Tile(i);
                var tile = Control(element + "Tile", panel, FlightInfoLayout.Relative(hit, panelRect), new Vector2(hit.width - 4f, FlightInfoLayout.TileVisualHeight),
                    HudStyle.PanelControl);
                tile.engagedColor = FocusedTile;
                tile.engagedLabelColor = HudStyle.TextPrimary;
                var visual = tile.background.rectTransform;
                info.tiles[i] = tile;
                info.tileLabels[i] = Label(visual, font, HudStyle.LabelSize, 8f, -6f, hit.width - 14f, 26f, HudStyle.TextSecondary, shadow: false);
                info.tileLabels[i].text = FlightReadouts.Label(element);
                info.tileValues[i] = Value(visual, font, HudStyle.ValueSize, 8f, 2f, hit.width - 10f, 52f, shadow: false);
            }

            BuildNavigationTile(panel, panelRect, font, info);
            BuildApproachTile(panel, panelRect, font, info);
            BuildFocus(panel, panelRect, font, info);

            var row = FlightInfoLayout.Relative(FlightInfoLayout.LowerRow, panelRect);
            var hint = NewText("Hint", panel, font, HudStyle.LabelSize, TextAnchor.MiddleLeft, HudStyle.TextSecondary, shadow: false);
            Place(hint.rectTransform, new Rect(row.xMin + 16f, row.yMin, row.width - 32f, row.height));
            hint.text = FlightInfoModel.HintLabel;
            info.hint = hint;
        }

        static void BuildNavigationTile(RectTransform panel, Rect panelRect, Font font, FlightInfoCluster info)
        {
            var region = FlightInfoLayout.RegionA;
            var tile = Control("NavigationTile", panel, FlightInfoLayout.Relative(region, panelRect), new Vector2(region.width - 4f, FlightInfoLayout.TileVisualHeight),
                HudStyle.PanelControl);
            tile.engagedColor = FocusedTile;
            tile.engagedLabelColor = HudStyle.TextPrimary;
            info.navigationTile = tile;
            var visual = tile.background.rectTransform;

            info.navigationArrow = Arrow("Arrow", visual, new Vector2(40f, 0f), 52f);
            info.navigationLabel = Label(visual, font, HudStyle.LabelSize, 80f, -8f, region.width - 92f, 26f, HudStyle.TextSecondary, shadow: false);
            info.navigationLabel.text = "AIRFIELD";
            info.navigationDistance = Value(visual, font, HudStyle.TitleSize, 80f, 6f, 150f, 42f, shadow: false);
            // Course to steer, quieter than the distance: optional heading guidance.
            info.navigationBearing = Value(visual, font, HudStyle.BodySize, 236f, 10f, region.width - 248f, 32f, shadow: false);
            info.navigationBearing.color = HudStyle.TextSecondary;
            tile.gameObject.SetActive(false);
        }

        static void BuildApproachTile(RectTransform panel, Rect panelRect, Font font, FlightInfoCluster info)
        {
            var region = FlightInfoLayout.RegionB;
            var tile = Control("ApproachTile", panel, FlightInfoLayout.Relative(region, panelRect), new Vector2(region.width - 4f, FlightInfoLayout.TileVisualHeight),
                HudStyle.PanelControl);
            tile.engagedColor = FocusedTile;
            tile.engagedLabelColor = HudStyle.TextPrimary;
            info.approachTile = tile;
            var visual = tile.background.rectTransform;

            info.approachRunway = Label(visual, font, HudStyle.BodySize, 12f, -8f, 110f, 32f, HudStyle.TextPrimary, shadow: false);
            info.approachDistance = Label(visual, font, HudStyle.BodySize, 128f, -8f, 140f, 32f, HudStyle.TextPrimary, shadow: false);
            info.approachHeight = Label(visual, font, HudStyle.BodySize, 278f, -8f, 140f, 32f, HudStyle.TextPrimary, shadow: false);

            // PAPI-style lights, bottom left: more white = higher.
            for (int k = 0; k < ApproachGuidance.PapiLights; k++)
            {
                var light = NewRect("Papi" + k, visual);
                light.anchorMin = light.anchorMax = light.pivot = new Vector2(0f, 0f);
                light.anchoredPosition = new Vector2(12f + k * 30f, 10f);
                light.sizeDelta = new Vector2(22f, 22f);
                info.papiLights[k] = AddImage(light, Color.white);
            }

            // Centreline cue: a track with a centre tick and a marker that shows where the centreline is.
            var track = NewRect("LateralTrack", visual);
            track.anchorMin = track.anchorMax = track.pivot = new Vector2(0f, 0f);
            track.anchoredPosition = new Vector2(170f, 18f);
            track.sizeDelta = new Vector2(180f, 6f);
            AddImage(track, new Color(1f, 1f, 1f, 0.5f));

            var tick = NewRect("CentreTick", track);
            tick.anchorMin = tick.anchorMax = tick.pivot = new Vector2(0.5f, 0.5f);
            tick.sizeDelta = new Vector2(3f, 18f);
            AddImage(tick, new Color(1f, 1f, 1f, 0.6f));

            var marker = NewRect("CentrelineMarker", track);
            marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(12f, 26f);
            AddImage(marker, HudStyle.Navigation);
            info.lateralMarker = marker;
            info.lateralHalfTravel = 80f;
            tile.gameObject.SetActive(false);
        }

        static void BuildFocus(RectTransform panel, Rect panelRect, Font font, FlightInfoCluster info)
        {
            // A focused reading, enlarged in region A.
            var regionA = FlightInfoLayout.RegionA;
            var focus = Control("FocusedReading", panel, FlightInfoLayout.Relative(regionA, panelRect), new Vector2(regionA.width - 4f, FlightInfoLayout.TileVisualHeight),
                FocusedTile);
            info.focusButton = focus;
            var visual = focus.background.rectTransform;
            info.focusTitle = Label(visual, font, HudStyle.CaptionSize, 14f, -6f, regionA.width - 24f, 22f, HudStyle.TextSecondary, shadow: false);
            info.focusValue = Value(visual, font, HudStyle.DisplaySize, 14f, -2f, regionA.width - 24f, 66f, shadow: false);
            focus.gameObject.SetActive(false);

            // The explanation: placed by the view in whichever region the focused element's own tile does not use.
            var regionB = FlightInfoLayout.RegionB;
            var explanation = Control("Explanation", panel, FlightInfoLayout.Relative(regionB, panelRect), new Vector2(regionB.width - 4f, FlightInfoLayout.TileVisualHeight),
                HudStyle.PanelControl, stretchVisual: true);
            info.explanationButton = explanation;
            var text = NewText("Text", explanation.background.rectTransform, font, HudStyle.LabelSize, TextAnchor.UpperLeft, HudStyle.TextPrimary, shadow: false);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(14f, 8f);
            text.rectTransform.offsetMax = new Vector2(-12f, -8f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            // Never clipped: a long explanation in the narrower region shrinks a little instead.
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = HudStyle.CaptionSize - 2;
            text.resizeTextMaxSize = HudStyle.LabelSize;
            text.lineSpacing = 0.95f;
            info.explanationText = text;
            explanation.gameObject.SetActive(false);
        }

        // ---- Building blocks -------------------------------------------------------------------------

        /// <summary>
        /// An invisible touch target of the given rectangle (relative to its parent's top-left corner) with a visual of its
        /// own size, left-aligned and centred vertically; or, stretched, the target's width less the gap between regions
        /// (it follows the target when the view moves it).
        /// </summary>
        static HudButton Control(string name, RectTransform parent, Rect hit, Vector2 visualSize, Color idle, bool stretchVisual = false)
        {
            var rect = Place(NewRect(name, parent), hit);
            rect.gameObject.AddComponent<RaycastOnlyHudGraphic>().raycastTarget = true;

            var visual = NewRect("Visual", rect);
            visual.anchorMin = new Vector2(0f, 0.5f);
            visual.anchorMax = new Vector2(stretchVisual ? 1f : 0f, 0.5f);
            visual.pivot = new Vector2(0f, 0.5f);
            visual.anchoredPosition = Vector2.zero;
            visual.sizeDelta = stretchVisual ? new Vector2(-FlightInfoLayout.TileGap, visualSize.y) : visualSize;
            var image = AddImage(visual, idle);

            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = idle;
            button.cornerRadius = HudStyle.ControlRadius;
            button.feedback = UiFeedback.Tap;
            return button;
        }

        /// <summary>A small caption or label, anchored at its parent's top-left corner.</summary>
        static Text Label(RectTransform parent, Font font, int size, float x, float y, float width, float height, Color color, bool shadow)
        {
            var text = NewText("Label", parent, font, size, TextAnchor.UpperLeft, color, shadow);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            text.text = string.Empty;
            return text;
        }

        /// <summary>A value, anchored at its parent's bottom-left corner.</summary>
        static Text Value(RectTransform parent, Font font, int size, float x, float bottom, float width, float height, bool shadow)
        {
            var text = NewText("Value", parent, font, size, TextAnchor.LowerLeft, HudStyle.TextPrimary, shadow);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, bottom);
            rect.sizeDelta = new Vector2(width, height);
            text.text = "-";
            return text;
        }

        static Image Icon(string name, RectTransform parent, Vector2 position, float size, Color color, bool centred = false)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = centred ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(size, size);
            var image = AddImage(rect, color);
            image.preserveAspect = true;
            return image;
        }

        /// <summary>A magenta arrow (course to the destination) centred at a point on its parent's left-centre line.</summary>
        static RectTransform Arrow(string name, RectTransform parent, Vector2 position, float size)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(size, size);
            AddShadow(AddImage(rect, HudStyle.Navigation));
            return rect;
        }

        /// <summary>Places a rect from a top-left rectangle: anchors and pivot at the parent's top-left corner.</summary>
        static RectTransform Place(RectTransform rect, Rect topLeft)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = FlightInfoLayout.AnchoredPosition(topLeft);
            rect.sizeDelta = topLeft.size;
            return rect;
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

        /// <summary>Sprites are generated at runtime; the factory leaves them empty (HudButton and FlightInfoCluster assign them in Awake).</summary>
        static Image AddImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor, Color color, bool shadow)
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

        /// <summary>A soft drop shadow keeps white text legible against bright sky.</summary>
        static void AddShadow(Graphic graphic)
        {
            var effect = graphic.gameObject.AddComponent<Shadow>();
            effect.effectColor = HudStyle.TextShadow;
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
