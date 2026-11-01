using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the focus panel in code (scene builder and tests). The column it
    /// uses spans from the right edge of the joystick region to the left edge of
    /// the action rail, between 17 % and 69 % of the safe-area height, so it never
    /// covers a flight control, the top bar or the most urgent warning banner.
    /// </summary>
    public static class CockpitFocusPanelFactory
    {
        public const float ColumnBottom = 0.17f;
        public const float ColumnTop = 0.69f;

        static readonly Color CardColour = new Color(0.03f, 0.03f, 0.04f, 0.62f);
        static readonly Color TitleColour = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color HintColour = new Color(1f, 1f, 1f, 0.7f);
        static readonly Color ConfirmColour = new Color(1f, 0.72f, 0.3f, 1f);

        /// <param name="joystickRegionRight">Right edge of the joystick touch region, fraction of the safe-area width.</param>
        /// <param name="railLeftInset">Distance from the right safe-area edge to the left edge of the action rail, canvas units.</param>
        public static CockpitFocusPanel Build(RectTransform safeArea, Font font, float joystickRegionRight, float railLeftInset)
        {
            var root = NewRect("CockpitFocusPanel", safeArea);
            root.gameObject.SetActive(false); // wire everything before Awake
            Stretch(root);
            var panel = root.gameObject.AddComponent<CockpitFocusPanel>();

            var column = NewRect("Column", root);
            column.anchorMin = new Vector2(joystickRegionRight, ColumnBottom);
            column.anchorMax = new Vector2(1f, ColumnTop);
            column.offsetMin = new Vector2(16f, 0f);
            column.offsetMax = new Vector2(-(railLeftInset + 16f), 0f);
            panel.column = column;

            BuildInstrumentCard(panel, column, font);
            BuildSystemCard(panel, column, font);

            root.gameObject.SetActive(true);
            return panel;
        }

        /// <summary>LOOK FWD: top bar, left of the HUD profile button (which is left of RESET and CAM).</summary>
        public static readonly Vector2 LookForwardPosition = new Vector2(-456f, -ContextualHudFactory.TopMargin);
        public static readonly Vector2 LookForwardSize = new Vector2(128f, 96f);

        /// <summary>Cockpit view controls in the top bar, hidden until the session shows them.</summary>
        public static CockpitViewControls BuildViewControls(RectTransform safeArea, Font font)
        {
            var root = NewRect("CockpitViewControls", safeArea);
            root.gameObject.SetActive(false); // wire everything before Awake
            Stretch(root);
            var controls = root.gameObject.AddComponent<CockpitViewControls>();

            var rect = NewRect("LookForwardButton", root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = LookForwardPosition;
            rect.sizeDelta = LookForwardSize;
            controls.lookForwardGroup = Hidden(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.35f); // same as the HUD profile button
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            var label = NewText("Label", rect, font, 22, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform);
            label.text = "LOOK\nFWD";
            button.label = label;
            controls.lookForward = button;

            root.gameObject.SetActive(true);
            return controls;
        }

        static void BuildInstrumentCard(CockpitFocusPanel panel, RectTransform column, Font font)
        {
            var card = Centred("InstrumentCard", column, new Vector2(400f, 400f));
            panel.instrumentCard = card;
            panel.instrumentGroup = Hidden(card);
            // The whole card is a tap target: tapping the enlarged instrument closes it.
            var background = card.gameObject.AddComponent<Image>();
            background.raycastTarget = true;
            var tap = card.gameObject.AddComponent<HudButton>();
            tap.background = background;
            tap.idleColor = CardColour;
            tap.pressedColor = new Color(0.1f, 0.1f, 0.12f, 0.7f);
            panel.instrumentTapToClose = tap;

            panel.instrumentTitle = TopText("Title", card, font, 30, TitleColour, -10f, 40f);
            panel.instrumentHint = TopText("Hint", card, font, 18, HintColour, -44f, 24f);
            panel.instrumentClose = CloseButton(card, font);

            var host = Centred("Faces", card, new Vector2(300f, 300f));
            host.gameObject.AddComponent<FitToSquare>().designSize = InstrumentFace.DesignSize;
            panel.faceHost = host;
            for (int i = 0; i < CockpitFocusPanel.InstrumentCount; i++)
                panel.faces[i] = InstrumentFaceFactory.Build((InstrumentKind)i, host, font);
        }

        static void BuildSystemCard(CockpitFocusPanel panel, RectTransform column, Font font)
        {
            var card = Centred("SystemCard", column, new Vector2(560f, 360f));
            panel.systemCard = card;
            panel.systemGroup = Hidden(card);
            var background = card.gameObject.AddComponent<Image>();
            background.color = CardColour;
            background.raycastTarget = true; // taps on the card never reach the cockpit behind it

            panel.systemTitle = TopText("Title", card, font, 30, TitleColour, -10f, 40f);
            panel.systemHint = TopText("Hint", card, font, 18, HintColour, -44f, 24f);
            panel.systemState = TopText("State", card, font, 24, TitleColour, -70f, 32f);
            panel.systemClose = CloseButton(card, font);

            for (int i = 0; i < CockpitFocusPanel.MaxOptions; i++)
            {
                var rect = NewRect("Option" + i, card);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(120f, 96f);
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = true;
                var button = rect.gameObject.AddComponent<HudButton>();
                button.background = image;
                button.idleColor = new Color(1f, 1f, 1f, 0.12f);
                var label = NewText("Label", rect, font, 30, TextAnchor.MiddleCenter, Color.white);
                Stretch(label.rectTransform);
                button.label = label;
                panel.options[i] = button;
            }

            var confirm = NewText("Confirmation", card, font, 20, TextAnchor.LowerLeft, ConfirmColour);
            var confirmRect = confirm.rectTransform;
            confirmRect.anchorMin = new Vector2(0f, 0f);
            confirmRect.anchorMax = new Vector2(1f, 0f);
            confirmRect.pivot = new Vector2(0f, 0f);
            confirmRect.offsetMin = new Vector2(14f, 8f);
            confirmRect.offsetMax = new Vector2(-14f, 36f);
            panel.confirmation = confirm;
        }

        static HudButton CloseButton(RectTransform card, Font font)
        {
            var rect = NewRect("Close", card);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-4f, -4f);
            rect.sizeDelta = new Vector2(96f, 96f);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = new Color(1f, 1f, 1f, 0.1f);
            var label = NewText("Label", rect, font, 48, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform);
            label.text = "×"; // multiplication sign, in the built-in font
            button.label = label;
            return button;
        }

        static Text TopText(string name, RectTransform card, Font font, int size, Color colour, float y, float height)
        {
            var text = NewText(name, card, font, size, TextAnchor.UpperLeft, colour);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(16f, y - height);
            rect.offsetMax = new Vector2(-108f, y); // clear of the close button
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        static RectTransform Centred(string name, RectTransform parent, Vector2 size)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
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

        static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor, Color colour)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = colour;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
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
