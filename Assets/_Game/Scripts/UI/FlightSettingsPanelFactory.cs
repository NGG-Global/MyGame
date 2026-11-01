using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the flight settings card and the guidance line in code (scene builder
    /// and tests). Both live in the free column between the joystick region and the
    /// action rail, like the cockpit focus panel, so they never cover a flight
    /// control: the card in the same band as the focus panel, the guidance line
    /// along the bottom of the column, below that band.
    /// </summary>
    public static class FlightSettingsPanelFactory
    {
        public const float GuidanceBottom = 12f;
        public const float GuidanceHeight = 64f;

        static readonly Color CardColour = new Color(0.03f, 0.03f, 0.04f, 0.72f);
        static readonly Color ButtonColour = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color TitleColour = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color TextColour = new Color(1f, 1f, 1f, 0.8f);

        public static FlightSettingsPanel Build(RectTransform safeArea, Font font, float joystickRegionRight, float railLeftInset)
        {
            var root = NewRect("FlightSettingsPanel", safeArea);
            root.gameObject.SetActive(false); // wire everything before Awake
            Stretch(root);
            var panel = root.gameObject.AddComponent<FlightSettingsPanel>();

            var column = NewRect("Column", root);
            column.anchorMin = new Vector2(joystickRegionRight, CockpitFocusPanelFactory.ColumnBottom);
            column.anchorMax = new Vector2(1f, CockpitFocusPanelFactory.ColumnTop);
            column.offsetMin = new Vector2(16f, 0f);
            column.offsetMax = new Vector2(-(railLeftInset + 16f), 0f);
            panel.column = column;

            var card = NewRect("Card", column);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(560f, 480f);
            panel.card = card;
            var group = card.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            panel.group = group;
            var background = card.gameObject.AddComponent<Image>();
            background.color = CardColour;
            background.raycastTarget = true; // taps on the card never reach the cockpit behind it

            panel.title = Label("Title", card, font, 28, TitleColour, TextAnchor.UpperLeft);
            var titleRect = panel.title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(16f, -50f);
            titleRect.offsetMax = new Vector2(-108f, -10f);
            panel.title.text = "Flight assistance";
            panel.close = CloseButton(card, font);

            for (int i = 0; i < FlightAssistProfiles.Count; i++) panel.profiles[i] = Button("Profile" + i, card, font, 22);
            panel.description = Label("Description", card, font, 20, TextColour, TextAnchor.MiddleLeft, bestFit: true);
            panel.sensitivityLabel = Label("SensitivityLabel", card, font, 20, TextColour, TextAnchor.LowerLeft);
            panel.sensitivityLabel.text = "Stick sensitivity";
            for (int i = 0; i < panel.sensitivities.Length; i++) panel.sensitivities[i] = Button("Sensitivity" + i, card, font, 22);
            for (int i = 0; i < FlightSettingsPanel.OptionButtons; i++) panel.options[i] = Button("Option" + i, card, font, 22);
            panel.optionNote = Label("OptionNote", card, font, 20, TextColour, TextAnchor.MiddleLeft, bestFit: true);

            root.gameObject.SetActive(true);
            return panel;
        }

        /// <summary>The flying-tip line along the bottom of the free column. Takes no touches.</summary>
        public static GuidanceLine BuildGuidanceLine(RectTransform safeArea, Font font, float joystickRegionRight, float railLeftInset)
        {
            var rect = NewRect("GuidanceLine", safeArea);
            rect.gameObject.SetActive(false);
            rect.anchorMin = new Vector2(joystickRegionRight, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(16f, GuidanceBottom);
            rect.offsetMax = new Vector2(-(railLeftInset + 16f), GuidanceBottom + GuidanceHeight);
            var line = rect.gameObject.AddComponent<GuidanceLine>();
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            line.group = group;
            // A subtle backing only so the tip stays readable over a bright sky or the cockpit panel.
            var backing = rect.gameObject.AddComponent<Image>();
            backing.color = new Color(0f, 0f, 0f, 0.45f);
            backing.raycastTarget = false;
            line.backing = backing; // rounded sprite assigned at runtime (generated sprites cannot be saved in a scene)
            var text = Label("Tip", rect, font, 24, TitleColour, TextAnchor.MiddleCenter, bestFit: true);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(12f, 4f);
            text.rectTransform.offsetMax = new Vector2(-12f, -4f);
            line.text = text;
            rect.gameObject.SetActive(true);
            return line;
        }

        static HudButton Button(string name, RectTransform parent, Font font, int size)
        {
            var rect = NewRect(name, parent);
            rect.sizeDelta = new Vector2(120f, 96f);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = ButtonColour;
            var label = Label("Label", rect, font, size, Color.white, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            button.label = label;
            return button;
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
            var label = Label("Label", rect, font, 48, Color.white, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "×";
            button.label = label;
            return button;
        }

        static Text Label(string name, Transform parent, Font font, int size, Color colour, TextAnchor anchor, bool bestFit = false)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = colour;
            text.raycastTarget = false;
            text.horizontalOverflow = bestFit ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = bestFit ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            if (bestFit)
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 14;
                text.resizeTextMaxSize = size;
            }
            return text;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
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
