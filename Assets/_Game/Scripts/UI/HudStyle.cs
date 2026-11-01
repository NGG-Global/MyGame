using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The interface's design tokens: colour roles, type scale, spacing, corner radii,
    /// touch size and timing. Every factory takes its values from here, so the HUD,
    /// menus, cards and cockpit panels read as one system. Rationale and usage rules:
    /// docs/VISUAL_STYLE_GUIDE.md. The touch controls (Input layer) keep a mirror in
    /// <c>TouchControlStyle</c>; an EditMode test keeps the two in step.
    ///
    /// Colour roles follow the convention of modern glass cockpits: white for normal
    /// information, cyan for what the pilot selected, green for success, magenta for
    /// navigation, amber for cautions, red for warnings. Amber and red are reserved
    /// for alerts.
    /// </summary>
    public static class HudStyle
    {
        // ---- Colour: text ----------------------------------------------------------------------
        public static readonly Color TextPrimary = new Color(1f, 1f, 1f, 0.95f);
        public static readonly Color TextSecondary = new Color(1f, 1f, 1f, 0.74f);
        public static readonly Color TextQuiet = new Color(1f, 1f, 1f, 0.55f);
        /// <summary>Dark text on a light or saturated fill (selected buttons, caution banners).</summary>
        public static readonly Color TextOnAccent = new Color(0.04f, 0.06f, 0.08f, 1f);
        public static readonly Color TextShadow = new Color(0f, 0f, 0f, 0.6f);

        // ---- Colour: surfaces --------------------------------------------------------------------
        /// <summary>Buttons drawn over the world.</summary>
        public static readonly Color ControlSurface = new Color(0.02f, 0.03f, 0.04f, 0.42f);
        public static readonly Color ControlPressed = new Color(1f, 1f, 1f, 0.32f);
        /// <summary>Cards and panels (pause menu, settings, cockpit focus).</summary>
        public static readonly Color PanelSurface = new Color(0.03f, 0.035f, 0.045f, 0.82f);
        /// <summary>Buttons on a panel.</summary>
        public static readonly Color PanelControl = new Color(1f, 1f, 1f, 0.1f);
        /// <summary>The plate behind flight data and guides: large values keep 3:1 contrast even over a white sky (small, so the view stays open).</summary>
        public static readonly Color DataBacking = new Color(0f, 0f, 0f, 0.45f);
        public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.45f);
        public static readonly Color Hairline = new Color(1f, 1f, 1f, 0.16f);

        // ---- Colour: meaning ---------------------------------------------------------------------
        /// <summary>Cyan: the pilot's selection, an engaged system, the coach pointing at a control.</summary>
        public static readonly Color Selected = new Color(0.33f, 0.8f, 0.95f, 0.92f);
        /// <summary>Green: success, a system working normally.</summary>
        public static readonly Color Active = new Color(0.4f, 0.85f, 0.5f, 1f);
        /// <summary>Magenta: course, destination and checkpoint.</summary>
        public static readonly Color Navigation = new Color(0.93f, 0.47f, 0.93f, 1f);
        public static readonly Color Caution = new Color(0.98f, 0.72f, 0.16f, 0.94f);
        public static readonly Color Warning = new Color(0.82f, 0.14f, 0.11f, 0.95f);
        /// <summary>A flight value tied to an active warning (airspeed in a stall).</summary>
        public static readonly Color AlertValue = new Color(1f, 0.6f, 0.5f, 1f);

        // ---- Type scale (built-in font; canvas units at the 1080 reference height) --------------
        public const int DisplaySize = 64;
        public const int TitleSize = 32;
        public const int ValueSize = 42;
        public const int BodySize = 24;
        public const int ButtonSize = 24;
        public const int LabelSize = 20;
        /// <summary>The smallest text in the game (two-line button captions, footnotes).</summary>
        public const int CaptionSize = 18;

        // ---- Space, shape, touch -----------------------------------------------------------------
        public const float Space = 8f;
        public const float Gap = 16f;
        public const float ScreenMargin = 24f;
        public const float ControlRadius = 14f;
        public const float PanelRadius = 18f;
        public const float BannerRadius = 10f;
        /// <summary>Every touch target is at least this tall (and wide), canvas units.</summary>
        public const float MinTouchTarget = 96f;

        // ---- Motion ------------------------------------------------------------------------------
        public const float FadeSeconds = 0.2f;
        public const float WarningFadeSeconds = 0.12f;
        /// <summary>A pressed button shrinks this much at once (no animation: feedback never delays the action).</summary>
        public const float PressScale = 0.96f;

        /// <summary>Gives an image the rounded 9-sliced shape with the given corner radius (canvas units).</summary>
        public static void Round(Image image, float radius)
        {
            if (image == null) return;
            image.sprite = HudSprites.RoundedRect;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = HudSprites.RoundedRectRadius / Mathf.Max(1f, radius);
        }

        /// <summary>Sets the text colour and the drop shadow that keeps white text legible on bright sky.</summary>
        public static void Shadow(Graphic graphic)
        {
            if (graphic == null) return;
            var shadow = graphic.GetComponent<UnityEngine.UI.Shadow>();
            if (shadow == null) shadow = graphic.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = TextShadow;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        /// <summary>Relative luminance (WCAG) of an opaque colour, for contrast checks in tests and the style guide.</summary>
        public static float Luminance(Color c)
        {
            float R(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * R(c.r) + 0.7152f * R(c.g) + 0.0722f * R(c.b);
        }

        /// <summary>WCAG contrast ratio between two opaque colours (1 to 21).</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }
    }
}
