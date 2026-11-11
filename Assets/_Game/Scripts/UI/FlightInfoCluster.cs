using System;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The top-left flight information cluster (docs/TAP_DISCLOSURE_DESIGN.md, milestone 4). It
    /// replaces the five separate flight data plates and the two guide plates with one cluster
    /// in three states, all anchored at the same corner:
    /// - Collapsed, the capsule: the trigger (a gauge symbol, always there), the readings the
    ///   flight context offers (at most three) and at most one guide chip (the approach lights
    ///   or the navigation arrow and distance).
    /// - Expanded, the panel: the trigger in the capsule's place, a tile per reading in the same
    ///   order, and a lower row with the navigation details (left) and the approach details
    ///   (right), or a hint when there are none.
    /// - Focused: a tapped reading is enlarged in the lower row with its explanation beside it;
    ///   a tapped guide keeps its tile and its explanation takes the other half of the row.
    ///
    /// A view only: it shows <see cref="FlightInfoModel"/> and the values it is given and raises
    /// taps. The session decides what a tap does and registers the panel with the disclosure
    /// coordinator (an outside tap folds it; no close button). Values are rounded by
    /// <see cref="FlightReadouts"/> and refreshed at a fixed interval; nothing allocates once a
    /// value has been shown. Runtime-generated sprites are assigned here, never stored in the scene.
    /// </summary>
    public sealed class FlightInfoCluster : MonoBehaviour
    {
        public const int CellCount = FlightInfoModel.MaxCapsuleReadings;
        public const int TileCount = FlightReadouts.ReadingCount;

        [Header("Capsule (collapsed)")]
        [Tooltip("The capsule's touch target (its width follows the content; anchored at the top-left corner).")]
        public HudButton capsuleButton;
        public CanvasGroup capsuleGroup;
        public Image capsuleIcon;
        public RectTransform[] cells = new RectTransform[CellCount];
        public CanvasGroup[] cellGroups = new CanvasGroup[CellCount];
        public Text[] cellLabels = new Text[CellCount];
        public Text[] cellValues = new Text[CellCount];
        public RectTransform chip;
        public RectTransform chipNavigation;
        public RectTransform chipArrow;
        public Text chipLabel;
        public Text chipDistance;
        public RectTransform chipApproach;
        public Text chipRunway;
        public Text chipApproachDistance;
        public Image[] chipPapi = new Image[ApproachGuidance.PapiLights];

        [Header("Panel (expanded and focused)")]
        public RectTransform panel;
        public CanvasGroup panelGroup;
        public Image panelBackground;
        [Tooltip("The trigger in the panel: the same place and symbol as the capsule's; a tap folds the cluster.")]
        public HudButton triggerButton;
        public Image triggerIcon;
        public HudButton[] tiles = new HudButton[TileCount];
        public Text[] tileLabels = new Text[TileCount];
        public Text[] tileValues = new Text[TileCount];

        [Header("Lower row: navigation (region A)")]
        public HudButton navigationTile;
        [Tooltip("Points towards the destination relative to the nose.")]
        public RectTransform navigationArrow;
        public Text navigationLabel;
        public Text navigationDistance;
        public Text navigationBearing;

        [Header("Lower row: approach (region B)")]
        public HudButton approachTile;
        public Text approachRunway;
        public Text approachDistance;
        public Text approachHeight;
        public Image[] papiLights = new Image[ApproachGuidance.PapiLights];
        [Tooltip("Shows where the extended centreline is: steer towards it.")]
        public RectTransform lateralMarker;
        [Tooltip("Marker travel either side of centre at full deflection, canvas units.")]
        public float lateralHalfTravel = 80f;

        [Header("Lower row: focus")]
        [Tooltip("A focused reading, enlarged in region A; a tap goes back to the panel.")]
        public HudButton focusButton;
        public Text focusTitle;
        public Text focusValue;
        [Tooltip("The explanation of the focused element, in whichever region its tile does not use; a tap goes back to the panel.")]
        public HudButton explanationButton;
        public Text explanationText;
        [Tooltip("Shown in the lower row when there are no guides to detail.")]
        public Text hint;

        [Header("Look and timing")]
        public HudMotionSettings motion = new HudMotionSettings();
        public FlightInfoSettings settings = new FlightInfoSettings();
        public Color valueColor = HudStyle.TextPrimary;
        public Color alertValueColor = HudStyle.AlertValue;
        public Color papiWhite = new Color(1f, 1f, 1f, 0.95f);
        public Color papiRed = new Color(0.92f, 0.16f, 0.12f, 0.95f);
        public Color papiOff = new Color(1f, 1f, 1f, 0.2f);

        /// <summary>The collapsed capsule was tapped.</summary>
        public event Action CapsuleTapped;
        /// <summary>The trigger in the open panel was tapped.</summary>
        public event Action TriggerTapped;
        /// <summary>A reading's tile or a guide's tile was tapped.</summary>
        public event Action<HudElement> TileTapped;
        /// <summary>The focused reading or the explanation was tapped (back to the panel).</summary>
        public event Action DetailTapped;

        const int Unset = int.MinValue;

        FlightInfoSettings _settings;
        HudFade _capsuleFade, _panelFade;
        HudExplanations _explanations;
        bool _initialized, _reducedMotion;
        float _opacity = 1f;
        float _nextRefresh;

        // What is on screen, so only changes touch the canvas.
        int _shownLevel = Unset, _shownFocus = Unset, _shownLowerRow = Unset;
        int _shownShape = Unset;
        readonly int[] _cellElement = new int[CellCount];
        readonly int[] _cellValue = new int[CellCount];
        readonly int[] _cellAlert = new int[CellCount];
        readonly int[] _cellProminence = new int[CellCount];
        readonly int[] _tileValue = new int[TileCount];
        readonly int[] _tileAlert = new int[TileCount];
        readonly int[] _quantised = new int[TileCount];
        int _shownChip = Unset, _shownChipArrow = Unset, _shownChipDistance = Unset, _shownChipRunway = Unset, _shownChipPapi = Unset, _shownChipApproachDistance = Unset;
        int _shownArrow = Unset, _shownNavDistance = Unset, _shownBearing = Unset;
        int _shownRunway = Unset, _shownApproachDistance = Unset, _shownApproachHeight = Unset, _shownPapi = Unset, _shownLateral = Unset;
        int _shownFocusValue = Unset, _shownFocusAlert = Unset;

        public bool ReducedMotion => _reducedMotion;
        public float Opacity => _opacity;
        /// <summary>The capsule is shown (collapsed) and takes touches.</summary>
        public bool CapsuleShown => _capsuleFade != null && _capsuleFade.IsShown;
        /// <summary>The panel is shown (or fading in) and takes touches.</summary>
        public bool PanelShown => _panelFade != null && _panelFade.IsShown;
        /// <summary>The level the view shows now.</summary>
        public FlightInfoLevel ShownLevel => _shownLevel == Unset ? FlightInfoLevel.Collapsed : (FlightInfoLevel)_shownLevel;
        /// <summary>The capsule's touch target (its rectangle follows the content).</summary>
        public RectTransform CapsuleRect => capsuleButton != null ? (RectTransform)capsuleButton.transform : null;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _settings = (settings ?? new FlightInfoSettings()).Sanitized();
            AssignSprites();
            for (int i = 0; i < CellCount; i++) _cellElement[i] = _cellValue[i] = _cellAlert[i] = _cellProminence[i] = Unset;
            for (int i = 0; i < TileCount; i++) _tileValue[i] = _tileAlert[i] = _quantised[i] = Unset;
            if (capsuleGroup != null) _capsuleFade = new HudFade(capsuleGroup, interactive: true, initialAlpha: 1f);
            if (panelGroup != null) _panelFade = new HudFade(panelGroup, interactive: true);

            // One subscription each, made once (no per-frame closures).
            if (capsuleButton != null) capsuleButton.Clicked += () => CapsuleTapped?.Invoke();
            if (triggerButton != null) triggerButton.Clicked += () => TriggerTapped?.Invoke();
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                var element = (HudElement)i;
                tiles[i].Clicked += () => TileTapped?.Invoke(element);
            }
            if (navigationTile != null) navigationTile.Clicked += () => TileTapped?.Invoke(HudElement.NavigationGuide);
            if (approachTile != null) approachTile.Clicked += () => TileTapped?.Invoke(HudElement.ApproachGuide);
            if (focusButton != null) focusButton.Clicked += () => DetailTapped?.Invoke();
            if (explanationButton != null) explanationButton.Clicked += () => DetailTapped?.Invoke();
        }

        /// <summary>Generated sprites cannot be stored in a scene: the symbols, the arrows and the lights get them here.</summary>
        void AssignSprites()
        {
            var gauge = HudSprites.Icon(HudIcon.FlightData);
            if (capsuleIcon != null) capsuleIcon.sprite = gauge;
            if (triggerIcon != null) triggerIcon.sprite = gauge;
            SetSprite(chipArrow, HudSprites.Arrow);
            SetSprite(navigationArrow, HudSprites.Arrow);
            SetSprite(lateralMarker, HudSprites.Disc);
            for (int k = 0; k < chipPapi.Length; k++) if (chipPapi[k] != null && chipPapi[k].sprite == null) chipPapi[k].sprite = HudSprites.Disc;
            for (int k = 0; k < papiLights.Length; k++) if (papiLights[k] != null && papiLights[k].sprite == null) papiLights[k].sprite = HudSprites.Disc;
            if (panelBackground != null && panelBackground.sprite == null) HudStyle.Round(panelBackground, HudStyle.PanelRadius);
        }

        static void SetSprite(RectTransform rect, Sprite sprite)
        {
            var image = rect != null ? rect.GetComponent<Image>() : null;
            if (image != null && image.sprite == null) image.sprite = sprite;
        }

        /// <summary>The explanations shown when an element is focused (built once per session from the aircraft's data).</summary>
        public void SetExplanations(HudExplanations explanations) => _explanations = explanations;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        /// <summary>
        /// The player's HUD opacity on the capsule (routine information at rest). The open panel
        /// stays at full strength: the player asked for it and it carries text.
        /// </summary>
        public void SetOpacity(float opacity)
        {
            EnsureInitialized();
            _opacity = Mathf.Clamp(opacity, 0.2f, 1f);
            _capsuleFade?.SetOpacityScale(_opacity);
        }

        // ---- Presenting ---------------------------------------------------------------------------

        /// <summary>
        /// Shows the model's level: the capsule or the panel, and what the panel's lower row
        /// holds (the guides, a focused element with its explanation, or the hint). Cheap and
        /// idempotent; the session calls it after every tap, before any telemetry exists.
        /// </summary>
        public void ShowLevel(FlightInfoModel model)
        {
            EnsureInitialized();
            if (model == null) return;
            int level = (int)model.Level;
            if (level != _shownLevel)
            {
                bool open = model.IsOpen;
                float fade = motion.FadeSeconds(_reducedMotion);
                // The capsule gives way to the panel in the same corner; the trigger stays in place.
                _capsuleFade?.SetTarget(open ? 0f : 1f, fade, 0f);
                _panelFade?.SetTarget(open ? 1f : 0f, fade, 0f);
                _shownLevel = level;
            }

            int focus = (int)model.Focus;
            int lower = (model.PanelNavigation ? 1 : 0) | (model.PanelApproach ? 2 : 0);
            if (focus == _shownFocus && lower == _shownLowerRow) return;
            _shownFocus = focus;
            _shownLowerRow = lower;

            var focused = model.Focus;
            bool readingFocused = FlightReadouts.IsReading(focused);
            bool navigationFocused = focused == HudElement.NavigationGuide;
            bool approachFocused = focused == HudElement.ApproachGuide;
            bool anyFocus = focused != FlightInfoModel.NoElement;

            SetActive(navigationTile, model.PanelNavigation && !readingFocused && !approachFocused);
            SetActive(approachTile, model.PanelApproach && !readingFocused && !navigationFocused);
            SetActive(focusButton, readingFocused);
            SetActive(explanationButton, anyFocus);
            if (hint != null) hint.gameObject.SetActive(!anyFocus && !model.PanelNavigation && !model.PanelApproach);

            for (int i = 0; i < tiles.Length; i++) if (tiles[i] != null) tiles[i].SetEngaged(readingFocused && (int)focused == i);
            if (navigationTile != null) navigationTile.SetEngaged(navigationFocused);
            if (approachTile != null) approachTile.SetEngaged(approachFocused);

            if (!anyFocus) return;
            // The explanation takes the region the focused element's own tile does not use.
            if (explanationButton != null)
            {
                var region = approachFocused ? FlightInfoLayout.RegionA : FlightInfoLayout.RegionB;
                Place((RectTransform)explanationButton.transform, FlightInfoLayout.Relative(region, FlightInfoLayout.Panel));
            }
            if (explanationText != null) explanationText.text = _explanations != null ? _explanations.ElementText(focused) : string.Empty;
            if (focusTitle != null && readingFocused) focusTitle.text = _explanations != null ? _explanations.ElementTitle(focused) : FlightReadouts.Label(focused);
            _shownFocusValue = _shownFocusAlert = Unset;
        }

        /// <summary>
        /// Updates the cluster from the model (composed this frame) and the session's values:
        /// the telemetry as shown (altitude with the altimeter's subscale), the runway relation
        /// and the navigation relation. Call once per frame; fades run in LateUpdate.
        /// </summary>
        public void Present(FlightInfoModel model, in FlightTelemetry telemetry, in RunwayRelation runway, in NavigationRelation navigation,
            DisclosureSettings disclosureSettings)
        {
            EnsureInitialized();
            if (model == null) return;
            ShowLevel(model);
            bool refresh = Time.unscaledTime >= _nextRefresh;
            if (refresh)
            {
                _nextRefresh = Time.unscaledTime + _settings.valueRefreshSeconds;
                for (int i = 0; i < TileCount; i++) _quantised[i] = FlightReadouts.Quantise((HudElement)i, telemetry);
            }
            else if (_quantised[0] == Unset)
                for (int i = 0; i < TileCount; i++) _quantised[i] = FlightReadouts.Quantise((HudElement)i, telemetry);

            PresentCapsule(model, runway, navigation, disclosureSettings, refresh);
            if (model.IsOpen || (_panelFade != null && _panelFade.Alpha > 0f)) PresentPanel(model, runway, navigation, disclosureSettings, refresh);
        }

        void PresentCapsule(FlightInfoModel model, in RunwayRelation runway, in NavigationRelation navigation, DisclosureSettings disclosureSettings, bool refresh)
        {
            int count = model.CapsuleCount;
            var chipElement = model.Chip;
            bool hasChip = chipElement != FlightInfoModel.NoElement;
            int shape = count | (hasChip ? 8 : 0);
            if (shape != _shownShape)
            {
                _shownShape = shape;
                Resize(count, hasChip);
                for (int i = 0; i < CellCount; i++) if (cells[i] != null) cells[i].gameObject.SetActive(i < count);
                if (chip != null) chip.gameObject.SetActive(hasChip);
            }

            for (int i = 0; i < count; i++)
            {
                var element = model.CapsuleElement(i);
                int e = (int)element;
                if (e != _cellElement[i])
                {
                    _cellElement[i] = e;
                    _cellValue[i] = Unset;
                    if (cellLabels[i] != null) cellLabels[i].text = FlightReadouts.Label(element);
                }
                int value = _quantised[e];
                if (value != _cellValue[i])
                {
                    _cellValue[i] = value;
                    if (cellValues[i] != null) cellValues[i].text = FlightReadouts.Text(element, value);
                }
                int alert = model.IsAlert(element) ? 1 : 0;
                if (alert != _cellAlert[i])
                {
                    _cellAlert[i] = alert;
                    if (cellValues[i] != null) cellValues[i].color = alert == 1 ? alertValueColor : valueColor;
                }
                int prominence = (int)model.CapsuleProminence(i);
                if (prominence != _cellProminence[i])
                {
                    _cellProminence[i] = prominence;
                    if (cellGroups[i] != null) cellGroups[i].alpha = prominence == (int)Prominence.Quiet ? _settings.quietAlpha : 1f;
                }
            }

            int chipKind = (int)chipElement;
            if (chipKind != _shownChip)
            {
                _shownChip = chipKind;
                if (chipNavigation != null) chipNavigation.gameObject.SetActive(chipElement == HudElement.NavigationGuide);
                if (chipApproach != null) chipApproach.gameObject.SetActive(chipElement == HudElement.ApproachGuide);
                _shownChipArrow = _shownChipDistance = _shownChipRunway = _shownChipPapi = _shownChipApproachDistance = Unset;
            }
            if (chipElement == HudElement.NavigationGuide && navigation.Valid)
            {
                ShowArrow(chipArrow, navigation, ref _shownChipArrow);
                if (chipLabel != null && navigation.Target.Name != null && !ReferenceEquals(chipLabel.text, navigation.Target.Name)) chipLabel.text = navigation.Target.Name;
                if (refresh || _shownChipDistance == Unset) ShowDistance(chipDistance, navigation.DistanceMetres, ref _shownChipDistance);
            }
            else if (chipElement == HudElement.ApproachGuide && runway.Valid)
            {
                if (runway.Designator != _shownChipRunway && chipRunway != null)
                {
                    _shownChipRunway = runway.Designator;
                    chipRunway.text = FlightReadouts.Runway(runway.Designator);
                }
                ShowPapi(chipPapi, FlightReadouts.PapiWhite(runway, disclosureSettings), ref _shownChipPapi);
                if (refresh || _shownChipApproachDistance == Unset) ShowDistance(chipApproachDistance, runway.DistanceToThresholdMetres, ref _shownChipApproachDistance);
            }
        }

        void PresentPanel(FlightInfoModel model, in RunwayRelation runway, in NavigationRelation navigation, DisclosureSettings disclosureSettings, bool refresh)
        {
            for (int i = 0; i < TileCount; i++)
            {
                var element = (HudElement)i;
                int value = _quantised[i];
                if (value != _tileValue[i])
                {
                    _tileValue[i] = value;
                    if (tileValues[i] != null) tileValues[i].text = FlightReadouts.Text(element, value);
                }
                int alert = model.IsAlert(element) ? 1 : 0;
                if (alert != _tileAlert[i])
                {
                    _tileAlert[i] = alert;
                    if (tileValues[i] != null) tileValues[i].color = alert == 1 ? alertValueColor : valueColor;
                }
            }

            var focused = model.Focus;
            if (FlightReadouts.IsReading(focused) && focusValue != null)
            {
                int value = _quantised[(int)focused];
                if (value != _shownFocusValue)
                {
                    _shownFocusValue = value;
                    focusValue.text = FlightReadouts.Text(focused, value);
                }
                int alert = model.IsAlert(focused) ? 1 : 0;
                if (alert != _shownFocusAlert)
                {
                    _shownFocusAlert = alert;
                    focusValue.color = alert == 1 ? alertValueColor : valueColor;
                }
            }

            if (model.PanelNavigation && navigation.Valid)
            {
                ShowArrow(navigationArrow, navigation, ref _shownArrow);
                if (navigationLabel != null && navigation.Target.Name != null && !ReferenceEquals(navigationLabel.text, navigation.Target.Name))
                    navigationLabel.text = navigation.Target.Name;
                if (refresh || _shownNavDistance == Unset)
                {
                    ShowDistance(navigationDistance, navigation.DistanceMetres, ref _shownNavDistance);
                    int bearing = FlightReadouts.CompassDegrees(navigation.BearingDegrees);
                    if (bearing != _shownBearing && navigationBearing != null)
                    {
                        _shownBearing = bearing;
                        navigationBearing.text = FlightReadouts.Bearing(bearing);
                    }
                }
            }

            if (model.PanelApproach && runway.Valid && (refresh || _shownRunway == Unset))
            {
                if (approachRunway != null && runway.Designator != _shownRunway)
                {
                    _shownRunway = runway.Designator;
                    approachRunway.text = FlightReadouts.Runway(runway.Designator);
                }
                ShowDistance(approachDistance, runway.DistanceToThresholdMetres, ref _shownApproachDistance);
                int tens = FlightReadouts.HeightTens(runway.HeightAboveThresholdMetres);
                if (approachHeight != null && tens != _shownApproachHeight)
                {
                    _shownApproachHeight = tens;
                    approachHeight.text = FlightReadouts.Height(tens);
                }
                ShowPapi(papiLights, FlightReadouts.PapiWhite(runway, disclosureSettings), ref _shownPapi);
                // The marker shows where the centreline is (opposite to the aircraft's offset).
                float deviation = disclosureSettings != null ? -ApproachGuidance.LateralDeviation(runway.CrossTrackMetres, disclosureSettings) : 0f;
                int lateral = Mathf.RoundToInt(deviation * lateralHalfTravel);
                if (lateralMarker != null && lateral != _shownLateral)
                {
                    _shownLateral = lateral;
                    lateralMarker.anchoredPosition = new Vector2(lateral, 0f);
                }
            }
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            float dt = Time.unscaledDeltaTime;
            _capsuleFade?.Tick(dt);
            _panelFade?.Tick(dt);
        }

        // ---- Pieces -------------------------------------------------------------------------------

        /// <summary>The capsule's pill and touch target follow the content; the corner never moves, only the right end.</summary>
        void Resize(int readings, bool hasChip)
        {
            if (capsuleButton == null) return;
            var hit = FlightInfoLayout.CapsuleHit(readings, hasChip);
            var rect = (RectTransform)capsuleButton.transform;
            rect.sizeDelta = hit.size;
            var visual = capsuleButton.background != null ? capsuleButton.background.rectTransform : null;
            if (visual != null) visual.sizeDelta = new Vector2(FlightInfoLayout.CapsuleVisualWidth(readings, hasChip), FlightInfoLayout.CapsuleVisualHeight);
            if (chip != null)
            {
                var chipRect = FlightInfoLayout.CapsuleChip(readings);
                var pill = FlightInfoLayout.CapsuleVisual(readings, hasChip);
                chip.anchoredPosition = new Vector2(chipRect.xMin - pill.xMin, 0f);
            }
        }

        static void ShowArrow(RectTransform arrow, in NavigationRelation navigation, ref int shown)
        {
            if (arrow == null) return;
            int relative = FlightReadouts.RelativeBearing(navigation);
            if (relative == shown) return;
            shown = relative;
            arrow.localRotation = Quaternion.Euler(0f, 0f, -relative); // UI rotation is counter-clockwise
        }

        static void ShowDistance(Text text, float metres, ref int shown)
        {
            int tenths = FlightReadouts.Tenths(metres);
            if (text == null || tenths == shown) return;
            shown = tenths;
            text.text = FlightReadouts.NauticalMiles(tenths);
        }

        void ShowPapi(Image[] lights, int white, ref int shown)
        {
            if (white == shown) return;
            shown = white;
            for (int k = 0; k < lights.Length; k++)
            {
                if (lights[k] == null) continue;
                lights[k].color = white < 0 ? papiOff : (k < white ? papiWhite : papiRed);
            }
        }

        static void SetActive(Component component, bool active)
        {
            if (component != null && component.gameObject.activeSelf != active) component.gameObject.SetActive(active);
        }

        static void Place(RectTransform rect, Rect topLeft)
        {
            rect.anchoredPosition = FlightInfoLayout.AnchoredPosition(topLeft);
            rect.sizeDelta = topLeft.size;
        }

        // ---- Hit tests (long-press explanations in the pull comparison, tests) --------------------

        /// <summary>The reading or guide drawn under a screen point (the capsule's cells while collapsed, the panel's tiles while open), or <see cref="FlightInfoModel.NoElement"/>.</summary>
        public HudElement ElementAt(Vector2 screenPoint)
        {
            if (PanelShown)
            {
                for (int i = 0; i < tiles.Length; i++)
                    if (tiles[i] != null && ScreenRects.Contains((RectTransform)tiles[i].transform, screenPoint)) return (HudElement)i;
                if (Active(navigationTile) && ScreenRects.Contains((RectTransform)navigationTile.transform, screenPoint)) return HudElement.NavigationGuide;
                if (Active(approachTile) && ScreenRects.Contains((RectTransform)approachTile.transform, screenPoint)) return HudElement.ApproachGuide;
                return FlightInfoModel.NoElement;
            }
            if (!CapsuleShown) return FlightInfoModel.NoElement;
            for (int i = 0; i < CellCount; i++)
                if (cells[i] != null && cells[i].gameObject.activeInHierarchy && _cellElement[i] != Unset && ScreenRects.Contains(cells[i], screenPoint))
                    return (HudElement)_cellElement[i];
            if (chip != null && chip.gameObject.activeInHierarchy && _shownChip >= 0 && ScreenRects.Contains(chip, screenPoint)) return (HudElement)_shownChip;
            return FlightInfoModel.NoElement;
        }

        /// <summary>Where an element is drawn now (its capsule cell or chip while collapsed, its tile while open), or null.</summary>
        public RectTransform RectOf(HudElement element)
        {
            if (PanelShown)
            {
                if (FlightReadouts.IsReading(element)) return tiles[(int)element] != null ? (RectTransform)tiles[(int)element].transform : null;
                if (element == HudElement.NavigationGuide && Active(navigationTile)) return (RectTransform)navigationTile.transform;
                if (element == HudElement.ApproachGuide && Active(approachTile)) return (RectTransform)approachTile.transform;
                return null;
            }
            for (int i = 0; i < CellCount; i++)
                if (_cellElement[i] == (int)element && cells[i] != null && cells[i].gameObject.activeInHierarchy) return cells[i];
            if (_shownChip == (int)element && chip != null && chip.gameObject.activeInHierarchy) return chip;
            return null;
        }

        static bool Active(Component component) => component != null && component.gameObject.activeInHierarchy;
    }
}
