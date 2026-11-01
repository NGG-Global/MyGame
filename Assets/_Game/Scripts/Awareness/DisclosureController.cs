using UnityEngine;

namespace OpenSkies.Awareness
{
    /// <summary>
    /// Decides which contextual HUD elements and rail actions are visible, from
    /// the HUD profile, the camera view, the estimated phase of flight, active
    /// warnings and the player's own choices. Plain C#, deterministic, no
    /// allocations; presentation reads the result and animates it.
    ///
    /// Rules that never bend:
    /// - Flight controls (joystick, throttle, brakes, camera, reset) are not
    ///   managed here at all, so no rule can hide or move them.
    /// - A warning shows the information related to it in every profile.
    /// - An action whose system is engaged (parking brake set, flaps out, a
    ///   guide on) stays visible so it can always be undone.
    /// - Manual access: the player can open the full action rail (SYSTEMS) and
    ///   show or hide any guide; their choice wins over the automatic rules.
    /// - Anti-flicker: things appear at once but disappear only after a hide
    ///   delay and a minimum visible time. Profile, view and player changes
    ///   apply at once.
    /// </summary>
    public sealed class DisclosureController
    {
        const float TimeEpsilon = 1e-4f;

        struct Slot
        {
            public Prominence Target;
            public Prominence Shown;
            public DisclosureReason Reason;
            public float VisibleTime;
            public float HideTimer;
            public bool Immediate;
        }

        readonly DisclosureSettings _settings;
        readonly Slot[] _elements = new Slot[HudElements.Count];
        readonly Slot[] _actions = new Slot[ContextActions.Count];
        readonly bool[] _actionEnabled = new bool[ContextActions.Count];
        readonly bool[] _actionEngaged = new bool[ContextActions.Count];
        readonly PlayerOverride[] _overrides = new PlayerOverride[HudElements.Count];
        readonly bool[] _autoRelevant = new bool[HudElements.Count];

        bool _railExpanded;
        bool _hasRun;
        HudProfile _lastProfile;
        ViewMode _lastView;
        float _stableTime;
        bool _stable;

        public DisclosureController(DisclosureSettings settings = null)
        {
            _settings = settings ?? new DisclosureSettings();
        }

        public DisclosureSettings Settings => _settings;

        public Prominence GetProminence(HudElement element) => _elements[(int)element].Shown;
        public bool IsVisible(HudElement element) => _elements[(int)element].Shown != Prominence.Hidden;
        public DisclosureReason GetReason(HudElement element) => _elements[(int)element].Reason;
        /// <summary>True while an element is kept visible by the hide delay or minimum visible time.</summary>
        public bool IsHeld(HudElement element) => _elements[(int)element].HideTimer > 0f;
        public PlayerOverride GetOverride(HudElement element) => _overrides[(int)element];

        public bool IsActionVisible(ContextAction action) => _actions[(int)action].Shown != Prominence.Hidden;
        public bool IsActionEnabled(ContextAction action) => _actionEnabled[(int)action];
        public bool IsActionEngaged(ContextAction action) => _actionEngaged[(int)action];
        public DisclosureReason GetActionReason(ContextAction action) => _actions[(int)action].Reason;

        /// <summary>SYSTEMS: the player opened the full action rail.</summary>
        public bool RailExpanded => _railExpanded;
        public bool IsStableFlight => _stable;
        public float StableSeconds => _stableTime;

        /// <summary>The SYSTEMS toggle has no effect in Detailed, where the full rail is always shown.</summary>
        public static bool SystemsToggleUseful(HudProfile profile) => profile != HudProfile.Detailed;

        public void SetRailExpanded(bool expanded)
        {
            if (expanded == _railExpanded) return;
            _railExpanded = expanded;
            for (int i = 0; i < _actions.Length; i++) _actions[i].Immediate = true;
        }

        public void ToggleRailExpanded() => SetRailExpanded(!_railExpanded);

        /// <summary>
        /// The player asks for a guide to be shown or hidden (the opposite of what
        /// is on screen). A "show" lasts until the player hides it again; a "hide"
        /// lasts until the guide stops being relevant on its own, so it comes back
        /// automatically next time (for example on the next approach).
        /// </summary>
        public void ToggleGuide(HudElement guide)
        {
            if (!HudElements.IsGuide(guide)) return;
            int i = (int)guide;
            bool visible = _elements[i].Shown != Prominence.Hidden;
            bool relevant = _autoRelevant[i];
            if (visible) _overrides[i] = relevant ? PlayerOverride.Hide : PlayerOverride.None;
            else _overrides[i] = relevant ? PlayerOverride.None : PlayerOverride.Show;
            _elements[i].Immediate = true;
            _actions[(int)ActionFor(guide)].Immediate = true;
        }

        /// <summary>New flight: forget guide choices, stability and timers. The SYSTEMS rail choice is kept.</summary>
        public void Reset()
        {
            for (int i = 0; i < _elements.Length; i++) _elements[i] = default;
            for (int i = 0; i < _actions.Length; i++) _actions[i] = default;
            for (int i = 0; i < _overrides.Length; i++)
            {
                _overrides[i] = PlayerOverride.None;
                _autoRelevant[i] = false;
            }
            for (int i = 0; i < _actionEnabled.Length; i++) _actionEnabled[i] = _actionEngaged[i] = false;
            _stableTime = 0f;
            _stable = false;
            _hasRun = false;
        }

        public void Update(in DisclosureInput input, float deltaTime)
        {
            if (!(deltaTime >= 0f) || float.IsInfinity(deltaTime)) deltaTime = 0f;
            bool immediate = !_hasRun || input.Profile != _lastProfile || input.View != _lastView;
            _hasRun = true;
            _lastProfile = input.Profile;
            _lastView = input.View;

            UpdateStability(input, deltaTime);

            for (int i = 0; i < HudElements.Count; i++)
            {
                var element = (HudElement)i;
                var target = AutomaticProminence(element, input, out var reason);
                if (MissionRequests(element, input))
                {
                    target = Prominence.Full;
                    reason = DisclosureReason.Mission;
                }
                bool relevant = target != Prominence.Hidden;
                if (_overrides[i] == PlayerOverride.Hide && _autoRelevant[i] && !relevant) _overrides[i] = PlayerOverride.None;
                _autoRelevant[i] = relevant;

                if (_overrides[i] == PlayerOverride.Show) { target = Prominence.Full; reason = DisclosureReason.PlayerShown; }
                else if (_overrides[i] == PlayerOverride.Hide) { target = Prominence.Hidden; reason = DisclosureReason.PlayerHidden; }

                if ((input.WarningMask & RelatedWarnings(element)) != 0)
                {
                    target = Prominence.Full;
                    reason = DisclosureReason.Warning;
                }
                Advance(ref _elements[i], target, reason, deltaTime, immediate);
            }

            for (int i = 0; i < ContextActions.Count; i++)
            {
                var action = (ContextAction)i;
                bool available = IsAvailable(action, input);
                bool engaged = available && IsEngaged(action, input);
                if (engaged != _actionEngaged[i]) _actions[i].Immediate = true;
                _actionEngaged[i] = engaged;
                _actionEnabled[i] = available && IsEnabled(action, input);

                Prominence target;
                DisclosureReason reason;
                if (!available) { target = Prominence.Hidden; reason = DisclosureReason.NotAvailable; }
                else if (engaged) { target = Prominence.Full; reason = DisclosureReason.Engaged; }
                else if ((input.MissionActions & (1 << i)) != 0) { target = Prominence.Full; reason = DisclosureReason.Mission; }
                else if (input.Profile == HudProfile.Detailed) { target = Prominence.Full; reason = DisclosureReason.Profile; }
                else if (_railExpanded) { target = Prominence.Full; reason = DisclosureReason.RailExpanded; }
                else if (input.Profile == HudProfile.Smart && IsRelevant(action, input))
                {
                    target = Prominence.Full;
                    reason = DisclosureReason.Phase;
                }
                else
                {
                    target = Prominence.Hidden;
                    reason = input.Profile == HudProfile.Cinematic ? DisclosureReason.Profile : DisclosureReason.Phase;
                }
                Advance(ref _actions[i], target, reason, deltaTime, immediate);
            }
        }

        void UpdateStability(in DisclosureInput input, float dt)
        {
            var s = _settings;
            bool steady = input.Phase.Phase == FlightPhase.Cruise
                          && input.Phase.Confidence >= s.stableMinimumConfidence
                          && Mathf.Abs(input.BankDegrees) <= s.stableMaxBankDegrees
                          && input.WarningMask == 0;
            _stableTime = steady ? _stableTime + dt : 0f;
            _stable = steady && _stableTime + TimeEpsilon >= s.stableFlightSeconds;
        }

        void Advance(ref Slot slot, Prominence target, DisclosureReason reason, float dt, bool immediate)
        {
            slot.Target = target;
            if (slot.Shown != Prominence.Hidden) slot.VisibleTime += dt;

            if (target >= slot.Shown || immediate || slot.Immediate)
            {
                if (slot.Shown == Prominence.Hidden && target != Prominence.Hidden) slot.VisibleTime = 0f;
                slot.Shown = target;
                slot.Reason = reason;
                slot.HideTimer = 0f;
                slot.Immediate = false;
                return;
            }

            // Less visible than now: wait for the hide delay and the minimum visible time.
            slot.HideTimer += dt;
            if (slot.HideTimer + TimeEpsilon >= _settings.hideDelaySeconds && slot.VisibleTime + TimeEpsilon >= _settings.minimumVisibleSeconds)
            {
                slot.Shown = target;
                slot.Reason = reason;
                slot.HideTimer = 0f;
            }
        }

        Prominence AutomaticProminence(HudElement element, in DisclosureInput input, out DisclosureReason reason)
        {
            var phase = input.Phase.Phase;
            switch (element)
            {
                case HudElement.ApproachGuide:
                    if (!input.HasRunway) { reason = DisclosureReason.NotAvailable; return Prominence.Hidden; }
                    if (input.Profile == HudProfile.Detailed)
                    {
                        reason = DisclosureReason.Profile;
                        return !input.OnGround && input.DistanceToRunwayNm <= _settings.approachGuideRangeNm ? Prominence.Full : Prominence.Hidden;
                    }
                    if (input.Profile == HudProfile.Smart)
                    {
                        reason = DisclosureReason.Phase;
                        return phase == FlightPhase.Approach || phase == FlightPhase.Landing ? Prominence.Full : Prominence.Hidden;
                    }
                    reason = DisclosureReason.Profile;
                    return Prominence.Hidden;

                case HudElement.NavigationGuide:
                    if (!input.HasRunway) { reason = DisclosureReason.NotAvailable; return Prominence.Hidden; }
                    if (input.View == ViewMode.Cockpit && _settings.cockpitHasInstruments)
                    {
                        reason = DisclosureReason.InstrumentsInView; // the GPS shows the way to the airfield
                        return Prominence.Hidden;
                    }
                    if (input.Profile == HudProfile.Detailed)
                    {
                        reason = DisclosureReason.Profile;
                        bool enRoute = !input.OnGround && phase != FlightPhase.Approach && phase != FlightPhase.Landing;
                        return enRoute ? Prominence.Full : Prominence.Hidden;
                    }
                    // Smart offers navigation as an action in cruise; it is shown on request.
                    reason = input.Profile == HudProfile.Smart ? DisclosureReason.Phase : DisclosureReason.Profile;
                    return Prominence.Hidden;

                default:
                    return FlightDataProminence(element, input, out reason);
            }
        }

        Prominence FlightDataProminence(HudElement element, in DisclosureInput input, out DisclosureReason reason)
        {
            if (input.View == ViewMode.Cockpit && _settings.cockpitHasInstruments)
            {
                reason = DisclosureReason.InstrumentsInView; // the instruments are the information (pillar 4)
                return Prominence.Hidden;
            }
            if (input.Profile == HudProfile.Detailed)
            {
                reason = DisclosureReason.Profile;
                return Prominence.Full;
            }
            if (input.Profile == HudProfile.Cinematic && input.View == ViewMode.External)
            {
                reason = DisclosureReason.Profile;
                return Prominence.Hidden;
            }

            // Smart, and Cinematic in a cockpit without instruments.
            if (input.Phase.Confidence < _settings.uncertainConfidence)
            {
                reason = DisclosureReason.UncertainPhase;
                return UncertainDefault(element, input.OnGround) ? Prominence.Full : Prominence.Hidden;
            }
            if (_stable)
            {
                reason = DisclosureReason.StableFlight;
                return element == HudElement.Airspeed || element == HudElement.Altitude ? Prominence.Quiet : Prominence.Hidden;
            }
            reason = DisclosureReason.Phase;
            return SmartShows(element, input.Phase.Phase) ? Prominence.Full : Prominence.Hidden;
        }

        /// <summary>A mission's guide request, when the guide has something to show.</summary>
        static bool MissionRequests(HudElement element, in DisclosureInput input)
        {
            if (!HudElements.IsGuide(element) || (input.MissionGuides & HudElements.Bit(element)) == 0) return false;
            return element == HudElement.ApproachGuide ? input.HasRunway : input.HasRunway || input.HasNavigationTarget;
        }

        /// <summary>When the phase is uncertain, show a safe superset instead of trusting the estimate.</summary>
        static bool UncertainDefault(HudElement element, bool onGround)
        {
            if (onGround) return element == HudElement.Airspeed;
            return element == HudElement.Airspeed || element == HudElement.Altitude || element == HudElement.VerticalSpeed;
        }

        /// <summary>The Smart profile's flight data table (docs/CONTEXTUAL_UI.md).</summary>
        public static bool SmartShows(HudElement element, FlightPhase phase)
        {
            switch (element)
            {
                case HudElement.Airspeed:
                    return phase != FlightPhase.Parked && phase != FlightPhase.Taxi;
                case HudElement.Altitude:
                    return phase == FlightPhase.InitialClimb || phase == FlightPhase.Climb || phase == FlightPhase.Cruise
                           || phase == FlightPhase.Descent || phase == FlightPhase.Approach;
                case HudElement.VerticalSpeed:
                    return phase == FlightPhase.InitialClimb || phase == FlightPhase.Climb || phase == FlightPhase.Descent
                           || phase == FlightPhase.Approach;
                case HudElement.Heading:
                    return phase == FlightPhase.Cruise;
                case HudElement.Groundspeed:
                    return phase == FlightPhase.Taxi;
                default:
                    return false;
            }
        }

        /// <summary>Warnings that force an element into view.</summary>
        public static int RelatedWarnings(HudElement element)
        {
            switch (element)
            {
                case HudElement.Airspeed: return FlightWarnings.Bit(FlightWarning.Stall) | FlightWarnings.Bit(FlightWarning.Overspeed);
                case HudElement.Altitude:
                case HudElement.VerticalSpeed: return FlightWarnings.Bit(FlightWarning.SinkRate);
                default: return 0;
            }
        }

        static ContextAction ActionFor(HudElement guide) =>
            guide == HudElement.ApproachGuide ? ContextAction.ApproachGuide : ContextAction.Navigation;

        static bool IsAvailable(ContextAction action, in DisclosureInput input)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: return input.Systems.HasParkingBrake;
                case ContextAction.Flaps: return input.Systems.HasFlaps;
                case ContextAction.Assistance: return input.HasAssistanceSettings;
                default: return input.HasRunway;
            }
        }

        bool IsEngaged(ContextAction action, in DisclosureInput input)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: return input.Systems.ParkingBrakeSet;
                case ContextAction.Flaps: return input.Systems.FlapsExtended;
                case ContextAction.ApproachGuide: return IsVisible(HudElement.ApproachGuide);
                case ContextAction.Navigation: return IsVisible(HudElement.NavigationGuide);
                default: return false;
            }
        }

        static bool IsEnabled(ContextAction action, in DisclosureInput input)
        {
            // The parking brake can be set only on the ground, and released anywhere.
            if (action == ContextAction.ParkingBrake) return input.OnGround || input.Systems.ParkingBrakeSet;
            return true;
        }

        /// <summary>The Smart profile's action table (docs/CONTEXTUAL_UI.md).</summary>
        bool IsRelevant(ContextAction action, in DisclosureInput input)
        {
            var phase = input.Phase.Phase;
            switch (action)
            {
                case ContextAction.ParkingBrake:
                    return phase == FlightPhase.Parked || phase == FlightPhase.Taxi;
                case ContextAction.Flaps:
                    return phase == FlightPhase.Taxi || phase == FlightPhase.TakeoffRoll || phase == FlightPhase.InitialClimb
                           || phase == FlightPhase.Approach || phase == FlightPhase.Landing;
                case ContextAction.ApproachGuide:
                    return phase == FlightPhase.Approach || phase == FlightPhase.Landing
                           || (phase == FlightPhase.Descent && input.DistanceToRunwayNm <= _settings.approachGuideRangeNm);
                case ContextAction.Navigation:
                    return phase == FlightPhase.Climb || phase == FlightPhase.Cruise || phase == FlightPhase.Descent;
                case ContextAction.Assistance:
                    return phase == FlightPhase.Parked; // choose before take-off; SYSTEMS reaches it any time
                default:
                    return false;
            }
        }
    }
}
