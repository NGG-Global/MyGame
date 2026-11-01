using System.Collections.Generic;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the settings screen in code (scene builder and tests). A centred panel
    /// over a dimmed, paused world: title and DONE, four tabs, up to five rows per
    /// page (label on the left, large segments or a stepper on the right) and a
    /// footnote. Canvas units for the 1080 reference height; every target is at
    /// least 96 units tall. The panel needs <see cref="PanelWidth"/> units of width,
    /// which every supported screen has at every allowed interface scale.
    /// </summary>
    public static class SettingsScreenFactory
    {
        public const float PanelWidth = 1180f;
        public const float PanelHeight = 872f;
        const float Pad = 32f;
        const float LabelWidth = 380f;
        const float ControlLeft = Pad + LabelWidth + 24f;
        const float SegmentGap = 12f;
        const float RowTop = -248f;
        const float RowStep = 112f;
        const float Row = HudStyle.MinTouchTarget;

        static readonly string[] PageLabels = { "DISPLAY", "CONTROLS", "SOUND", "GRAPHICS" };
        static readonly string[] HudProfileLabels = { "SMART", "DETAILED", "CINEMATIC" };
        static readonly string[] MotionLabels = { "FOLLOW SYSTEM", "REDUCED", "FULL" };
        static readonly string[] OnOffLabels = { "ON", "OFF" };
        static readonly string[] GraphicsLabels = { "BATTERY SAVER", "BALANCED", "HIGH QUALITY" };
        static readonly string[] FrameRateLabels = { "30 FPS", "60 FPS" };

        public static SettingsScreen Build(RectTransform safeArea, Font font)
        {
            var root = NewRect("SettingsScreen", safeArea);
            root.gameObject.SetActive(false); // wire everything before Awake
            Stretch(root);
            var screen = root.gameObject.AddComponent<SettingsScreen>();
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            screen.group = group;

            // Darker than the pause scrim: text on the panel must read over any scene.
            var scrim = root.gameObject.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.55f);
            scrim.raycastTarget = true; // takes every touch around the panel

            var panel = NewRect("Panel", root);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            var background = panel.gameObject.AddComponent<Image>();
            background.color = HudStyle.PanelSurface;
            background.raycastTarget = true;
            screen.panel = panel;

            screen.title = NewText("Title", panel, font, HudStyle.TitleSize, TextAnchor.MiddleLeft, HudStyle.TextPrimary);
            screen.title.fontStyle = FontStyle.Bold;
            TopLeft(screen.title.rectTransform, Pad, -24f, 600f, 64f);
            screen.title.text = "Settings";

            screen.doneButton = Button("Done", panel, font, HudStyle.ButtonSize, "DONE");
            var done = (RectTransform)screen.doneButton.transform;
            done.anchorMin = done.anchorMax = done.pivot = new Vector2(1f, 1f);
            done.anchoredPosition = new Vector2(-24f, -16f);
            done.sizeDelta = new Vector2(180f, Row);
            screen.doneButton.feedback = UiFeedback.Confirm;

            float tabWidth = 240f;
            for (int i = 0; i < SettingsScreen.PageCount; i++)
            {
                var tab = Button("Tab" + PageLabels[i], panel, font, HudStyle.ButtonSize, PageLabels[i]);
                TopLeft((RectTransform)tab.transform, Pad + i * (tabWidth + 16f), -128f, tabWidth, Row);
                screen.tabs[i] = tab;
            }

            var rows = new List<SettingsRow>();
            var display = Page(panel, SettingsPage.Display, screen);
            rows.Add(Segments(display, font, 0, SettingId.HudProfile, "HUD", HudProfileLabels));
            rows.Add(Stepper(display, font, 1, SettingId.UiScale, "Interface scale"));
            rows.Add(Stepper(display, font, 2, SettingId.HudOpacity, "HUD opacity"));
            rows.Add(Segments(display, font, 3, SettingId.Motion, "Motion", MotionLabels));

            var controls = Page(panel, SettingsPage.Controls, screen);
            rows.Add(Segments(controls, font, 0, SettingId.AssistProfile, "Flight assistance", Labels(FlightAssistProfiles.Count, i =>
                FlightAssistProfiles.Label((FlightAssistProfile)i))));
            rows.Add(Segments(controls, font, 1, SettingId.Sensitivity, "Stick sensitivity", Labels(3, i =>
                FlightAssistProfiles.SensitivityLabel((ControlSensitivity)i))));
            rows.Add(Segments(controls, font, 2, SettingId.Haptics, "Haptic feedback", OnOffLabels));

            var sound = Page(panel, SettingsPage.Sound, screen);
            rows.Add(Stepper(sound, font, 0, SettingId.MasterVolume, "Master volume"));
            rows.Add(Stepper(sound, font, 1, SettingId.AircraftVolume, "Aircraft"));
            rows.Add(Stepper(sound, font, 2, SettingId.EnvironmentVolume, "Wind and surroundings"));
            rows.Add(Stepper(sound, font, 3, SettingId.InterfaceVolume, "Interface"));
            rows.Add(Stepper(sound, font, 4, SettingId.WarningVolume, "Warnings"));

            var graphics = Page(panel, SettingsPage.Graphics, screen);
            rows.Add(Segments(graphics, font, 0, SettingId.Graphics, "Graphics quality", GraphicsLabels));
            rows.Add(Segments(graphics, font, 1, SettingId.FrameRate, "Frame rate", FrameRateLabels));
            screen.rows = rows.ToArray();

            screen.footnote = NewText("Footnote", panel, font, HudStyle.CaptionSize, TextAnchor.MiddleLeft, HudStyle.TextQuiet);
            var foot = screen.footnote.rectTransform;
            foot.anchorMin = foot.anchorMax = foot.pivot = new Vector2(0f, 0f);
            foot.anchoredPosition = new Vector2(Pad, 20f);
            foot.sizeDelta = new Vector2(PanelWidth - 2f * Pad, 40f);

            root.gameObject.SetActive(true);
            return screen;
        }

        static RectTransform Page(RectTransform panel, SettingsPage page, SettingsScreen screen)
        {
            var rect = NewRect(page + "Page", panel);
            Stretch(rect);
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            bool first = page == SettingsPage.Display;
            group.alpha = first ? 1f : 0f;
            group.blocksRaycasts = first;
            group.interactable = first;
            screen.pages[(int)page] = group;
            return rect;
        }

        static SettingsRow Segments(RectTransform page, Font font, int index, SettingId id, string label, string[] options)
        {
            var row = RowBase(page, font, index, id, label);
            float width = PanelWidth - ControlLeft - Pad;
            float segment = (width - (options.Length - 1) * SegmentGap) / options.Length;
            row.segments = new HudButton[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                var button = Button(id + "Option" + i, row.root, font, options[i].Length > 11 ? HudStyle.LabelSize : HudStyle.ButtonSize - 2, options[i]);
                TopLeft((RectTransform)button.transform, ControlLeft + i * (segment + SegmentGap), 0f, segment, Row);
                row.segments[i] = button;
            }
            return row;
        }

        static SettingsRow Stepper(RectTransform page, Font font, int index, SettingId id, string label)
        {
            var row = RowBase(page, font, index, id, label);
            row.minus = Button(id + "Minus", row.root, font, HudStyle.TitleSize, "–");
            TopLeft((RectTransform)row.minus.transform, ControlLeft, 0f, Row, Row);
            row.value = NewText(id + "Value", row.root, font, 30, TextAnchor.MiddleCenter, HudStyle.TextPrimary);
            TopLeft(row.value.rectTransform, ControlLeft + Row + 8f, 0f, 168f, Row);
            row.plus = Button(id + "Plus", row.root, font, HudStyle.TitleSize, "+");
            TopLeft((RectTransform)row.plus.transform, ControlLeft + Row + 184f, 0f, Row, Row);
            return row;
        }

        static SettingsRow RowBase(RectTransform page, Font font, int index, SettingId id, string label)
        {
            var root = NewRect(id + "Row", page);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(0f, RowTop - index * RowStep);
            root.sizeDelta = new Vector2(PanelWidth, Row);
            var row = new SettingsRow { id = id, root = root };
            row.label = NewText("Label", root, font, HudStyle.BodySize + 2, TextAnchor.MiddleLeft, HudStyle.TextPrimary);
            TopLeft(row.label.rectTransform, Pad, -6f, LabelWidth, 44f);
            row.label.text = label;
            row.note = NewText("Note", root, font, HudStyle.CaptionSize, TextAnchor.UpperLeft, HudStyle.TextQuiet);
            TopLeft(row.note.rectTransform, Pad, -52f, LabelWidth, 40f);
            row.note.horizontalOverflow = HorizontalWrapMode.Wrap;
            row.note.text = string.Empty;
            return row;
        }

        static string[] Labels(int count, System.Func<int, string> label)
        {
            var labels = new string[count];
            for (int i = 0; i < count; i++) labels[i] = label(i);
            return labels;
        }

        static HudButton Button(string name, RectTransform parent, Font font, int size, string caption)
        {
            var rect = NewRect(name, parent);
            rect.sizeDelta = new Vector2(Row, Row);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = HudStyle.PanelControl;
            var label = NewText("Label", rect, font, size, TextAnchor.MiddleCenter, HudStyle.TextPrimary);
            Stretch(label.rectTransform);
            label.text = caption;
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
