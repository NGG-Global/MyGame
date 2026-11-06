using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the pause sheet in code: a full-screen scrim with the sheet rising from
    /// the bottom of the safe area, its grip band, the first stop's title, line and
    /// four buttons, the pull hint, and the settings embedded for the second stop.
    /// Built after the HUD so it draws over everything but the entry screen.
    /// </summary>
    public static class PauseSheetFactory
    {
        public const float MaxWidth = 1180f;

        public static PauseSheet Build(RectTransform safeArea, Font font, InteractionSurface surface)
        {
            var root = NewRect("PauseSheet", safeArea);
            root.gameObject.SetActive(false);
            Stretch(root);
            var sheetView = root.gameObject.AddComponent<PauseSheet>();
            sheetView.group = root.gameObject.AddComponent<CanvasGroup>();

            // The scrim: dims the paused world and takes the touches around the sheet (a tap resumes).
            var scrim = root.gameObject.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0f);
            scrim.raycastTarget = true;
            sheetView.scrim = scrim;
            var scrimButton = root.gameObject.AddComponent<HudButton>();
            scrimButton.background = null;
            scrimButton.pressScale = 1f;
            scrimButton.feedback = UiFeedback.None;
            sheetView.scrimButton = scrimButton;

            // The sheet: bottom-anchored, as wide as the safe area up to MaxWidth, centred.
            var sheet = NewRect("Sheet", root);
            sheet.anchorMin = new Vector2(0.5f, 0f);
            sheet.anchorMax = new Vector2(0.5f, 0f);
            sheet.pivot = new Vector2(0.5f, 0f);
            sheet.anchoredPosition = Vector2.zero;
            float width = Mathf.Min(MaxWidth, Mathf.Max(600f, safeArea.rect.width));
            sheet.sizeDelta = new Vector2(width, 0f);
            sheetView.sheet = sheet;
            var surfaceImage = sheet.gameObject.AddComponent<Image>();
            surfaceImage.color = HudStyle.PanelSurface;
            surfaceImage.raycastTarget = true; // touches on the sheet never reach the world
            sheetView.surface = surfaceImage;
            // ...and never bubble up to the scrim's tap handler either (uGUI looks for a handler up the hierarchy).
            var sheetCatch = sheet.gameObject.AddComponent<HudButton>();
            sheetCatch.background = null;
            sheetCatch.pressScale = 1f;
            sheetCatch.feedback = UiFeedback.None;

            // The grip band across the top: the drag hotspot, with a short bar as the affordance.
            var grip = NewRect("Grip", sheet);
            grip.anchorMin = new Vector2(0f, 1f);
            grip.anchorMax = new Vector2(1f, 1f);
            grip.pivot = new Vector2(0.5f, 1f);
            grip.anchoredPosition = Vector2.zero;
            grip.sizeDelta = new Vector2(0f, HudStyle.MinTouchTarget);
            var gripGraphic = grip.gameObject.AddComponent<RaycastOnlyHudGraphic>();
            gripGraphic.raycastTarget = true;
            var hotspot = grip.gameObject.AddComponent<GestureHotspot>();
            hotspot.surface = surface;
            hotspot.route = InteractionRoute.Sheet;
            sheetView.grip = hotspot;
            var bar = NewRect("Bar", grip);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -14f);
            bar.sizeDelta = new Vector2(72f, 8f);
            sheetView.gripBar = AddImage(bar, new Color(1f, 1f, 1f, 0.45f));

            sheetView.title = NewText("Title", sheet, font, HudStyle.TitleSize, TextAnchor.MiddleLeft, HudStyle.TextPrimary);
            sheetView.title.fontStyle = FontStyle.Bold;
            TopLeft(sheetView.title.rectTransform, 24f, -40f, width - 48f, 44f);
            sheetView.subtitle = NewText("Subtitle", sheet, font, HudStyle.BodySize, TextAnchor.MiddleRight, HudStyle.TextSecondary);
            TopLeft(sheetView.subtitle.rectTransform, width * 0.45f, -40f, width * 0.55f - 24f, 44f);

            for (int i = 0; i < PauseSheet.MaxButtons; i++)
            {
                var button = Button("Button" + i, sheet, font);
                button.feedback = UiFeedback.Confirm;
                sheetView.buttons[i] = button;
            }

            sheetView.pullHint = NewText("PullHint", sheet, font, HudStyle.CaptionSize, TextAnchor.MiddleCenter, HudStyle.TextSecondary);
            var hint = sheetView.pullHint.rectTransform;
            hint.anchorMin = new Vector2(0f, 1f);
            hint.anchorMax = new Vector2(1f, 1f);
            hint.pivot = new Vector2(0.5f, 1f);
            hint.anchoredPosition = new Vector2(0f, -(48f + 60f + HudStyle.MinTouchTarget + 24f));
            hint.sizeDelta = new Vector2(0f, 26f);
            sheetView.pullHint.text = "Pull up for settings";

            // The second stop: the settings, revealed as the sheet rises.
            var second = NewRect("SecondStop", sheet);
            second.anchorMin = new Vector2(0f, 1f);
            second.anchorMax = new Vector2(1f, 1f);
            second.pivot = new Vector2(0.5f, 1f);
            second.anchoredPosition = new Vector2(0f, -300f);
            second.sizeDelta = new Vector2(0f, 700f);
            sheetView.secondStopGroup = second.gameObject.AddComponent<CanvasGroup>();
            sheetView.secondStopGroup.alpha = 0f;
            sheetView.secondStopGroup.blocksRaycasts = false;
            sheetView.secondStopGroup.interactable = false;
            sheetView.settings = SettingsScreenFactory.BuildEmbedded(second, font, width);

            root.gameObject.SetActive(true);
            return sheetView;
        }

        static HudButton Button(string name, RectTransform parent, Font font)
        {
            var rect = NewRect(name, parent);
            rect.sizeDelta = new Vector2(HudStyle.MinTouchTarget, HudStyle.MinTouchTarget);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = HudStyle.PanelControl;
            var label = NewText("Label", rect, font, HudStyle.ButtonSize, TextAnchor.MiddleCenter, HudStyle.TextPrimary);
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
