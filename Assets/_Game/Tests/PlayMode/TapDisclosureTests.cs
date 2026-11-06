using System.Collections;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Gameplay;
using OpenSkies.Gestures;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The tap-first disclosure contract (docs/TAP_DISCLOSURE_DESIGN.md) inside a real
    /// <see cref="FlightSession"/> with the Rigidbody aircraft, the touch controls, the
    /// contextual and gesture HUDs, the flight settings card, the cockpit focus panel
    /// and, where needed, the game modes with the pause sheet, on a 1920 × 1080
    /// world-space canvas driven by synthetic pointers. Proves opening and closing by
    /// taps, nesting, cluster switching, the consumed outside tap, touch ownership,
    /// pending-change cancellation, warning persistence, the paused interface, rapid
    /// taps, focus loss and hidden controls taking no input. Not how any of it feels.
    /// </summary>
    public class TapDisclosureTests
    {
        sealed class ScriptedControls : IFlightControlSource
        {
            public FlightControls Controls = FlightControls.Neutral;
            public FlightControls Read(float deltaTime) => Controls;
            public void ResetControls() => Controls = FlightControls.Neutral;
        }

        GameObject _root;
        FlightSession _session;
        GameModeSession _modes;
        TouchFlightControls _touch;
        ContextualHud _hud;
        GestureHud _gestureHud;
        InteractionSurface _surface;
        FlightSettingsPanel _settings;
        CockpitFocusPanel _focus;
        PauseSheet _sheet;
        CameraRig _camera;
        ScriptedControls _controls;
        float _width;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_root != null) Object.Destroy(_root);
        }

        IEnumerator Build(float width = 1920f, bool withModes = false)
        {
            _width = width;
            _root = new GameObject("TapDisclosureTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, new Vector3(0f, 0f, -500f), out var telemetry);

            var cameraGo = new GameObject("Camera", typeof(Camera));
            cameraGo.transform.SetParent(_root.transform, false);
            _camera = cameraGo.AddComponent<CameraRig>();
            _camera.target = aircraft.transform;

            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(_root.transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)canvasGo.transform;
            canvas.sizeDelta = new Vector2(width, 1080f);
            canvas.pivot = Vector2.zero;
            canvas.position = new Vector3(0f, 0f, 5000f);
            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(canvas, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var surfaceGo = new GameObject("InteractionSurface", typeof(RectTransform), typeof(RaycastOnlyGraphic));
            ((RectTransform)surfaceGo.transform).SetParent(safeArea, false);
            _surface = surfaceGo.AddComponent<InteractionSurface>();
            _surface.surface = surfaceGo.GetComponent<RaycastOnlyGraphic>();
            _surface.screenHeightOverride = 1080f;

            _touch = TouchControlsFactory.Build(safeArea, font);
            _touch.visibility = TouchControlsVisibility.Always;
            _focus = CockpitFocusPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset, _surface);
            _focus.SetReducedMotion(true);
            _settings = FlightSettingsPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            _settings.SetReducedMotion(true);
            _hud = ContextualHudFactory.Build(safeArea, font);
            _hud.SetMotionPreference(MotionPreference.Reduced);
            _gestureHud = GestureHudFactory.Build(safeArea, font, _surface);
            MissionCard card = null;
            if (withModes)
            {
                card = MissionUiFactory.BuildMissionCard(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset,
                    CockpitFocusPanelFactory.ColumnBottom, CockpitFocusPanelFactory.ColumnTop);
                _sheet = PauseSheetFactory.Build(safeArea, font, _surface);
            }
            Canvas.ForceUpdateCanvases();

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = _camera;
            _session.touchControls = _touch;
            _session.contextualHud = _hud;
            _session.focusPanel = _focus;
            _session.flightSettings = _settings;
            _session.interactionSurface = _surface;
            _session.gestureHud = _gestureHud;
            _session.rememberHudPreferences = false;
            _session.homeRunway = new RunwayInfo(Vector3.zero, 0f, 2000f, 30f);
            _controls = new ScriptedControls();
            _session.SetControlSource(_controls);
            sessionGo.SetActive(true);
            _gestureHud.SetReducedMotion(true);
            _session.ApplyMotionPreference(MotionPreference.Reduced);

            if (withModes)
            {
                var modesGo = new GameObject("GameModes");
                modesGo.SetActive(false);
                modesGo.transform.SetParent(_root.transform, false);
                _modes = modesGo.AddComponent<GameModeSession>();
                _modes.flight = _session;
                _modes.card = card;
                _modes.pauseSheet = _sheet;
                _modes.showModeSelectOnStart = false;
                _modes.rememberProgress = false;
                modesGo.SetActive(true);
                _sheet.SetReducedMotion(true);
            }

            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate(); // the aircraft settles on its wheels
            yield return null;
            yield return null;
        }

        // ---- Helpers ------------------------------------------------------------------------------

        static PointerEventData Pointer(int id, Vector2 position) =>
            new PointerEventData(EventSystem.current) { pointerId = id, position = position, pressPosition = position };

        static Rect WorldRect(Component component)
        {
            var corners = new Vector3[4];
            ((RectTransform)component.transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        Vector2 HandleCentre => WorldRect(_gestureHud.handleHotspot).center;
        Vector2 Sky => new Vector2(_width * 0.5f, 900f);
        GestureSession Gestures => _session.Gestures;
        DisclosureCoordinator Coordinator => _session.Coordinator;

        Vector2 ArcPoint(int slot, float radius) => HandleCentre + GestureMath.FromAngle(Gestures.Arc.SlotAngle(slot)) * radius;

        Vector2 OuterPoint(int option)
        {
            var s = Gestures.Arc.Settings;
            return HandleCentre + GestureMath.FromAngle(Gestures.Arc.OuterAngle(option)) * ((s.OuterRingInner + s.OuterRingOuter) * 0.5f);
        }

        int SlotOf(ContextAction action)
        {
            int slot = 0;
            for (int a = 0; a < ContextActions.Count; a++)
            {
                if ((_session.AvailableActions & (1 << a)) == 0) continue;
                if ((ContextAction)a == action) return slot;
                slot++;
            }
            return -1;
        }

        /// <summary>A tap on the handle hotspot (press and release in place).</summary>
        IEnumerator TapHandle(int id)
        {
            var p = Pointer(id, HandleCentre);
            _gestureHud.handleHotspot.OnPointerDown(p);
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
        }

        /// <summary>A tap on the open arc's hit area (the segments and the outer ring).</summary>
        IEnumerator TapArc(int id, Vector2 point)
        {
            var p = Pointer(id, point);
            _gestureHud.arcHotspot.OnPointerDown(p);
            _gestureHud.arcHotspot.OnPointerUp(p);
            yield return null;
        }

        /// <summary>A tap on the open surface (nothing drawn on top took it), as uGUI would deliver it.</summary>
        IEnumerator TapSurface(int id, Vector2 point)
        {
            var p = Pointer(id, point);
            _surface.OnPointerDown(p);
            _surface.OnPointerUp(p);
            yield return null;
        }

        IEnumerator OpenArcByTap()
        {
            yield return TapHandle(100);
            Assert.IsTrue(Gestures.ArcOpen, "a tap on SYSTEMS opens the arc");
        }

        // ---- Opening, closing, the trigger again ------------------------------------------------

        [UnityTest]
        public IEnumerator TapOnTheHandle_OpensTheArcAndKeepsIt_TapAgainCloses()
        {
            yield return Build();
            Assert.IsFalse(Gestures.ArcOpen);
            Assert.IsFalse(_gestureHud.arc.TakesTouches, "a folded arc intercepts nothing");
            yield return OpenArcByTap();
            yield return null;
            Assert.AreEqual(GestureSession.Doing.Nothing, Gestures.Current, "no finger is needed to keep it open");
            Assert.AreEqual(1f, _gestureHud.arc.ShownUnfold, 1e-3f, "fully unfolded (reduced motion: at once)");
            Assert.IsTrue(_gestureHud.arc.TakesTouches);
            Assert.IsTrue(Coordinator.IsActive(Gestures.ArcLayer));
            Assert.AreEqual(1, Coordinator.Depth);
            Assert.IsTrue(Gestures.Arc.OuterRevealed, "every reachable system is on the open arc, the lights too");
            Assert.AreEqual(0, _touch.Ownership.Count, "the tap released its pointer");

            yield return TapHandle(101);
            Assert.IsFalse(Gestures.ArcOpen, "the trigger again folds it");
            Assert.IsFalse(Coordinator.HasActive);
            yield return null;
            Assert.AreEqual(0f, _gestureHud.arc.ShownUnfold, 1e-3f);
            Assert.IsFalse(_gestureHud.arc.TakesTouches);
        }

        [UnityTest]
        public IEnumerator TapOnASegment_PerformsTheAction_LikeTheRail_AndTheArcFolds()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Navigation);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            yield return OpenArcByTap();
            yield return TapArc(102, ArcPoint(slot, 230f));
            Assert.AreEqual(PlayerOverride.Show, _session.Disclosure.GetOverride(HudElement.NavigationGuide), "the same request the NAV button makes");
            Assert.IsFalse(Gestures.ArcOpen, "after a choice the arc folds");
            Assert.IsFalse(Coordinator.HasActive);
        }

        [UnityTest]
        public IEnumerator TapOnFlaps_OpensTheDetentSelector_WhichAppliesThroughTheGate_AndClosesOnAnOutsideTap()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Flaps);
            yield return OpenArcByTap();
            yield return TapArc(103, ArcPoint(slot, 230f));
            Assert.IsTrue(_focus.IsShowingSystem(out var system), "FLAPS opens the tappable detent selector");
            Assert.AreEqual(AircraftSystem.Flaps, system);
            Assert.IsFalse(Gestures.ArcOpen, "one cluster at a time: the selector replaced the arc");
            Assert.AreEqual(0, _session.Systems.FlapDetent, "opening the selector changes nothing");

            _focus.options[2].OnPointerClick(Pointer(104, Vector2.zero));
            yield return null;
            Assert.AreEqual(2, _session.Systems.FlapDetent, "a detent tapped: applied through the gate");

            yield return TapSurface(105, Sky);
            Assert.IsFalse(_focus.IsOpen, "a tap outside the selector closes it");
            Assert.AreEqual(2, _session.Systems.FlapDetent, "closing applies nothing more");
            Assert.IsFalse(Coordinator.HasActive);
        }

        // ---- Outside taps --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TapOutsideTheArc_FoldsIt_AndIsConsumed_NothingUnderneathReacts()
        {
            yield return Build();
            yield return OpenArcByTap();
            var p = Pointer(106, Sky);
            _surface.OnPointerDown(p);
            Assert.AreEqual(InteractionRoute.Dismiss, _surface.RouteOf(106), "the press is taken by the dismissal, not by the free look");
            Assert.AreSame(_surface, _touch.Ownership.OwnerOf(106), "owned, so no other consumer can have it");
            _surface.OnPointerUp(p);
            yield return null;
            Assert.IsFalse(Gestures.ArcOpen);
            Assert.IsFalse(Coordinator.HasActive);
            Assert.AreEqual(GestureSession.Doing.Nothing, Gestures.Current);
            Assert.IsFalse(_camera.IsChaseLookHeld, "the dismissing tap never became a look");
            Assert.AreEqual(0f, _camera.ChaseLook.magnitude, 1e-3f);
            Assert.AreEqual(0, _touch.Ownership.Count);

            // Quiet again: the next press on the sky is the world's.
            var q = Pointer(107, Sky);
            _surface.OnPointerDown(q);
            Assert.AreEqual(InteractionRoute.World, _surface.RouteOf(107));
            _surface.OnPointerUp(q);
        }

        [UnityTest]
        public IEnumerator DragOutsideTheArc_DoesNothingAtAll_TheArcStays()
        {
            yield return Build();
            yield return OpenArcByTap();
            var p = Pointer(108, Sky);
            _surface.OnPointerDown(p);
            p.position = Sky + new Vector2(250f, 0f);
            _surface.OnDrag(p);
            yield return null;
            _surface.OnPointerUp(p);
            yield return null;
            Assert.IsTrue(Gestures.ArcOpen, "only a tap collapses; a drag outside is neither a look nor a dismissal");
            Assert.AreEqual(0f, _camera.ChaseLook.magnitude, 1e-3f, "the camera never moved");
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        [UnityTest]
        public IEnumerator TheOpenArcBand_IsInside_TheGapBetweenBandAndRing_IsOutside()
        {
            yield return Build();
            yield return OpenArcByTap();
            var s = Gestures.Arc.Settings;
            var onBand = ArcPoint(0, (s.innerRadius + s.outerRadius) * 0.5f);
            Assert.IsFalse(Coordinator.WantsOutsideTap(onBand), "a tap on a segment is inside the arc");
            Assert.IsTrue(_gestureHud.arcHitArea.IsRaycastLocationValid(onBand, null), "the band takes the press");
            var onRing = OuterPoint(0);
            Assert.IsFalse(Coordinator.WantsOutsideTap(onRing), "the lights ring is inside too");
            Assert.IsTrue(_gestureHud.arcHitArea.IsRaycastLocationValid(onRing, null));
            var inTheGap = ArcPoint(0, (s.outerRadius + s.OuterRingInner) * 0.5f);
            Assert.IsFalse(_gestureHud.arcHitArea.IsRaycastLocationValid(inTheGap, null), "the gap between band and ring is not a target");
            Assert.IsTrue(Coordinator.WantsOutsideTap(inTheGap), "so a tap there is outside");
            yield return TapSurface(109, inTheGap);
            Assert.IsFalse(Gestures.ArcOpen, "and folds the arc");
        }

        // ---- Nesting and switching clusters --------------------------------------------------------

        [UnityTest]
        public IEnumerator TapOnAssist_FansOutItsOptions_TapAgainReturnsToTheArc_TapOutsideFoldsEverything()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Assistance);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0), "the flight settings give ASSIST its options");
            yield return OpenArcByTap();
            yield return TapArc(110, ArcPoint(slot, 230f));
            Assert.IsTrue(Gestures.ArcOpen);
            Assert.IsTrue(Gestures.SubArcOpen, "the options fanned out; the arc stays");
            Assert.AreEqual(slot, Gestures.OpenSubArcParent);
            Assert.AreEqual(2, Coordinator.Depth, "the sub-arc is a child of the arc");
            Assert.IsTrue(Coordinator.IsActive(Gestures.SubArcLayer));
            Assert.IsFalse(_settings.IsOpen, "opening the options opens no card");

            yield return TapArc(111, ArcPoint(slot, 230f));
            Assert.IsTrue(Gestures.ArcOpen, "the open action again: back to the arc");
            Assert.IsFalse(Gestures.SubArcOpen);
            Assert.AreEqual(1, Coordinator.Depth);
            Assert.IsTrue(Gestures.Arc.OuterRevealed, "the lights ring is back on the outer ring");

            yield return TapArc(112, ArcPoint(slot, 230f));
            Assert.IsTrue(Gestures.SubArcOpen);
            yield return TapSurface(113, Sky);
            Assert.IsFalse(Gestures.ArcOpen, "outside everything: the whole branch folds");
            Assert.IsFalse(Gestures.SubArcOpen);
            Assert.AreEqual(0, Coordinator.Depth);
        }

        [UnityTest]
        public IEnumerator ChoosingSettingsOnTheSubArc_SwitchesToTheFlightSettingsCluster_WhichAnOutsideTapCloses()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Assistance);
            yield return OpenArcByTap();
            yield return TapArc(114, ArcPoint(slot, 230f));
            Assert.IsTrue(Gestures.SubArcOpen);
            yield return TapArc(115, OuterPoint(0)); // the first option is the action itself: the settings card
            Assert.IsTrue(_settings.IsOpen, "the flight settings opened");
            Assert.IsFalse(Gestures.ArcOpen, "and replaced the arc: one cluster at a time");
            Assert.AreEqual(1, Coordinator.Depth);
            Assert.AreEqual(FlightAssistProfile.Assisted, _session.AssistProfile, "nothing about the flight changed");

            var cardCentre = WorldRect(_settings.card).center;
            Assert.IsFalse(Coordinator.WantsOutsideTap(cardCentre), "a tap on the card is inside");
            Assert.IsTrue(Coordinator.WantsOutsideTap(Sky));
            yield return TapSurface(116, Sky);
            Assert.IsFalse(_settings.IsOpen, "a tap outside the card closes it; there is no close button");
            Assert.AreEqual(0, Coordinator.Depth);
        }

        [UnityTest]
        public IEnumerator ChoosingAProfileOnTheSubArc_SetsIt_AndFoldsTheArc()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Assistance);
            yield return OpenArcByTap();
            yield return TapArc(117, ArcPoint(slot, 230f));
            yield return TapArc(118, OuterPoint(2)); // SETTINGS, ASSISTED, STANDARD, MANUAL: the third is STANDARD
            Assert.AreEqual(FlightAssistProfile.Standard, _session.AssistProfile);
            Assert.IsFalse(Gestures.ArcOpen);
            Assert.IsFalse(_settings.IsOpen);
            Assert.AreEqual(0, Coordinator.Depth);
        }

        [UnityTest]
        public IEnumerator OpeningTheArcWhileTheFlightSettingsAreOpen_ClosesThem()
        {
            yield return Build();
            _session.ToggleFlightSettings();
            yield return null;
            Assert.IsTrue(_settings.IsOpen);
            Assert.AreEqual(1, Coordinator.Depth);
            yield return OpenArcByTap();
            Assert.IsFalse(_settings.IsOpen, "tapping another cluster's trigger closes the previous cluster");
            Assert.AreEqual(1, Coordinator.Depth);
            Assert.AreEqual(Gestures.ArcLayer, Coordinator.ActiveRoot);
        }

        // ---- Touch ownership ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AJoystickFinger_IsNeverAnOutsideTap_AndKeepsFlying()
        {
            yield return Build();
            yield return OpenArcByTap();
            var stick = Pointer(1, WorldRect(_touch.joystick).center);
            _touch.joystick.OnPointerDown(stick);
            _surface.OnPointerDown(stick); // even if the event system dispatched the same finger on
            stick.position += new Vector2(0f, -120f);
            _touch.joystick.OnDrag(stick);
            yield return null;
            Assert.AreEqual(InteractionRoute.None, _surface.RouteOf(1), "an owned pointer is never routed, so never a dismissal");
            Assert.IsTrue(Gestures.ArcOpen, "the arc stays open while the player flies");
            Assert.That(_session.Router.Read(1f).Pitch, Is.GreaterThan(0.5f), "the stick steers while the arc is open");
            _touch.joystick.OnPointerUp(stick);
            _surface.OnPointerUp(stick);
            yield return null;
            Assert.IsTrue(Gestures.ArcOpen, "releasing the stick is not a tap outside");
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        [UnityTest]
        public IEnumerator TheArcHitArea_NeverCoversAFlightControl_AndTakesNothingWhenFolded()
        {
            yield return Build();
            var slotPoint = ArcPoint(0, 230f);
            Assert.IsFalse(_gestureHud.arcHitArea.IsRaycastLocationValid(slotPoint, null), "folded: a press on the band's place goes through");
            yield return OpenArcByTap();
            Assert.IsTrue(_gestureHud.arcHitArea.IsRaycastLocationValid(slotPoint, null));
            foreach (var control in new Component[] { _touch.joystick, _touch.throttle, _touch.brake })
            {
                var rect = WorldRect(control);
                Assert.IsFalse(_gestureHud.arcHitArea.IsRaycastLocationValid(rect.center, null), control.name + " under the arc's hit area");
            }
            Assert.IsFalse(_gestureHud.arcHitArea.IsRaycastLocationValid(HandleCentre, null), "the handle is its own hotspot");
            Assert.IsFalse(_settings.group.blocksRaycasts, "a closed card intercepts nothing");
            Assert.IsFalse(_focus.instrumentGroup.blocksRaycasts);
            Assert.IsFalse(_focus.systemGroup.blocksRaycasts);
        }

        // ---- Safety ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator PendingHazardousChange_IsCancelledByTheOutsideTap_NothingApplied()
        {
            yield return Build();
            var airborne = MissionStarts.Airborne(_session.aircraft.configuration, new Vector3(0f, 400f, 0f), 0f);
            _session.StartFlight(airborne, 0);
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsFalse(_session.Telemetry.WeightOnWheels, "in the air");
            Assert.AreEqual(1, _session.Systems.Get(AircraftSystem.Ignition));

            _session.Cockpit.OpenSystem(AircraftSystem.Ignition);
            yield return null;
            Assert.IsTrue(_focus.IsShowingSystem(out var system));
            Assert.AreEqual(AircraftSystem.Ignition, system);
            _focus.options[0].OnPointerClick(Pointer(119, Vector2.zero)); // OFF
            yield return null;
            Assert.AreEqual(0, _focus.PendingConfirmation, "engine off in flight waits for a second tap");
            Assert.AreEqual(1, _session.Systems.Get(AircraftSystem.Ignition), "nothing applied yet");

            yield return TapSurface(120, Sky);
            Assert.IsFalse(_focus.IsOpen, "the outside tap closed the selector");
            Assert.AreEqual(-1, _focus.PendingConfirmation, "and dropped the pending confirmation");
            Assert.AreEqual(1, _session.Systems.Get(AircraftSystem.Ignition), "the engine keeps running");
            Assert.AreEqual(SystemChangeGate.Outcome.AwaitingConfirmation,
                _session.RequestSystemChange(new SystemChange(AircraftSystem.Ignition, 0)), "a later request starts the confirmation afresh");
            _session.RequestSystemChange(new SystemChange(AircraftSystem.Ignition, 1)); // leave it running
        }

        [UnityTest]
        public IEnumerator Warnings_ArePersistentOverlays_UntouchedByOutsideTaps()
        {
            yield return Build();
            _session.RequestSystemChange(new SystemChange(AircraftSystem.Ignition, 0)); // on the ground: applied at once
            _controls.Controls = new FlightControls(0f, 0f, 0f, 0.6f);
            // The caution needs its delay; the HUD presents only once the phase estimator has a first estimate.
            float end = Time.unscaledTime + 8f;
            while (Time.unscaledTime < end && !(_session.Warnings.IsActive(FlightWarning.EngineOff) && _session.PhaseEstimator.TransitionTotal > 0))
                yield return null;
            Assert.IsTrue(_session.Warnings.IsActive(FlightWarning.EngineOff), "power expected with the engine off: the caution is up");
            Assert.That(_session.PhaseEstimator.TransitionTotal, Is.GreaterThan(0));
            yield return null;
            yield return null;
            Assert.AreEqual(1f, _hud.warningSlots[0].alpha, 1e-3f, "the banner is shown");
            Assert.AreEqual(0, Coordinator.Depth, "a warning is not a transient interface: nothing to dismiss");
            var banner = WorldRect(_hud.warningSlots[0]).center;
            Assert.IsFalse(Coordinator.WantsOutsideTap(banner));

            yield return OpenArcByTap();
            yield return TapSurface(121, Sky);
            Assert.IsFalse(Gestures.ArcOpen);
            Assert.IsTrue(_session.Warnings.IsActive(FlightWarning.EngineOff), "the outside tap dismissed the arc, never the warning");
            Assert.AreEqual(1f, _hud.warningSlots[0].alpha, 1e-3f);

            yield return OpenArcByTap();
            yield return TapSurface(122, banner);
            Assert.IsFalse(Gestures.ArcOpen, "a tap on the banner is outside the arc");
            Assert.IsTrue(_session.Warnings.IsActive(FlightWarning.EngineOff), "and the banner is not dismissable");
            _controls.Controls = FlightControls.Neutral;
            _session.RequestSystemChange(new SystemChange(AircraftSystem.Ignition, 1));
        }

        // ---- Pause ---------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator PausedInterface_OutsideTapNeverResumes_AndFoldsTheExpandedSettingsBackToIt()
        {
            yield return Build(withModes: true);
            yield return OpenArcByTap();
            _modes.OpenMenu();
            yield return null;
            Assert.IsTrue(_modes.IsPaused);
            Assert.IsTrue(_sheet.IsOpen);
            Assert.IsFalse(Gestures.ArcOpen, "the pause folded the arc");
            Assert.AreEqual(1, Coordinator.Depth);
            Assert.AreEqual(_modes.PauseSheetLayer, Coordinator.ActiveRoot);
            Assert.IsFalse(Coordinator.DismissesOnOutsideTap(_modes.PauseSheetLayer), "resuming is a deliberate action");

            _sheet.scrimButton.OnPointerClick(Pointer(123, Sky));
            yield return null;
            Assert.IsTrue(_modes.IsPaused, "a tap on the dimmed world does not resume");
            Assert.IsTrue(_sheet.IsOpen);
            Assert.AreEqual(0f, Time.timeScale);

            _sheet.GoTo(SheetModel.SecondStop);
            yield return null;
            yield return null;
            Assert.AreEqual(SheetModel.SecondStop, _sheet.Stop);
            Assert.IsTrue(_sheet.SettingsVisible);
            Assert.AreEqual(2, Coordinator.Depth, "the settings are the sheet's child");
            Assert.IsTrue(Coordinator.IsActive(_modes.PauseSheetSettingsLayer));

            _sheet.scrimButton.OnPointerClick(Pointer(124, Sky));
            yield return null;
            yield return null;
            Assert.AreEqual(SheetModel.FirstStop, _sheet.Stop, "outside the expanded settings: back to the paused sheet");
            Assert.IsFalse(_sheet.SettingsVisible);
            Assert.IsTrue(_modes.IsPaused, "still paused");
            Assert.AreEqual(1, Coordinator.Depth);

            _sheet.buttons[0].OnPointerClick(Pointer(125, Vector2.zero)); // RESUME
            yield return null;
            Assert.IsFalse(_modes.IsPaused, "RESUME is the deliberate way out");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(0, Coordinator.Depth);
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        // ---- Rapid taps, focus loss, reset ---------------------------------------------------------

        [UnityTest]
        public IEnumerator RapidRepeatedTapsOnTheHandle_LeaveTheArcConsistent()
        {
            yield return Build();
            for (int i = 0; i < 5; i++)
            {
                var p = Pointer(130 + i, HandleCentre);
                _gestureHud.handleHotspot.OnPointerDown(p);
                _gestureHud.handleHotspot.OnPointerUp(p);
            }
            yield return null;
            Assert.IsTrue(Gestures.ArcOpen, "five taps: open");
            Assert.AreEqual(1, Coordinator.Depth);
            yield return TapHandle(140);
            Assert.IsFalse(Gestures.ArcOpen, "six: closed");
            Assert.AreEqual(0, Coordinator.Depth);
            for (int i = 0; i < 4; i++)
            {
                yield return OpenArcByTap();
                yield return TapSurface(150 + i, Sky);
                Assert.IsFalse(Gestures.ArcOpen);
            }
            Assert.AreEqual(0, _touch.Ownership.Count);
            Assert.AreEqual(GestureSession.Doing.Nothing, Gestures.Current);
        }

        [UnityTest]
        public IEnumerator FocusLoss_FoldsTheArc_WithoutChangingAnything()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Assistance);
            yield return OpenArcByTap();
            yield return TapArc(160, ArcPoint(slot, 230f));
            Assert.IsTrue(Gestures.SubArcOpen);
            int version = _session.Systems.Version;
            _surface.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.IsFalse(Gestures.ArcOpen, "the app lost focus: the expanded interface folds");
            Assert.IsFalse(Gestures.SubArcOpen);
            Assert.AreEqual(0, Coordinator.Depth);
            Assert.AreEqual(version, _session.Systems.Version, "nothing applied");
            Assert.AreEqual(FlightAssistProfile.Assisted, _session.AssistProfile);
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        [UnityTest]
        public IEnumerator Reset_FoldsTheFlightSettings_AndTheArc()
        {
            yield return Build();
            _session.ToggleFlightSettings();
            yield return null;
            Assert.IsTrue(_settings.IsOpen);
            _session.ResetFlight();
            yield return null;
            Assert.IsFalse(_settings.IsOpen, "a new flight starts with the quiet HUD");
            Assert.AreEqual(0, Coordinator.Depth);
            yield return OpenArcByTap();
            _session.ResetFlight();
            yield return null;
            Assert.IsFalse(Gestures.ArcOpen);
            Assert.AreEqual(0, Coordinator.Depth);
        }

        // ---- The continuous path remains ----------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePullStillWorks_AndASlideOnTheOpenArcChooses()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Navigation);
            // A pull from the folded handle, as before.
            var p = Pointer(170, HandleCentre);
            _gestureHud.handleHotspot.OnPointerDown(p);
            yield return null;
            p.position = HandleCentre + new Vector2(0f, 60f);
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            p.position = ArcPoint(slot, 230f);
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(GestureSession.Doing.ArcPull, Gestures.Current);
            Assert.IsFalse(Gestures.ArcOpen, "a pull is not the open menu");
            Assert.IsTrue(Gestures.Arc.IsArmed);
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(PlayerOverride.Show, _session.Disclosure.GetOverride(HudElement.NavigationGuide));

            // A slide that starts on the open arc and releases on a segment chooses it too.
            yield return OpenArcByTap();
            var q = Pointer(171, ArcPoint(0, 230f));
            _gestureHud.arcHotspot.OnPointerDown(q);
            yield return null;
            q.position = ArcPoint(slot, 230f);
            _gestureHud.arcHotspot.OnDrag(q);
            yield return null;
            Assert.AreEqual(GestureSession.Doing.ArcPull, Gestures.Current);
            Assert.IsTrue(Gestures.ArcOpen, "the menu stays unfolded under the slide");
            Assert.AreEqual(1f, _gestureHud.arc.ShownUnfold, 1e-3f);
            _gestureHud.arcHotspot.OnPointerUp(q);
            yield return null;
            Assert.AreEqual(PlayerOverride.None, _session.Disclosure.GetOverride(HudElement.NavigationGuide), "toggled back: the slide chose NAV");
            Assert.IsFalse(Gestures.ArcOpen, "and the menu folded after the choice");
        }
    }
}
