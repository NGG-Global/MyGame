using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// The HUD's symbols (docs/TAP_DISCLOSURE_DESIGN.md): the systems wheel's, each with a label
    /// beside it, and the command cluster's (camera with the view's name, options, pause).
    /// </summary>
    public enum HudIcon
    {
        None = 0,
        ParkingBrake = 1,
        Flaps = 2,
        Switches = 3,
        Approach = 4,
        Navigation = 5,
        Assist = 6,
        Camera = 7,
        More = 8,
        Pause = 9,
    }

    /// <summary>
    /// White anti-aliased sprites generated once at runtime and tinted by Image
    /// colours, so the HUD needs no imported art. (The touch controls have their
    /// own set in the Input layer; layers do not share code.)
    /// </summary>
    public static class HudSprites
    {
        const int Size = 128;
        const int Supersample = 4;
        /// <summary>Corner radius of <see cref="RoundedRect"/> in texels (canvas units at pixels-per-unit multiplier 1).</summary>
        public const float RoundedRectRadius = Size * 0.3f;
        static Sprite _disc, _roundedRect, _arrow, _ring, _bezel, _roundedOutline, _diamond, _cautionIcon, _warningIcon;
        const int IconSize = 64;
        const int IconCount = 10;
        static readonly Sprite[] _icons = new Sprite[IconCount];
        static readonly Sprite[] _annuli = new Sprite[4];
        static readonly float[] _annulusRatios = new float[4];
        static int _annulusCount;

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

        /// <summary>Caution symbol: a triangle with an exclamation mark (severity is never shown by colour alone).</summary>
        public static Sprite CautionIcon => _cautionIcon != null ? _cautionIcon : _cautionIcon = Create("HudCautionIcon", false, (x, y) =>
            (InsidePolygon(CautionOuter, x, y) && !InsidePolygon(CautionInner, x, y)) || Exclamation(x, y, 0.36f));

        /// <summary>Warning symbol: an octagon with an exclamation mark.</summary>
        public static Sprite WarningIcon => _warningIcon != null ? _warningIcon : _warningIcon = Create("HudWarningIcon", false, (x, y) =>
        {
            float dx = Mathf.Abs(x - 0.5f), dy = Mathf.Abs(y - 0.5f);
            bool outer = Mathf.Max(dx, dy) <= 0.46f && dx + dy <= 0.65f;
            bool inner = Mathf.Max(dx, dy) <= 0.38f && dx + dy <= 0.54f;
            return (outer && !inner) || Exclamation(x, y, 0.3f);
        });

        static readonly Vector2[] CautionOuter = { new Vector2(0.5f, 0.95f), new Vector2(0.96f, 0.1f), new Vector2(0.04f, 0.1f) };
        static readonly Vector2[] CautionInner = { new Vector2(0.5f, 0.78f), new Vector2(0.83f, 0.18f), new Vector2(0.17f, 0.18f) };

        /// <summary>An exclamation mark: a bar above a dot whose centre is at <paramref name="dotY"/>.</summary>
        static bool Exclamation(float x, float y, float dotY)
        {
            float dx = x - 0.5f;
            bool bar = Mathf.Abs(dx) <= 0.045f && y >= dotY + 0.1f && y <= dotY + 0.36f;
            float dy = y - dotY;
            return bar || dx * dx + dy * dy <= 0.05f * 0.05f;
        }

        static bool InsidePolygon(Vector2[] shape, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = shape.Length - 1; i < shape.Length; j = i++)
            {
                Vector2 a = shape[i], b = shape[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

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

        /// <summary>
        /// A thick ring (annulus) whose hole is <paramref name="innerRatio"/> of the
        /// outer radius, for arc segments drawn with a radial fill. Cached per ratio
        /// (a few distinct values at most).
        /// </summary>
        public static Sprite Annulus(float innerRatio)
        {
            innerRatio = Mathf.Clamp(innerRatio, 0.05f, 0.95f);
            for (int i = 0; i < _annulusCount; i++)
                if (_annuli[i] != null && Mathf.Abs(_annulusRatios[i] - innerRatio) < 1e-3f) return _annuli[i];
            float outer = 0.485f, inner = outer * innerRatio;
            var sprite = Create("HudAnnulus" + Mathf.RoundToInt(innerRatio * 100f), false, (x, y) =>
            {
                float dx = x - 0.5f, dy = y - 0.5f;
                float r2 = dx * dx + dy * dy;
                return r2 <= outer * outer && r2 >= inner * inner;
            });
            int slot = _annulusCount < _annuli.Length ? _annulusCount++ : _annuli.Length - 1;
            _annuli[slot] = sprite;
            _annulusRatios[slot] = innerRatio;
            return sprite;
        }

        /// <summary>Opaque square with a round hole: turns a square window into a round instrument face.</summary>
        public static Sprite Bezel => _bezel != null ? _bezel : _bezel = Create("HudBezel", false, (x, y) =>
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            return dx * dx + dy * dy > 0.25f;
        });

        // ---- Wheel symbols ---------------------------------------------------------------------------

        /// <summary>
        /// A HUD symbol (white, tinted by its Image), generated once at 64 × 64; null for
        /// <see cref="HudIcon.None"/>. Simple, high-contrast shapes: a P in a ring (parking
        /// brake), a wing with its flap lowered, a toggle switch, a runway in perspective, a
        /// compass needle, three sliders (assistance settings); a camera with its lens, three
        /// dots (more options) and two bars (pause).
        /// </summary>
        public static Sprite Icon(HudIcon icon)
        {
            int i = (int)icon;
            if (i <= 0 || i >= IconCount) return null;
            if (_icons[i] != null) return _icons[i];
            var shape = IconShape(icon);
            if (shape == null) return null;
            _icons[i] = Create("HudIcon" + icon, false, shape, IconSize);
            return _icons[i];
        }

        static Inside IconShape(HudIcon icon)
        {
            switch (icon)
            {
                case HudIcon.ParkingBrake: return (x, y) => RingAt(x, y, 0.5f, 0.5f, 0.4f, 0.47f) || LetterP(x, y);
                case HudIcon.Flaps: return Wing;
                case HudIcon.Switches: return Toggle;
                case HudIcon.Approach: return Runway;
                case HudIcon.Navigation: return (x, y) => RingAt(x, y, 0.5f, 0.5f, 0.4f, 0.47f) || Needle(x, y);
                case HudIcon.Assist: return Sliders;
                case HudIcon.Camera: return CameraBody;
                case HudIcon.More: return (x, y) => DiscAt(x, y, 0.2f, 0.5f, 0.09f) || DiscAt(x, y, 0.5f, 0.5f, 0.09f) || DiscAt(x, y, 0.8f, 0.5f, 0.09f);
                case HudIcon.Pause: return (x, y) => y >= 0.24f && y <= 0.76f && ((x >= 0.28f && x <= 0.42f) || (x >= 0.58f && x <= 0.72f));
                default: return null;
            }
        }

        static bool RingAt(float x, float y, float cx, float cy, float inner, float outer)
        {
            float dx = x - cx, dy = y - cy;
            float r2 = dx * dx + dy * dy;
            return r2 <= outer * outer && r2 >= inner * inner;
        }

        static bool DiscAt(float x, float y, float cx, float cy, float radius)
        {
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>Distance from a point to the segment from a to b, in normalised texture units.</summary>
        static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / Mathf.Max(1e-6f, vx * vx + vy * vy));
            float dx = x - (ax + t * vx), dy = y - (ay + t * vy);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>A P: a stem and a bowl that joins it at the top and the middle.</summary>
        static bool LetterP(float x, float y)
        {
            bool stem = x >= 0.35f && x <= 0.44f && y >= 0.25f && y <= 0.75f;
            bool bowl = x >= 0.44f && RingAt(x, y, 0.47f, 0.61f, 0.055f, 0.14f);
            return stem || bowl;
        }

        /// <summary>A wing section with its flap deflected down behind it.</summary>
        static bool Wing(float x, float y) =>
            SegmentDistance(x, y, 0.16f, 0.62f, 0.6f, 0.62f) <= 0.075f
            || SegmentDistance(x, y, 0.72f, 0.56f, 0.86f, 0.34f) <= 0.05f;

        /// <summary>A toggle switch: a rounded track with its knob on the right.</summary>
        static bool Toggle(float x, float y)
        {
            float d = SegmentDistance(x, y, 0.3f, 0.5f, 0.7f, 0.5f);
            return (d <= 0.2f && d >= 0.14f) || DiscAt(x, y, 0.7f, 0.5f, 0.11f);
        }

        /// <summary>A runway seen on approach: converging edges, a threshold bar and centreline dashes.</summary>
        static bool Runway(float x, float y)
        {
            if (y < 0.1f || y > 0.9f) return false;
            float t = (y - 0.1f) / 0.8f;
            float half = Mathf.Lerp(0.34f, 0.09f, t);
            float across = Mathf.Abs(x - 0.5f);
            bool edges = Mathf.Abs(across - half) <= 0.035f;
            bool threshold = y <= 0.15f && across <= half;
            bool dash = y > 0.22f && across <= Mathf.Lerp(0.03f, 0.012f, t) && Mathf.Repeat(t * 3.5f, 1f) < 0.5f;
            return edges || threshold || dash;
        }

        /// <summary>A compass needle pointing to the upper right.</summary>
        static bool Needle(float x, float y)
        {
            float px = x - 0.5f, py = y - 0.5f;
            float along = (px + py) * 0.70710678f, across = (py - px) * 0.70710678f;
            return Mathf.Abs(along) / 0.3f + Mathf.Abs(across) / 0.1f <= 1f;
        }

        /// <summary>A camera: a rounded body with a viewfinder on top and a lens (a ring cut out of the body).</summary>
        static bool CameraBody(float x, float y)
        {
            bool body = RoundedBox(x, y, 0.1f, 0.2f, 0.9f, 0.72f, 0.08f);
            bool finder = x >= 0.32f && x <= 0.56f && y >= 0.7f && y <= 0.82f;
            float dx = x - 0.5f, dy = y - 0.46f;
            float r2 = dx * dx + dy * dy;
            bool lensGap = r2 <= 0.18f * 0.18f && r2 >= 0.11f * 0.11f;
            return (body || finder) && !lensGap;
        }

        /// <summary>A box with rounded corners (normalised texture units).</summary>
        static bool RoundedBox(float x, float y, float xMin, float yMin, float xMax, float yMax, float radius)
        {
            float cx = Mathf.Clamp(x, xMin + radius, xMax - radius), cy = Mathf.Clamp(y, yMin + radius, yMax - radius);
            float dx = x - cx, dy = y - cy;
            return x >= xMin && x <= xMax && y >= yMin && y <= yMax && dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>Three sliders at different settings.</summary>
        static bool Sliders(float x, float y)
        {
            for (int i = 0; i < 3; i++)
            {
                float line = 0.26f + 0.24f * i;
                float knob = i == 0 ? 0.62f : (i == 1 ? 0.36f : 0.54f);
                if (x >= 0.14f && x <= 0.86f && Mathf.Abs(y - line) <= 0.028f) return true;
                if (DiscAt(x, y, knob, line, 0.085f)) return true;
            }
            return false;
        }

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

        static Sprite Create(string name, bool sliced, Inside inside) => Create(name, sliced, inside, Size);

        static Sprite Create(string name, bool sliced, Inside inside, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[size * size];
            float step = 1f / (size * Supersample);
            for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
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
                pixels[py * size + px] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            float border = sliced ? size * 0.3f + 2f : 0f;
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
