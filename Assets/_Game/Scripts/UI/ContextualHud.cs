using System;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Presents the contextual HUD decided by <see cref="DisclosureController"/>:
    /// flight data, approach and navigation guides, warning banners and the
    /// contextual action rail. It only reads state and raises requests; it never
    /// changes the flight, and it never moves anything: every element has one
    /// fixed home and disclosure only fades it in or out.
    ///
    /// Built in code by <see cref="ContextualHudFactory"/>. Value text is cached
    /// per number, so steady flight allocates nothing.
    /// </summary>
    public sealed class ContextualHud : MonoBehaviour
    {
        /// <summary>Flight data items are the first HUD elements: Airspeed to Groundspeed.</summary>
        public const int DataItemCount = 5;
        public const int WarningSlotCount = FlightWarnings.Count;
        public const int RailSlotCount = ContextActions.Count;

        [Header("Flight data (index = HudElement)")]
        public CanvasGroup[] dataItems = new CanvasGroup[DataItemCount];
        public Text[] dataValues = new Text[DataItemCount];

        [Header("Approach guide")]
        public CanvasGroup approachGuide;
        public Text approachRunway;
        public Text approachDistance;
        public Text approachHeight;
        public Image[] papiLights = new Image[ApproachGuidance.PapiLights];
        [Tooltip("Shows where the extended centreline is: steer towards it.")]
        public RectTransform lateralMarker;
        [Tooltip("Marker travel either side of centre at full deflection, canvas units.")]
        public float lateralHalfTravel = 70f;

        [Header("Navigation guide")]
        public CanvasGroup navigationGuide;
        [Tooltip("Points towards the destination (the airfield or a mission checkpoint) relative to the nose.")]
        public RectTransform navigationArrow;
        public Text navigationDistance;
        [Tooltip("Destination name, e.g. AIRFIELD or LIGHTHOUSE.")]
        public Text navigationLabel;
        [Tooltip("Course to steer to the destination, degrees.")]
        public Text navigationBearing;

        [Header("Warnings, most urgent first")]
        public CanvasGroup[] warningSlots = new CanvasGroup[WarningSlotCount];
        public Image[] warningBackgrounds = new Image[WarningSlotCount];
        public Text[] warningLabels = new Text[WarningSlotCount];
        [Tooltip("Severity symbol on each banner (octagon: warning, triangle: caution), so severity is never colour alone.")]
        public Image[] warningIcons = new Image[WarningSlotCount];

        [Header("Legibility")]
        [Tooltip("Faint plates behind the flight data and guides (rounded at runtime).")]
        public Image[] backings = new Image[0];

        [Header("Action rail and HUD buttons")]
        [Tooltip("Fixed slots, bottom up. Each available action keeps one slot for the whole session.")]
        public HudButton[] railSlots = new HudButton[RailSlotCount];
        [Tooltip("Always visible: opens every available action (manual access to aircraft systems).")]
        public HudButton systemsButton;
        public HudButton profileButton;
        [Tooltip("Opacity groups of SYSTEMS and the HUD profile button (the player's HUD opacity).")]
        public CanvasGroup systemsGroup;
        public CanvasGroup profileGroup;

        [Header("Development")]
        public DisclosureInspector inspector;

        [Header("Look and motion")]
        public HudMotionSettings motion = new HudMotionSettings();
        public MotionPreference motionPreference = MotionPreference.FollowSystem;
        [Tooltip("Opacity of receded (quiet) information in stable flight.")]
        [Range(0.1f, 1f)] public float quietAlpha = 0.55f;
        [Tooltip("Numbers refresh at this interval, s.")]
        public float valueRefreshSeconds = 0.1f;
        public Color valueColor = HudStyle.TextPrimary;
        public Color alertValueColor = HudStyle.AlertValue;
        public Color warningColor = HudStyle.Warning;
        public Color cautionColor = HudStyle.Caution;
        public Color warningTextColor = Color.white;
        public Color cautionTextColor = HudStyle.TextOnAccent;
        public Color papiWhite = new Color(1f, 1f, 1f, 0.95f);
        public Color papiRed = new Color(0.92f, 0.16f, 0.12f, 0.95f);
        public Color papiOff = new Color(1f, 1f, 1f, 0.2f);

        public event Action<ContextAction> ActionRequested;
        public event Action SystemsToggleRequested;
        public event Action ProfileCycleRequested;

        const int NoValue = int.MinValue;

        static readonly NumberStrings Integers = new NumberStrings(0, 999, v => v.ToString());
        static readonly NumberStrings Altitudes = new NumberStrings(-100, 6000, v => (v * 10).ToString());     // tens of feet
        static readonly NumberStrings VerticalSpeeds = new NumberStrings(-200, 200, v => NumberStrings.Signed(v * 50)); // 50 fpm steps
        static readonly NumberStrings Headings = new NumberStrings(1, 360, NumberStrings.Heading);
        static readonly NumberStrings NauticalMiles = new NumberStrings(0, 999, v => NumberStrings.Tenths(v) + " NM");
        static readonly NumberStrings HeightsFeet = new NumberStrings(0, 500, v => (v * 10).ToString() + " FT");  // tens of feet
        static readonly NumberStrings Runways = new NumberStrings(1, 36, v => "RWY " + v.ToString("00"));
        static readonly NumberStrings Bearings = new NumberStrings(1, 360, v => NumberStrings.Heading(v) + "°");

        HudFade[] _dataFades;
        HudFade _approachFade, _navigationFade;
        HudFade[] _warningFades;
        HudFade[] _railFades;
        readonly ContextAction[] _slotAction = new ContextAction[RailSlotCount];
        readonly bool[] _slotUsed = new bool[RailSlotCount];
        readonly int[] _shownValue = new int[DataItemCount];
        readonly int[] _shownAlert = new int[DataItemCount];
        readonly int[] _slotWarning = new int[WarningSlotCount];
        int _shownRunway = NoValue, _shownApproachDistance = NoValue, _shownApproachHeight = NoValue, _shownNavDistance = NoValue;
        int _shownPapi = NoValue, _shownLateral = NoValue, _shownArrow = NoValue, _shownBearing = NoValue;
        HudProfile _shownProfile = (HudProfile)(-1);
        float _nextValueRefresh;
        float _opacity = 1f;
        bool _initialized, _actionsConfigured, _reducedMotion;

        public bool ReducedMotion => _reducedMotion;
        /// <summary>The player's HUD opacity (routine information and buttons; warnings are never dimmed).</summary>
        public float Opacity => _opacity;

        /// <summary>The action shown in a rail slot, if the slot is in use.</summary>
        public bool TryGetSlotAction(int slot, out ContextAction action)
        {
            bool used = slot >= 0 && slot < RailSlotCount && _slotUsed[slot];
            action = used ? _slotAction[slot] : default;
            return used;
        }

        /// <summary>Rail slot of an action, or -1 if the aircraft does not have it.</summary>
        public int SlotOf(ContextAction action)
        {
            for (int i = 0; i < RailSlotCount; i++)
                if (_slotUsed[i] && _slotAction[i] == action) return i;
            return -1;
        }

        public static int ActionBit(ContextAction action) => 1 << (int)action;
        public static int AllActions => (1 << ContextActions.Count) - 1;

        void Awake() => EnsureInitialized();

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) RefreshMotionPreference(); // the player may have changed the system setting
        }

        /// <summary>
        /// Assigns the aircraft's actions to rail slots, bottom up in
        /// <see cref="ContextAction"/> order. Slots never change afterwards, so an
        /// action is always in the same place. Unused slots stay hidden.
        /// </summary>
        public void ConfigureActions(int availableMask)
        {
            _actionsConfigured = true; // before initialisation, which would otherwise configure every action
            EnsureInitialized();
            int slot = 0;
            for (int a = 0; a < ContextActions.Count && slot < RailSlotCount; a++)
            {
                if ((availableMask & (1 << a)) == 0) continue;
                var action = (ContextAction)a;
                _slotAction[slot] = action;
                _slotUsed[slot] = true;
                if (railSlots[slot] != null) railSlots[slot].SetLabel(ActionLabel(action, false));
                slot++;
            }
            for (; slot < RailSlotCount; slot++) _slotUsed[slot] = false;
        }

        /// <summary>
        /// Dims routine information (flight data, guides) and the HUD buttons to the
        /// player's opacity, 0.6-1. Warning banners always stay at full strength.
        /// </summary>
        public void SetOpacity(float opacity)
        {
            EnsureInitialized();
            _opacity = Mathf.Clamp(opacity, 0.2f, 1f);
            for (int i = 0; i < DataItemCount; i++) _dataFades[i]?.SetOpacityScale(_opacity);
            _approachFade?.SetOpacityScale(_opacity);
            _navigationFade?.SetOpacityScale(_opacity);
            for (int i = 0; i < RailSlotCount; i++) _railFades[i]?.SetOpacityScale(_opacity);
            if (systemsGroup != null) systemsGroup.alpha = _opacity;
            if (profileGroup != null) profileGroup.alpha = _opacity;
        }

        public void SetMotionPreference(MotionPreference preference)
        {
            motionPreference = preference;
            RefreshMotionPreference();
        }

        void RefreshMotionPreference() =>
            _reducedMotion = SystemMotion.Resolve(motionPreference, motionPreference == MotionPreference.FollowSystem && SystemMotion.PrefersReducedMotion());

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _dataFades = new HudFade[DataItemCount];
            for (int i = 0; i < DataItemCount; i++)
            {
                if (dataItems[i] != null) _dataFades[i] = new HudFade(dataItems[i], interactive: false);
                _shownValue[i] = NoValue;
                _shownAlert[i] = NoValue;
            }
            if (approachGuide != null) _approachFade = new HudFade(approachGuide, interactive: false);
            if (navigationGuide != null) _navigationFade = new HudFade(navigationGuide, interactive: false);

            _warningFades = new HudFade[WarningSlotCount];
            for (int i = 0; i < WarningSlotCount; i++)
            {
                if (warningSlots[i] != null) _warningFades[i] = new HudFade(warningSlots[i], interactive: false);
                _slotWarning[i] = NoValue;
            }

            _railFades = new HudFade[RailSlotCount];
            for (int i = 0; i < RailSlotCount; i++)
            {
                var slot = railSlots[i];
                if (slot == null) continue;
                var group = slot.GetComponent<CanvasGroup>();
                if (group == null) group = slot.gameObject.AddComponent<CanvasGroup>();
                _railFades[i] = new HudFade(group, interactive: true);
                int index = i; // one closure per slot, created once
                slot.Clicked += () => OnRailSlotClicked(index);
            }
            if (systemsButton != null) systemsButton.Clicked += OnSystemsClicked;
            if (profileButton != null) profileButton.Clicked += OnProfileClicked;

            AssignSprites();
            RefreshMotionPreference();
            if (!_actionsConfigured) ConfigureActions(AllActions);
        }

        /// <summary>Runtime-generated sprites cannot be stored in a scene, so they are assigned here.</summary>
        void AssignSprites()
        {
            for (int k = 0; k < papiLights.Length; k++) SetSprite(papiLights[k], HudSprites.Disc, sliced: false);
            if (lateralMarker != null) SetSprite(lateralMarker.GetComponent<Image>(), HudSprites.Disc, sliced: false);
            if (navigationArrow != null) SetSprite(navigationArrow.GetComponent<Image>(), HudSprites.Arrow, sliced: false);
            for (int k = 0; k < warningBackgrounds.Length; k++)
                if (warningBackgrounds[k] != null && warningBackgrounds[k].sprite == null) HudStyle.Round(warningBackgrounds[k], HudStyle.BannerRadius);
            for (int k = 0; k < backings.Length; k++)
                if (backings[k] != null && backings[k].sprite == null) HudStyle.Round(backings[k], HudStyle.BannerRadius);
        }

        static void SetSprite(Image image, Sprite sprite, bool sliced)
        {
            if (image == null || image.sprite != null) return;
            image.sprite = sprite;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        }

        /// <summary>Updates targets and text with the airfield as the destination. Call once per frame.</summary>
        public void Present(in FlightTelemetry telemetry, in RunwayRelation runway, DisclosureController disclosure,
            FlightWarningMonitor warnings, HudProfile profile) =>
            Present(telemetry, runway, NavigationMath.FromRunway(runway, telemetry.HeadingDegrees), disclosure, warnings, profile);

        /// <summary>Updates targets and text. Call once per frame; animation runs in LateUpdate.</summary>
        public void Present(in FlightTelemetry telemetry, in RunwayRelation runway, in NavigationRelation navigation,
            DisclosureController disclosure, FlightWarningMonitor warnings, HudProfile profile)
        {
            if (disclosure == null) return;
            EnsureInitialized();

            float fade = motion.FadeSeconds(_reducedMotion);
            float slide = motion.Slide(_reducedMotion);
            bool refresh = Time.unscaledTime >= _nextValueRefresh;
            if (refresh) _nextValueRefresh = Time.unscaledTime + valueRefreshSeconds;
            int warningMask = warnings != null ? warnings.ActiveMask : 0;

            PresentFlightData(telemetry, disclosure, warningMask, fade, slide, refresh);
            PresentApproachGuide(runway, disclosure, fade, slide, refresh);
            PresentNavigationGuide(navigation, disclosure, fade, slide, refresh);
            PresentWarnings(warnings, fade);
            PresentRail(disclosure, profile, fade, slide);
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _dataFades.Length; i++) _dataFades[i]?.Tick(dt);
            _approachFade?.Tick(dt);
            _navigationFade?.Tick(dt);
            for (int i = 0; i < _warningFades.Length; i++) _warningFades[i]?.Tick(dt);
            for (int i = 0; i < _railFades.Length; i++) _railFades[i]?.Tick(dt);
        }

        // ---- Flight data ------------------------------------------------------------

        void PresentFlightData(in FlightTelemetry t, DisclosureController disclosure, int warningMask, float fade, float slide, bool refresh)
        {
            for (int i = 0; i < DataItemCount; i++)
            {
                var itemFade = _dataFades[i];
                if (itemFade == null) continue;
                var element = (HudElement)i;
                itemFade.SetTarget(AlphaFor(disclosure.GetProminence(element)), fade, slide);
                if (!itemFade.IsShown || dataValues[i] == null) continue;

                if (refresh || _shownValue[i] == NoValue) ShowValue(i, t);
                int alert = (warningMask & DisclosureController.RelatedWarnings(element)) != 0 ? 1 : 0;
                if (alert != _shownAlert[i])
                {
                    _shownAlert[i] = alert;
                    dataValues[i].color = alert == 1 ? alertValueColor : valueColor;
                }
            }
        }

        void ShowValue(int index, in FlightTelemetry t)
        {
            int quantised;
            NumberStrings strings;
            switch ((HudElement)index)
            {
                case HudElement.Airspeed:
                    quantised = Mathf.Clamp(Mathf.RoundToInt(SoftLogic.Finite(t.IndicatedAirspeedKnots, 0f)), 0, 999);
                    strings = Integers;
                    break;
                case HudElement.Altitude:
                    quantised = Mathf.RoundToInt(Mathf.Clamp(SoftLogic.Finite(t.AltitudeFeet, 0f), -99999f, 99999f) / 10f);
                    strings = Altitudes;
                    break;
                case HudElement.VerticalSpeed:
                    quantised = Mathf.RoundToInt(Mathf.Clamp(SoftLogic.Finite(t.VerticalSpeedFeetPerMinute, 0f), -99999f, 99999f) / 50f);
                    strings = VerticalSpeeds;
                    break;
                case HudElement.Heading:
                    quantised = Mathf.RoundToInt(SoftLogic.Finite(t.HeadingDegrees, 0f)) % 360;
                    if (quantised <= 0) quantised += 360; // compass cards show north as 360
                    strings = Headings;
                    break;
                default:
                    quantised = Mathf.Clamp(Mathf.RoundToInt(SoftLogic.Finite(t.GroundspeedKnots, 0f)), 0, 999);
                    strings = Integers;
                    break;
            }
            if (quantised == _shownValue[index]) return;
            _shownValue[index] = quantised;
            dataValues[index].text = strings.Get(quantised);
        }

        // ---- Guides -----------------------------------------------------------------

        void PresentApproachGuide(in RunwayRelation runway, DisclosureController disclosure, float fade, float slide, bool refresh)
        {
            if (_approachFade == null) return;
            _approachFade.SetTarget(AlphaFor(disclosure.GetProminence(HudElement.ApproachGuide)), fade, slide);
            if (!_approachFade.IsShown || !runway.Valid || !(refresh || _shownRunway == NoValue)) return;

            if (approachRunway != null && runway.Designator != _shownRunway)
            {
                _shownRunway = runway.Designator;
                approachRunway.text = Runways.Get(runway.Designator);
            }
            int tenths = Mathf.Clamp(Mathf.RoundToInt(runway.DistanceToThresholdMetres / 185.2f), 0, 9999);
            if (approachDistance != null && tenths != _shownApproachDistance)
            {
                _shownApproachDistance = tenths;
                approachDistance.text = NauticalMiles.Get(tenths);
            }
            int tens = Mathf.Max(0, Mathf.RoundToInt(runway.HeightAboveThresholdMetres * FlightUnits.FeetPerMetre / 10f));
            if (approachHeight != null && tens != _shownApproachHeight)
            {
                _shownApproachHeight = tens;
                approachHeight.text = HeightsFeet.Get(tens);
            }

            int white = runway.GlidepathValid ? ApproachGuidance.PapiWhiteLights(runway.GlidepathAngleDegrees, disclosure.Settings) : -1;
            if (white != _shownPapi)
            {
                _shownPapi = white;
                for (int k = 0; k < papiLights.Length; k++)
                {
                    if (papiLights[k] == null) continue;
                    papiLights[k].color = white < 0 ? papiOff : (k < white ? papiWhite : papiRed);
                }
            }

            // The marker shows where the centreline is (opposite to the aircraft's offset).
            float deviation = -ApproachGuidance.LateralDeviation(runway.CrossTrackMetres, disclosure.Settings);
            int lateral = Mathf.RoundToInt(deviation * lateralHalfTravel);
            if (lateralMarker != null && lateral != _shownLateral)
            {
                _shownLateral = lateral;
                lateralMarker.anchoredPosition = new Vector2(lateral, 0f);
            }
        }

        void PresentNavigationGuide(in NavigationRelation nav, DisclosureController disclosure, float fade, float slide, bool refresh)
        {
            if (_navigationFade == null) return;
            _navigationFade.SetTarget(AlphaFor(disclosure.GetProminence(HudElement.NavigationGuide)), fade, slide);
            if (!_navigationFade.IsShown || !nav.Valid) return;

            if (navigationArrow != null)
            {
                // Whole degrees: the transform (and the canvas batch) changes only when the arrow visibly turns.
                int relative = Mathf.RoundToInt(SoftLogic.Finite(nav.RelativeBearingDegrees, 0f));
                if (relative != _shownArrow)
                {
                    _shownArrow = relative;
                    navigationArrow.localRotation = Quaternion.Euler(0f, 0f, -relative); // UI rotation is counter-clockwise
                }
            }
            // Cached names: the label changes only with the destination.
            if (navigationLabel != null && nav.Target.Name != null && !ReferenceEquals(navigationLabel.text, nav.Target.Name))
                navigationLabel.text = nav.Target.Name;
            if (!(refresh || _shownNavDistance == NoValue)) return;
            int tenths = Mathf.Clamp(Mathf.RoundToInt(nav.DistanceMetres / 185.2f), 0, 9999);
            if (navigationDistance != null && tenths != _shownNavDistance)
            {
                _shownNavDistance = tenths;
                navigationDistance.text = NauticalMiles.Get(tenths);
            }
            int bearing = Mathf.RoundToInt(SoftLogic.Finite(nav.BearingDegrees, 0f)) % 360;
            if (bearing <= 0) bearing += 360; // compass cards show north as 360
            if (navigationBearing != null && bearing != _shownBearing)
            {
                _shownBearing = bearing;
                navigationBearing.text = Bearings.Get(bearing);
            }
        }

        // ---- Warnings ---------------------------------------------------------------

        void PresentWarnings(FlightWarningMonitor warnings, float fade)
        {
            int count = warnings != null ? warnings.ActiveCount : 0;
            float fadeIn = motion.WarningFadeSeconds(_reducedMotion);
            for (int k = 0; k < WarningSlotCount; k++)
            {
                var slotFade = _warningFades[k];
                if (slotFade == null) continue;
                if (k >= count)
                {
                    slotFade.SetTarget(0f, fade, 0f); // keeps its last text while it fades
                    continue;
                }

                var warning = warnings.ActiveAt(k);
                if (_slotWarning[k] != (int)warning)
                {
                    _slotWarning[k] = (int)warning;
                    bool red = FlightWarnings.Severity(warning) == WarningSeverity.Warning;
                    if (warningLabels[k] != null)
                    {
                        warningLabels[k].text = FlightWarnings.Label(warning);
                        warningLabels[k].color = red ? warningTextColor : cautionTextColor;
                    }
                    if (warningBackgrounds[k] != null) warningBackgrounds[k].color = red ? warningColor : cautionColor;
                    var icon = k < warningIcons.Length ? warningIcons[k] : null;
                    if (icon != null)
                    {
                        icon.sprite = red ? HudSprites.WarningIcon : HudSprites.CautionIcon;
                        icon.color = red ? warningTextColor : cautionTextColor;
                    }
                }
                slotFade.SetTarget(1f, fadeIn, 0f); // appears in place, never slides
            }
        }

        // ---- Action rail ------------------------------------------------------------

        void PresentRail(DisclosureController disclosure, HudProfile profile, float fade, float slide)
        {
            for (int s = 0; s < RailSlotCount; s++)
            {
                var slotFade = _railFades[s];
                var button = railSlots[s];
                if (slotFade == null || button == null) continue;
                if (!_slotUsed[s])
                {
                    slotFade.SetTarget(0f, 0f, 0f);
                    continue;
                }

                var action = _slotAction[s];
                bool engaged = disclosure.IsActionEngaged(action);
                // Never pull a button away from under a finger that is pressing it.
                bool visible = disclosure.IsActionVisible(action) || button.IsPressed;
                slotFade.SetTarget(visible ? 1f : 0f, fade, slide);
                button.SetInteractable(disclosure.IsActionEnabled(action));
                button.SetEngaged(engaged);
                button.SetLabel(ActionLabel(action, engaged));
            }

            if (systemsButton != null)
            {
                bool useful = DisclosureController.SystemsToggleUseful(profile);
                systemsButton.SetInteractable(useful);
                systemsButton.SetEngaged(useful && disclosure.RailExpanded);
            }
            if (profileButton != null && profile != _shownProfile)
            {
                _shownProfile = profile;
                profileButton.SetLabel(ProfileLabel(profile));
            }
        }

        void OnRailSlotClicked(int slot)
        {
            if (_slotUsed[slot]) ActionRequested?.Invoke(_slotAction[slot]);
        }

        void OnSystemsClicked() => SystemsToggleRequested?.Invoke();
        void OnProfileClicked() => ProfileCycleRequested?.Invoke();

        float AlphaFor(Prominence prominence) =>
            prominence == Prominence.Full ? 1f : (prominence == Prominence.Quiet ? quietAlpha : 0f);

        public static string ActionLabel(ContextAction action, bool engaged)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: return engaged ? "BRAKE SET" : "PARK BRAKE";
                case ContextAction.Flaps: return "FLAPS";
                case ContextAction.ApproachGuide: return "APPROACH";
                case ContextAction.Navigation: return "NAV";
                case ContextAction.Assistance: return "ASSIST";
                default: return "?";
            }
        }

        public static string ProfileLabel(HudProfile profile)
        {
            switch (profile)
            {
                case HudProfile.Cinematic: return "HUD\nCINEMATIC";
                case HudProfile.Detailed: return "HUD\nDETAILED";
                default: return "HUD\nSMART";
            }
        }
    }
}
