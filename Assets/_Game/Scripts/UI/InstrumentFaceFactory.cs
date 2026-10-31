using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Builds instrument faces in code at 400 × 400 units: the same faces go on
    /// the cockpit panel (world-space canvas, scaled down) and into the focus view
    /// (scaled up to fit). Placeholder art: flat shapes and the built-in font; see
    /// docs/COCKPIT.md for the visual assets still to make.
    /// </summary>
    public static class InstrumentFaceFactory
    {
        static readonly Color Face = new Color(0.07f, 0.07f, 0.08f, 1f);
        static readonly Color Bezel = new Color(0.22f, 0.22f, 0.24f, 1f);
        static readonly Color Marking = new Color(0.93f, 0.93f, 0.9f, 1f);
        static readonly Color Needle = new Color(1f, 1f, 1f, 1f);
        static readonly Color Pointer = new Color(1f, 0.6f, 0.1f, 1f);
        static readonly Color Window = new Color(0f, 0f, 0f, 1f);
        static readonly Color Sky = new Color(0.22f, 0.5f, 0.85f, 1f);
        static readonly Color Ground = new Color(0.47f, 0.31f, 0.16f, 1f);
        static readonly Color MapBackground = new Color(0.04f, 0.06f, 0.08f, 1f);

        sealed class Parts
        {
            public readonly List<Image> Discs = new List<Image>();
            public readonly List<Image> Rings = new List<Image>();
            public readonly List<Image> Bezels = new List<Image>();
            public readonly List<Image> Arrows = new List<Image>();
            public readonly List<Image> Rounded = new List<Image>();
            public Font Font;
        }

        /// <summary>Builds a face centred in <paramref name="parent"/>. Its root is 400 × 400 units; scale it to size.</summary>
        public static InstrumentFace Build(InstrumentKind kind, RectTransform parent, Font font)
        {
            var root = NewRect(kind + "Instrument", parent);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(InstrumentFace.DesignSize, InstrumentFace.DesignSize);
            var face = root.gameObject.AddComponent<InstrumentFace>();
            face.kind = kind;
            var parts = new Parts { Font = font };

            switch (kind)
            {
                case InstrumentKind.Airspeed: BuildAirspeed(root, face, parts); break;
                case InstrumentKind.Attitude: BuildAttitude(root, face, parts); break;
                case InstrumentKind.Altimeter: BuildAltimeter(root, face, parts); break;
                case InstrumentKind.Heading: BuildHeading(root, face, parts); break;
                case InstrumentKind.VerticalSpeed: BuildVerticalSpeed(root, face, parts); break;
                default: BuildNavigation(root, face, parts); break;
            }

            face.discs = parts.Discs.ToArray();
            face.rings = parts.Rings.ToArray();
            face.bezels = parts.Bezels.ToArray();
            face.arrows = parts.Arrows.ToArray();
            face.roundedRects = parts.Rounded.ToArray();
            face.ConfigureAirspeed(AirspeedMarkings.Default);
            return face;
        }

        // ---- Faces ------------------------------------------------------------------------

        static void BuildAirspeed(RectTransform root, InstrumentFace face, Parts p)
        {
            DialBackground(root, p);
            face.whiteArc = Arc("FlapArc", root, p, 336f, new Color(1f, 1f, 1f, 0.95f));
            face.greenArc = Arc("NormalArc", root, p, 372f, new Color(0.2f, 0.8f, 0.3f, 1f));
            face.yellowArc = Arc("CautionArc", root, p, 372f, new Color(0.95f, 0.8f, 0.15f, 1f));
            face.redLine = Plain("NeverExceed", root, new Vector2(7f, 30f), new Color(0.9f, 0.1f, 0.1f, 1f));

            for (int knots = 0; knots <= 200; knots += 10)
            {
                if (knots < 40 && knots % 20 != 0) continue;
                bool major = knots % 20 == 0;
                Tick(root, InstrumentMath.AirspeedAngle(knots), 192f, major ? 24f : 13f, major ? 5f : 3f, Marking);
                if (major && knots >= 40) Label(root, p, InstrumentMath.AirspeedAngle(knots), 140f, knots.ToString(), 34);
            }
            Caption(root, p, "AIRSPEED", new Vector2(0f, 68f), 20);
            Caption(root, p, "KNOTS", new Vector2(0f, 42f), 18);
            face.digital = DigitalWindow(root, p, new Vector2(0f, -66f), 120f);
            face.needle = NeedleShape("Needle", root, 168f, 9f, 0.14f, Needle);
            Cap(root, p);
            Rim(root, p);
        }

        static void BuildAltimeter(RectTransform root, InstrumentFace face, Parts p)
        {
            DialBackground(root, p);
            for (int i = 0; i < 50; i++)
            {
                bool major = i % 5 == 0;
                Tick(root, i * 7.2f, 192f, major ? 24f : 12f, major ? 5f : 3f, Marking);
            }
            for (int digit = 0; digit < 10; digit++) Label(root, p, digit * 36f, 145f, digit.ToString(), 40);
            Caption(root, p, "ALT", new Vector2(0f, 70f), 22);
            Caption(root, p, "FEET", new Vector2(0f, 44f), 18);
            face.digital = DigitalWindow(root, p, new Vector2(0f, -66f), 140f);
            face.shortNeedle = NeedleShape("Thousands", root, 105f, 18f, 0.12f, Needle);
            face.needle = NeedleShape("Hundreds", root, 172f, 9f, 0.14f, Needle);
            Cap(root, p);
            Rim(root, p);
        }

        static void BuildVerticalSpeed(RectTransform root, InstrumentFace face, Parts p)
        {
            DialBackground(root, p);
            for (int fpm = -2000; fpm <= 2000; fpm += 100)
            {
                bool major = fpm % 500 == 0;
                Tick(root, InstrumentMath.VerticalSpeedAngle(fpm), 192f, major ? 24f : 12f, major ? 5f : 3f, Marking);
                if (major) Label(root, p, InstrumentMath.VerticalSpeedAngle(fpm), 142f, (Mathf.Abs(fpm) / 100).ToString(), 34);
            }
            Caption(root, p, "UP", new Vector2(-78f, 70f), 22);
            Caption(root, p, "DN", new Vector2(-78f, -70f), 22);
            Caption(root, p, "VERT SPEED", new Vector2(40f, 40f), 18);
            Caption(root, p, "100 FT/MIN", new Vector2(40f, 18f), 16);
            face.digital = DigitalWindow(root, p, new Vector2(30f, -66f), 120f);
            face.needle = NeedleShape("Needle", root, 168f, 9f, 0.14f, Needle);
            Cap(root, p);
            Rim(root, p);
        }

        static void BuildHeading(RectTransform root, InstrumentFace face, Parts p)
        {
            DialBackground(root, p);
            var card = NewRect("Card", root);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(400f, 400f);
            face.card = card;
            string[] names = { "N", "3", "6", "E", "12", "15", "S", "21", "24", "W", "30", "33" };
            for (int degrees = 0; degrees < 360; degrees += 5)
            {
                bool ten = degrees % 10 == 0, thirty = degrees % 30 == 0;
                Tick(card, degrees, 192f, thirty ? 26f : (ten ? 18f : 10f), ten ? 4f : 2.5f, Marking);
                if (thirty) Label(card, p, degrees, 145f, names[degrees / 30], degrees % 90 == 0 ? 44 : 34, rotateWithDial: true);
            }
            Plain("LubberLine", root, new Vector2(6f, 32f), Pointer).anchoredPosition = new Vector2(0f, 172f);
            AircraftSymbol(root);
            face.digital = DigitalWindow(root, p, new Vector2(0f, -100f), 110f);
            Rim(root, p);
        }

        static void BuildAttitude(RectTransform root, InstrumentFace face, Parts p)
        {
            DialBackground(root, p);
            var window = NewRect("Window", root);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(300f, 300f);
            window.gameObject.AddComponent<RectMask2D>();

            var roll = NewRect("RollCard", window);
            roll.anchorMin = roll.anchorMax = roll.pivot = new Vector2(0.5f, 0.5f);
            roll.sizeDelta = new Vector2(300f, 300f);
            face.card = roll;

            var pitch = NewRect("PitchCard", roll);
            pitch.anchorMin = pitch.anchorMax = pitch.pivot = new Vector2(0.5f, 0.5f);
            pitch.sizeDelta = new Vector2(10f, 10f);
            face.pitchCard = pitch;
            float ppd = face.pixelsPerDegree;
            Plain("Sky", pitch, new Vector2(1400f, 1400f), Sky).anchoredPosition = new Vector2(0f, 700f);
            Plain("Ground", pitch, new Vector2(1400f, 1400f), Ground).anchoredPosition = new Vector2(0f, -700f);
            Plain("Horizon", pitch, new Vector2(1400f, 4f), Marking);
            for (int degrees = -30; degrees <= 30; degrees += 5)
            {
                if (degrees == 0) continue;
                bool ten = degrees % 10 == 0;
                Plain("Ladder" + degrees, pitch, new Vector2(ten ? 110f : 50f, 3f), Marking).anchoredPosition = new Vector2(0f, degrees * ppd);
                if (!ten) continue;
                string text = Mathf.Abs(degrees).ToString();
                LabelAt(pitch, p, new Vector2(-80f, degrees * ppd), text, 22);
                LabelAt(pitch, p, new Vector2(80f, degrees * ppd), text, 22);
            }
            var pointer = Plain("BankPointer", roll, new Vector2(26f, 22f), Pointer);
            pointer.anchoredPosition = new Vector2(0f, 128f);
            p.Arrows.Add(pointer.GetComponent<Image>());

            var bezel = Plain("RoundWindow", root, new Vector2(302f, 302f), Face);
            p.Bezels.Add(bezel.GetComponent<Image>());

            // Bank scale (fixed, drawn over the window's corners): 0, 10, 20, 30, 45, 60 degrees either side.
            float[] bank = { 0f, 10f, 20f, 30f, 45f, 60f };
            foreach (float b in bank)
            {
                bool major = b == 0f || b == 30f || b == 60f;
                Tick(root, b, 190f, major ? 26f : 16f, major ? 5f : 3f, Marking);
                if (b > 0f) Tick(root, -b, 190f, major ? 26f : 16f, major ? 5f : 3f, Marking);
            }

            // Fixed aircraft symbol.
            Plain("LeftWing", root, new Vector2(80f, 8f), Pointer).anchoredPosition = new Vector2(-80f, 0f);
            Plain("RightWing", root, new Vector2(80f, 8f), Pointer).anchoredPosition = new Vector2(80f, 0f);
            var dot = Plain("Centre", root, new Vector2(14f, 14f), Pointer);
            p.Discs.Add(dot.GetComponent<Image>());
            Rim(root, p);
        }

        static void BuildNavigation(RectTransform root, InstrumentFace face, Parts p)
        {
            var background = Plain("Screen", root, new Vector2(400f, 400f), MapBackground);
            p.Rounded.Add(background.GetComponent<Image>());

            var map = NewRect("Map", root);
            map.anchorMin = map.anchorMax = map.pivot = new Vector2(0.5f, 0.5f);
            map.anchoredPosition = new Vector2(0f, -40f);
            map.sizeDelta = new Vector2(380f, 300f);
            map.gameObject.AddComponent<RectMask2D>();
            face.mapOwnship = new Vector2(0f, -100f);
            face.mapRadius = 190f;

            var ring = Plain("RangeRing", map, new Vector2(380f, 380f), new Color(0.5f, 0.75f, 1f, 0.45f));
            ring.anchoredPosition = face.mapOwnship;
            p.Rings.Add(ring.GetComponent<Image>());
            var runway = Plain("Runway", map, new Vector2(8f, 40f), face.mapRunwayColour);
            face.mapRunway = runway;
            face.mapRunwayImage = runway.GetComponent<Image>();
            var ownship = Plain("Aircraft", map, new Vector2(30f, 30f), Pointer);
            ownship.anchoredPosition = face.mapOwnship;
            p.Arrows.Add(ownship.GetComponent<Image>());
            face.mapRange = LabelAt(map, p, new Vector2(140f, 60f), "-", 20);

            face.mapRunwayLabel = TextAt(root, p, new Vector2(-185f, 182f), new Vector2(170f, 40f), "-", 32, TextAnchor.UpperLeft);
            face.mapDistance = TextAt(root, p, new Vector2(15f, 182f), new Vector2(170f, 40f), "-", 32, TextAnchor.UpperRight);
            TextAt(root, p, new Vector2(-185f, 140f), new Vector2(60f, 30f), "BRG", 18, TextAnchor.UpperLeft);
            face.mapBearing = TextAt(root, p, new Vector2(-135f, 142f), new Vector2(70f, 30f), "-", 24, TextAnchor.UpperLeft);
            TextAt(root, p, new Vector2(-55f, 140f), new Vector2(40f, 30f), "GS", 18, TextAnchor.UpperLeft);
            face.mapGroundspeed = TextAt(root, p, new Vector2(-20f, 142f), new Vector2(60f, 30f), "-", 24, TextAnchor.UpperLeft);
            TextAt(root, p, new Vector2(60f, 140f), new Vector2(50f, 30f), "TRK", 18, TextAnchor.UpperLeft);
            face.mapTrack = TextAt(root, p, new Vector2(110f, 142f), new Vector2(70f, 30f), "-", 24, TextAnchor.UpperLeft);
        }

        // ---- Building blocks --------------------------------------------------------------

        static void DialBackground(RectTransform root, Parts p)
        {
            var disc = Plain("Face", root, new Vector2(400f, 400f), Face);
            p.Discs.Add(disc.GetComponent<Image>());
        }

        static void Rim(RectTransform root, Parts p)
        {
            var rim = Plain("Rim", root, new Vector2(406f, 406f), Bezel);
            p.Rings.Add(rim.GetComponent<Image>());
        }

        static void Cap(RectTransform root, Parts p)
        {
            var cap = Plain("Cap", root, new Vector2(30f, 30f), new Color(0.3f, 0.3f, 0.3f, 1f));
            p.Discs.Add(cap.GetComponent<Image>());
        }

        static Image Arc(string name, RectTransform root, Parts p, float diameter, Color colour)
        {
            var rect = Plain(name, root, new Vector2(diameter, diameter), colour);
            var image = rect.GetComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0f;
            return image;
        }

        /// <summary>A tick mark on a dial, pointing at the centre.</summary>
        static void Tick(RectTransform parent, float angle, float outerRadius, float length, float width, Color colour)
        {
            var tick = Plain("Tick", parent, new Vector2(width, length), colour);
            tick.anchoredPosition = InstrumentMath.DialPoint(angle, outerRadius - length * 0.5f);
            tick.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }

        /// <summary>A number on a dial, upright (or turning with a card when <paramref name="rotateWithDial"/>).</summary>
        static void Label(RectTransform parent, Parts p, float angle, float radius, string text, int size, bool rotateWithDial = false)
        {
            var label = LabelAt(parent, p, InstrumentMath.DialPoint(angle, radius), text, size);
            if (rotateWithDial) label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }

        static Text LabelAt(RectTransform parent, Parts p, Vector2 position, string text, int size)
        {
            var label = NewText("Label", parent, p.Font, size, TextAnchor.MiddleCenter, Marking);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = new Vector2(90f, size + 8f);
            label.text = text;
            return label;
        }

        static void Caption(RectTransform parent, Parts p, string text, Vector2 position, int size)
        {
            var caption = LabelAt(parent, p, position, text, size);
            caption.rectTransform.sizeDelta = new Vector2(220f, size + 8f);
            caption.color = new Color(0.8f, 0.8f, 0.78f, 1f);
        }

        static Text TextAt(RectTransform parent, Parts p, Vector2 topLeft, Vector2 size, string text, int fontSize, TextAnchor anchor)
        {
            var label = NewText("Text", parent, p.Font, fontSize, anchor, Marking);
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
            label.text = text;
            return label;
        }

        /// <summary>A dark window with large digits: readable on a phone where a needle alone is not.</summary>
        static Text DigitalWindow(RectTransform parent, Parts p, Vector2 position, float width)
        {
            var window = Plain("DigitalWindow", parent, new Vector2(width, 48f), Window);
            window.anchoredPosition = position;
            p.Rounded.Add(window.GetComponent<Image>());
            var digits = NewText("Digits", window, p.Font, 36, TextAnchor.MiddleCenter, Marking);
            var rect = digits.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            digits.text = "-";
            return digits;
        }

        /// <summary>A needle pivoting at the dial centre; <paramref name="tail"/> is the part behind the pivot.</summary>
        static RectTransform NeedleShape(string name, RectTransform parent, float length, float width, float tail, Color colour)
        {
            var needle = Plain(name, parent, new Vector2(width, length * (1f + tail)), colour);
            needle.pivot = new Vector2(0.5f, tail / (1f + tail));
            needle.anchoredPosition = Vector2.zero;
            return needle;
        }

        static void AircraftSymbol(RectTransform parent)
        {
            Plain("Fuselage", parent, new Vector2(8f, 80f), Pointer).anchoredPosition = new Vector2(0f, 0f);
            Plain("Wings", parent, new Vector2(96f, 9f), Pointer).anchoredPosition = new Vector2(0f, 12f);
            Plain("Tail", parent, new Vector2(38f, 7f), Pointer).anchoredPosition = new Vector2(0f, -32f);
        }

        static RectTransform Plain(string name, RectTransform parent, Vector2 size, Color colour)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return rect;
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
    }
}
