using OpenSkies.Awareness;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// One flight instrument face (built by <see cref="InstrumentFaceFactory"/> at
    /// 400 × 400 units). The same design is used on the cockpit panel (world
    /// space) and, enlarged, in the focus view. <see cref="Render"/> only places
    /// needles, cards and text from <see cref="InstrumentReadings"/>; text changes
    /// only when the rounded value changes and is cached, so rendering allocates
    /// nothing.
    /// </summary>
    public sealed class InstrumentFace : MonoBehaviour
    {
        public const float DesignSize = 400f;

        public InstrumentKind kind;

        [Header("Moving parts")]
        [Tooltip("Airspeed, vertical speed, altimeter (hundreds of feet).")]
        public RectTransform needle;
        [Tooltip("Altimeter (thousands of feet).")]
        public RectTransform shortNeedle;
        [Tooltip("Heading card, or the attitude indicator's roll card.")]
        public RectTransform card;
        [Tooltip("Attitude indicator: sky, ground and pitch ladder.")]
        public RectTransform pitchCard;
        public float pixelsPerDegree = 6f;
        public float pitchLimitDegrees = 35f;
        [Tooltip("Digital readout window: easier to read on a phone than a needle alone.")]
        public Text digital;

        [Header("Airspeed markings")]
        public Image whiteArc;
        public Image greenArc;
        public Image yellowArc;
        public RectTransform redLine;

        [Header("Navigation display")]
        public RectTransform mapRunway;
        public Image mapRunwayImage;
        public Vector2 mapOwnship = new Vector2(0f, -100f);
        public float mapRadius = 190f;
        public Text mapRunwayLabel;
        public Text mapDistance;
        public Text mapBearing;
        public Text mapGroundspeed;
        public Text mapTrack;
        public Text mapRange;
        public Color mapRunwayColour = new Color(1f, 1f, 1f, 0.95f);
        public Color mapRunwayOffScaleColour = new Color(1f, 0.75f, 0.2f, 0.9f);

        [Header("Generated sprites (assigned at runtime; they cannot be saved in a scene)")]
        public Image[] discs = new Image[0];
        public Image[] rings = new Image[0];
        public Image[] bezels = new Image[0];
        public Image[] arrows = new Image[0];
        public Image[] roundedRects = new Image[0];

        const int NoValue = int.MinValue;
        static readonly NumberStrings Integers = new NumberStrings(0, 999, v => v.ToString());
        static readonly NumberStrings Altitudes = new NumberStrings(-100, 6000, v => (v * 10).ToString());
        static readonly NumberStrings VerticalSpeeds = new NumberStrings(-200, 200, v => NumberStrings.Signed(v * 50));
        static readonly NumberStrings Headings = new NumberStrings(1, 360, NumberStrings.Heading);
        static readonly NumberStrings NauticalMiles = new NumberStrings(0, 999, v => NumberStrings.Tenths(v) + " NM");
        static readonly NumberStrings Runways = new NumberStrings(1, 36, v => "RWY " + v.ToString("00"));
        static readonly NumberStrings Ranges = new NumberStrings(0, 100, v => v.ToString() + " NM");

        int _shownDigital = NoValue, _shownDistance = NoValue, _shownBearing = NoValue, _shownGroundspeed = NoValue;
        int _shownTrack = NoValue, _shownRunway = NoValue, _shownRange = NoValue, _shownRunwayLength = NoValue, _shownInRange = NoValue;
        float _rangeNm;
        bool _spritesAssigned;

        /// <summary>Times this face has been rendered (tests use it to prove a hidden face is no longer drawn).</summary>
        public int RenderCount { get; private set; }

        void Awake() => AssignSprites();

        public void AssignSprites()
        {
            if (_spritesAssigned) return;
            _spritesAssigned = true;
            Assign(discs, HudSprites.Disc, Image.Type.Simple);
            Assign(rings, HudSprites.Ring, Image.Type.Simple);
            Assign(bezels, HudSprites.Bezel, Image.Type.Simple);
            Assign(arrows, HudSprites.Arrow, Image.Type.Simple);
            Assign(roundedRects, HudSprites.RoundedRect, Image.Type.Sliced);
            // Arcs keep their Filled type; they only need the ring sprite.
            AssignKeepType(whiteArc, HudSprites.Ring);
            AssignKeepType(greenArc, HudSprites.Ring);
            AssignKeepType(yellowArc, HudSprites.Ring);
        }

        static void Assign(Image[] images, Sprite sprite, Image.Type type)
        {
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                if (image == null || image.sprite != null) continue;
                image.sprite = sprite;
                image.type = type;
            }
        }

        static void AssignKeepType(Image image, Sprite sprite)
        {
            if (image != null && image.sprite == null) image.sprite = sprite;
        }

        /// <summary>Places the airspeed arcs and red line. Call once; the markings come from the aircraft data.</summary>
        public void ConfigureAirspeed(in AirspeedMarkings m)
        {
            SetArc(whiteArc, m.FlapStall, m.FlapExtended);
            SetArc(greenArc, m.CleanStall, m.NormalOperating);
            SetArc(yellowArc, m.NormalOperating, m.NeverExceed);
            if (redLine != null)
            {
                float angle = InstrumentMath.AirspeedAngle(m.NeverExceed);
                redLine.anchoredPosition = InstrumentMath.DialPoint(angle, 176f);
                redLine.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
        }

        static void SetArc(Image arc, float fromKnots, float toKnots)
        {
            if (arc == null) return;
            float start = InstrumentMath.AirspeedAngle(fromKnots);
            float end = InstrumentMath.AirspeedAngle(toKnots);
            arc.fillAmount = Mathf.Clamp01((end - start) / 360f);
            arc.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -start);
        }

        public void Render(in InstrumentReadings r)
        {
            RenderCount++;
            switch (kind)
            {
                case InstrumentKind.Airspeed:
                    Rotate(needle, InstrumentMath.AirspeedAngle(r.AirspeedKnots));
                    ShowDigital(Mathf.Clamp(Mathf.RoundToInt(r.AirspeedKnots), 0, 999), Integers);
                    break;
                case InstrumentKind.Altimeter:
                    Rotate(needle, InstrumentMath.AltimeterHundredsAngle(r.AltitudeFeet));
                    Rotate(shortNeedle, InstrumentMath.AltimeterThousandsAngle(r.AltitudeFeet));
                    ShowDigital(Mathf.RoundToInt(Mathf.Clamp(r.AltitudeFeet, -99999f, 99999f) / 10f), Altitudes);
                    break;
                case InstrumentKind.VerticalSpeed:
                    Rotate(needle, InstrumentMath.VerticalSpeedAngle(r.VerticalSpeedFeetPerMinute));
                    ShowDigital(Mathf.RoundToInt(Mathf.Clamp(r.VerticalSpeedFeetPerMinute, -99999f, 99999f) / 50f), VerticalSpeeds);
                    break;
                case InstrumentKind.Heading:
                    if (card != null) card.localRotation = Quaternion.Euler(0f, 0f, InstrumentMath.HeadingCardRotation(r.HeadingDegrees));
                    ShowDigital(CompassValue(r.HeadingDegrees), Headings);
                    break;
                case InstrumentKind.Attitude:
                    if (card != null) card.localRotation = Quaternion.Euler(0f, 0f, InstrumentMath.AttitudeRoll(r.BankDegrees));
                    if (pitchCard != null)
                        pitchCard.anchoredPosition = new Vector2(0f, InstrumentMath.AttitudePitchOffset(r.PitchDegrees, pixelsPerDegree, pitchLimitDegrees));
                    break;
                case InstrumentKind.Navigation:
                    RenderMap(r);
                    break;
            }
        }

        static void Rotate(RectTransform part, float clockwiseDegrees)
        {
            if (part != null) part.localRotation = Quaternion.Euler(0f, 0f, -clockwiseDegrees);
        }

        static int CompassValue(float degrees)
        {
            int value = Mathf.RoundToInt(SoftLogic.Finite(degrees, 0f)) % 360;
            return value <= 0 ? value + 360 : value; // compass cards show north as 360
        }

        void ShowDigital(int value, NumberStrings strings)
        {
            if (digital == null || value == _shownDigital) return;
            _shownDigital = value;
            digital.text = strings.Get(value);
        }

        void RenderMap(in InstrumentReadings r)
        {
            if (!r.HasRunway)
            {
                if (mapRunway != null && mapRunway.gameObject.activeSelf) mapRunway.gameObject.SetActive(false);
                return;
            }
            if (mapRunway != null && !mapRunway.gameObject.activeSelf) mapRunway.gameObject.SetActive(true);

            float distanceNm = r.RunwayDistanceMetres / 1852f;
            _rangeNm = InstrumentMath.MapRangeNm(distanceNm, _rangeNm);
            float rangeMetres = _rangeNm * 1852f;

            if (mapRunway != null)
            {
                mapRunway.anchoredPosition = mapOwnship + InstrumentMath.MapPosition(r.RunwayBearingDegrees, r.HeadingDegrees,
                    r.RunwayDistanceMetres, rangeMetres, mapRadius, out bool inRange);
                mapRunway.localRotation = Quaternion.Euler(0f, 0f, -Mathf.DeltaAngle(r.HeadingDegrees, r.RunwayHeadingDegrees));
                int length = Mathf.RoundToInt(Mathf.Clamp(r.RunwayLengthMetres / rangeMetres * mapRadius, 10f, 120f));
                if (length != _shownRunwayLength)
                {
                    _shownRunwayLength = length;
                    mapRunway.sizeDelta = new Vector2(8f, length);
                }
                int state = inRange ? 1 : 0;
                if (mapRunwayImage != null && state != _shownInRange)
                {
                    _shownInRange = state;
                    mapRunwayImage.color = inRange ? mapRunwayColour : mapRunwayOffScaleColour;
                }
            }

            SetCached(mapRunwayLabel, Mathf.Clamp(r.RunwayDesignator, 1, 36), ref _shownRunway, Runways);
            SetCached(mapDistance, Mathf.Clamp(Mathf.RoundToInt(distanceNm * 10f), 0, 9999), ref _shownDistance, NauticalMiles);
            SetCached(mapBearing, CompassValue(r.RunwayBearingDegrees), ref _shownBearing, Headings);
            SetCached(mapGroundspeed, Mathf.Clamp(Mathf.RoundToInt(r.GroundspeedKnots), 0, 999), ref _shownGroundspeed, Integers);
            SetCached(mapTrack, CompassValue(r.TrackDegrees), ref _shownTrack, Headings);
            SetCached(mapRange, Mathf.RoundToInt(_rangeNm), ref _shownRange, Ranges);
        }

        static void SetCached(Text text, int value, ref int shown, NumberStrings strings)
        {
            if (text == null || value == shown) return;
            shown = value;
            text.text = strings.Get(value);
        }
    }
}
