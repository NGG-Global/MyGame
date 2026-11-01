using OpenSkies.Missions;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the mission UI in code (scene builder and tests), in canvas units
    /// for the 1920 × 1080 reference, inside the safe area:
    /// - Coaching message: bottom of the free column (between the joystick region
    ///   and the action rail), 12-108 units up, below the panel band and the
    ///   cockpit instrument panel.
    /// - Mission card (pause menu, results): the free column's panel band, built
    ///   after the HUD so a modal card's dimming takes every touch.
    /// - MENU: top right under CAM; SKIP TUTORIAL under MENU, only in the First
    ///   Flight. Both above the throttle, clear of the rail.
    /// - Checkpoint marker and control highlights take no touches.
    /// - Entry screen: full screen, on top of everything.
    /// </summary>
    public static class MissionUiFactory
    {
        public const float CoachBottom = 12f;
        public const float CoachHeight = 96f;
        public static readonly Vector2 SessionButtonSize = new Vector2(128f, 96f);
        public static readonly Vector2 MenuPosition = new Vector2(-24f, -128f);
        public static readonly Vector2 SkipPosition = new Vector2(-24f, -240f);

        static readonly Color CardColour = new Color(0.03f, 0.03f, 0.04f, 0.78f);
        static readonly Color ButtonColour = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color TitleColour = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color TextColour = new Color(1f, 1f, 1f, 0.82f);
        static readonly Color QuietColour = new Color(1f, 1f, 1f, 0.55f);

        // ---- Coaching message ---------------------------------------------------------------------

        public static CoachingPanel BuildCoachingPanel(RectTransform safeArea, Font font, float joystickRegionRight, float railLeftInset)
        {
            var rect = NewRect("CoachingPanel", safeArea);
            rect.gameObject.SetActive(false);
            rect.anchorMin = new Vector2(joystickRegionRight, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(16f, CoachBottom);
            rect.offsetMax = new Vector2(-(railLeftInset + 16f), CoachBottom + CoachHeight);
            var panel = rect.gameObject.AddComponent<CoachingPanel>();
            panel.group = Hidden(rect);

            // The whole message is the tap target for the explanation (96 units tall).
            var background = rect.gameObject.AddComponent<Image>();
            background.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = background;
            button.idleColor = new Color(0f, 0f, 0f, 0.5f);
            button.pressedColor = new Color(1f, 1f, 1f, 0.25f);
            panel.button = button;

            var accent = NewRect("Accent", rect);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.offsetMin = new Vector2(8f, 12f);
            accent.offsetMax = new Vector2(14f, -12f);
            panel.accent = AddImage(accent, Color.white);

            panel.label = NewText("Label", rect, font, 18, TextAnchor.UpperLeft, QuietColour);
            var labelRect = panel.label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.offsetMin = new Vector2(26f, -30f);
            labelRect.offsetMax = new Vector2(-56f, -6f);

            panel.message = NewText("Message", rect, font, 26, TextAnchor.MiddleLeft, TitleColour, bestFit: true);
            var messageRect = panel.message.rectTransform;
            messageRect.anchorMin = Vector2.zero;
            messageRect.anchorMax = Vector2.one;
            messageRect.offsetMin = new Vector2(26f, 6f);
            messageRect.offsetMax = new Vector2(-56f, -28f);

            panel.detailGlyph = NewText("Detail", rect, font, 30, TextAnchor.MiddleCenter, QuietColour);
            var glyph = panel.detailGlyph.rectTransform;
            glyph.anchorMin = new Vector2(1f, 0f);
            glyph.anchorMax = new Vector2(1f, 1f);
            glyph.pivot = new Vector2(1f, 0.5f);
            glyph.offsetMin = new Vector2(-52f, 0f);
            glyph.offsetMax = new Vector2(-8f, 0f);
            panel.detailGlyph.text = string.Empty;

            rect.gameObject.SetActive(true);
            return panel;
        }

        // ---- Control highlights and checkpoint marker -------------------------------------------

        public static ControlHighlights BuildHighlights(RectTransform safeArea)
        {
            var root = NewRect("ControlHighlights", safeArea);
            root.gameObject.SetActive(false);
            Stretch(root);
            var highlights = root.gameObject.AddComponent<ControlHighlights>();
            highlights.area = safeArea;
            for (int i = 0; i < ControlHighlights.TargetCount; i++)
            {
                // Frames are direct children of the safe area, so their positions are safe-area coordinates.
                var frame = NewRect("Frame" + i, safeArea);
                var image = AddImage(frame, Color.clear);
                image.enabled = false;
                highlights.frames[i] = image;
            }
            root.gameObject.SetActive(true);
            return highlights;
        }

        public static CheckpointMarker BuildCheckpointMarker(RectTransform safeArea, Font font, Camera viewCamera)
        {
            var root = NewRect("CheckpointMarker", safeArea);
            root.gameObject.SetActive(false);
            Stretch(root);
            var marker = root.gameObject.AddComponent<CheckpointMarker>();
            marker.viewCamera = viewCamera;
            marker.area = safeArea;
            marker.group = root.gameObject.AddComponent<CanvasGroup>();
            marker.group.alpha = 0f;
            marker.group.blocksRaycasts = false;
            marker.group.interactable = false;

            var symbol = NewRect("Symbol", root);
            symbol.anchorMin = symbol.anchorMax = symbol.pivot = new Vector2(0.5f, 0.5f);
            symbol.sizeDelta = new Vector2(34f, 34f);
            AddShadow(AddImage(symbol, new Color(1f, 1f, 1f, 0.95f)));
            marker.marker = symbol;

            marker.nameText = NewText("Name", symbol, font, 20, TextAnchor.LowerCenter, TextColour);
            var name = marker.nameText.rectTransform;
            name.anchorMin = name.anchorMax = new Vector2(0.5f, 1f);
            name.pivot = new Vector2(0.5f, 0f);
            name.anchoredPosition = new Vector2(0f, 4f);
            name.sizeDelta = new Vector2(220f, 26f);

            marker.distanceText = NewText("Distance", symbol, font, 20, TextAnchor.UpperCenter, QuietColour);
            var distance = marker.distanceText.rectTransform;
            distance.anchorMin = distance.anchorMax = new Vector2(0.5f, 0f);
            distance.pivot = new Vector2(0.5f, 1f);
            distance.anchoredPosition = new Vector2(0f, -4f);
            distance.sizeDelta = new Vector2(160f, 26f);

            root.gameObject.SetActive(true);
            return marker;
        }

        // ---- Mission card -----------------------------------------------------------------------

        public static MissionCard BuildMissionCard(RectTransform safeArea, Font font, float joystickRegionRight, float railLeftInset,
            float columnBottom, float columnTop)
        {
            var root = NewRect("MissionCard", safeArea);
            root.gameObject.SetActive(false);
            Stretch(root);
            var card = root.gameObject.AddComponent<MissionCard>();
            card.group = Hidden(root);

            var scrim = NewRect("Scrim", root);
            Stretch(scrim);
            card.scrim = AddImage(scrim, new Color(0f, 0f, 0f, 0.4f));
            card.scrim.raycastTarget = true;

            var column = NewRect("Column", root);
            column.anchorMin = new Vector2(joystickRegionRight, columnBottom);
            column.anchorMax = new Vector2(1f, columnTop);
            column.offsetMin = new Vector2(16f, 0f);
            column.offsetMax = new Vector2(-(railLeftInset + 16f), 0f);
            card.column = column;

            var panel = NewRect("Card", column);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(560f, 400f);
            var background = AddImage(panel, CardColour);
            background.raycastTarget = true; // taps on the card never reach the cockpit behind it
            card.card = panel;

            card.title = NewText("Title", panel, font, 30, TextAnchor.MiddleLeft, TitleColour);
            card.title.fontStyle = FontStyle.Bold;
            card.body = NewText("Body", panel, font, 22, TextAnchor.UpperLeft, TextColour, bestFit: true); // shrinks on narrow 4:3 columns
            card.footnote = NewText("Footnote", panel, font, 17, TextAnchor.MiddleLeft, QuietColour, bestFit: true);
            for (int i = 0; i < MissionCard.MaxButtons; i++) card.buttons[i] = Button("Button" + i, panel, font, 24, new Vector2(200f, 96f));

            root.gameObject.SetActive(true);
            return card;
        }

        // ---- Session buttons ---------------------------------------------------------------------

        /// <summary>MENU, top right under CAM (always), and SKIP TUTORIAL under it (First Flight only).</summary>
        public static HudButton BuildMenuButton(RectTransform safeArea, Font font) =>
            SessionButton("MenuButton", safeArea, font, MenuPosition, "MENU", 24);

        public static HudButton BuildSkipButton(RectTransform safeArea, Font font)
        {
            var button = SessionButton("SkipButton", safeArea, font, SkipPosition, "SKIP\nTUTORIAL", 18);
            Hidden((RectTransform)button.transform);
            return button;
        }

        static HudButton SessionButton(string name, RectTransform parent, Font font, Vector2 position, string caption, int fontSize)
        {
            var rect = NewRect(name, parent);
            rect.gameObject.SetActive(false); // wire everything before HudButton.Awake (rounded sprite, colours)
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = SessionButtonSize;
            var image = AddImage(rect, new Color(0f, 0f, 0f, 0.3f));
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = new Color(0f, 0f, 0f, 0.3f);
            var label = NewText("Label", rect, font, fontSize, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f), shadow: false);
            Stretch(label.rectTransform);
            label.text = caption;
            button.label = label;
            button.labelColor = new Color(1f, 1f, 1f, 0.85f);
            rect.gameObject.SetActive(true);
            return button;
        }

        // ---- Entry screen ------------------------------------------------------------------------

        public static ModeSelectScreen BuildModeSelect(RectTransform safeArea, Font font)
        {
            var root = NewRect("ModeSelect", safeArea);
            root.gameObject.SetActive(false);
            Stretch(root);
            var screen = root.gameObject.AddComponent<ModeSelectScreen>();
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f; // open at start; the session decides
            group.blocksRaycasts = true;
            group.interactable = true;
            screen.group = group;

            // A gradient-free dim over the world: the aircraft on the runway stays visible.
            var scrim = AddImage(root, new Color(0f, 0f, 0f, 0.42f));
            scrim.raycastTarget = true;

            const float left = 120f;
            screen.title = NewText("Title", root, font, 64, TextAnchor.LowerLeft, TitleColour);
            screen.title.fontStyle = FontStyle.Bold;
            TopLeft(screen.title.rectTransform, left, -96f, 900f, 80f);
            screen.title.text = "OPEN SKIES";
            screen.subtitle = NewText("Subtitle", root, font, 26, TextAnchor.UpperLeft, TextColour);
            TopLeft(screen.subtitle.rectTransform, left, -182f, 900f, 40f);
            screen.subtitle.text = "Choose how you want to fly";

            var size = new Vector2(640f, 120f);
            screen.freeFlightButton = ModeButton("FreeFlight", root, font, new Vector2(left, -268f), size, "Free Flight", out screen.freeFlightNote);
            screen.freeFlightNote.text = "Fly anywhere, at your own pace";
            screen.firstFlightButton = ModeButton("FirstFlight", root, font, new Vector2(left, -404f), size, "First Flight", out screen.firstFlightNote);
            screen.landingButton = ModeButton("LandingChallenge", root, font, new Vector2(left, -540f), size, "Landing Challenge", out screen.landingNote);

            var options = NewRect("FreeFlightOptions", root);
            options.anchorMin = options.anchorMax = options.pivot = new Vector2(0f, 1f);
            options.anchoredPosition = new Vector2(left + size.x + 24f, -268f);
            options.sizeDelta = new Vector2(584f, 120f);
            screen.freeFlightOptions = Hidden(options);
            screen.fromRunwayButton = Button("FromRunway", options, font, 26, new Vector2(280f, 120f));
            Place(screen.fromRunwayButton, 0f, 0f, new Vector2(280f, 120f));
            screen.fromRunwayButton.SetLabel("From the runway");
            screen.inTheAirButton = Button("InTheAir", options, font, 26, new Vector2(280f, 120f));
            Place(screen.inTheAirButton, 304f, 0f, new Vector2(280f, 120f));
            screen.inTheAirButton.SetLabel("In the air");

            var hint = NewText("Footer", root, font, 20, TextAnchor.LowerLeft, QuietColour);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = hint.rectTransform.pivot = new Vector2(0f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(left, 40f);
            hint.rectTransform.sizeDelta = new Vector2(1000f, 30f);
            hint.text = "MENU (top right) pauses the flight and brings you back here.";

            root.gameObject.SetActive(true);
            return screen;
        }

        static HudButton ModeButton(string name, RectTransform parent, Font font, Vector2 topLeft, Vector2 size, string caption, out Text note)
        {
            var button = Button(name, parent, font, 34, size);
            Place(button, topLeft.x, topLeft.y, size);
            button.idleColor = new Color(0f, 0f, 0f, 0.45f);
            var label = button.label;
            label.alignment = TextAnchor.UpperLeft;
            label.fontStyle = FontStyle.Bold;
            label.rectTransform.offsetMin = new Vector2(28f, 0f);
            label.rectTransform.offsetMax = new Vector2(-28f, -18f);
            button.SetLabel(caption);
            note = NewText("Note", button.transform, font, 22, TextAnchor.LowerLeft, TextColour, shadow: false);
            Stretch(note.rectTransform);
            note.rectTransform.offsetMin = new Vector2(28f, 18f);
            note.rectTransform.offsetMax = new Vector2(-28f, 0f);
            return button;
        }

        // ---- Helpers ------------------------------------------------------------------------------

        static HudButton Button(string name, RectTransform parent, Font font, int size, Vector2 rectSize)
        {
            var rect = NewRect(name, parent);
            rect.sizeDelta = rectSize;
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = ButtonColour;
            var label = NewText("Label", rect, font, size, TextAnchor.MiddleCenter, Color.white, shadow: false);
            Stretch(label.rectTransform);
            button.label = label;
            return button;
        }

        static void Place(Component item, float x, float top, Vector2 size)
        {
            var rect = (RectTransform)item.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, top);
            rect.sizeDelta = size;
        }

        static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

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

        static Image AddImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor, Color color, bool shadow = true, bool bestFit = false)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = bestFit ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = bestFit ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            if (bestFit)
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 16;
                text.resizeTextMaxSize = size;
            }
            if (shadow) AddShadow(text);
            return text;
        }

        static void AddShadow(Graphic graphic)
        {
            var effect = graphic.gameObject.AddComponent<Shadow>();
            effect.effectColor = new Color(0f, 0f, 0f, 0.55f);
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
