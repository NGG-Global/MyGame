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
    /// The tap-first systems wheel (docs/TAP_DISCLOSURE_DESIGN.md, milestone 2) inside a real
    /// <see cref="FlightSession"/> with the Rigidbody aircraft, the touch controls, both HUDs,
    /// the flight settings card and the cockpit focus panel, on a world-space canvas driven by
    /// synthetic pointers: opening and closing by taps, choosing a segment, the focused options
    /// (flap detents from the aircraft data, the switch panel, an owner's options), the outside
    /// tap that cancels, the hazardous confirmation, active states, the shared flap state with
    /// the cockpit, the flight controls left usable, and the layout at 4:3, 16:9 and 20:9.
    /// Not how any of it feels under a thumb.
    /// </summary>
    public class SystemsWheelPlayModeTests
    {
        sealed class ScriptedControls : IFlightControlSource
        {
            public FlightControls Controls = FlightControls.Neutral;
            public FlightControls Read(float deltaTime) => Controls;
            public void ResetControls() => Controls = FlightControls.Neutral;
        }

        GameObject _root;
        FlightSession _session;
        TouchFlightControls _touch;
        ContextualHud _hud;
        GestureHud _gestureHud;
        InteractionSurface _surface;
        FlightSettingsPanel _settings;
        CockpitFocusPanel _focus;
        CameraRig _camera;
        float _width;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_root != null) Object.Destroy(_root);
        }

        IEnumerator Build(float width = 1920f, float[] flapDetents = null)
        {
            _width = width;
            _root = new GameObject("SystemsWheelTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, new Vector3(0f, 0f, -500f), out var telemetry);
            if (flapDetents != null) aircraft.configuration.flapDetentDegrees = flapDetents;

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
            _session.SetControlSource(new ScriptedControls());
            sessionGo.SetActive(true);
            _gestureHud.SetReducedMotion(true);
            _session.ApplyMotionPreference(MotionPreference.Reduced);

            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate(); // the aircraft settles on its wheels
            yield return null;
            yield return null;
        }

        IEnumerator Airborne()
        {
            var airborne = MissionStarts.Airborne(_session.aircraft.configuration, new Vector3(0f, 400f, 0f), 0f);
            _session.StartFlight(airborne, 0);
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsFalse(_session.Telemetry.WeightOnWheels, "in the air");
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

        GestureSession Gestures => _session.Gestures;
        DisclosureCoordinator Coordinator => _session.Coordinator;
        SystemsArcModel Wheel => Gestures.Arc;
        Vector2 HandleCentre => WorldRect(_gestureHud.handleHotspot).center;
        Vector2 Sky => new Vector2(_width * 0.5f, 900f);
        float Mid => (Wheel.Settings.innerRadius + Wheel.Settings.outerRadius) * 0.5f;
        Vector2 SegmentPoint(int slot) => HandleCentre + GestureMath.FromAngle(Wheel.SlotAngle(slot)) * Mid;
        Vector2 OptionPoint(int option) => HandleCentre + GestureMath.FromAngle(Wheel.OptionAngle(option)) * Mid;
        int Slot(ContextAction action) => Gestures.SlotOf(action);

        IEnumerator TapHandle(int id)
        {
            var p = Pointer(id, HandleCentre);
            _gestureHud.handleHotspot.OnPointerDown(p);
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
        }

        IEnumerator TapWheel(int id, Vector2 point)
        {
            Assert.IsTrue(_gestureHud.arcHitArea.IsRaycastLocationValid(point, null), "the wheel takes a press at " + point);
            var p = Pointer(id, point);
            _gestureHud.arcHotspot.OnPointerDown(p);
            _gestureHud.arcHotspot.OnPointerUp(p);
            yield return null;
        }

        IEnumerator TapSurface(int id, Vector2 point)
        {
            var p = Pointer(id, point);
            _surface.OnPointerDown(p);
            _surface.OnPointerUp(p);
            yield return null;
        }

        IEnumerator Open()
        {
            yield return TapHandle(200);
            Assert.IsTrue(Gestures.ArcOpen, "a tap on SYSTEMS opens the wheel");
        }

        /// <summary>Two frames: whichever of the surface and the session runs first, the view has presented the open wheel.</summary>
        IEnumerator Presented()
        {
            yield return null;
            yield return null;
        }

        IEnumerator FocusOn(int slot, int id)
        {
            yield return TapWheel(id, SegmentPoint(slot));
            Assert.IsTrue(Gestures.Focused, "slot " + slot + " has options: the wheel focuses on them");
            Assert.AreEqual(slot, Gestures.FocusedSlot);
            yield return Presented(); // the options follow the aircraft and are drawn
        }

        int[] SystemValues()
        {
            var values = new int[AircraftSystems.SystemCount];
            for (int i = 0; i < values.Length; i++) values[i] = _session.Systems.Get((AircraftSystem)i);
            return values;
        }

        // ---- Opening and closing --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ATapOpensTheWheel_TheSameTriggerClosesIt_NoFingerHeld()
        {
            yield return Build();
            Assert.IsFalse(Gestures.ArcOpen);
            Assert.IsFalse(Gestures.PullGestures, "tap-first is the primary design");
            yield return Open();
            yield return Presented();
            Assert.AreEqual(GestureSession.Doing.Nothing, Gestures.Current, "it stays open with no finger on the screen");
            Assert.AreEqual(0, _touch.Ownership.Count);
            Assert.IsTrue(Gestures.Fit.Fits, "fitted to the screen with full touch targets");
            Assert.AreEqual(6, Wheel.InnerCount, "PARK, FLAPS, SWITCHES, APPROACH, NAV, ASSIST");
            Assert.AreEqual(Slot(ContextAction.Flaps) + 1, Gestures.SwitchesSlot, "the switch panel sits right after FLAPS");
            for (int i = 0; i < Wheel.InnerCount; i++)
            {
                Assert.IsTrue(_gestureHud.arc.innerSegments[i].enabled, "segment " + i + " drawn");
                Assert.IsTrue(_gestureHud.arc.innerIcons[i].enabled, "segment " + i + " has its symbol");
                Assert.IsFalse(string.IsNullOrEmpty(_gestureHud.arc.innerLabels[i].text), "segment " + i + " has its label");
            }

            yield return TapHandle(201);
            Assert.IsFalse(Gestures.ArcOpen, "the same trigger closes it");
            Assert.AreEqual(0, Coordinator.Depth);
        }

        [UnityTest]
        public IEnumerator ASloppyTapOnTheHandle_ThatMovesAlittle_StillOpensIt_AndAPullUnfoldsNothing()
        {
            yield return Build();
            var p = Pointer(202, HandleCentre);
            _gestureHud.handleHotspot.OnPointerDown(p);
            yield return null;
            p.position = HandleCentre + new Vector2(30f, 25f); // past the tap slop, still on the handle
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.IsFalse(Gestures.ArcOpen, "moving on the handle unfolds nothing");
            Assert.AreEqual(GestureSession.Doing.Nothing, Gestures.Current);
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
            Assert.IsTrue(Gestures.ArcOpen, "released on the handle: a tap");
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        // ---- Segments -------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASegmentWithoutOptions_Acts_ThroughTheGate_AndTheWheelFolds()
        {
            yield return Build();
            var before = SystemValues();
            yield return Open();
            yield return TapWheel(203, SegmentPoint(Slot(ContextAction.ParkingBrake)));
            Assert.IsTrue(_session.Systems.ParkingBrakeSet, "PARK set the parking brake (on the ground)");
            Assert.IsFalse(Gestures.ArcOpen, "the selection is complete: the wheel folds");
            var after = SystemValues();
            for (int i = 0; i < after.Length; i++)
                if ((AircraftSystem)i != AircraftSystem.ParkingBrake) Assert.AreEqual(before[i], after[i], "only the parking brake changed");

            yield return Open();
            yield return TapWheel(204, SegmentPoint(Slot(ContextAction.ApproachGuide)));
            Assert.AreEqual(PlayerOverride.Show, _session.Disclosure.GetOverride(HudElement.ApproachGuide), "the same request the APPROACH button makes");
            Assert.IsFalse(Gestures.ArcOpen);
        }

        // ---- FLAPS: the reference interaction -------------------------------------------------------

        [UnityTest]
        public IEnumerator Flaps_TapSystems_TapFlaps_TapADetent_FromTheAircraftsOwnDetents()
        {
            yield return Build(flapDetents: new[] { 0f, 15f, 25f, 40f });
            Assert.AreEqual(4, _session.Systems.FlapDetentCount);
            yield return Open();
            yield return FocusOn(Slot(ContextAction.Flaps), 205);
            Assert.AreEqual(4, Wheel.OptionCount, "one option per configured detent");
            string[] expected = { "UP", "15°", "25°", "40°" };
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(expected[i], Wheel.OptionItem(i).Label, "detent " + i + " from the aircraft data");
                Assert.AreEqual(expected[i], _gestureHud.arc.optionLabels[i].text, "and drawn");
            }
            Assert.AreEqual(ArcEmphasis.Engaged, Wheel.OptionItem(0).Emphasis, "UP is the current one");
            Assert.That(Wheel.OptionAngle(1), Is.GreaterThan(Wheel.OptionAngle(0)), "in order, like a lever: down the wheel is more flap");
            Assert.AreEqual(GestureSession.WheelLabel(ContextAction.Flaps), _gestureHud.handleLabel.text, "the hub says FLAPS");
            Assert.IsTrue(_gestureHud.ShowsBack);
            Assert.AreEqual(0, _session.Systems.FlapDetent, "showing the detents changes nothing");

            int changes = 0;
            _session.SystemChanged += _ => changes++;
            yield return TapWheel(206, OptionPoint(3));
            Assert.AreEqual(3, _session.Systems.FlapDetent, "40° set through the gate");
            Assert.AreEqual(1, changes, "one change");
            Assert.IsFalse(Gestures.ArcOpen, "and the wheel folds");
            Assert.AreEqual(0, Coordinator.Depth);
            Assert.IsFalse(_gestureHud.ShowsBack, "the hub says SYSTEMS again");
            Assert.AreEqual(GestureHud.SystemsLabel, _gestureHud.handleLabel.text);

            yield return Open();
            yield return Presented();
            Assert.AreEqual("40°", Wheel.InnerItem(Slot(ContextAction.Flaps)).State, "the FLAPS segment shows the setting");
            Assert.AreEqual("40°", _gestureHud.arc.innerStates[Slot(ContextAction.Flaps)].text);
            yield return FocusOn(Slot(ContextAction.Flaps), 207);
            Assert.AreEqual(ArcEmphasis.Engaged, Wheel.OptionItem(3).Emphasis, "the set detent is marked");
            Assert.AreEqual("SET", Wheel.OptionItem(3).State);
        }

        [UnityTest]
        public IEnumerator Flaps_TheCockpitAndTheWheel_OperateOneState()
        {
            yield return Build();
            int flaps = Slot(ContextAction.Flaps);
            _session.Cockpit.OpenSystem(AircraftSystem.Flaps); // the cockpit's lever selector, as a tap on the lever opens it
            yield return null;
            _focus.options[2].OnPointerClick(Pointer(208, Vector2.zero));
            yield return null;
            Assert.AreEqual(2, _session.Systems.FlapDetent, "set in the cockpit");
            yield return Open(); // replaces the cockpit card: one cluster at a time
            Assert.IsFalse(_focus.IsOpen);
            yield return Presented();
            Assert.AreEqual("20°", Wheel.InnerItem(flaps).State, "the wheel shows the cockpit's setting");

            yield return FocusOn(flaps, 209);
            Assert.AreEqual(ArcEmphasis.Engaged, Wheel.OptionItem(2).Emphasis);
            _session.RequestSystemChange(new SystemChange(AircraftSystem.Flaps, 3)); // the ] key or the lever, while the options are out
            yield return null;
            Assert.AreEqual(ArcEmphasis.Engaged, Wheel.OptionItem(3).Emphasis, "the open options follow the aircraft");
            Assert.AreEqual(ArcEmphasis.Full, Wheel.OptionItem(2).Emphasis);

            yield return TapWheel(210, OptionPoint(1));
            Assert.AreEqual(1, _session.Systems.FlapDetent, "and the wheel sets the same flaps the cockpit lever moves");
        }

        // ---- Other options --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Switches_EachOption_ChangesItsOwnSystemOnly()
        {
            yield return Build();
            int panel = Gestures.SwitchesSlot;
            for (int option = 0; option < GestureSession.SwitchSystems.Length; option++)
            {
                var system = GestureSession.SwitchSystems[option];
                var before = SystemValues();
                yield return Open();
                yield return FocusOn(panel, 211 + option * 2);
                Assert.AreEqual(GestureSession.SwitchSystems.Length, Wheel.OptionCount);
                Assert.AreEqual(before[(int)system] == 1 ? "ON" : "OFF", Wheel.OptionItem(option).State, system + " shows its state");
                yield return TapWheel(212 + option * 2, OptionPoint(option));
                Assert.IsFalse(Gestures.ArcOpen, "flipped: the wheel folds");
                var after = SystemValues();
                for (int i = 0; i < after.Length; i++)
                {
                    if ((AircraftSystem)i == system) Assert.AreEqual(1 - before[i], after[i], system + " flipped (on the ground, no confirmation)");
                    else Assert.AreEqual(before[i], after[i], "option " + option + " left " + (AircraftSystem)i + " alone");
                }
            }
            _session.RequestSystemChange(new SystemChange(AircraftSystem.Ignition, 1));
        }

        [UnityTest]
        public IEnumerator AnOwnersOptions_AreFocusedAndChosen_OnTheWheel()
        {
            yield return Build();
            int chosen = -1;
            Gestures.RegisterSubOptions(ContextAction.Navigation, new[] { "AIRFIELD", "LIGHTHOUSE", "TOWER" }, () => 0, i => chosen = i);
            yield return Open();
            yield return FocusOn(Slot(ContextAction.Navigation), 220);
            Assert.AreEqual(3, Wheel.OptionCount);
            Assert.AreEqual("LIGHTHOUSE", Wheel.OptionItem(1).Label);
            Assert.AreEqual(ArcEmphasis.Engaged, Wheel.OptionItem(0).Emphasis, "the current destination is marked");
            yield return TapWheel(221, OptionPoint(1));
            Assert.AreEqual(1, chosen, "the owner's choice ran");
            Assert.IsFalse(Gestures.ArcOpen);

            yield return Open();
            yield return FocusOn(Slot(ContextAction.Assistance), 222);
            yield return TapWheel(223, OptionPoint(3)); // SETTINGS, ASSISTED, STANDARD, MANUAL
            Assert.AreEqual(FlightAssistProfile.Manual, _session.AssistProfile);
            Assert.IsFalse(Gestures.ArcOpen);
        }

        [UnityTest]
        public IEnumerator AnOutsideTap_WhileFocused_CancelsEverything_AppliesNothing_AndIsConsumed()
        {
            yield return Build();
            int version = _session.Systems.Version;
            yield return Open();
            yield return FocusOn(Slot(ContextAction.Flaps), 224);
            Assert.AreEqual(2, Coordinator.Depth);
            var p = Pointer(225, Sky);
            _surface.OnPointerDown(p);
            Assert.AreEqual(InteractionRoute.Dismiss, _surface.RouteOf(225), "taken by the dismissal, never by the free look");
            _surface.OnPointerUp(p);
            yield return null;
            Assert.IsFalse(Gestures.ArcOpen);
            Assert.AreEqual(0, Coordinator.Depth);
            Assert.AreEqual(version, _session.Systems.Version, "nothing applied");
            Assert.AreEqual(0f, _camera.ChaseLook.magnitude, 1e-3f);
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        // ---- Active states ----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ActiveStates_AreShown_OnTheSegmentsAndOnTheOptions()
        {
            yield return Build();
            _session.RequestSystemChange(new SystemChange(AircraftSystem.ParkingBrake, 1));
            _session.RequestSystemChange(new SystemChange(AircraftSystem.Flaps, 2));
            _session.RequestSystemChange(new SystemChange(AircraftSystem.LandingLight, 1));
            yield return null;
            yield return Open();
            yield return Presented();
            int park = Slot(ContextAction.ParkingBrake), flaps = Slot(ContextAction.Flaps), panel = Gestures.SwitchesSlot;
            Assert.AreEqual(ArcEmphasis.Engaged, Wheel.InnerItem(park).Emphasis, "the parking brake is set: engaged");
            Assert.AreEqual("SET", _gestureHud.arc.innerStates[park].text);
            Assert.IsTrue(_gestureHud.arc.innerStates[park].enabled);
            Assert.IsTrue(_gestureHud.arc.innerMarks[park].enabled, "and marked");
            Assert.AreEqual("20°", _gestureHud.arc.innerStates[flaps].text);
            Assert.AreEqual("LDG ON", _gestureHud.arc.innerStates[panel].text, "the switch panel says what is on");

            yield return FocusOn(panel, 226);
            Assert.AreEqual(ArcEmphasis.Engaged, Wheel.OptionItem(0).Emphasis, "the landing light is on");
            Assert.AreEqual("ON", _gestureHud.arc.optionStates[0].text);
            Assert.AreEqual(ArcEmphasis.Engaged, Wheel.OptionItem(1).Emphasis, "the navigation lights are on from the start");
            Assert.AreEqual("ON", Wheel.OptionItem(2).State, "the engine runs");
            Assert.IsTrue(_gestureHud.arc.optionMarks[0].enabled);
        }

        // ---- Hazardous changes keep their confirmation --------------------------------------------------

        [UnityTest]
        public IEnumerator EngineOffInFlight_WaitsForASecondTap_OnTheWheel()
        {
            yield return Build();
            yield return Airborne();
            int engine = System.Array.IndexOf(GestureSession.SwitchSystems, AircraftSystem.Ignition);
            yield return Open();
            yield return FocusOn(Gestures.SwitchesSlot, 230);
            yield return TapWheel(231, OptionPoint(engine));
            Assert.AreEqual(1, _session.Systems.Get(AircraftSystem.Ignition), "nothing applied on the first tap");
            Assert.IsTrue(Gestures.Focused, "the wheel waits for the confirmation");
            yield return Presented();
            Assert.AreEqual(ArcEmphasis.Confirm, Wheel.OptionItem(engine).Emphasis);
            Assert.AreEqual(GestureSession.ConfirmLabel, _gestureHud.arc.optionLabels[engine].text, "the word says so, not the colour alone");
            Assert.AreEqual(SystemsArcView.ConfirmCaption, _gestureHud.arc.caption.text);

            yield return TapWheel(232, OptionPoint(engine));
            Assert.AreEqual(0, _session.Systems.Get(AircraftSystem.Ignition), "the second tap confirms");
            Assert.IsFalse(Gestures.ArcOpen, "and the wheel folds");
            _session.RequestSystemChange(new SystemChange(AircraftSystem.Ignition, 1));
        }

        [UnityTest]
        public IEnumerator EngineOffInFlight_AnOutsideTapOrTheHandle_CancelsTheConfirmation()
        {
            yield return Build();
            yield return Airborne();
            int engine = System.Array.IndexOf(GestureSession.SwitchSystems, AircraftSystem.Ignition);
            yield return Open();
            yield return FocusOn(Gestures.SwitchesSlot, 233);
            yield return TapWheel(234, OptionPoint(engine));
            Assert.IsTrue(_session.TryGetPendingSystemChange(out _), "the gate holds the change");
            yield return TapSurface(235, Sky);
            Assert.IsFalse(Gestures.ArcOpen, "the outside tap folded the wheel");
            Assert.IsFalse(_session.TryGetPendingSystemChange(out _), "and dropped the confirmation");
            Assert.AreEqual(1, _session.Systems.Get(AircraftSystem.Ignition), "the engine keeps running");

            yield return Open();
            yield return FocusOn(Gestures.SwitchesSlot, 236);
            yield return TapWheel(237, OptionPoint(engine));
            Assert.IsTrue(_session.TryGetPendingSystemChange(out _));
            yield return TapHandle(238);
            Assert.IsTrue(Gestures.ArcOpen, "the handle goes back one level");
            Assert.IsFalse(Gestures.Focused);
            Assert.IsFalse(_session.TryGetPendingSystemChange(out _), "leaving the options drops the confirmation");
            Assert.AreEqual(1, _session.Systems.Get(AircraftSystem.Ignition));
            Assert.AreEqual(SystemChangeGate.Outcome.AwaitingConfirmation,
                _session.RequestSystemChange(new SystemChange(AircraftSystem.Ignition, 0)), "a later request starts the confirmation afresh");
            _session.ClearPendingSystemChange();
        }

        // ---- The flight controls stay usable ------------------------------------------------------------

        [UnityTest]
        public IEnumerator JoystickAndThrottle_StayUsable_WhileTheWheelIsOpenAndFocused()
        {
            yield return Build();
            yield return Open();
            yield return FocusOn(Slot(ContextAction.Flaps), 240);
            var stickRect = WorldRect(_touch.joystick);
            var throttleRect = WorldRect(_touch.throttle);
            var stick = Pointer(1, new Vector2(stickRect.xMin + stickRect.width * 0.4f, stickRect.yMin + stickRect.height * 0.5f));
            var lever = Pointer(2, new Vector2(throttleRect.center.x, throttleRect.yMin + throttleRect.height * 0.3f));
            _touch.joystick.OnPointerDown(stick);
            _touch.throttle.OnPointerDown(lever);
            _surface.OnPointerDown(stick); // even if the event system dispatched the same fingers on
            _surface.OnPointerDown(lever);
            stick.position += new Vector2(0f, -200f);
            _touch.joystick.OnDrag(stick);
            lever.position += new Vector2(0f, 230f);
            _touch.throttle.OnDrag(lever);
            yield return null;
            Assert.AreEqual(InteractionRoute.None, _surface.RouteOf(1), "an owned finger is never a dismissal");
            Assert.AreEqual(InteractionRoute.None, _surface.RouteOf(2));
            var controls = _session.Router.Read(1f);
            Assert.That(controls.Pitch, Is.GreaterThan(0.5f), "the stick steers with the wheel open");
            Assert.That(controls.Throttle, Is.GreaterThan(0.3f), "the throttle moves with the wheel open");
            Assert.IsTrue(Gestures.Focused, "and the wheel stays as it was");
            _touch.joystick.OnPointerUp(stick);
            _touch.throttle.OnPointerUp(lever);
            _surface.OnPointerUp(stick);
            _surface.OnPointerUp(lever);
            yield return null;
            Assert.IsTrue(Gestures.Focused, "releasing them is not a tap outside");
            Assert.AreEqual(0, _session.Systems.FlapDetent, "nothing applied");
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        [UnityTest]
        public IEnumerator ThePullComparison_IsDevelopmentOnly()
        {
            yield return Build();
            Assert.IsTrue(Gestures.PullComparisonAllowed, "the Editor counts as a development build");
            Gestures.SetPullComparisonAllowed(false); // a release build
            _session.SetPullGestureComparison(true);
            Assert.IsFalse(Gestures.PullGestures, "a release build never switches to the pull");
            Gestures.ToggleMode();
            Assert.IsFalse(Gestures.PullGestures);
            Assert.AreEqual(InteractionMode.Legacy, _session.CurrentInteractionMode, "F4 goes from the tap wheel straight to the legacy rail");
            Gestures.ToggleMode();
            Assert.AreEqual(InteractionMode.GestureFirst, _session.CurrentInteractionMode);
        }

        // ---- Layout -----------------------------------------------------------------------------------

        [UnityTest] public IEnumerator Layout_4by3_NoOverlap() => Layout(1440f);
        [UnityTest] public IEnumerator Layout_16by9_NoOverlap() => Layout(1920f);
        [UnityTest] public IEnumerator Layout_20by9_NoOverlap() => Layout(2400f);

        IEnumerator Layout(float width)
        {
            yield return Build(width);
            Assert.IsTrue(Gestures.Fit.Fits, "the wheel fits at width " + width);
            Assert.That(Gestures.Fit.SegmentLength, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "segments are touch targets at width " + width);
            Assert.That(Wheel.Settings.outerRadius - Wheel.Settings.innerRadius, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget));

            var avoid = new System.Collections.Generic.List<Rect> { WorldRect(_touch.joystick), WorldRect(_touch.throttle), WorldRect(_touch.brake), WorldRect(_gestureHud.handleHotspot) };
            for (int k = 0; k < ContextualHud.WarningSlotCount; k++) avoid.Add(WorldRect(_hud.warningSlots[k]));

            yield return Open();
            AssertWedgesClear(width, avoid, Wheel.InnerCount, false);
            yield return FocusOn(Slot(ContextAction.Flaps), 250);
            AssertWedgesClear(width, avoid, Wheel.OptionCount, true);
            yield return TapHandle(251);
            yield return FocusOn(Gestures.SwitchesSlot, 252);
            AssertWedgesClear(width, avoid, Wheel.OptionCount, true);

            var caption = _gestureHud.arc.caption;
            if (caption.enabled)
            {
                var rect = WorldRect(caption);
                Assert.IsFalse(rect.Overlaps(WorldRect(_touch.joystick)), "the caption stays off the joystick region at width " + width);
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0f));
            }
        }

        void AssertWedgesClear(float width, System.Collections.Generic.List<Rect> avoid, int count, bool options)
        {
            var s = Wheel.Settings;
            float step = options ? Wheel.OptionStep : s.slotStep;
            for (int i = 0; i < count; i++)
            {
                float centre = options ? Wheel.OptionAngle(i) : Wheel.SlotAngle(i);
                for (int a = 0; a <= 6; a++)
                {
                    float angle = centre + (a / 6f - 0.5f) * step;
                    for (int r = 0; r <= 4; r++)
                    {
                        var point = HandleCentre + GestureMath.FromAngle(angle) * Mathf.Lerp(s.innerRadius, s.outerRadius, r / 4f);
                        string where = (options ? "option " : "segment ") + i + " at width " + width;
                        Assert.That(point.x, Is.InRange(0f, width), where);
                        Assert.That(point.y, Is.InRange(0f, 1080f), where);
                        foreach (var rect in avoid) Assert.IsFalse(rect.Contains(point), where + " covers a flight control, the handle or a warning banner");
                    }
                }
            }
        }
    }
}
