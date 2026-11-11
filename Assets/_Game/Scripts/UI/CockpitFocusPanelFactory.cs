using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds the focus panel in code (scene builder and tests). The column it
    /// uses spans from the right edge of the joystick region to the left edge of
    /// the action rail, between 17 % and 69 % of the safe-area height, so it never
    /// covers a flight control, the top bar or the most urgent warning banner. The
    /// cards carry no close button: a tap outside them collapses them (the tap-first
    /// contract, docs/TAP_DISCLOSURE_DESIGN.md).
    /// </summary>
    public static class CockpitFocusPanelFactory
    {
        public const float ColumnBottom = 0.17f;
        public const float ColumnTop = 0.69f;

        static readonly Color CardColour = new Color(HudStyle.PanelSurface.r, HudStyle.PanelSurface.g, HudStyle.PanelSurface.b, 0.62f); // lighter: the instrument shows through less chrome
        static readonly Color TitleColour = HudStyle.TextPrimary;
        static readonly Color HintColour = HudStyle.TextSecondary;
        static readonly Color ConfirmColour = new Color(1f, 0.72f, 0.3f, 1f);

        /// <param name="joystickRegionRight">Right edge of the joystick touch region, fraction of the safe-area width.</param>
        /// <param name="railLeftInset">Distance from the right safe-area edge to the left edge of the action rail, canvas units.</param>
        public static CockpitFocusPanel Build(RectTransform safeArea, Font font, float joystickRegionRight, float railLeftInset) =>
            Build(safeArea, font, joystickRegionRight, railLeftInset, null);

        /// <param name="surface">The interaction surface the enlarged face's gestures go to (null: the face only closes on tap).</param>
        public static CockpitFocusPanel Build(RectTransform safeArea, Font font, float joystickRegionRight, float railLeftInset, InteractionSurface surface)
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
            if (panel.faceHotspot != null) panel.faceHotspot.surface = surface;

            root.gameObject.SetActive(true);
            return panel;
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

            var host = Centred("Faces", card, new Vector2(300f, 300f));
            host.gameObject.AddComponent<FitToSquare>().designSize = InstrumentFace.DesignSize;
            panel.faceHost = host;
            for (int i = 0; i < CockpitFocusPanel.InstrumentCount; i++)
                panel.faces[i] = InstrumentFaceFactory.Build((InstrumentKind)i, host, font);

            // A thin ring at the rim that lights while the instrument is being adjusted (over the faces, no touches).
            var ring = NewRect("ManipulationRing", host);
            Stretch(ring);
            ring.offsetMin = new Vector2(-6f, -6f);
            ring.offsetMax = new Vector2(6f, 6f);
            var ringImage = ring.gameObject.AddComponent<Image>();
            ringImage.sprite = null; // assigned at runtime (HudSprites.Ring) by the panel
            ringImage.color = new Color(1f, 1f, 1f, 0f);
            ringImage.raycastTarget = false;
            panel.manipulationRing = ringImage;

            // The hotspot last, so it is on top of the faces and the ring: drags on the face reach the surface.
            var hot = NewRect("FaceHotspot", host);
            Stretch(hot);
            var graphic = hot.gameObject.AddComponent<RaycastOnlyHudGraphic>();
            graphic.raycastTarget = true;
            var hotspot = hot.gameObject.AddComponent<GestureHotspot>();
            hotspot.route = InteractionRoute.Instrument;
            panel.faceHotspot = hotspot;

            panel.manipulationNote = TopText("Note", card, font, 18, HintColour, 0f, 30f);
            panel.manipulationNote.alignment = TextAnchor.MiddleCenter;
            panel.manipulationNote.text = string.Empty;
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

            for (int i = 0; i < CockpitFocusPanel.MaxOptions; i++)
            {
                var rect = NewRect("Option" + i, card);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(120f, 96f);
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = true;
                var button = rect.gameObject.AddComponent<HudButton>();
                button.background = image;
                button.idleColor = HudStyle.PanelControl;
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

        static Text TopText(string name, RectTransform card, Font font, int size, Color colour, float y, float height)
        {
            var text = NewText(name, card, font, size, TextAnchor.UpperLeft, colour);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(16f, y - height);
            rect.offsetMax = new Vector2(-16f, y);
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
