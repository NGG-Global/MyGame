using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// The touch controls' share of the design tokens (docs/VISUAL_STYLE_GUIDE.md).
    /// Layers do not share code, so these mirror <c>OpenSkies.UI.HudStyle</c>; an
    /// EditMode test fails if the two drift apart.
    /// </summary>
    public static class TouchControlStyle
    {
        public static readonly Color TextPrimary = new Color(1f, 1f, 1f, 0.95f);
        /// <summary>Tint of the control outlines and fills (the control's alpha comes from its state).</summary>
        public static readonly Color Stroke = Color.white;
        /// <summary>The dark edge that keeps white controls visible over bright sky and runway markings.</summary>
        public static readonly Color Edge = new Color(0f, 0f, 0f, 0.35f);
        public static readonly Color TextShadow = new Color(0f, 0f, 0f, 0.6f);
        /// <summary>Cyan, as HudStyle.Selected: the active (held) state of the brake.</summary>
        public static readonly Color Selected = new Color(0.33f, 0.8f, 0.95f, 0.92f);
        public const float ControlRadius = 14f;
        public const int ButtonSize = 24;
        public const int ValueSize = 30;
        public const float MinTouchTarget = 96f;

        /// <summary>Corner radius of the generated rounded sprites, texels (canvas units at multiplier 1).</summary>
        public const float SpriteCornerRadius = 40f;

        public static void Round(Image image, bool outline)
        {
            if (image == null) return;
            image.sprite = outline ? TouchControlSprites.RoundedOutline : TouchControlSprites.RoundedRect;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = SpriteCornerRadius / ControlRadius;
        }

        /// <summary>A soft dark edge around a white graphic.</summary>
        public static void AddEdge(Graphic graphic, float distance = 1.5f)
        {
            if (graphic == null || graphic.GetComponent<Outline>() != null) return;
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = Edge;
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = true;
        }
    }
}
