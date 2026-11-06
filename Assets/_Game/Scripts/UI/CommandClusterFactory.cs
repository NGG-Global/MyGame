using OpenSkies.Awareness;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the top-right command cluster in code (scene builder and tests), from the
    /// rectangles of <see cref="CommandClusterLayout"/>, inside the safe area. Every control is
    /// an invisible 96-unit touch target (<see cref="RaycastOnlyHudGraphic"/>) around a smaller
    /// visual, so visual weight and touch size are chosen separately. Built after the contextual
    /// and gesture HUDs (it draws over them) and before the coaching highlights, the mission card,
    /// the pause sheet and the entry screen (they draw over it).
    /// </summary>
    public static class CommandClusterFactory
    {
        static readonly Color QuietSurface = new Color(0.02f, 0.03f, 0.04f, 0.3f);
        static readonly Color QuietIcon = HudStyle.TextSecondary;
        /// <summary>The options trigger while its panel is open: lighter, not cyan (cyan means a selection).</summary>
        static readonly Color OpenTrigger = new Color(1f, 1f, 1f, 0.26f);
        static readonly Color SlotSurface = new Color(1f, 1f, 1f, 0.06f);

        public static CommandCluster Build(RectTransform safeArea, Font font)
        {
            var root = NewRect("CommandCluster", safeArea);
            root.gameObject.SetActive(false); // wire everything before Awake
            Stretch(root);
            var cluster = root.gameObject.AddComponent<CommandCluster>();

            // ---- Primary row: no card behind it; each control carries its own small visual.
            var row = NewRect("PrimaryRow", root);
            Stretch(row);
            cluster.rowGroup = row.gameObject.AddComponent<CanvasGroup>();

            cluster.cameraButton = Control("CameraButton", row, CommandClusterLayout.CameraHit, CommandClusterLayout.CameraVisualSize, HudStyle.ControlRadius,
                HudStyle.ControlSurface);
            var cameraVisual = cluster.cameraButton.background.rectTransform;
            cluster.cameraIcon = Icon("Icon", cameraVisual, new Vector2(0f, 12f), 34f, HudStyle.TextPrimary);
            cluster.cameraCaption = NewText("Caption", cameraVisual, font, HudStyle.CaptionSize, TextAnchor.LowerCenter, HudStyle.TextPrimary);
            Stretch(cluster.cameraCaption.rectTransform);
            cluster.cameraCaption.rectTransform.offsetMin = new Vector2(2f, 5f);
            cluster.cameraCaption.rectTransform.offsetMax = new Vector2(-2f, 0f);
            cluster.cameraCaption.text = CommandClusterModel.ChaseLabel;
            cluster.cameraButton.label = cluster.cameraCaption;
            cluster.cameraButton.feedback = UiFeedback.Tap;

            float optionsRadius = CommandClusterLayout.OptionsVisualSize.y * 0.5f;
            cluster.optionsButton = Control("OptionsButton", row, CommandClusterLayout.OptionsHit, CommandClusterLayout.OptionsVisualSize, optionsRadius,
                HudStyle.ControlSurface);
            cluster.optionsButton.engagedColor = OpenTrigger;
            cluster.optionsIcon = Icon("Icon", cluster.optionsButton.background.rectTransform, Vector2.zero, 36f, HudStyle.TextPrimary);

            float pauseRadius = CommandClusterLayout.PauseVisualSize.y * 0.5f;
            cluster.pauseButton = Control("PauseButton", row, CommandClusterLayout.PauseHit, CommandClusterLayout.PauseVisualSize, pauseRadius, QuietSurface);
            cluster.pauseIcon = Icon("Icon", cluster.pauseButton.background.rectTransform, Vector2.zero, 26f, QuietIcon);

            // ---- Contextual: LOOK FWD under the camera.
            cluster.lookForward = BuildLookForward(root, font);

            // ---- The options panel, against the right edge under the row; hidden until the trigger opens it.
            var panelRect = CommandClusterLayout.Panel;
            var panel = Place(NewRect("OptionsPanel", root), panelRect);
            cluster.panel = panel;
            cluster.panelGroup = Hidden(panel);
            cluster.panelBackground = AddImage(panel, HudStyle.PanelSurface);
            cluster.panelBackground.raycastTarget = true; // a press between rows is the panel's, never the world's

            var dividers = new System.Collections.Generic.List<Image>();

            // Options level: HUD (secondary), then RESET, SETTINGS, SKIP TUTORIAL (tertiary).
            var options = NewRect("OptionsLevel", panel);
            Stretch(options);
            cluster.optionsLevel = options;
            cluster.hudRow = Row("HudRow", options, panelRect, CommandClusterLayout.SecondaryRow, font, CommandClusterModel.HudLabel, TextAnchor.MiddleLeft,
                out cluster.hudRowChevron, chevronPointsRight: true);
            cluster.hudRowValue = NewText("Value", cluster.hudRow.background.rectTransform, font, HudStyle.LabelSize, TextAnchor.MiddleRight, HudStyle.Selected);
            Stretch(cluster.hudRowValue.rectTransform);
            cluster.hudRowValue.rectTransform.offsetMax = new Vector2(-44f, 0f);
            cluster.hudRowValue.text = CommandClusterModel.ProfileLabel(HudProfile.Smart);
            dividers.Add(Divider(options, panelRect));
            cluster.resetButton = SlotButton("ResetButton", options, panelRect, CommandClusterLayout.ResetSlot, font, CommandClusterModel.ResetLabel);
            cluster.resetButton.feedback = UiFeedback.Confirm;
            cluster.settingsButton = SlotButton("SettingsButton", options, panelRect, CommandClusterLayout.SettingsSlot, font, CommandClusterModel.SettingsLabel);
            cluster.skipButton = SlotButton("SkipButton", options, panelRect, CommandClusterLayout.SkipSlot, font, CommandClusterModel.SkipLabel);
            cluster.skipButton.feedback = UiFeedback.Confirm;
            cluster.skipButton.gameObject.SetActive(false); // offered only while the First Flight runs

            // HUD selector: the title row goes back; the three profiles are chosen directly.
            var selector = NewRect("HudSelector", panel);
            Stretch(selector);
            cluster.selectorLevel = selector;
            cluster.selectorBack = Row("BackRow", selector, panelRect, CommandClusterLayout.SecondaryRow, font, string.Empty, TextAnchor.MiddleLeft,
                out cluster.selectorChevron, chevronPointsRight: false);
            cluster.selectorTitle = cluster.selectorBack.label;
            cluster.selectorTitle.text = CommandClusterModel.SelectorTitle(HudProfile.Smart);
            dividers.Add(Divider(selector, panelRect));
            for (int i = 0; i < CommandClusterModel.ProfileCount; i++)
            {
                var profile = CommandClusterModel.ProfileAt(i);
                var button = SlotButton("Profile" + CommandClusterModel.ProfileLabel(profile), selector, panelRect, i, font, CommandClusterModel.ProfileLabel(profile));
                button.labelColor = HudStyle.TextPrimary;
                button.feedback = UiFeedback.Confirm;
                cluster.profileButtons[i] = button;
            }
            cluster.dividers = dividers.ToArray();
            selector.gameObject.SetActive(false);

            root.gameObject.SetActive(true);
            return cluster;
        }

        /// <summary>LOOK FWD, the contextual camera option: a compact chip under the camera, hidden until the cockpit session shows it.</summary>
        public static CockpitViewControls BuildLookForward(RectTransform parent, Font font)
        {
            var root = NewRect("CockpitViewControls", parent);
            root.gameObject.SetActive(false); // wire everything before Awake
            Stretch(root);
            var controls = root.gameObject.AddComponent<CockpitViewControls>();

            var button = Control("LookForwardButton", root, CommandClusterLayout.LookForwardHit, CommandClusterLayout.LookForwardVisualSize,
                CommandClusterLayout.LookForwardVisualSize.y * 0.5f, HudStyle.ControlSurface);
            controls.lookForwardGroup = Hidden((RectTransform)button.transform);
            var label = NewText("Label", button.background.rectTransform, font, HudStyle.CaptionSize, TextAnchor.MiddleCenter, HudStyle.TextPrimary);
            Stretch(label.rectTransform);
            label.text = CommandClusterModel.LookForwardLabel;
            button.label = label;
            controls.lookForward = button;

            root.gameObject.SetActive(true);
            return controls;
        }

        // ---- Building blocks -------------------------------------------------------------------------

        /// <summary>An invisible touch target of the given rectangle with a centred visual of its own size and radius.</summary>
        static HudButton Control(string name, RectTransform parent, Rect hit, Vector2 visualSize, float radius, Color idle)
        {
            var rect = Place(NewRect(name, parent), hit);
            var target = rect.gameObject.AddComponent<RaycastOnlyHudGraphic>();
            target.raycastTarget = true;

            var visual = NewRect("Visual", rect);
            visual.anchorMin = visual.anchorMax = visual.pivot = new Vector2(0.5f, 0.5f);
            visual.sizeDelta = visualSize;
            var image = AddImage(visual, idle);

            var button = rect.gameObject.AddComponent<HudButton>();
            button.background = image;
            button.idleColor = idle;
            button.cornerRadius = radius;
            return button;
        }

        /// <summary>A full-width panel row: a pill inside the 96-unit target, a caption and a chevron.</summary>
        static HudButton Row(string name, RectTransform parent, Rect panel, Rect row, Font font, string caption, TextAnchor anchor,
            out Image chevron, bool chevronPointsRight)
        {
            var visualSize = new Vector2(row.width, CommandClusterLayout.RowVisualHeight);
            var button = Control(name, parent, Relative(row, panel), visualSize, HudStyle.ControlRadius, HudStyle.PanelControl);
            var visual = button.background.rectTransform;
            var label = NewText("Label", visual, font, HudStyle.ButtonSize, anchor, HudStyle.TextPrimary);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(chevronPointsRight ? 20f : 52f, 0f);
            label.rectTransform.offsetMax = new Vector2(-20f, 0f);
            label.text = caption;
            button.label = label;

            var mark = NewRect("Chevron", visual);
            mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(chevronPointsRight ? 1f : 0f, 0.5f);
            mark.anchoredPosition = new Vector2(chevronPointsRight ? -16f : 16f, 0f);
            mark.sizeDelta = new Vector2(20f, 20f);
            mark.localEulerAngles = new Vector3(0f, 0f, chevronPointsRight ? -90f : 90f);
            chevron = AddImage(mark, HudStyle.TextSecondary);
            return button;
        }

        /// <summary>A tertiary action (or a profile of the selector) in one of the lower row's fixed slots: smaller and quieter.</summary>
        static HudButton SlotButton(string name, RectTransform parent, Rect panel, int slot, Font font, string caption)
        {
            var hit = CommandClusterLayout.Slot(slot);
            var visualSize = new Vector2(hit.width - 8f, CommandClusterLayout.SlotVisualHeight);
            var button = Control(name, parent, Relative(hit, panel), visualSize, HudStyle.ControlRadius, SlotSurface);
            var label = NewText("Label", button.background.rectTransform, font, HudStyle.CaptionSize, TextAnchor.MiddleCenter, HudStyle.TextSecondary);
            Stretch(label.rectTransform);
            label.text = caption;
            button.label = label;
            button.labelColor = HudStyle.TextSecondary;
            return button;
        }

        static Image Divider(RectTransform parent, Rect panel)
        {
            var row = CommandClusterLayout.SecondaryRow;
            var line = Rect.MinMaxRect(row.xMin + 12f, row.yMin - CommandClusterLayout.PanelPadding * 0.5f - 0.5f, row.xMax - 12f,
                row.yMin - CommandClusterLayout.PanelPadding * 0.5f + 0.5f);
            var rect = Place(NewRect("Divider", parent), Relative(line, panel));
            return AddImage(rect, HudStyle.Hairline);
        }

        static Image Icon(string name, RectTransform visual, Vector2 offset, float size, Color color)
        {
            var rect = NewRect(name, visual);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(size, size);
            var image = AddImage(rect, color);
            image.preserveAspect = true;
            return image;
        }

        /// <summary>A rectangle of the layout relative to the panel's top-right corner (children of the panel).</summary>
        static Rect Relative(Rect r, Rect panel) => new Rect(r.x - panel.xMax, r.y - panel.yMax, r.width, r.height);

        /// <summary>Places a rect from a top-right rectangle: anchors and pivot at the parent's top-right corner.</summary>
        static RectTransform Place(RectTransform rect, Rect topRight)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = CommandClusterLayout.AnchoredPosition(topRight);
            rect.sizeDelta = topRight.size;
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

        /// <summary>Sprites are generated at runtime; the factory leaves them empty (HudButton and CommandCluster assign them in Awake).</summary>
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
            text.lineSpacing = 0.9f;
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
