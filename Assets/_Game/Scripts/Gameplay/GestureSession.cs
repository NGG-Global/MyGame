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
    /// Turns recognised gestures into what the interface does, and what the
    /// aircraft is asked: the systems handle pull unfolds the arc and hands over
    /// to the flap lever, the HUD band pull previews a HUD density, a drag on the
    /// world looks around the chase view, a long press explains. It owns the
    /// interaction state (what the current gesture is doing), feeds the views each
    /// frame, and reaches the aircraft only through <see cref="FlightSession"/>
    /// (every system change passes the gate). Previews never touch committed state:
    /// the flap lever shows a detent the aircraft does not have until release, and the
    /// density preview is a disclosure input, not the saved profile.
    ///
    /// Tap-first (docs/TAP_DISCLOSURE_DESIGN.md): a tap on the handle opens the arc as a
    /// menu that stays; a tap on a segment acts, or fans out its options; a tap on the
    /// handle again, on the open action, or anywhere outside folds it. The arc and its
    /// sub-arc are registered with the <see cref="DisclosureCoordinator"/>, which also
    /// closes them when another cluster opens. The pull remains as the continuous way
    /// through the same arc.
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
            [Tooltip("Resting on FLAPS this long hands the gesture over to the lever, s.")]
            public float leverHandoverSeconds = 0.18f;
            [Tooltip("Pulling through the FLAPS segment this far beyond the band also hands over, canvas units.")]
            public float leverHandoverOverreach = 30f;
            [Tooltip("Chase free look: degrees for a drag across the full screen height.")]
            public float lookDegreesPerScreenHeight = 120f;
            [Tooltip("Chase zoom: metres of distance for a pinch across the full screen height.")]
            public float zoomMetresPerScreenHeight = 30f;
            [Tooltip("The handle hint (the arc shown briefly) is offered at most this many times, ever.")]
            public int handleHintLimit = 3;
            [Tooltip("Hints at least this far apart, s.")]
            public float handleHintSpacingSeconds = 25f;
            [Tooltip("A long press shows its hold ring once this share of the threshold has passed (quick taps never flash).")]
            [Range(0f, 1f)] public float holdRingFrom = 0.2f;
        }

        const int MaxInner = SystemsArcModel.MaxItems;
        const int MaxSubOptions = 5;
        static readonly AircraftSystem[] OuterSystems = { AircraftSystem.LandingLight, AircraftSystem.NavigationLights };

        /// <summary>The options a rested segment fans out: short labels, which one is current, and what choosing one does.</summary>
        sealed class SubOptions
        {
            public string[] Labels;
            public Func<int> Current;
            public Action<int> Choose;
        }
        static readonly string[] OuterLabels = { "LDG LIGHT", "NAV LIGHTS" };
        static readonly string[] OuterEngagedLabels = { "LDG LIGHT ON", "NAV LIGHTS ON" };

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
        readonly FlapsLeverInteraction _lever;
        readonly HudDensityModel _density;
        readonly ContextAction[] _innerActions = new ContextAction[MaxInner];
        readonly SubOptions[] _subOptions = new SubOptions[ContextActions.Count];
        readonly string[] _flapLabels;
        readonly StringBuilder _ownershipText = new StringBuilder(128);
        int _innerCount;
        int _arcLayer = DisclosureCoordinator.None, _subArcLayer = DisclosureCoordinator.None;
        bool _arcOpen;
        Doing _doing;
        bool _gestureFirst = true, _reducedMotion;
        float _flapsHoverSince = float.NegativeInfinity;
        int _lastHovered = SystemsArcModel.NoItem;
        bool _lastHoveredOuter, _lastArmed;
        int _hintsShown;
        float _lastHintTime = float.NegativeInfinity;
        bool _everPulled;
        int _explainedTarget = -1;
        HudProfile _profile;
        bool _blocked;
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
            _arc = new SystemsArcModel(hud != null && hud.arc != null ? hud.arc.settings : null);
            _lever = lever ?? new FlapsLeverInteraction(flight, hud != null ? hud.lever : null, hud);
            _density = new HudDensityModel(hud != null && hud.ladder != null ? hud.ladder.settings : null);

            int detents = systems != null ? systems.FlapDetentCount : 0;
            _flapLabels = new string[detents];
            for (int i = 0; i < detents; i++) _flapLabels[i] = i == 0 ? "UP" : Mathf.RoundToInt(systems.FlapDetentDegrees(i)) + "°";

            ConfigureArc(flight.AvailableActions);
            _surface.TargetResolver = FindTarget;
            _surface.GestureRaised += OnGesture;

            // The arc is a transient interface: the coordinator closes it on an outside tap or when another cluster opens.
            _disclosure = surface.Disclosure;
            _arcLayer = _disclosure.Register("Systems arc", IsOnArc, CollapseArc);
            _subArcLayer = _disclosure.Register("Systems sub-arc", IsOnOuterRing, CollapseSubArc, true, _arcLayer);
            if (_hud != null && _hud.arcHitArea != null) _hud.arcHitArea.Filter = ArcHitFilter;
            if (_hud != null && _hud.inspector != null) _hud.inspector.ModeToggleRequested += ToggleMode;

            // Gesture-first until told otherwise: the handle and arc are on, the rail is off.
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
        /// <summary>The arc stands open as a tappable menu (no finger needed).</summary>
        public bool ArcOpen => _arcOpen;
        /// <summary>The options of one action are fanned out on the outer ring of the open arc.</summary>
        public bool SubArcOpen => _arcOpen && _arc.OuterRevealed && _arc.OuterParent != SystemsArcModel.NoItem;
        public DisclosureCoordinator Disclosure => _disclosure;
        /// <summary>The arc's id with the coordinator (tests, inspectors).</summary>
        public int ArcLayer => _arcLayer;
        public int SubArcLayer => _subArcLayer;
        public SystemsArcModel Arc => _arc;
        public FlapsLeverModel Lever => _lever.Model;
        public HudDensityModel Density => _density;
        /// <summary>While the HUD band is pulled: the profile to present instead of the committed one.</summary>
        public bool HasPreviewProfile => _doing == Doing.DensityPull && _density.IsActive;
        public HudProfile PreviewProfile => _density.Preview;
        public int HintsShown => _hintsShown;
        /// <summary>The sub-arc is open from this inner segment (its options), or -1 (none, or the free lights ring).</summary>
        public int OpenSubArcParent => (_doing == Doing.ArcPull || _arcOpen) && _arc.OuterRevealed ? _arc.OuterParent : SystemsArcModel.NoItem;

        /// <summary>
        /// Gives an action a second level: tapping or resting on its segment fans its options
        /// out on the outer ring (NAV: the guide and the destinations; ASSIST: the settings and
        /// the profiles). By convention the first option is the action itself, so the tap path
        /// loses nothing the rail offers. Labels are fixed strings; <paramref name="current"/>
        /// marks the engaged option; <paramref name="choose"/> runs when one is chosen. At
        /// most five options.
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

        /// <summary>The player's interface choice changed (development): the handle and arc, or the rail.</summary>
        public event Action<InteractionMode> ModeChanged;

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

        public void ToggleMode() => SetGestureFirst(!_gestureFirst);

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
            CloseArc();
            EndGesture();
            _surface.CancelAll();
        }

        /// <summary>The aircraft's actions, in <see cref="ContextAction"/> order, keep fixed arc slots for the session.</summary>
        void ConfigureArc(int availableMask)
        {
            _innerCount = 0;
            for (int a = 0; a < ContextActions.Count && _innerCount < MaxInner; a++)
            {
                if ((availableMask & (1 << a)) == 0) continue;
                var action = (ContextAction)a;
                _innerActions[_innerCount] = action;
                _arc.InnerItem(_innerCount) = new ArcItem
                {
                    Label = ContextualHud.ActionLabel(action, false),
                    EngagedLabel = ContextualHud.ActionLabel(action, true),
                    Emphasis = ArcEmphasis.Quiet,
                    Available = true,
                };
                _innerCount++;
            }
            _arc.SetInnerCount(_innerCount);
            ConfigureLightsRing();
        }

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

            RefreshArcEmphasis(disclosure, systems);
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

        void RefreshArcEmphasis(DisclosureController disclosure, AircraftSystems systems)
        {
            for (int i = 0; i < _innerCount; i++)
            {
                var action = _innerActions[i];
                ref var item = ref _arc.InnerItem(i);
                if (disclosure == null) { item.Emphasis = ArcEmphasis.Quiet; continue; }
                if (disclosure.IsActionEngaged(action)) item.Emphasis = ArcEmphasis.Engaged;
                else if (!disclosure.IsActionEnabled(action) && disclosure.GetActionReason(action) != DisclosureReason.None) item.Emphasis = ArcEmphasis.Disabled;
                else if (disclosure.IsActionVisible(action) && disclosure.GetActionReason(action) != DisclosureReason.RailExpanded) item.Emphasis = ArcEmphasis.Full;
                else item.Emphasis = ArcEmphasis.Quiet;
            }
            if (_arc.OuterParent == SystemsArcModel.NoItem)
                for (int i = 0; i < OuterSystems.Length && i < _arc.OuterCount; i++)
                {
                    ref var item = ref _arc.OuterItem(i);
                    item.Emphasis = systems != null && systems.Get(OuterSystems[i]) == 1 ? ArcEmphasis.Engaged : ArcEmphasis.Quiet;
                }
        }

        /// <summary>
        /// The first time the phase of flight offers an action, the handle is pointed out: a
        /// cue at the handle (the hint policy, remembered once the arc has been opened) or,
        /// without one, the arc showing itself briefly. Opening is learnt by seeing.
        /// </summary>
        void OfferHandleHint(DisclosureController disclosure)
        {
            if (!_gestureFirst || _everPulled || _blocked || disclosure == null || _hud == null || _hud.arc == null) return;
            if (_doing != Doing.Nothing || _arcOpen) return;
            bool offered = false;
            for (int i = 0; i < _innerCount && !offered; i++)
                offered = disclosure.IsActionVisible(_innerActions[i]) && disclosure.GetActionReason(_innerActions[i]) == DisclosureReason.Phase;
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
                // Resting on a segment that has options: the ring fills until the sub-arc opens.
                var handle = _surface.Channel(InteractionRoute.SystemsHandle);
                bool pending = handle != null && handle.IsTracking && !_arc.OuterRevealed && _arc.HasHovered && !_arc.HoveredIsOuter
                               && HasSubOptions(_innerActions[_arc.HoveredIndex]) && !handle.IsDwelling;
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
            _report.Ownership = _ownershipText.ToString();
            _report.Mode = _doing == Doing.Nothing && _arcOpen ? (SubArcOpen ? "ArcOpen+SubArc" : "ArcOpen") : _doing.ToString();
            _report.HudProfile = profile.ToString();
            _report.PreviewProfile = HasPreviewProfile ? _density.Preview.ToString() : null;
            _report.Diary = _diary != null ? _diary.Summary() : null;
            if (_doing == Doing.ArcPull)
            {
                _report.Target = _arc.HasHovered ? _arc.Item(_arc.HoveredIsOuter, _arc.HoveredIndex).Label : "-";
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

        void OnHandle(in GestureEvent e)
        {
            var channel = _surface.Channel(InteractionRoute.SystemsHandle);
            switch (e.Kind)
            {
                case GestureKind.Pull:
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
                    break;
                case GestureKind.Dwell:
                    if (e.Type == GestureEventType.Dwell && _doing == Doing.ArcPull && !_arc.OuterRevealed) OpenSecondLevel();
                    break;
                case GestureKind.Tap:
                    if (e.Type != GestureEventType.Completed || _hud == null || _hud.arc == null) break;
                    if (_arcOpen) OnArcTap(e.Position);
                    else OpenArc();
                    break;
                case GestureKind.LongPress:
                    OnLongPress(e, HudExplanations.HandleId, _hud != null && _hud.handleHotspot != null ? (RectTransform)_hud.handleHotspot.transform : null);
                    break;
            }
        }

        /// <summary>The finger rested: the hovered segment's options fan out from it, or the lights ring opens on nothing.</summary>
        void OpenSecondLevel()
        {
            if (_arc.HasHovered && !_arc.HoveredIsOuter)
            {
                OpenSubArc(_arc.HoveredIndex);
                return;
            }
            if (_arc.HasHovered) return;
            if (_arcOpen)
            {
                CloseSubArc(); // the open arc already shows the lights ring; resting on nothing only returns to it
                return;
            }
            ConfigureLightsRing();
            _arc.RevealOuter(true);
            HudButton.Report(UiFeedback.Tick);
        }

        /// <summary>The options of an inner segment fan out on the outer ring (a child of the open arc for the coordinator).</summary>
        bool OpenSubArc(int index)
        {
            var options = _subOptions[(int)_innerActions[index]];
            if (options == null) return false; // a segment without options: resting does nothing (and never selects)
            int current = options.Current != null ? options.Current() : -1;
            for (int i = 0; i < options.Labels.Length; i++)
                _arc.OuterItem(i) = new ArcItem { Label = options.Labels[i], Emphasis = i == current ? ArcEmphasis.Engaged : ArcEmphasis.Full, Available = true };
            _arc.RevealSubArc(index, options.Labels.Length);
            if (_arcOpen && _arc.OuterRevealed) _disclosure.Open(_subArcLayer);
            HudButton.Report(UiFeedback.Tick);
            return true;
        }

        // ---- Tap-first arc ----------------------------------------------------------------------

        /// <summary>A tap on the handle: the arc opens and stays, with every reachable system on it (the lights ring too).</summary>
        public void OpenArc()
        {
            if (_arcOpen || _hud == null || _hud.arc == null || !_gestureFirst || _blocked) return;
            EndGesture();
            _arcOpen = true;
            _everPulled = true;
            _hints?.Learned(GestureId.PullSystems, Time.unscaledTime);
            _arc.Begin();
            _arc.Update(Vector2.zero, 1f); // unfolded, nothing under a finger
            ConfigureLightsRing();
            _arc.RevealOuter(true);
            _hud.arc.SetTakesTouches(true);
            _hud.SetHandleState(false, true);
            _disclosure.Open(_arcLayer);
            HudButton.Report(UiFeedback.Tap);
        }

        /// <summary>The arc folds (the trigger again, a choice made, an outside tap, a reset). Nothing is applied.</summary>
        public void CloseArc(DisclosureCollapseReason reason = DisclosureCollapseReason.Trigger)
        {
            if (!_arcOpen) return;
            _arcOpen = false;
            if (_doing == Doing.ArcPull)
            {
                // A finger sliding on the arc when it was closed from elsewhere: its gesture ends without choosing.
                _surface.Channel(InteractionRoute.SystemsHandle)?.Cancel();
                EndGesture();
            }
            _arc.RevealOuter(false);
            _arc.Update(Vector2.zero, 0f);
            if (_hud != null)
            {
                _hud.arc?.SetTakesTouches(false);
                _hud.arc?.Fold();
                _hud.SetHandleState(false, false);
            }
            _disclosure.Close(_arcLayer, reason); // also closes the sub-arc; no-op when the coordinator closed it first
        }

        /// <summary>The coordinator collapsed the arc (outside tap, another cluster, focus loss).</summary>
        void CollapseArc(DisclosureCollapseReason reason) => CloseArc(reason);

        /// <summary>Back from an action's options to the arc, which shows the lights ring again.</summary>
        void CloseSubArc()
        {
            bool wasOpen = _arc.OuterRevealed && _arc.OuterParent != SystemsArcModel.NoItem;
            _arc.RevealOuter(false);
            if (_arcOpen)
            {
                ConfigureLightsRing();
                _arc.RevealOuter(true);
            }
            if (wasOpen) _disclosure.Close(_subArcLayer, DisclosureCollapseReason.ParentTap);
        }

        void CollapseSubArc(DisclosureCollapseReason reason)
        {
            if (!_arcOpen) return;
            _arc.RevealOuter(false);
            ConfigureLightsRing();
            _arc.RevealOuter(true);
        }

        /// <summary>A tap on the open arc: a segment acts or fans out, the handle folds the arc, the open action folds its options.</summary>
        void OnArcTap(Vector2 screenPoint)
        {
            if (_hud == null) return;
            Vector2 fromAnchor = _hud.ToLocal(screenPoint) - _hud.HandleCentre;
            _arc.Update(fromAnchor, 1f);
            bool hovered = _arc.HasHovered, armed = _arc.IsArmed, outer = _arc.HoveredIsOuter;
            int index = _arc.HoveredIndex;
            _arc.Update(Vector2.zero, 1f); // nothing stays lifted after a tap
            if (hovered)
            {
                if (!armed) { HudButton.Report(UiFeedback.Tap); return; } // not available now: the arc stays
                ChooseByTap(outer, index);
                return;
            }
            if (fromAnchor.magnitude < _arc.Settings.selectionInnerRadius)
            {
                CloseArc(DisclosureCollapseReason.Trigger); // the trigger again
                HudButton.Report(UiFeedback.Tap);
                return;
            }
            if (SubArcOpen) CloseSubArc(); // the band on nothing while options are out: back to the arc
        }

        void ChooseByTap(bool outer, int index)
        {
            if (!outer)
            {
                if (_arc.OuterRevealed && _arc.OuterParent == index)
                {
                    CloseSubArc(); // the open action again: back to the arc
                    HudButton.Report(UiFeedback.Tap);
                    return;
                }
                if (HasSubOptions(_innerActions[index]))
                {
                    if (SubArcOpen) CloseSubArc();
                    OpenSubArc(index);
                    return;
                }
                PerformInner(index);
                CloseArc(DisclosureCollapseReason.Owner);
                return;
            }
            if (_arc.OuterParent >= 0) ChooseSubOption(_arc.OuterParent, index);
            else ToggleOuter(index);
            CloseArc(DisclosureCollapseReason.Owner);
        }

        /// <summary>A screen point on the open arc's band (for the coordinator: a tap there is inside, not outside).</summary>
        bool IsOnArcBand(Vector2 screenPoint)
        {
            if (!_arcOpen || _hud == null) return false;
            Vector2 v = _hud.ToLocal(screenPoint) - _hud.HandleCentre;
            float r = v.magnitude;
            var s = _arc.Settings;
            if (r < s.innerRadius || r > s.outerRadius) return false;
            return _arc.SlotAt(GestureMath.AngleDegrees(v), _arc.InnerCount) != SystemsArcModel.NoItem;
        }

        /// <summary>A screen point on the open arc's outer ring (the lights, or an action's options).</summary>
        bool IsOnOuterRing(Vector2 screenPoint)
        {
            if (!_arcOpen || _hud == null || !_arc.OuterRevealed) return false;
            Vector2 v = _hud.ToLocal(screenPoint) - _hud.HandleCentre;
            float r = v.magnitude;
            var s = _arc.Settings;
            if (r < s.OuterRingInner || r > s.OuterRingOuter) return false;
            return _arc.OuterAt(GestureMath.AngleDegrees(v)) != SystemsArcModel.NoItem;
        }

        /// <summary>The open arc as one surface for the coordinator: its band and its outer ring, the same shape its hotspot takes presses on.</summary>
        bool IsOnArc(Vector2 screenPoint) => IsOnArcBand(screenPoint) || IsOnOuterRing(screenPoint);

        /// <summary>The arc's hotspot shape: presses land only on the band and the outer ring, never on the controls around them.</summary>
        bool ArcHitFilter(Vector2 screenPoint, Camera eventCamera) => IsOnArc(screenPoint);

        void ConfigureLightsRing()
        {
            _arc.SetOuterCount(OuterSystems.Length);
            for (int i = 0; i < OuterSystems.Length; i++)
                _arc.OuterItem(i) = new ArcItem { Label = OuterLabels[i], EngagedLabel = OuterEngagedLabels[i], Emphasis = ArcEmphasis.Quiet, Available = true };
        }

        void BeginArc()
        {
            EndGesture();
            _doing = Doing.ArcPull;
            _everPulled = true;
            _hints?.Learned(GestureId.PullSystems, Time.unscaledTime);
            if (!_arcOpen) _arc.Begin(); // from the open arc the finger slides over what is already there
            _lastHovered = SystemsArcModel.NoItem;
            _lastArmed = false;
            _flapsHoverSince = float.NegativeInfinity;
            if (_hud != null) _hud.SetHandleState(true, true);
        }

        void UpdateArc(in GestureEvent e, GestureRecognizer channel)
        {
            if (_hud == null) return;
            Vector2 fromAnchor = _hud.ToLocal(e.Position) - _hud.HandleCentre;
            _arc.Update(fromAnchor, _arcOpen ? 1f : e.Progress);
            channel?.SetTargetArmed(_arc.IsArmed);

            bool onFlaps = _arc.HasHovered && !_arc.HoveredIsOuter && _innerActions[_arc.HoveredIndex] == ContextAction.Flaps;
            if (_arc.HasHovered && (_arc.HoveredIndex != _lastHovered || _arc.HoveredIsOuter != _lastHoveredOuter))
            {
                HudButton.Report(UiFeedback.Tick); // a tick as the thumb crosses onto a segment
                _flapsHoverSince = onFlaps ? Time.unscaledTime : float.NegativeInfinity;
            }
            else if (!onFlaps) _flapsHoverSince = float.NegativeInfinity;
            _lastHovered = _arc.HoveredIndex;
            _lastHoveredOuter = _arc.HoveredIsOuter;

            if (onFlaps && _arc.IsArmed)
            {
                bool rested = Time.unscaledTime - _flapsHoverSince >= _settings.leverHandoverSeconds;
                bool through = fromAnchor.magnitude >= _arc.Settings.outerRadius + _settings.leverHandoverOverreach;
                if (rested || through) HandOverToLever(e, channel);
            }
        }

        /// <summary>The same finger continues into the lever: the arc folds, the current detent sits under the thumb.</summary>
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
                else if (_arc.OuterParent >= 0) ChooseSubOption(_arc.OuterParent, _arc.HoveredIndex);
                else ToggleOuter(_arc.HoveredIndex);
            }
            EndGesture();
            if (chose) CloseArc(DisclosureCollapseReason.Owner); // a slide on the open arc that chose: the menu folds
        }

        void ChooseSubOption(int parent, int option)
        {
            var options = _subOptions[(int)_innerActions[parent]];
            if (options == null || option < 0 || option >= options.Labels.Length) return;
            options.Choose(option);
            HudButton.Report(UiFeedback.Confirm);
        }

        void PerformInner(int index)
        {
            // FLAPS, tapped or released on without the lever, opens the same tappable detent selector as the
            // cockpit lever (the session steps one detent instead when there is no selector), like the rail.
            _flight.PerformContextAction(_innerActions[index]);
            HudButton.Report(UiFeedback.Confirm);
        }

        void ToggleOuter(int index)
        {
            var systems = _flight.Systems;
            if (systems == null || index < 0 || index >= OuterSystems.Length) return;
            var system = OuterSystems[index];
            var outcome = _flight.RequestSystemChange(new SystemChange(system, systems.Get(system) == 1 ? 0 : 1));
            HudButton.Report(outcome == SystemChangeGate.Outcome.Applied ? UiFeedback.Confirm : UiFeedback.Tap);
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
            if (_contextualHud == null) return -1;
            for (int k = 0; k < ContextualHud.WarningSlotCount; k++)
            {
                var slot = _contextualHud.warningSlots[k];
                if (slot == null || slot.alpha < 0.5f) continue;
                int warning = _contextualHud.WarningInSlot(k);
                if (warning >= 0 && Contains((RectTransform)slot.transform, screenPoint)) return HudExplanations.WarningBase + warning;
            }
            for (int i = 0; i < ContextualHud.DataItemCount; i++)
            {
                var item = _contextualHud.dataItems[i];
                if (item != null && item.alpha > 0.05f && Contains((RectTransform)item.transform, screenPoint)) return i;
            }
            if (_contextualHud.approachGuide != null && _contextualHud.approachGuide.alpha > 0.05f && Contains((RectTransform)_contextualHud.approachGuide.transform, screenPoint))
                return (int)HudElement.ApproachGuide;
            if (_contextualHud.navigationGuide != null && _contextualHud.navigationGuide.alpha > 0.05f && Contains((RectTransform)_contextualHud.navigationGuide.transform, screenPoint))
                return (int)HudElement.NavigationGuide;
            return -1;
        }

        RectTransform TargetRect(int target)
        {
            if (_contextualHud == null) return null;
            if (target >= HudExplanations.WarningBase && target < HudExplanations.WarningBase + ContextualHud.WarningSlotCount)
            {
                for (int k = 0; k < ContextualHud.WarningSlotCount; k++)
                    if (_contextualHud.WarningInSlot(k) == target - HudExplanations.WarningBase && _contextualHud.warningSlots[k] != null)
                        return (RectTransform)_contextualHud.warningSlots[k].transform;
                return null;
            }
            if (target >= 0 && target < ContextualHud.DataItemCount) return _contextualHud.dataItems[target] != null ? (RectTransform)_contextualHud.dataItems[target].transform : null;
            if (target == (int)HudElement.ApproachGuide) return _contextualHud.approachGuide != null ? (RectTransform)_contextualHud.approachGuide.transform : null;
            if (target == (int)HudElement.NavigationGuide) return _contextualHud.navigationGuide != null ? (RectTransform)_contextualHud.navigationGuide.transform : null;
            return null;
        }

        static bool Contains(RectTransform rect, Vector2 screenPoint) => ScreenRects.Contains(rect, screenPoint);
    }
}
