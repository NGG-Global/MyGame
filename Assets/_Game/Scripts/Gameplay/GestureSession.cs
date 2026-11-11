using System;
using System.Text;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Gestures;
using OpenSkies.Missions;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>Which interface the prototype shows. Development setting; gesture-first is the experiment.</summary>
    public enum InteractionMode
    {
        GestureFirst = 0,
        /// <summary>The SYSTEMS button and the action rail, for comparison only.</summary>
        Legacy = 1,
    }

    /// <summary>
    /// Turns recognised gestures into what the interface does, and what the aircraft is
    /// asked. It owns the interaction state, feeds the views each frame, and reaches the
    /// aircraft only through <see cref="FlightSession"/> (every system change passes the
    /// gate). Previews never touch committed state.
    ///
    /// The systems wheel is tap-first (docs/TAP_DISCLOSURE_DESIGN.md, milestone 2): a tap on
    /// SYSTEMS unfolds the wheel and keeps it; a tap on an action acts (PARK, APPROACH) or
    /// focuses it, so its options take the band around it (the flap detents, the switch
    /// panel, the destinations, the assistance profiles) with the action's name on the hub;
    /// a tap on an option applies it through the gate and the wheel folds. A hazardous option
    /// (engine off in flight) waits, red, for a second tap. The handle goes back one level
    /// (options to the wheel, the wheel to folded); a tap anywhere else folds everything and
    /// applies nothing. The wheel is fitted to the screen (<see cref="SystemsWheelLayout"/>).
    /// The wheel and its focus are registered with the <see cref="DisclosureCoordinator"/>.
    ///
    /// The pull gestures of M13/M14 (pull to unfold, rest for a sub-arc, slide into the flap
    /// lever) remain only as a development comparison (<see cref="PullGestures"/>), and since
    /// milestone 4 the HUD band's pull with them: the top left is the flight information
    /// cluster, whose taps open it. The world gestures and the long press explanations are
    /// unchanged; a held reading of the cluster resolves through <see cref="FlightInfoCluster.ElementAt"/>.
    /// </summary>
    public sealed class GestureSession : IDisposable
    {
        /// <summary>What the current gesture is doing.</summary>
        public enum Doing
        {
            Nothing = 0,
            ArcPull,
            Lever,
            DensityPull,
            Look,
            Explain,
        }

        /// <summary>Behaviour that is tuning rather than design.</summary>
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Pull comparison: resting on FLAPS this long hands the gesture over to the lever, s.")]
            public float leverHandoverSeconds = 0.18f;
            [Tooltip("Pull comparison: pulling through the FLAPS segment this far beyond the band also hands over, canvas units.")]
            public float leverHandoverOverreach = 30f;
            [Tooltip("Chase free look: degrees for a drag across the full screen height.")]
            public float lookDegreesPerScreenHeight = 120f;
            [Tooltip("Chase zoom: metres of distance for a pinch across the full screen height.")]
            public float zoomMetresPerScreenHeight = 30f;
            [Tooltip("The handle hint (the wheel shown briefly) is offered at most this many times, ever.")]
            public int handleHintLimit = 3;
            [Tooltip("Hints at least this far apart, s.")]
            public float handleHintSpacingSeconds = 25f;
            [Tooltip("A long press shows its hold ring once this share of the threshold has passed (quick taps never flash).")]
            [Range(0f, 1f)] public float holdRingFrom = 0.2f;
        }

        /// <summary>What a wheel entry is: one of the aircraft's contextual actions, or the switch panel.</summary>
        enum Entry
        {
            Action = 0,
            Switches = 1,
        }

        /// <summary>What choosing an option led to.</summary>
        enum Choice
        {
            Done = 0,
            /// <summary>A hazardous change waits for its second tap.</summary>
            Waiting = 1,
            Refused = 2,
        }

        const int MaxInner = SystemsArcModel.MaxItems;
        const int MaxSubOptions = 5;

        /// <summary>The switch panel's switches, in the order its options show them.</summary>
        public static readonly AircraftSystem[] SwitchSystems = { AircraftSystem.LandingLight, AircraftSystem.NavigationLights, AircraftSystem.Ignition };
        static readonly string[] SwitchLabels = { "LANDING", "NAV LTS", "ENGINE" };
        static readonly string[] SwitchConfirmStates = { "LANDING OFF", "NAV LTS OFF", "ENGINE OFF" };
        public const string SwitchesLabel = "SWITCHES";
        public const string ConfirmLabel = "CONFIRM";
        const string OnState = "ON", OffState = "OFF", SetState = "SET", EngineOffState = "ENGINE OFF", LandingOnState = "LDG ON";

        /// <summary>The options of an action registered by its owner (NAV, ASSIST): short labels, which one is current, and what choosing one does.</summary>
        sealed class SubOptions
        {
            public string[] Labels;
            public Func<int> Current;
            public Action<int> Choose;
        }

        readonly FlightSession _flight;
        readonly InteractionSurface _surface;
        readonly GestureHud _hud;
        readonly ContextualHud _contextualHud;
        readonly CameraRig _camera;
        readonly PointerOwnership _ownership;
        readonly HudExplanations _explanations;
        readonly DisclosureCoordinator _disclosure;
        readonly Settings _settings;
        readonly SystemsArcModel _arc;
        readonly SystemsArcSettings _preferredArc;
        readonly FlapsLeverInteraction _lever;
        readonly HudDensityModel _density;
        readonly Entry[] _innerKind = new Entry[MaxInner];
        readonly ContextAction[] _innerActions = new ContextAction[MaxInner];
        readonly string[] _focusCaptions = new string[MaxInner];
        readonly SubOptions[] _subOptions = new SubOptions[ContextActions.Count];
        readonly string[] _flapLabels;
        readonly Rect[] _keepOut = new Rect[SystemsWheelLayout.MaxKeepOut];
        readonly StringBuilder _ownershipText = new StringBuilder(128);
        int _innerCount;
        int _arcLayer = DisclosureCoordinator.None, _subArcLayer = DisclosureCoordinator.None;
        bool _arcOpen, _pull, _wheelPending;
        bool _pullAllowed = true;
        Doing _doing;
        bool _gestureFirst = true, _reducedMotion;
        float _flapsHoverSince = float.NegativeInfinity;
        int _lastHovered = SystemsArcModel.NoItem;
        bool _lastHoveredOuter;
        int _hintsShown;
        float _lastHintTime = float.NegativeInfinity;
        bool _everPulled;
        int _explainedTarget = -1;
        HudProfile _profile;
        bool _blocked;
        bool _fitted;
        Vector2 _fittedSize;
        int _fittedCount;
        SystemsWheelLayout.Result _fit;
        GestureInspector.Report _report;
        float _nextInspectorRefresh;

        /// <summary>Two fingers tapped the world together: the player wants the pause menu.</summary>
        public event Action PauseRequested;

        public GestureSession(FlightSession flight, InteractionSurface surface, GestureHud hud, ContextualHud contextualHud, CameraRig camera,
            PointerOwnership ownership, HudExplanations explanations, AircraftSystems systems, Settings settings = null, FlapsLeverInteraction lever = null)
        {
            _flight = flight ?? throw new ArgumentNullException(nameof(flight));
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            _hud = hud;
            _contextualHud = contextualHud;
            _camera = camera;
            _ownership = ownership;
            _explanations = explanations;
            _settings = settings ?? new Settings();
            _preferredArc = hud != null && hud.arc != null && hud.arc.settings != null ? hud.arc.settings : new SystemsArcSettings();
            _arc = new SystemsArcModel(_preferredArc);
            _lever = lever ?? new FlapsLeverInteraction(flight, hud != null ? hud.lever : null, hud);
            _density = new HudDensityModel(hud != null && hud.ladder != null ? hud.ladder.settings : null);

            int detents = systems != null ? systems.FlapDetentCount : 0;
            _flapLabels = new string[detents];
            for (int i = 0; i < detents; i++) _flapLabels[i] = i == 0 ? "UP" : Mathf.RoundToInt(systems.FlapDetentDegrees(i)) + "°";

            ConfigureArc(flight.AvailableActions);
            _surface.TargetResolver = FindTarget;
            _surface.GestureRaised += OnGesture;

            // The wheel is a transient interface: the coordinator closes it on an outside tap or when another cluster opens.
            // Its focus (one action's options) is a child: the handle goes back to the wheel; a tap anywhere but an option
            // (the free part of the band included: the hit area takes only the options) folds everything.
            _disclosure = surface.Disclosure;
            _arcLayer = _disclosure.Register("Systems wheel", IsOnArc, CollapseArc);
            _subArcLayer = _disclosure.Register("Systems wheel options", IsOnSubLevel, CollapseSubArc, true, _arcLayer);
            if (_hud != null && _hud.arcHitArea != null) _hud.arcHitArea.Filter = ArcHitFilter;
            if (_hud != null && _hud.inspector != null) _hud.inspector.ModeToggleRequested += ToggleMode;

            // Gesture-first until told otherwise: the handle and wheel are on, the rail is off; tap-first, not the pull.
            _surface.GestureFirst = true;
            if (_hud != null) _hud.SetGestureFirst(true);
            if (_contextualHud != null) _contextualHud.SetLegacyRailEnabled(false);
        }

        public void Dispose()
        {
            _surface.GestureRaised -= OnGesture;
            _surface.TargetResolver = null;
            _disclosure.Unregister(_subArcLayer);
            _disclosure.Unregister(_arcLayer);
            if (_hud != null && _hud.arcHitArea != null) _hud.arcHitArea.Filter = null;
            if (_hud != null && _hud.inspector != null) _hud.inspector.ModeToggleRequested -= ToggleMode;
        }

        public Doing Current => _doing;
        public bool GestureFirst => _gestureFirst;
        /// <summary>Development comparison: the M13/M14 pull gestures on the handle instead of the tap-first wheel.</summary>
        public bool PullGestures => _pull;
        /// <summary>The wheel stands open (no finger needed).</summary>
        public bool ArcOpen => _arcOpen;
        /// <summary>An action's options are shown: focused on the band (tap-first) or on the outer ring (pull comparison).</summary>
        public bool SubArcOpen => _arcOpen && (_arc.Focused || (_arc.OuterRevealed && _arc.OuterParent != SystemsArcModel.NoItem));
        /// <summary>Tap-first: the wheel shows one action's options on its band.</summary>
        public bool Focused => _arcOpen && _arc.Focused;
        /// <summary>The slot whose options are shown, or -1.</summary>
        public int FocusedSlot => Focused ? _arc.FocusParent : SystemsArcModel.NoItem;
        public DisclosureCoordinator Disclosure => _disclosure;
        /// <summary>The wheel's id with the coordinator (tests, inspectors).</summary>
        public int ArcLayer => _arcLayer;
        public int SubArcLayer => _subArcLayer;
        public SystemsArcModel Arc => _arc;
        public FlapsLeverModel Lever => _lever.Model;
        public HudDensityModel Density => _density;
        /// <summary>The wheel's last fit to the screen (geometry, whether every segment is a full touch target).</summary>
        public SystemsWheelLayout.Result Fit => _fit;
        /// <summary>While the HUD band is pulled: the profile to present instead of the committed one.</summary>
        public bool HasPreviewProfile => _doing == Doing.DensityPull && _density.IsActive;
        public HudProfile PreviewProfile => _density.Preview;
        public int HintsShown => _hintsShown;
        /// <summary>The options are shown from this slot (focused or on the outer ring), or -1.</summary>
        public int OpenSubArcParent => _arc.Focused ? _arc.FocusParent
            : ((_doing == Doing.ArcPull || _arcOpen) && _arc.OuterRevealed ? _arc.OuterParent : SystemsArcModel.NoItem);

        /// <summary>The wheel slot of an action, or -1 when the aircraft or the airfield does not offer it.</summary>
        public int SlotOf(ContextAction action)
        {
            for (int i = 0; i < _innerCount; i++)
                if (_innerKind[i] == Entry.Action && _innerActions[i] == action) return i;
            return SystemsArcModel.NoItem;
        }

        /// <summary>The wheel slot of the switch panel (landing light, navigation lights, engine), or -1.</summary>
        public int SwitchesSlot
        {
            get
            {
                for (int i = 0; i < _innerCount; i++)
                    if (_innerKind[i] == Entry.Switches) return i;
                return SystemsArcModel.NoItem;
            }
        }

        /// <summary>
        /// Gives an action options: tapping its segment focuses the wheel on them (NAV: the
        /// destinations; ASSIST: the settings and the profiles). By convention the first option
        /// is the action itself, so the wheel loses nothing the rail offers. Labels are fixed
        /// strings; <paramref name="current"/> marks the engaged option; <paramref name="choose"/>
        /// runs when one is chosen. At most five options.
        /// </summary>
        public void RegisterSubOptions(ContextAction action, string[] labels, Func<int> current, Action<int> choose)
        {
            if (labels == null || labels.Length == 0 || choose == null) { _subOptions[(int)action] = null; return; }
            var trimmed = labels;
            if (labels.Length > MaxSubOptions)
            {
                trimmed = new string[MaxSubOptions];
                Array.Copy(labels, trimmed, MaxSubOptions);
            }
            _subOptions[(int)action] = new SubOptions { Labels = trimmed, Current = current, Choose = choose };
        }

        public bool HasSubOptions(ContextAction action) => _subOptions[(int)action] != null;

        /// <summary>The player's interface choice changed (development): the handle and wheel, or the rail.</summary>
        public event Action<InteractionMode> ModeChanged;
        /// <summary>The pull comparison was switched (development).</summary>
        public event Action<bool> PullGesturesChanged;

        public void SetGestureFirst(bool on)
        {
            if (_gestureFirst == on) return;
            _gestureFirst = on;
            CloseArc();
            EndGesture();
            _surface.CancelAll();
            _surface.GestureFirst = on;
            if (_hud != null) _hud.SetGestureFirst(on);
            if (_contextualHud != null) _contextualHud.SetLegacyRailEnabled(!on);
            ModeChanged?.Invoke(on ? InteractionMode.GestureFirst : InteractionMode.Legacy);
        }

        /// <summary>
        /// Development comparison: on, the handle unfolds the arc by a pull, a rest opens an
        /// action's options on the outer ring and FLAPS hands over to the lever (M13/M14); off
        /// (the default), the wheel is tap-first. Nothing about the aircraft changes.
        /// </summary>
        public void SetPullGestures(bool on)
        {
            on = on && _pullAllowed;
            if (_hud != null) _hud.SetBandEnabled(on); // the HUD band's pull belongs to the same comparison
            if (_pull == on) return;
            _pull = on;
            CloseArc(DisclosureCollapseReason.Owner);
            EndGesture();
            _surface.CancelAll();
            PullGesturesChanged?.Invoke(on);
        }

        /// <summary>Whether the pull comparison may be switched on at all (development builds and the Editor only).</summary>
        public bool PullComparisonAllowed => _pullAllowed;

        /// <summary>Release builds never offer the pull comparison, whatever a saved preference says.</summary>
        public void SetPullComparisonAllowed(bool allowed)
        {
            _pullAllowed = allowed;
            if (!allowed) SetPullGestures(false);
        }

        /// <summary>Development (F4, a tap on the inspector): tap-first wheel, then the pull comparison, then the legacy rail.</summary>
        public void ToggleMode()
        {
            if (!_gestureFirst)
            {
                SetPullGestures(false);
                SetGestureFirst(true);
            }
            else if (!_pull && _pullAllowed) SetPullGestures(true);
            else
            {
                SetPullGestures(false);
                SetGestureFirst(false);
            }
        }

        public void SetReducedMotion(bool reduced)
        {
            _reducedMotion = reduced;
            if (_hud != null) _hud.SetReducedMotion(reduced);
        }

        float _lookScale = 1f;
        GestureHints _hints;
        InteractionDiary _diary;
        /// <summary>The player's look-around sensitivity (1 = the tuning's value).</summary>
        public void SetLookScale(float scale) => _lookScale = Mathf.Clamp(scale, 0.25f, 3f);
        public void SetHints(GestureHints hints) => _hints = hints;
        public void SetDiary(InteractionDiary diary) => _diary = diary;

        /// <summary>A new flight: every transient control folds away.</summary>
        public void OnReset()
        {
            CloseArc(DisclosureCollapseReason.Reset);
            EndGesture();
            _surface.CancelAll();
        }

        /// <summary>
        /// The wheel's entries keep fixed slots for the session: the aircraft's actions in
        /// <see cref="ContextAction"/> order, with the switch panel right after FLAPS.
        /// </summary>
        void ConfigureArc(int availableMask)
        {
            _innerCount = 0;
            bool switches = false;
            for (int a = 0; a < ContextActions.Count && _innerCount < MaxInner; a++)
            {
                if ((availableMask & (1 << a)) == 0) continue;
                var action = (ContextAction)a;
                AddEntry(Entry.Action, action);
                if (action == ContextAction.Flaps && _innerCount < MaxInner) { AddEntry(Entry.Switches, default); switches = true; }
            }
            if (!switches && _innerCount < MaxInner) AddEntry(Entry.Switches, default);
            _arc.SetInnerCount(_innerCount);
        }

        void AddEntry(Entry kind, ContextAction action)
        {
            int i = _innerCount++;
            _innerKind[i] = kind;
            _innerActions[i] = action;
            _focusCaptions[i] = kind == Entry.Switches ? "SWITCHES: tap one to flip it · the handle goes back" : FocusCaption(action);
            _arc.InnerItem(i) = new ArcItem
            {
                Label = kind == Entry.Switches ? SwitchesLabel : WheelLabel(action),
                EngagedLabel = null,
                Emphasis = ArcEmphasis.Quiet,
                Available = true,
                Icon = kind == Entry.Switches ? HudIcon.Switches : WheelIcon(action),
            };
        }

        /// <summary>The short label on the wheel (PARK, not BRAKE: the hold-to-brake button is right below).</summary>
        public static string WheelLabel(ContextAction action)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: return "PARK";
                case ContextAction.Flaps: return "FLAPS";
                case ContextAction.ApproachGuide: return "APPROACH";
                case ContextAction.Navigation: return "NAV";
                case ContextAction.Assistance: return "ASSIST";
                default: return "?";
            }
        }

        static HudIcon WheelIcon(ContextAction action)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: return HudIcon.ParkingBrake;
                case ContextAction.Flaps: return HudIcon.Flaps;
                case ContextAction.ApproachGuide: return HudIcon.Approach;
                case ContextAction.Navigation: return HudIcon.Navigation;
                case ContextAction.Assistance: return HudIcon.Assist;
                default: return HudIcon.None;
            }
        }

        static string FocusCaption(ContextAction action)
        {
            switch (action)
            {
                case ContextAction.Flaps: return "FLAPS: tap a setting · the handle goes back";
                case ContextAction.Navigation: return "NAV: tap a destination · the handle goes back";
                case ContextAction.Assistance: return "ASSIST: tap a profile or SETTINGS · the handle goes back";
                default: return SystemsArcView.FocusCaptionDefault;
            }
        }

        // ---- Fitting the wheel to the screen --------------------------------------------------------

        /// <summary>
        /// Fits the wheel to the free space around the handle (radii, angles), keeping clear of
        /// the joystick region, the throttle, the brake, the handle, the warning banners, the top
        /// bar and the session buttons. Runs when the screen or the entries change, never while
        /// the wheel is open or under a finger (<paramref name="force"/> refits a closed wheel at once).
        /// </summary>
        public void FitWheel(bool force = false)
        {
            if (_hud == null || _hud.arc == null) return;
            if (_arcOpen || _doing == Doing.ArcPull || _doing == Doing.Lever) return;
            var bounds = _hud.Bounds;
            bool same = _fitted && Mathf.Abs(bounds.width - _fittedSize.x) < 0.5f && Mathf.Abs(bounds.height - _fittedSize.y) < 0.5f && _fittedCount == _innerCount;
            if (same && !force) return;
            _fitted = true;
            _fittedSize = bounds.size;
            _fittedCount = _innerCount;
            if (!_preferredArc.fitToScreen) return;

            int keepOutCount = CollectKeepOut(bounds, out float joystickRight);
            var space = new SystemsWheelLayout.Space
            {
                Bounds = bounds,
                Anchor = _hud.HandleCentre,
                KeepOut = _keepOut,
                KeepOutCount = keepOutCount,
            };
            _fit = SystemsWheelLayout.Fit(_preferredArc, space, Mathf.Max(1, _innerCount));
            _arc.SetSettings(_fit.Geometry);
            _hud.arc.ApplySettings(_fit.Geometry);

            // The caption sits left of the wheel, in the free space before the joystick region.
            float outer = _fit.Geometry.outerRadius;
            float right = space.Anchor.x - outer - HudStyle.Gap;
            _hud.arc.PlaceCaption(new Vector2(-(outer + HudStyle.Gap), 0f), Mathf.Min(360f, right - (joystickRight + HudStyle.Gap)));
        }

        int CollectKeepOut(Rect bounds, out float joystickRight)
        {
            int n = 0;
            joystickRight = bounds.xMin + bounds.width * TouchControlsFactory.JoystickRegionRight;
            var touch = _flight.touchControls;
            if (touch != null)
            {
                if (touch.joystick != null)
                {
                    var region = _hud.LocalRect((RectTransform)touch.joystick.transform);
                    joystickRight = region.xMax;
                    n = AddKeepOut(n, region);
                }
                if (touch.throttle != null) n = AddKeepOut(n, _hud.LocalRect((RectTransform)touch.throttle.transform));
                if (touch.brake != null) n = AddKeepOut(n, _hud.LocalRect((RectTransform)touch.brake.transform));
            }
            if (_hud.handleHotspot != null) n = AddKeepOut(n, _hud.LocalRect((RectTransform)_hud.handleHotspot.transform));
            if (_contextualHud != null)
                for (int k = 0; k < ContextualHud.WarningSlotCount; k++)
                    if (_contextualHud.warningSlots[k] != null) n = AddKeepOut(n, _hud.LocalRect((RectTransform)_contextualHud.warningSlots[k].transform));
            // The top bar (the command cluster's primary row) and LOOK FWD under the camera. The cluster's options
            // panel needs no keep-out: it and the wheel are never open together (one coordinator).
            float top = CommandClusterLayout.RowBottom + HudStyle.Gap;
            n = AddKeepOut(n, Rect.MinMaxRect(bounds.xMin, bounds.yMax - top, bounds.xMax, bounds.yMax));
            n = AddKeepOut(n, TopRightRect(bounds, CommandClusterLayout.LookForwardHit));
            return n;
        }

        int AddKeepOut(int count, Rect rect)
        {
            if (count >= _keepOut.Length) return count;
            _keepOut[count] = rect;
            return count + 1;
        }

        /// <summary>A rectangle given from the safe area's top-right corner (<see cref="CommandClusterLayout"/>), in the HUD's local units.</summary>
        static Rect TopRightRect(Rect bounds, Rect topRight) =>
            new Rect(bounds.xMax + topRight.xMin, bounds.yMax + topRight.yMin, topRight.width, topRight.height);

        // ---- Per frame -----------------------------------------------------------------------------

        /// <summary>Per frame from the flight session, before disclosure is decided.</summary>
        public void Update(ViewMode view, bool blocked, HudProfile profile, DisclosureController disclosure, AircraftSystems systems)
        {
            _profile = profile;
            _blocked = blocked;
            _surface.CockpitView = view == ViewMode.Cockpit;
            _surface.Blocked = blocked;
            if (blocked && _doing != Doing.Nothing) EndGesture();
            if (blocked && _arcOpen) CloseArc(DisclosureCollapseReason.Blocked);

            if (!_arcOpen && _doing == Doing.Nothing) FitWheel();
            RefreshArcEmphasis(disclosure, systems, profile);
            RefreshOptions();
            if (_doing == Doing.Lever) _lever.Sync();
            OfferHandleHint(disclosure);

            if (_hud != null)
            {
                if (_hud.arc != null) _hud.arc.Present(_arc, _doing == Doing.ArcPull, _arcOpen);
                if (_hud.ladder != null && _doing == Doing.DensityPull) _hud.ladder.Present(_density);
                PresentHoldRing();
                if (_hud.inspector != null) PresentInspector(profile);
            }
        }

        /// <summary>Every primary segment's emphasis (by phase, profile and mission, as the rail) and its state line.</summary>
        void RefreshArcEmphasis(DisclosureController disclosure, AircraftSystems systems, HudProfile profile)
        {
            var phase = _flight.PhaseEstimator != null ? _flight.PhaseEstimator.Current.Phase : FlightPhase.Parked;
            for (int i = 0; i < _innerCount; i++)
            {
                ref var item = ref _arc.InnerItem(i);
                if (_innerKind[i] == Entry.Switches)
                {
                    // Never hidden: every switch stays reachable by hand; in full where a pilot reaches for it.
                    item.Emphasis = disclosure != null && DisclosureController.SwitchPanelProminence(profile, phase) == Prominence.Full
                        ? ArcEmphasis.Full : ArcEmphasis.Quiet;
                    item.State = SwitchesState(systems);
                    continue;
                }
                var action = _innerActions[i];
                if (disclosure == null) item.Emphasis = ArcEmphasis.Quiet;
                else if (disclosure.IsActionEngaged(action)) item.Emphasis = ArcEmphasis.Engaged;
                else if (!disclosure.IsActionEnabled(action) && disclosure.GetActionReason(action) != DisclosureReason.None) item.Emphasis = ArcEmphasis.Disabled;
                else if (disclosure.IsActionVisible(action) && disclosure.GetActionReason(action) != DisclosureReason.RailExpanded) item.Emphasis = ArcEmphasis.Full;
                else item.Emphasis = ArcEmphasis.Quiet;
                item.State = ActionState(action, item.Emphasis == ArcEmphasis.Engaged, systems);
            }
        }

        string ActionState(ContextAction action, bool engaged, AircraftSystems systems)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: return engaged ? SetState : null;
                case ContextAction.Flaps:
                    if (systems == null || _flapLabels.Length == 0) return null;
                    return _flapLabels[Mathf.Clamp(systems.FlapDetent, 0, _flapLabels.Length - 1)];
                case ContextAction.ApproachGuide:
                case ContextAction.Navigation:
                    return engaged ? OnState : null;
                case ContextAction.Assistance: return FlightAssistProfiles.Label(_flight.AssistProfile);
                default: return null;
            }
        }

        /// <summary>The switch panel's state line: what matters most (the engine off, then the landing light on).</summary>
        static string SwitchesState(AircraftSystems systems)
        {
            if (systems == null) return null;
            if (!systems.IgnitionOn) return EngineOffState;
            return systems.LandingLightOn ? LandingOnState : null;
        }

        /// <summary>The shown options follow the aircraft while they are open (a detent set by the keys, a confirmation running out).</summary>
        void RefreshOptions()
        {
            if (_arc.Focused)
            {
                int parent = _arc.FocusParent;
                int current = CurrentOption(parent);
                for (int i = 0; i < _arc.OptionCount; i++) _arc.OptionItem(i) = OptionFor(parent, i, current);
                if (_wheelPending && !_flight.TryGetPendingSystemChange(out _)) _wheelPending = false; // the confirmation window ran out
            }
            else if (_arc.OuterRevealed && _arc.OuterParent >= 0)
            {
                int parent = _arc.OuterParent;
                int current = CurrentOption(parent);
                for (int i = 0; i < _arc.OuterCount; i++) _arc.OuterItem(i) = OptionFor(parent, i, current);
            }
        }

        /// <summary>
        /// The first time the phase of flight offers an action, the handle is pointed out: a
        /// cue at the handle (the hint policy, remembered once the wheel has been opened) or,
        /// without one, the wheel showing itself briefly. Opening is learnt by seeing.
        /// </summary>
        void OfferHandleHint(DisclosureController disclosure)
        {
            if (!_gestureFirst || _everPulled || _blocked || disclosure == null || _hud == null || _hud.arc == null) return;
            if (_doing != Doing.Nothing || _arcOpen) return;
            bool offered = false;
            for (int i = 0; i < _innerCount && !offered; i++)
                offered = _innerKind[i] == Entry.Action && disclosure.IsActionVisible(_innerActions[i]) && disclosure.GetActionReason(_innerActions[i]) == DisclosureReason.Phase;
            if (!offered) return;
            if (_hints != null && _hints.HasView)
            {
                if (_hints.HasLearned(GestureId.PullSystems)) return;
                _hints.Offer(GestureId.PullSystems, _hud.HandleCentre, Vector2.up, 40f, "Tap SYSTEMS for flaps and more", _hud.Bounds, Time.unscaledTime);
                return;
            }
            if (_hintsShown >= _settings.handleHintLimit || Time.unscaledTime - _lastHintTime < _settings.handleHintSpacingSeconds) return;
            _hintsShown++;
            _lastHintTime = Time.unscaledTime;
            _hud.arc.ShowHint();
        }

        void PresentHoldRing()
        {
            if (_hud.plate == null) return;
            if (_doing == Doing.ArcPull)
            {
                // Pull comparison: resting on a segment that has options, the ring fills until they open.
                var handle = _surface.Channel(InteractionRoute.SystemsHandle);
                bool pending = _pull && handle != null && handle.IsTracking && !_arc.OuterRevealed && _arc.HasHovered && !_arc.HoveredIsOuter
                               && !_arc.HoveredIsOption && RingOptionCount(_arc.HoveredIndex) > 0 && !handle.IsDwelling;
                if (pending)
                {
                    float progress = Mathf.Clamp01(handle.RestSeconds / Mathf.Max(0.05f, handle.Settings.dwellSeconds));
                    if (progress >= _settings.holdRingFrom) _hud.plate.ShowHold(_hud.ToLocal(handle.Position), progress);
                    else _hud.plate.HideHold();
                }
                else _hud.plate.HideHold();
                return;
            }
            if (_doing == Doing.Explain || _doing == Doing.Lever || _doing == Doing.DensityPull || _doing == Doing.Look)
            {
                _hud.plate.HideHold();
                return;
            }
            for (int r = 0; r < InteractionRouter.RouteCount; r++)
            {
                var channel = _surface.Channel((InteractionRoute)r);
                if (channel == null || !channel.IsTracking || channel.State != GestureState.Candidate || channel.Kind != GestureKind.None) continue;
                if ((channel.Mask & GestureMask.LongPress) == 0 || channel.Progress < _settings.holdRingFrom) continue;
                _hud.plate.ShowHold(_hud.ToLocal(channel.Position), channel.Progress);
                return;
            }
            _hud.plate.HideHold();
        }

        void PresentInspector(HudProfile profile)
        {
            if (!_hud.inspector.Visible || Time.unscaledTime < _nextInspectorRefresh) return;
            _nextInspectorRefresh = Time.unscaledTime + _hud.inspector.refreshSeconds; // development only; strings a few times per second
            _ownershipText.Clear();
            if (_ownership != null)
                for (int i = 0; i < _ownership.Count; i++)
                {
                    if (i > 0) _ownershipText.Append("  ");
                    _ownershipText.Append('#').Append(_ownership.PointerIdAt(i)).Append('>').Append(_ownership.OwnerAt(i).OwnerName);
                }
            _report.GestureFirst = _gestureFirst;
            _report.PullGestures = _pull;
            _report.Ownership = _ownershipText.ToString();
            _report.Mode = _doing == Doing.Nothing && _arcOpen ? (SubArcOpen ? "WheelOpen+Options" : "WheelOpen") : _doing.ToString();
            _report.HudProfile = profile.ToString();
            _report.PreviewProfile = HasPreviewProfile ? _density.Preview.ToString() : null;
            _report.Diary = _diary != null ? _diary.Summary() : null;
            if (_doing == Doing.ArcPull)
            {
                _report.Target = _arc.HasHovered ? _arc.HoveredItem.Label : "-";
                _report.Armed = _arc.IsArmed ? _report.Target : "-";
            }
            else if (_doing == Doing.Lever)
            {
                _report.Target = "FLAPS lever";
                _report.Armed = _lever.Cancelled ? "cancelled" : (_lever.Changed ? _flapLabels[_lever.Model.Preview] : "unchanged");
            }
            else if (_doing == Doing.DensityPull)
            {
                _report.Target = "HUD band";
                _report.Armed = _density.Changed ? _density.Preview.ToString() : "unchanged";
            }
            else if (_doing == Doing.Explain)
            {
                _report.Target = _hud.plate != null ? _hud.plate.CurrentTitle : "-";
                _report.Armed = "-";
            }
            else
            {
                _report.Target = "-";
                _report.Armed = "-";
            }
            _hud.inspector.Present(_surface, _report);
        }

        // ---- Gesture events ------------------------------------------------------------------------

        void OnGesture(InteractionRoute route, GestureEvent e, int target)
        {
            if (!_gestureFirst) return;
            switch (route)
            {
                case InteractionRoute.SystemsHandle: OnHandle(e); break;
                case InteractionRoute.HudBand: OnBand(e, target); break;
                case InteractionRoute.World: OnWorld(e); break;
                case InteractionRoute.Target: OnTarget(e, target); break;
            }
        }

        /// <summary>The handle and the open wheel share one route: taps choose, a slide on the open wheel chooses where it is released.</summary>
        void OnHandle(in GestureEvent e)
        {
            var channel = _surface.Channel(InteractionRoute.SystemsHandle);
            switch (e.Kind)
            {
                case GestureKind.Pull:
                    if (_pull) OnHandlePull(e, channel);
                    else OnHandleSlide(e, channel);
                    break;
                case GestureKind.Dwell:
                    if (_pull && e.Type == GestureEventType.Dwell && _doing == Doing.ArcPull && !_arc.OuterRevealed) OpenSecondLevel();
                    break;
                case GestureKind.Tap:
                    if (e.Type == GestureEventType.Completed) OnHandleTap(e.Position);
                    break;
                case GestureKind.None:
                    // A slow tap (held a little longer than a tap, never pulled): on the tap-first wheel it is still a tap.
                    if (!_pull && e.Type == GestureEventType.Cancelled && e.Reason == CancelReason.Incomplete && (IsOnHandle(e.Position) || IsOnArc(e.Position)))
                        OnHandleTap(e.Position);
                    break;
                case GestureKind.LongPress:
                    OnLongPress(e, HudExplanations.HandleId, _hud != null && _hud.handleHotspot != null ? (RectTransform)_hud.handleHotspot.transform : null);
                    break;
            }
        }

        /// <summary>
        /// Tap-first: a finger that moves on the handle never unfolds anything (released on the
        /// handle it is a tap); on the open wheel it slides over the segments and chooses the one
        /// it is released on, exactly as a tap there would.
        /// </summary>
        void OnHandleSlide(in GestureEvent e, GestureRecognizer channel)
        {
            switch (e.Type)
            {
                case GestureEventType.Began:
                    if (!_arcOpen)
                    {
                        channel?.SetTargetArmed(false);
                        break;
                    }
                    BeginArc();
                    UpdateArc(e, channel);
                    break;
                case GestureEventType.Changed:
                    if (_doing == Doing.ArcPull) UpdateArc(e, channel);
                    else channel?.SetTargetArmed(false);
                    break;
                case GestureEventType.Completed:
                    if (_doing == Doing.ArcPull) CompleteSlide();
                    break;
                case GestureEventType.Cancelled:
                    if (_doing == Doing.ArcPull) EndGesture();
                    if (e.Reason == CancelReason.ReleasedUnarmed && IsOnHandle(e.Position)) OnHandleTap(e.Position);
                    break;
            }
        }

        /// <summary>The pull comparison (M13/M14): pull to unfold, rest for options or the lever, release to choose.</summary>
        void OnHandlePull(in GestureEvent e, GestureRecognizer channel)
        {
            switch (e.Type)
            {
                case GestureEventType.Began:
                    BeginArc();
                    UpdateArc(e, channel);
                    break;
                case GestureEventType.Changed:
                    if (_doing == Doing.Lever) UpdateLever(e, channel);
                    else if (_doing == Doing.ArcPull) UpdateArc(e, channel);
                    break;
                case GestureEventType.Armed:
                    if (_doing == Doing.ArcPull) HudButton.Report(UiFeedback.Tap);
                    break;
                case GestureEventType.Completed:
                    CompleteHandle();
                    break;
                case GestureEventType.Cancelled:
                    EndGesture();
                    break;
            }
        }

        /// <summary>Pull comparison: the finger rested on a segment with options; they fan out on the outer ring.</summary>
        void OpenSecondLevel()
        {
            if (_arc.HasHovered && !_arc.HoveredIsOuter && !_arc.HoveredIsOption) OpenSubArc(_arc.HoveredIndex);
        }

        /// <summary>Pull comparison: an action's options on the outer ring (a child of the open arc for the coordinator).</summary>
        bool OpenSubArc(int index)
        {
            int count = Mathf.Min(RingOptionCount(index), SystemsArcModel.MaxItems);
            if (count <= 0) return false; // a segment without options: resting does nothing (and never selects)
            int current = CurrentOption(index);
            for (int i = 0; i < count; i++) _arc.OuterItem(i) = OptionFor(index, i, current);
            _arc.RevealSubArc(index, count);
            if (_arcOpen && _arc.OuterRevealed) _disclosure.Open(_subArcLayer);
            HudButton.Report(UiFeedback.Tick);
            return true;
        }

        // ---- The tap-first wheel ---------------------------------------------------------------------

        /// <summary>A tap on SYSTEMS: the wheel unfolds and stays, every system on it.</summary>
        public void OpenArc()
        {
            if (_arcOpen || _hud == null || _hud.arc == null || !_gestureFirst || _blocked) return;
            EndGesture();
            FitWheel();
            _arcOpen = true;
            _everPulled = true;
            _hints?.Learned(GestureId.PullSystems, Time.unscaledTime);
            _arc.Begin();
            _arc.Update(Vector2.zero, 1f); // unfolded, nothing under a finger
            _hud.arc.SetTakesTouches(true);
            _hud.SetHub(null, false);
            _hud.SetHandleState(false, true);
            _disclosure.Open(_arcLayer);
            HudButton.Report(UiFeedback.Tap);
        }

        /// <summary>The wheel folds (the trigger again, a choice made, an outside tap, a reset). Nothing more is applied.</summary>
        public void CloseArc(DisclosureCollapseReason reason = DisclosureCollapseReason.Trigger)
        {
            if (!_arcOpen) return;
            _arcOpen = false;
            if (_doing == Doing.ArcPull)
            {
                // A finger sliding on the wheel when it was closed from elsewhere: its gesture ends without choosing.
                _surface.Channel(InteractionRoute.SystemsHandle)?.Cancel();
                EndGesture();
            }
            LeaveFocus();
            _arc.RevealOuter(false);
            _arc.Update(Vector2.zero, 0f);
            if (_hud != null)
            {
                _hud.arc?.SetTakesTouches(false);
                _hud.arc?.Fold();
                _hud.SetHub(null, false);
                _hud.SetHandleState(false, false);
            }
            _disclosure.Close(_arcLayer, reason); // also closes the options; no-op when the coordinator closed it first
        }

        /// <summary>The coordinator collapsed the wheel (outside tap, another cluster, focus loss, reset).</summary>
        void CollapseArc(DisclosureCollapseReason reason) => CloseArc(reason);

        /// <summary>The coordinator collapsed the options (the handle, a tap outside them, the wheel closing): back to the primary segments.</summary>
        void CollapseSubArc(DisclosureCollapseReason reason)
        {
            if (_arc.Focused)
            {
                LeaveFocus();
                return;
            }
            if (_arcOpen) _arc.RevealOuter(false);
        }

        /// <summary>
        /// The wheel focuses on one action: its options take the band (the primary segments fold
        /// into it) and its name is on the hub. A child of the wheel for the coordinator.
        /// </summary>
        void Focus(int index)
        {
            int count = Mathf.Min(OptionCount(index), SystemsArcModel.MaxItems);
            if (count <= 0) return;
            int current = CurrentOption(index);
            for (int i = 0; i < count; i++) _arc.OptionItem(i) = OptionFor(index, i, current);
            _arc.Focus(index, count);
            _arc.FocusCaption = _focusCaptions[index];
            _wheelPending = false;
            if (_hud != null) _hud.SetHub(_arc.InnerItem(index).Label, true);
            _disclosure.Open(_subArcLayer);
            HudButton.Report(UiFeedback.Tick);
        }

        /// <summary>The focus ends (back, outside, closed): a confirmation the wheel was waiting for is dropped with it.</summary>
        void LeaveFocus()
        {
            if (!_arc.Focused) return;
            _arc.Unfocus();
            if (_wheelPending) _flight.ClearPendingSystemChange();
            _wheelPending = false;
            if (_hud != null) _hud.SetHub(null, false);
        }

        /// <summary>One level back: from an action's options to the wheel.</summary>
        void BackToWheel()
        {
            if (_disclosure.IsActive(_subArcLayer)) _disclosure.Close(_subArcLayer, DisclosureCollapseReason.Trigger); // the callback leaves the focus
            if (_arc.Focused) LeaveFocus();
            if (_arc.OuterRevealed) _arc.RevealOuter(false);
        }

        /// <summary>A tap on the handle or on the open wheel.</summary>
        void OnHandleTap(Vector2 screenPoint)
        {
            if (_hud == null || _hud.arc == null) return;
            if (!_arcOpen)
            {
                OpenArc();
                return;
            }
            if (IsOnArc(screenPoint))
            {
                OnArcTap(screenPoint);
                return;
            }
            // The trigger again: one level back, or folded.
            if (SubArcOpen) BackToWheel();
            else CloseArc(DisclosureCollapseReason.Trigger);
            HudButton.Report(UiFeedback.Tap);
        }

        /// <summary>A tap on the open wheel: the segment or option under it.</summary>
        void OnArcTap(Vector2 screenPoint)
        {
            Vector2 fromAnchor = _hud.ToLocal(screenPoint) - _hud.HandleCentre;
            _arc.Update(fromAnchor, 1f);
            bool hovered = _arc.HasHovered, armed = _arc.IsArmed, outer = _arc.HoveredIsOuter, option = _arc.HoveredIsOption;
            int index = _arc.HoveredIndex;
            _arc.Update(Vector2.zero, 1f); // nothing stays lifted after a tap
            if (!hovered) return;
            if (!armed) { HudButton.Report(UiFeedback.Tap); return; } // not available now (the parking brake in the air): the wheel stays
            Choose(option, outer, index);
        }

        void Choose(bool option, bool outer, int index)
        {
            if (option) ChooseFocusedOption(index);
            else if (outer) ChooseRingOption(index);
            else ChooseInner(index);
        }

        /// <summary>A primary segment: its options take the wheel, or it acts and the wheel folds.</summary>
        void ChooseInner(int index)
        {
            if (!_pull)
            {
                if (OptionCount(index) > 0)
                {
                    Focus(index);
                    return;
                }
                PerformInner(index);
                CloseArc(DisclosureCollapseReason.Owner);
                return;
            }
            // Pull comparison with the tap menu of milestone 1: options on the outer ring; FLAPS opens the cockpit's selector.
            if (_arc.OuterRevealed && _arc.OuterParent == index)
            {
                BackToWheel();
                HudButton.Report(UiFeedback.Tap);
                return;
            }
            if (RingOptionCount(index) > 0)
            {
                if (SubArcOpen) BackToWheel();
                OpenSubArc(index);
                return;
            }
            PerformInner(index);
            CloseArc(DisclosureCollapseReason.Owner);
        }

        /// <summary>A focused option: applied through the gate; the wheel folds, or waits for the second tap of a hazardous change.</summary>
        void ChooseFocusedOption(int option)
        {
            int parent = _arc.FocusParent;
            if (parent < 0) return;
            switch (ChooseOption(parent, option))
            {
                case Choice.Waiting:
                    _wheelPending = true;
                    HudButton.Report(UiFeedback.Tap);
                    RefreshOptions();
                    break;
                case Choice.Refused:
                    HudButton.Report(UiFeedback.Tap);
                    break;
                default:
                    _wheelPending = false;
                    HudButton.Report(UiFeedback.Confirm);
                    CloseArc(DisclosureCollapseReason.Owner); // already folded when the choice opened another cluster
                    break;
            }
        }

        /// <summary>Pull comparison: an option on the outer ring, then the arc folds.</summary>
        void ChooseRingOption(int option)
        {
            int parent = _arc.OuterParent;
            if (parent >= 0) ChooseOption(parent, option);
            HudButton.Report(UiFeedback.Confirm);
            CloseArc(DisclosureCollapseReason.Owner);
        }

        /// <summary>A screen point on the open wheel's band, on a segment (or a focused option).</summary>
        bool IsOnArcBand(Vector2 screenPoint)
        {
            if (!_arcOpen || _hud == null) return false;
            Vector2 v = _hud.ToLocal(screenPoint) - _hud.HandleCentre;
            float r = v.magnitude;
            var s = _arc.Settings;
            if (r < s.innerRadius || r > s.outerRadius) return false;
            float angle = GestureMath.AngleDegrees(v);
            if (_arc.Focused) return _arc.OptionAt(angle) != SystemsArcModel.NoItem;
            return _arc.SlotAt(angle, _arc.InnerCount) != SystemsArcModel.NoItem;
        }

        /// <summary>Pull comparison: a screen point on the outer ring's options.</summary>
        bool IsOnOuterRing(Vector2 screenPoint)
        {
            if (!_arcOpen || _hud == null || !_arc.OuterRevealed || _arc.Focused) return false;
            Vector2 v = _hud.ToLocal(screenPoint) - _hud.HandleCentre;
            float r = v.magnitude;
            var s = _arc.Settings;
            if (r < s.OuterRingInner || r > s.OuterRingOuter) return false;
            return _arc.OuterAt(GestureMath.AngleDegrees(v)) != SystemsArcModel.NoItem;
        }

        /// <summary>The open wheel as one surface for the coordinator: the shape its hotspot takes presses on.</summary>
        bool IsOnArc(Vector2 screenPoint) => IsOnArcBand(screenPoint) || IsOnOuterRing(screenPoint);

        /// <summary>The options level for the coordinator: the focused band, or the outer ring.</summary>
        bool IsOnSubLevel(Vector2 screenPoint) => _arc.Focused ? IsOnArcBand(screenPoint) : IsOnOuterRing(screenPoint);

        /// <summary>A screen point on the open wheel's segments or options (tests, the inspector).</summary>
        public bool IsOnWheel(Vector2 screenPoint) => IsOnArc(screenPoint);

        bool IsOnHandle(Vector2 screenPoint) => _hud != null && _hud.handleHotspot != null && ScreenRects.Contains((RectTransform)_hud.handleHotspot.transform, screenPoint);

        /// <summary>The wheel's hotspot shape: presses land only on its segments, never on the controls around them.</summary>
        bool ArcHitFilter(Vector2 screenPoint, Camera eventCamera) => IsOnArc(screenPoint);

        // ---- Options --------------------------------------------------------------------------------

        /// <summary>How many options a primary segment focuses on (tap-first); 0 when it acts at once.</summary>
        int OptionCount(int index)
        {
            if (index < 0 || index >= _innerCount) return 0;
            if (_innerKind[index] == Entry.Switches) return SwitchSystems.Length;
            var action = _innerActions[index];
            if (action == ContextAction.Flaps) return _flapLabels.Length >= 2 ? _flapLabels.Length : 0;
            var sub = _subOptions[(int)action];
            return sub != null ? sub.Labels.Length : 0;
        }

        /// <summary>How many options a segment fans out on the outer ring in the pull comparison (FLAPS has the lever instead).</summary>
        int RingOptionCount(int index)
        {
            if (index < 0 || index >= _innerCount) return 0;
            if (_innerKind[index] == Entry.Switches) return SwitchSystems.Length;
            if (_innerActions[index] == ContextAction.Flaps) return 0;
            var sub = _subOptions[(int)_innerActions[index]];
            return sub != null ? sub.Labels.Length : 0;
        }

        /// <summary>The option that is currently set (the detent, the destination, the profile), or -1.</summary>
        int CurrentOption(int index)
        {
            if (index < 0 || index >= _innerCount || _innerKind[index] == Entry.Switches) return -1;
            var action = _innerActions[index];
            if (action == ContextAction.Flaps) return _flight.Systems != null ? _flight.Systems.FlapDetent : -1;
            var sub = _subOptions[(int)action];
            return sub != null && sub.Current != null ? sub.Current() : -1;
        }

        /// <summary>One option as drawn: its label, its state line and whether it is the one set (or waiting for a confirmation).</summary>
        ArcItem OptionFor(int index, int option, int current)
        {
            var item = new ArcItem { Available = true, Emphasis = ArcEmphasis.Full };
            if (index < 0 || index >= _innerCount) { item.Available = false; return item; }
            var systems = _flight.Systems;
            if (_innerKind[index] == Entry.Switches)
            {
                if (systems == null || option < 0 || option >= SwitchSystems.Length) { item.Available = false; return item; }
                var system = SwitchSystems[option];
                bool on = systems.Get(system) == 1;
                if (on && IsPending(system, 0))
                {
                    // A hazardous change waiting for its second tap: red, and the word says what the tap will do.
                    item.Label = ConfirmLabel;
                    item.State = SwitchConfirmStates[option];
                    item.Emphasis = ArcEmphasis.Confirm;
                    return item;
                }
                item.Label = SwitchLabels[option];
                item.State = on ? OnState : OffState;
                item.Emphasis = on ? ArcEmphasis.Engaged : ArcEmphasis.Full;
                return item;
            }
            var action = _innerActions[index];
            if (action == ContextAction.Flaps)
            {
                if (option < 0 || option >= _flapLabels.Length) { item.Available = false; return item; }
                item.Label = _flapLabels[option];
                item.State = option == current ? SetState : null;
                item.Emphasis = option == current ? ArcEmphasis.Engaged : ArcEmphasis.Full;
                return item;
            }
            var sub = _subOptions[(int)action];
            if (sub == null || option < 0 || option >= sub.Labels.Length) { item.Available = false; return item; }
            item.Label = sub.Labels[option];
            item.Emphasis = option == current ? ArcEmphasis.Engaged : ArcEmphasis.Full;
            return item;
        }

        bool IsPending(AircraftSystem system, int value) =>
            _flight.TryGetPendingSystemChange(out var change) && change.System == system && change.Value == value;

        /// <summary>Chooses an option: a system change through the gate, or the owner's choice (a destination, a profile).</summary>
        Choice ChooseOption(int index, int option)
        {
            if (index < 0 || index >= _innerCount) return Choice.Refused;
            var systems = _flight.Systems;
            if (_innerKind[index] == Entry.Switches)
            {
                if (systems == null || option < 0 || option >= SwitchSystems.Length) return Choice.Refused;
                var system = SwitchSystems[option];
                return ChoiceOf(_flight.RequestSystemChange(new SystemChange(system, systems.Get(system) == 1 ? 0 : 1)));
            }
            var action = _innerActions[index];
            if (action == ContextAction.Flaps)
            {
                if (option < 0 || option >= _flapLabels.Length) return Choice.Refused;
                return ChoiceOf(_flight.RequestSystemChange(new SystemChange(AircraftSystem.Flaps, option)));
            }
            var sub = _subOptions[(int)action];
            if (sub == null || option < 0 || option >= sub.Labels.Length) return Choice.Refused;
            sub.Choose(option);
            return Choice.Done;
        }

        static Choice ChoiceOf(SystemChangeGate.Outcome outcome) =>
            outcome == SystemChangeGate.Outcome.AwaitingConfirmation ? Choice.Waiting
            : (outcome == SystemChangeGate.Outcome.Refused ? Choice.Refused : Choice.Done);

        // ---- Sliding on the wheel, and the pull comparison ------------------------------------------

        void BeginArc()
        {
            EndGesture();
            _doing = Doing.ArcPull;
            _everPulled = true;
            _hints?.Learned(GestureId.PullSystems, Time.unscaledTime);
            if (!_arcOpen) _arc.Begin(); // from the open wheel the finger slides over what is already there
            _lastHovered = SystemsArcModel.NoItem;
            _flapsHoverSince = float.NegativeInfinity;
            if (_hud != null) _hud.SetHandleState(true, true);
        }

        void UpdateArc(in GestureEvent e, GestureRecognizer channel)
        {
            if (_hud == null) return;
            Vector2 fromAnchor = _hud.ToLocal(e.Position) - _hud.HandleCentre;
            _arc.Update(fromAnchor, _arcOpen ? 1f : e.Progress);
            channel?.SetTargetArmed(_arc.IsArmed);

            bool onInner = _arc.HasHovered && !_arc.HoveredIsOuter && !_arc.HoveredIsOption;
            bool onFlaps = onInner && _innerKind[_arc.HoveredIndex] == Entry.Action && _innerActions[_arc.HoveredIndex] == ContextAction.Flaps;
            if (_arc.HasHovered && (_arc.HoveredIndex != _lastHovered || _arc.HoveredIsOuter != _lastHoveredOuter))
            {
                HudButton.Report(UiFeedback.Tick); // a tick as the thumb crosses onto a segment
                _flapsHoverSince = onFlaps ? Time.unscaledTime : float.NegativeInfinity;
            }
            else if (!onFlaps) _flapsHoverSince = float.NegativeInfinity;
            _lastHovered = _arc.HoveredIndex;
            _lastHoveredOuter = _arc.HoveredIsOuter;

            if (_pull && onFlaps && _arc.IsArmed)
            {
                bool rested = Time.unscaledTime - _flapsHoverSince >= _settings.leverHandoverSeconds;
                bool through = fromAnchor.magnitude >= _arc.Settings.outerRadius + _settings.leverHandoverOverreach;
                if (rested || through) HandOverToLever(e, channel);
            }
        }

        /// <summary>Pull comparison: the same finger continues into the lever; the arc folds, the current detent sits under the thumb.</summary>
        void HandOverToLever(in GestureEvent e, GestureRecognizer channel)
        {
            if (!_lever.Available || _hud == null) return;
            if (_arcOpen)
            {
                // The lever takes over from the open arc: the menu is done; the slide carries on into the lever.
                _arcOpen = false;
                _arc.RevealOuter(false);
                _hud.arc.SetTakesTouches(false);
                _disclosure.Close(_arcLayer, DisclosureCollapseReason.Owner);
            }
            _doing = Doing.Lever;
            _lever.Begin(e.Position);
            _hints?.Learned(GestureId.DragLever, Time.unscaledTime);
            _hud.arc.Fold();
            _hud.SetHandleState(true, false);
            channel?.SetTargetArmed(true); // releasing commits whatever the lever shows (an unchanged detent changes nothing)
        }

        void UpdateLever(in GestureEvent e, GestureRecognizer channel)
        {
            _lever.Move(e.Position);
            channel?.SetTargetArmed(!_lever.Cancelled);
        }

        /// <summary>Tap-first: a slide on the open wheel released on a segment or option chooses it, as a tap there would.</summary>
        void CompleteSlide()
        {
            bool armed = _arc.IsArmed, outer = _arc.HoveredIsOuter, option = _arc.HoveredIsOption;
            int index = _arc.HoveredIndex;
            EndGesture();
            _arc.Update(Vector2.zero, 1f);
            if (armed) Choose(option, outer, index);
        }

        /// <summary>Pull comparison: released while armed. The lever commits; a segment acts; an outer option is chosen.</summary>
        void CompleteHandle()
        {
            if (_doing == Doing.Lever)
            {
                _lever.Commit();
                _doing = Doing.Nothing;
                EndGesture();
                if (_hud != null)
                {
                    _hud.arc?.Fold();
                    _hud.SetHandleState(false, false);
                }
                return;
            }
            bool chose = false;
            if (_doing == Doing.ArcPull && _arc.IsArmed)
            {
                chose = true;
                if (!_arc.HoveredIsOuter) PerformInner(_arc.HoveredIndex);
                else if (_arc.OuterParent >= 0)
                {
                    ChooseOption(_arc.OuterParent, _arc.HoveredIndex);
                    HudButton.Report(UiFeedback.Confirm);
                }
            }
            EndGesture();
            if (chose) CloseArc(DisclosureCollapseReason.Owner); // a slide on the open arc that chose: the menu folds
        }

        /// <summary>A segment that acts at once: the same request the rail button makes (the switch panel has no single action).</summary>
        void PerformInner(int index)
        {
            if (index < 0 || index >= _innerCount || _innerKind[index] != Entry.Action)
            {
                HudButton.Report(UiFeedback.Tap);
                return;
            }
            // In the pull comparison FLAPS released on without the lever opens the cockpit's detent selector, like the rail.
            _flight.PerformContextAction(_innerActions[index]);
            HudButton.Report(UiFeedback.Confirm);
        }

        void OnBand(in GestureEvent e, int target)
        {
            var channel = _surface.Channel(InteractionRoute.HudBand);
            switch (e.Kind)
            {
                case GestureKind.Pull:
                    switch (e.Type)
                    {
                        case GestureEventType.Began:
                            EndGesture();
                            _doing = Doing.DensityPull;
                            if (_hud != null)
                            {
                                Vector2 local = _hud.ToLocal(e.Origin);
                                _density.Begin(_profile, local.y);
                                if (_hud.ladder != null)
                                {
                                    var b = _hud.Bounds;
                                    _hud.ladder.Show(_density, local, Rect.MinMaxRect(b.xMin + HudStyle.ScreenMargin, b.yMin + HudStyle.ScreenMargin,
                                        b.xMax - HudStyle.ScreenMargin, b.yMax - HudStyle.ScreenMargin));
                                }
                            }
                            channel?.SetTargetArmed(false);
                            break;
                        case GestureEventType.Changed:
                            if (_doing != Doing.DensityPull || _hud == null) break;
                            {
                                var before = _density.Preview;
                                _density.Update(_hud.ToLocal(e.Position).y);
                                if (_density.Preview != before) HudButton.Report(UiFeedback.Tick);
                                channel?.SetTargetArmed(_density.Changed);
                            }
                            break;
                        case GestureEventType.Completed:
                            if (_doing == Doing.DensityPull && _density.Changed)
                            {
                                _flight.SetHudProfile(_density.Preview);
                                HudButton.Report(UiFeedback.Confirm);
                            }
                            EndGesture();
                            break;
                        case GestureEventType.Cancelled:
                            EndGesture();
                            break;
                    }
                    break;
                case GestureKind.LongPress:
                    if (target >= 0) OnLongPress(e, target, TargetRect(target));
                    break;
                case GestureKind.Tap:
                    // The band opens nothing by a tap, so a tap on it counts as a tap outside whatever is expanded.
                    if (e.Type == GestureEventType.Completed) _disclosure.OutsideTap(e.Position);
                    break;
            }
        }

        void OnWorld(in GestureEvent e)
        {
            switch (e.Kind)
            {
                case GestureKind.Drag:
                    if (_camera == null) break;
                    if (e.Type == GestureEventType.Began) { EndGesture(); _doing = Doing.Look; }
                    if (e.Type == GestureEventType.Changed && _doing == Doing.Look)
                    {
                        var look = CockpitLookMath.DragToLook(e.Step, ScreenHeight, _settings.lookDegreesPerScreenHeight * _lookScale, followFinger: true);
                        _camera.AddChaseLook(look.x, look.y);
                    }
                    if (e.Type == GestureEventType.Completed || e.Type == GestureEventType.Cancelled) EndGesture();
                    break;
                case GestureKind.Pinch:
                    if (_camera != null && e.Type == GestureEventType.Changed)
                        _camera.SetChaseDistance(_camera.chaseDistance - e.PinchDelta * _settings.zoomMetresPerScreenHeight);
                    break;
                case GestureKind.DoubleTap:
                    if (e.Type == GestureEventType.Completed)
                    {
                        EndGesture();
                        _flight.ToggleCamera();
                        HudButton.Report(UiFeedback.Tap);
                    }
                    break;
                case GestureKind.TwoFingerTap:
                    if (e.Type == GestureEventType.Completed)
                    {
                        EndGesture();
                        _hints?.Learned(GestureId.TwoFingerPause, Time.unscaledTime);
                        PauseRequested?.Invoke();
                    }
                    break;
            }
        }

        void OnTarget(in GestureEvent e, int target)
        {
            if (e.Kind == GestureKind.LongPress && target >= 0) OnLongPress(e, target, TargetRect(target));
            else if (e.Kind == GestureKind.Tap && e.Type == GestureEventType.Completed) _disclosure.OutsideTap(e.Position);
        }

        void OnLongPress(in GestureEvent e, int target, RectTransform rect)
        {
            if (_hud == null || _hud.plate == null || _explanations == null) return;
            switch (e.Type)
            {
                case GestureEventType.Began:
                    if (!_explanations.TryGet(target, out var title, out var text)) return;
                    EndGesture();
                    _doing = Doing.Explain;
                    _explainedTarget = target;
                    var b = _hud.Bounds;
                    var bounds = Rect.MinMaxRect(b.xMin + b.width * TouchControlsFactory.JoystickRegionRight * 0.5f, b.yMin + HudStyle.ScreenMargin,
                        b.xMax - HudStyle.ScreenMargin, b.yMax - HudStyle.ScreenMargin);
                    var anchor = rect != null ? _hud.LocalRect(rect) : new Rect(_hud.ToLocal(e.Position) - new Vector2(40f, 40f), new Vector2(80f, 80f));
                    _hud.plate.Show(title, text, anchor, bounds);
                    HudButton.Report(UiFeedback.Tap);
                    break;
                case GestureEventType.Completed:
                case GestureEventType.Cancelled:
                    if (_doing == Doing.Explain && _explainedTarget == target) EndGesture();
                    break;
            }
        }

        /// <summary>Every transient control folds away; nothing is applied.</summary>
        void EndGesture()
        {
            if (_hud != null)
            {
                bool onArc = _doing == Doing.ArcPull || _doing == Doing.Lever;
                if (_hud.arc != null && onArc && !_arcOpen) _hud.arc.Fold(); // the open menu stays after a slide that chose nothing
                if (_hud.ladder != null && _doing == Doing.DensityPull) _hud.ladder.Hide();
                if (_hud.plate != null && _doing == Doing.Explain) _hud.plate.Hide();
                if (onArc) _hud.SetHandleState(false, _arcOpen);
            }
            if (_doing == Doing.Look && _camera != null) _camera.ReleaseChaseLook(_reducedMotion);
            if (_doing == Doing.Lever) _lever.Cancel();
            if (_doing == Doing.DensityPull) _density.End();
            _doing = Doing.Nothing;
            _explainedTarget = -1;
        }

        // ---- Targets ------------------------------------------------------------------------------

        float ScreenHeight => _surface.screenHeightOverride > 1f ? _surface.screenHeightOverride : Mathf.Max(1f, Screen.height);

        /// <summary>The explainable HUD element under a screen point: a flight value, a guide or a warning banner, or -1.</summary>
        int FindTarget(Vector2 screenPoint)
        {
            for (int k = 0; _contextualHud != null && k < ContextualHud.WarningSlotCount; k++)
            {
                var slot = _contextualHud.warningSlots[k];
                if (slot == null || slot.alpha < 0.5f) continue;
                int warning = _contextualHud.WarningInSlot(k);
                if (warning >= 0 && Contains((RectTransform)slot.transform, screenPoint)) return HudExplanations.WarningBase + warning;
            }
            // A reading or a guide of the flight information cluster (the capsule's cells, or the open panel's tiles).
            var info = _flight.flightInfo;
            if (info != null)
            {
                var element = info.ElementAt(screenPoint);
                if (element != FlightInfoModel.NoElement) return (int)element;
            }
            return -1;
        }

        RectTransform TargetRect(int target)
        {
            if (target >= HudExplanations.WarningBase && target < HudExplanations.WarningBase + ContextualHud.WarningSlotCount)
            {
                if (_contextualHud == null) return null;
                for (int k = 0; k < ContextualHud.WarningSlotCount; k++)
                    if (_contextualHud.WarningInSlot(k) == target - HudExplanations.WarningBase && _contextualHud.warningSlots[k] != null)
                        return (RectTransform)_contextualHud.warningSlots[k].transform;
                return null;
            }
            if (target >= 0 && target < HudElements.Count) return _flight.flightInfo != null ? _flight.flightInfo.RectOf((HudElement)target) : null;
            return null;
        }

        static bool Contains(RectTransform rect, Vector2 screenPoint) => ScreenRects.Contains(rect, screenPoint);
    }
}
