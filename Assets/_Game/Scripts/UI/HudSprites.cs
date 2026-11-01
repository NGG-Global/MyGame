using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// White anti-aliased sprites generated once at runtime and tinted by Image
    /// colours, so the HUD needs no imported art. (The touch controls have their
    /// own set in the Input layer; layers do not share code.)
    /// </summary>
    public static class HudSprites
    {
        const int Size = 128;
        const int Supersample = 4;
        static Sprite _disc, _roundedRect, _arrow, _ring, _bezel, _roundedOutline, _diamond;

        /// <summary>Filled circle.</summary>
        public static Sprite Disc => _disc != null ? _disc : _disc = Create("HudDisc", false, (x, y) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            return dx * dx + dy * dy <= 0.235f; // radius ≈ 0.485 of the texture
        });

        /// <summary>Rounded rectangle, 9-sliced.</summary>
        public static Sprite RoundedRect => _roundedRect != null ? _roundedRect : _roundedRect = Create("HudRoundedRect", true, (x, y) =>
            InsideRounded(x, y, 0.5f, 0.3f));

        /// <summary>Rounded rectangle outline, 9-sliced: frames a control without covering it.</summary>
        public static Sprite RoundedOutline => _roundedOutline != null ? _roundedOutline : _roundedOutline = Create("HudRoundedOutline", true, (x, y) =>
            InsideRounded(x, y, 0.5f, 0.3f) && !InsideRounded(x, y, 0.44f, 0.24f));

        /// <summary>Diamond outline: an understated checkpoint symbol.</summary>
        public static Sprite Diamond => _diamond != null ? _diamond : _diamond = Create("HudDiamond", false, (x, y) =>
        {
            float d = Mathf.Abs(x - 0.5f) + Mathf.Abs(y - 0.5f);
            return d <= 0.47f && d >= 0.36f;
        });

        static bool InsideRounded(float x, float y, float half, float r)
        {
            float qx = Mathf.Abs(x - 0.5f) - (half - r);
            float qy = Mathf.Abs(y - 0.5f) - (half - r);
            if (qx <= 0f || qy <= 0f) return qx <= r && qy <= r;
            return qx * qx + qy * qy <= r * r;
        }

        /// <summary>Arrowhead pointing up (+Y), used as a direction cue.</summary>
        public static Sprite Arrow => _arrow != null ? _arrow : _arrow = Create("HudArrow", false, (x, y) =>
            InsideArrow(x, y));

        /// <summary>Thin circle outline (7 % of the size), for dial arcs (Image type Filled, radial).</summary>
        public static Sprite Ring => _ring != null ? _ring : _ring = Create("HudRing", false, (x, y) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            float r2 = dx * dx + dy * dy;
            return r2 <= 0.25f && r2 >= 0.43f * 0.43f;
        });

        /// <summary>Opaque square with a round hole: turns a square window into a round instrument face.</summary>
        public static Sprite Bezel => _bezel != null ? _bezel : _bezel = Create("HudBezel", false, (x, y) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            return dx * dx + dy * dy > 0.25f;
        });

        delegate bool Inside(float x, float y);

        // Concave arrowhead: tip, right barb, notch, left barb (normalised texture coordinates).
        static readonly Vector2[] ArrowShape =
        {
            new Vector2(0.5f, 0.94f), new Vector2(0.86f, 0.1f), new Vector2(0.5f, 0.32f), new Vector2(0.14f, 0.1f),
        };

        static bool InsideArrow(float x, float y)
        {
            // Even-odd rule.
            bool inside = false;
            for (int i = 0, j = ArrowShape.Length - 1; i < ArrowShape.Length; j = i++)
            {
                Vector2 a = ArrowShape[i], b = ArrowShape[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static Sprite Create(string name, bool sliced, Inside inside)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[Size * Size];
            const float step = 1f / (Size * Supersample);
            for (int py = 0; py < Size; py++)
            for (int px = 0; px < Size; px++)
            {
                int hits = 0;
                for (int sy = 0; sy < Supersample; sy++)
                for (int sx = 0; sx < Supersample; sx++)
                {
                    float x = (px * Supersample + sx + 0.5f) * step;
                    float y = (py * Supersample + sy + 0.5f) * step;
                    if (inside(x, y)) hits++;
                }
                byte alpha = (byte)Mathf.RoundToInt(255f * hits / (Supersample * Supersample));
                pixels[py * Size + px] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            float border = sliced ? Size * 0.3f + 2f : 0f;
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
