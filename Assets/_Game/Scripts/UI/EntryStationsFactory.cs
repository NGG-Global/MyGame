using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the entry stations screen in code: a full-screen hotspot (drag, tap)
    /// that draws nothing, a small title top left, the station's name, description
    /// and progress on a plate at the bottom left, three station chips and START at
    /// the bottom right, SETTINGS top right, the Free Flight starts beside the
    /// chips. The world stays visible: there is no scrim.
    /// </summary>
    public static class EntryStationsFactory
    {
        public static readonly Vector2 ChipSize = new Vector2(300f, HudStyle.MinTouchTarget);

        public static EntryStationsScreen Build(RectTransform safeArea, Font font, InteractionSurface surface)
        {
            var root = NewRect("EntryStations", safeArea);
            root.gameObject.SetActive(false);
            Stretch(root);
            var screen = root.gameObject.AddComponent<EntryStationsScreen>();
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f; // open at start; the session decides
            group.blocksRaycasts = true;
            group.interactable = true;
            screen.group = group;

            // The scene hotspot: the whole screen, under every button.
            var scene = NewRect("Scene", root);
            Stretch(scene);
            var graphic = scene.gameObject.AddComponent<RaycastOnlyHudGraphic>();
            graphic.raycastTarget = true;
            var hotspot = scene.gameObject.AddComponent<GestureHotspot>();
            hotspot.surface = surface;
            hotspot.route = InteractionRoute.Entry;
            screen.sceneHotspot = hotspot;

            screen.title = NewText("Title", root, font, HudStyle.TitleSize, TextAnchor.UpperLeft, HudStyle.TextPrimary);
            screen.title.fontStyle = FontStyle.Bold;
            TopLeft(screen.title.rectTransform, 48f, -32f, 600f, 44f);

            // The station's text on a plate at the bottom left, clear of the chips on the right.
            var plate = NewRect("TextPlate", root);
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(0f, 0f);
            plate.anchoredPosition = new Vector2(48f, 48f);
            plate.sizeDelta = new Vector2(760f, 236f);
            screen.textPlate = AddImage(plate, HudStyle.DataBacking);
            screen.stationName = NewText("StationName", plate, font, HudStyle.DisplaySize - 12, TextAnchor.UpperLeft, HudStyle.TextPrimary);
            screen.stationName.fontStyle = FontStyle.Bold;
            TopLeft(screen.stationName.rectTransform, 28f, -20f, 720f, 64f);
            screen.description = NewText("Description", plate, font, HudStyle.BodySize + 2, TextAnchor.UpperLeft, HudStyle.TextSecondary);
            TopLeft(screen.description.rectTransform, 28f, -96f, 704f, 72f);
            screen.description.horizontalOverflow = HorizontalWrapMode.Wrap;
            screen.progress = NewText("Progress", plate, font, HudStyle.LabelSize, TextAnchor.LowerLeft, HudStyle.Selected);
            TopLeft(screen.progress.rectTransform, 28f, -180f, 704f, 36f);

            // Chips and START at the bottom right; the Free Flight starts above the chips.
            float right = -48f;
            for (int i = 0; i < EntryStationsScreen.StationCount; i++)
            {
                var chip = Button("Chip" + i, root, font, HudStyle.LabelSize, ChipSize);
                var rect = (RectTransform)chip.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(right - (EntryStationsScreen.StationCount - 1 - i) * (ChipSize.x + 12f), 48f);
                screen.chips[i] = chip;
            }
            screen.startButton = Button("Start", root, font, HudStyle.ButtonSize, new Vector2(ChipSize.x * 3f + 24f, HudStyle.MinTouchTarget));
            var start = (RectTransform)screen.startButton.transform;
            start.anchorMin = start.anchorMax = start.pivot = new Vector2(1f, 0f);
            start.anchoredPosition = new Vector2(right, 48f + HudStyle.MinTouchTarget + 12f);
            screen.startButton.SetLabel("START  ·  or tap the aircraft");
            screen.startButton.engagedColor = HudStyle.Selected;
            screen.startButton.feedback = UiFeedback.Confirm;

            var options = NewRect("FreeFlightOptions", root);
            options.anchorMin = options.anchorMax = options.pivot = new Vector2(1f, 0f);
            options.anchoredPosition = new Vector2(right, 48f + 2f * (HudStyle.MinTouchTarget + 12f));
            options.sizeDelta = new Vector2(ChipSize.x * 2f + 12f, HudStyle.MinTouchTarget);
            screen.freeFlightOptions = Hidden(options);
            screen.fromRunwayButton = Button("FromRunway", options, font, HudStyle.LabelSize, ChipSize);
            PlaceRight((RectTransform)screen.fromRunwayButton.transform, -(ChipSize.x + 12f));
            screen.fromRunwayButton.SetLabel("FROM THE RUNWAY");
            screen.inTheAirButton = Button("InTheAir", options, font, HudStyle.LabelSize, ChipSize);
            PlaceRight((RectTransform)screen.inTheAirButton.transform, 0f);
            screen.inTheAirButton.SetLabel("IN THE AIR");

            screen.settingsButton = Button("Settings", root, font, HudStyle.ButtonSize, new Vector2(200f, HudStyle.MinTouchTarget));
            var settings = (RectTransform)screen.settingsButton.transform;
            settings.anchorMin = settings.anchorMax = settings.pivot = new Vector2(1f, 1f);
            settings.anchoredPosition = new Vector2(right, -24f);
            screen.settingsButton.SetLabel("SETTINGS");

            screen.swipeHint = NewText("SwipeHint", root, font, HudStyle.CaptionSize, TextAnchor.LowerCenter, HudStyle.TextQuiet);
            var hint = screen.swipeHint.rectTransform;
            hint.anchorMin = hint.anchorMax = new Vector2(0.5f, 0f);
            hint.pivot = new Vector2(0.5f, 0f);
            hint.anchoredPosition = new Vector2(0f, 300f);
            hint.sizeDelta = new Vector2(600f, 30f);
            screen.swipeHint.text = "Swipe to explore";

            root.gameObject.SetActive(true);
            return screen;
        }

        static void PlaceRight(RectTransform rect, float x)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(x, 0f);
        }

        static HudButton Button(string name, RectTransform parent, Font font, int size, Vector2 rectSize)
        {
            var rect = NewRect(name, parent);
            rect.sizeDelta = rectSize;
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = new Color(0f, 0f, 0f, 0.45f);
            var label = NewText("Label", rect, font, size, TextAnchor.MiddleCenter, HudStyle.TextPrimary);
            Stretch(label.rectTransform);
            button.label = label;
            return button;
        }

        static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

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

        static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor, Color color)
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
            HudStyle.Shadow(text);
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
