using UnityEngine;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// Anti-aliased white sprites generated once at runtime and tinted by Image
    /// colours, so the touch controls need no imported art.
    /// </summary>
    public static class TouchControlSprites
    {
        const int Size = 128;
        static Sprite _disc, _ring, _roundedRect, _roundedOutline;

        /// <summary>Filled circle with a soft edge.</summary>
        public static Sprite Disc => _disc != null ? _disc : _disc = Create("TouchDisc", (x, y) => Coverage(Radial(x, y), 0.5f - 1.5f / Size, 1f / Size));

        /// <summary>Circle outline.</summary>
        public static Sprite Ring => _ring != null ? _ring : _ring = Create("TouchRing", (x, y) =>
        {
            float r = Radial(x, y);
            return Coverage(r, 0.5f - 1.5f / Size, 1f / Size) * (1f - Coverage(r, 0.5f - 6f / Size, 1f / Size));
        });

        /// <summary>Rounded rectangle, 9-sliced.</summary>
        public static Sprite RoundedRect => _roundedRect != null ? _roundedRect : _roundedRect = CreateSliced("TouchRoundedRect", false);

        /// <summary>Rounded rectangle outline, 9-sliced.</summary>
        public static Sprite RoundedOutline => _roundedOutline != null ? _roundedOutline : _roundedOutline = CreateSliced("TouchRoundedOutline", true);

        delegate float Alpha(float x, float y);

        static float Radial(float x, float y)
        {
            float dx = (x + 0.5f) / Size - 0.5f, dy = (y + 0.5f) / Size - 0.5f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>1 inside the edge, 0 outside, smooth over <paramref name="feather"/>.</summary>
        static float Coverage(float distance, float edge, float feather) => Mathf.Clamp01((edge - distance) / feather + 0.5f);

        static Texture2D NewTexture(string name)
        {
            return new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
        }

        static Sprite Create(string name, Alpha alpha)
        {
            var texture = NewTexture(name);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(x, y) * 255f));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        static Sprite CreateSliced(string name, bool outline)
        {
            const float corner = 40f;     // corner radius in texels
            const float stroke = 4f;      // outline width in texels
            var texture = NewTexture(name);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = RoundedRectDistance(x + 0.5f, y + 0.5f, corner);
                float a = Mathf.Clamp01(0.5f - d);                                      // inside the shape
                if (outline) a *= 1f - Mathf.Clamp01(0.5f - (d + stroke));             // minus the inner area
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = corner + 2f;
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>Signed distance (texels) to a rounded rectangle filling the texture; negative inside.</summary>
        static float RoundedRectDistance(float x, float y, float radius)
        {
            float half = Size / 2f;
            float qx = Mathf.Abs(x - half) - (half - radius);
            float qy = Mathf.Abs(y - half) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }
    }
}
