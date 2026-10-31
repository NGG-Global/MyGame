using System.Collections;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.Cockpit;
using OpenSkies.FlightInput;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Gameplay;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The interactive cockpit inside a real <see cref="FlightSession"/> with the
    /// Rigidbody aircraft, touch controls and focus panel. Touches are synthetic
    /// pointer events sent to the cockpit's touch surface, as uGUI would deliver
    /// them for touches no other control took; screen positions come from the
    /// cockpit camera. This proves the routing, ownership and state logic, not how
    /// a device delivers touches (see docs/COCKPIT.md for the device checklist).
    /// </summary>
    public class CockpitTests
    {
        sealed class ScriptedControls : IFlightControlSource
        {
            public FlightControls Controls = new FlightControls(0f, 0f, 0f, 0f);
            public FlightControls Read(float deltaTime) => Controls;
            public void ResetControls() => Controls = FlightControls.Neutral;
        }

        sealed class OtherOwner : IPointerOwner
        {
            public string OwnerName => "Other";
            public void OnPointerRevoked(int pointerId) { }
        }

        GameObject _root;
        FlightSession _session;
        CockpitRig _rig;
        CockpitInteractionManager _interaction;
        CockpitFocusPanel _panel;
        TouchFlightControls _touch;
        CameraRig _camera;
        ContextualHud _hud;
        AircraftLightsView _lights;
        ScriptedControls _controls;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        IEnumerator Build(bool scripted = true, bool withHud = false)
        {
            _root = new GameObject("CockpitTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, new Vector3(0f, 0f, -500f), out var telemetry);

            var eye = new GameObject("Eye").transform;
            eye.SetParent(aircraft.transform, false);
            eye.localPosition = new Vector3(-0.3f, 1.9f, 0.9f);
            eye.localRotation = Quaternion.Euler(CockpitLayout.RestingPitchDegrees, 0f, 0f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _rig = CockpitFactory.BuildInterior(eye, font);
            _lights = CockpitFactory.BuildExteriorLights(aircraft.transform);

            var cameraGo = new GameObject("Camera", typeof(Camera));
            cameraGo.transform.SetParent(_root.transform, false);
            _camera = cameraGo.AddComponent<CameraRig>();
            _camera.target = aircraft.transform;
            _camera.cockpitAnchor = eye;
            _camera.SetMode(CameraMode.Cockpit);

            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(_root.transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)canvasGo.transform;
            canvas.sizeDelta = new Vector2(1920f, 1080f);
            canvas.pivot = Vector2.zero;
            canvas.position = new Vector3(0f, 0f, 5000f); // out of the cockpit camera's way
            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(canvas, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;

            var surfaceGo = new GameObject("CockpitTouchSurface", typeof(RectTransform), typeof(TouchSurfaceGraphic));
            surfaceGo.transform.SetParent(safeArea, false);
            _interaction = surfaceGo.AddComponent<CockpitInteractionManager>();
            _interaction.surface = surfaceGo.GetComponent<TouchSurfaceGraphic>();
            _touch = TouchControlsFactory.Build(safeArea, font);
            _touch.visibility = TouchControlsVisibility.Always;
            _panel = CockpitFocusPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            if (withHud)
            {
                _hud = ContextualHudFactory.Build(safeArea, font);
                _hud.SetMotionPreference(MotionPreference.Reduced);
            }

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = _camera;
            _session.touchControls = _touch;
            _session.contextualHud = _hud;
            _session.cockpit = _rig;
            _session.cockpitInteraction = _interaction;
            _session.focusPanel = _panel;
            _session.aircraftLights = _lights;
            _session.rememberHudPreferences = false;
            _session.homeRunway = new RunwayInfo(Vector3.zero, 0f, 2000f, 30f);
            if (scripted)
            {
                _controls = new ScriptedControls();
                _session.SetControlSource(_controls);
            }
            sessionGo.SetActive(true);
            _panel.SetReducedMotion(true); // instant transitions: deterministic checks

            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;
        }

        Vector2 ScreenPointOf(CockpitItem item) => _camera.ViewCamera.WorldToScreenPoint(_rig.Find(item).transform.position);

        static Vector2 EmptySky => new Vector2(Screen.width * 0.5f, Screen.height * 0.93f);

        static PointerEventData Pointer(int id, Vector2 position) =>
            new PointerEventData(EventSystem.current) { pointerId = id, position = position };

        void Tap(Vector2 position, int pointerId = 10)
        {
            var pointer = Pointer(pointerId, position);
            _interaction.OnPointerDown(pointer);
            _interaction.OnPointerUp(pointer);
        }

        void Choose(int option) => _panel.options[option].OnPointerClick(Pointer(20, Vector2.zero));

        // ---- Instruments -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator TappingAnInstrument_OpensItsFocusView_AndDismissalLeavesOneCopy()
        {
            yield return Build();
            Assert.IsTrue(_rig.gameObject.activeSelf, "cockpit shown in cockpit view");
            Assert.AreEqual(CockpitItem.Altimeter, _interaction.PickInteractable(ScreenPointOf(CockpitItem.Altimeter)).item);

            Tap(ScreenPointOf(CockpitItem.Altimeter));
            Assert.IsTrue(_panel.IsShowingInstrument(out var kind));
            Assert.AreEqual(InstrumentKind.Altimeter, kind);
            Assert.AreEqual("Altimeter", _panel.instrumentTitle.text, "accessible name as the title");
            var focused = _panel.ActiveFace;
            Assert.IsTrue(focused.gameObject.activeInHierarchy);
            int renders = focused.RenderCount;
            yield return null;
            yield return null;
            Assert.That(focused.RenderCount, Is.GreaterThan(renders), "drawn from the shared telemetry every frame");

            Tap(EmptySky);
            Assert.IsFalse(_panel.IsOpen, "a tap on nothing dismisses");
            yield return null;
            Assert.IsFalse(_panel.faces[(int)InstrumentKind.Altimeter].gameObject.activeSelf, "the enlarged copy is gone");
            Assert.IsTrue(_rig.instruments[(int)InstrumentKind.Altimeter].gameObject.activeInHierarchy, "the cockpit instrument remains");
            int afterClose = _panel.faces[(int)InstrumentKind.Altimeter].RenderCount;
            yield return null;
            Assert.AreEqual(afterClose, _panel.faces[(int)InstrumentKind.Altimeter].RenderCount, "no longer rendered");
        }

        [UnityTest]
        public IEnumerator FlyingWithAnInstrumentFocused_KeepsControlsAndPhysics()
        {
            yield return Build();
            _controls.Controls = new FlightControls(0.2f, 0f, 0f, 1f);
            Tap(ScreenPointOf(CockpitItem.AttitudeIndicator));
            Assert.IsTrue(_panel.IsOpen);
            float speed = _session.Telemetry.GroundspeedKnots;
            yield return new WaitForSeconds(2f);
            Assert.That(_session.Telemetry.GroundspeedKnots, Is.GreaterThan(speed + 2f), "physics keeps running: no pause");
            Assert.AreEqual(1f, _session.aircraft.Controls.Throttle);
            Assert.AreEqual(0.2f, _session.aircraft.Controls.Pitch, 1e-4f);
            Assert.AreEqual(1f, Time.timeScale);

            _panel.instrumentClose.OnPointerClick(Pointer(21, Vector2.zero));
            Assert.IsFalse(_panel.IsOpen);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1f, _session.aircraft.Controls.Throttle, "closing changes no flight control");
            Assert.AreEqual(0.2f, _session.aircraft.Controls.Pitch, 1e-4f);
        }

        // ---- Ownership and gestures -------------------------------------------------------

        [UnityTest]
        public IEnumerator OwnedPointer_NeverActivatesTheCockpit()
        {
            yield return Build();
            var other = new OtherOwner();
            Assert.IsTrue(_touch.Ownership.TryClaim(42, other));
            Tap(ScreenPointOf(CockpitItem.AirspeedIndicator), 42);
            Assert.IsFalse(_panel.IsOpen, "a pointer owned by a flight control or UI is ignored");
            Assert.AreEqual(0, _interaction.ActivePointers);
            Assert.IsTrue(_touch.Ownership.IsOwnedBy(42, other), "and keeps its owner");

            // A finger on the joystick, sliding over an instrument and lifting there.
            var stick = Pointer(43, Vector2.zero);
            _touch.joystick.OnPointerDown(stick);
            Assert.IsTrue(_touch.joystick.IsEngaged);
            Tap(ScreenPointOf(CockpitItem.AirspeedIndicator), 43);
            Assert.IsFalse(_panel.IsOpen);
            _touch.joystick.OnPointerUp(stick);
        }

        [UnityTest]
        public IEnumerator SwitchTap_OpensSelector_AndOnlyTheSelectorChangesState()
        {
            yield return Build();
            var systems = _session.Systems;
            Assert.IsFalse(systems.LandingLightOn);

            Tap(ScreenPointOf(CockpitItem.LandingLightSwitch));
            Assert.IsTrue(_panel.IsShowingSystem(out var system));
            Assert.AreEqual(AircraftSystem.LandingLight, system);
            Assert.IsFalse(systems.LandingLightOn, "tapping the small switch only selects it");
            Assert.AreEqual(2, _panel.OptionCount);

            Choose(1); // ON
            Assert.IsTrue(systems.LandingLightOn);
            yield return null;
            var view = System.Array.Find(_rig.switchViews, v => v.system == AircraftSystem.LandingLight);
            Assert.AreEqual(1f, view.TargetPosition, "cockpit switch shows ON");
            Assert.IsTrue(view.IndicatorLit);
            Assert.IsTrue(_lights.LandingLightShown, "exterior light follows the same state");
            Assert.IsTrue(_lights.landingLight.enabled);
        }

        [UnityTest]
        public IEnumerator CameraSwitch_ClosesThePanel_WithoutTouchingControls()
        {
            yield return Build();
            _controls.Controls = new FlightControls(0f, 0.3f, 0f, 0.8f);
            Tap(ScreenPointOf(CockpitItem.HeadingIndicator));
            Assert.IsTrue(_panel.IsOpen);

            _camera.ToggleMode();
            yield return null;
            Assert.IsFalse(_panel.IsOpen, "switching camera closes the panel");
            Assert.IsFalse(_rig.gameObject.activeSelf, "the cockpit is not drawn in chase view");
            Assert.IsFalse(_interaction.IsActive);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0.8f, _session.aircraft.Controls.Throttle, 1e-4f);
            Assert.AreEqual(0.3f, _session.aircraft.Controls.Roll, 1e-4f);

            _camera.ToggleMode();
            yield return null;
            Assert.IsTrue(_rig.gameObject.activeSelf);
            Assert.IsTrue(_interaction.IsActive);
        }

        [UnityTest]
        public IEnumerator InteractingWhileBankingAndClimbing_PicksTheRightControl()
        {
            yield return Build();
            var body = _session.aircraft.Body;
            body.isKinematic = true;
            var pose = Quaternion.Euler(-10f, 20f, -30f); // nose 10° up, right bank 30°
            body.position = new Vector3(0f, 300f, -500f);
            body.rotation = pose;
            _session.aircraft.transform.SetPositionAndRotation(new Vector3(0f, 300f, -500f), pose);
            yield return new WaitForFixedUpdate();
            yield return null;
            _camera.SnapToTarget();

            Tap(ScreenPointOf(CockpitItem.FlapSelector));
            Assert.IsTrue(_panel.IsShowingSystem(out var system));
            Assert.AreEqual(AircraftSystem.Flaps, system);
            Choose(1);
            Assert.AreEqual(1, _session.Systems.FlapDetent);

            Tap(ScreenPointOf(CockpitItem.VerticalSpeedIndicator));
            Assert.IsTrue(_panel.IsShowingInstrument(out var kind));
            Assert.AreEqual(InstrumentKind.VerticalSpeed, kind);
        }

        [UnityTest]
        public IEnumerator RapidRepeatedTaps_ToggleDeterministically_AndLeakNoPointers()
        {
            yield return Build();
            Vector2 lever = ScreenPointOf(CockpitItem.FlapSelector);
            for (int i = 1; i <= 11; i++)
            {
                Tap(lever, 30 + i);
                Assert.AreEqual(i % 2 == 1, _panel.IsOpen, $"after {i} taps");
            }
            Assert.AreEqual(0, _session.Systems.FlapDetent, "taps on the lever never change the flaps by themselves");
            Assert.AreEqual(0, _interaction.ActivePointers);
            Assert.IsFalse(_touch.Ownership.IsOwned(41));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MultipleFingers_JoystickCockpitTapAndPinch_DoNotInterfere()
        {
            yield return Build(scripted: false);
            // Finger 1 flies: joystick pulled back.
            var stickRect = (RectTransform)_touch.joystick.transform;
            Vector2 stickCentre = RectTransformUtility.WorldToScreenPoint(null, stickRect.TransformPoint(stickRect.rect.center));
            var stick = Pointer(1, stickCentre);
            _touch.joystick.OnPointerDown(stick);
            stick.position += new Vector2(0f, -200f);
            _touch.joystick.OnDrag(stick);

            // Finger 2 taps an instrument.
            Tap(ScreenPointOf(CockpitItem.AirspeedIndicator), 2);
            Assert.IsTrue(_panel.IsShowingInstrument(out var kind));
            Assert.AreEqual(InstrumentKind.Airspeed, kind);

            // Fingers 3 and 4 pinch outwards on the sky: narrower field of view (zoom in).
            float fov = _camera.cockpitFieldOfView;
            Vector2 centre = EmptySky;
            var a = Pointer(3, centre - new Vector2(100f, 0f));
            var b = Pointer(4, centre + new Vector2(100f, 0f));
            _interaction.OnPointerDown(a);
            _interaction.OnPointerDown(b);
            var c = Pointer(5, centre + new Vector2(0f, -50f));
            _interaction.OnPointerDown(c);
            Assert.AreEqual(2, _interaction.ActivePointers, "a third cockpit finger is ignored");
            a.position -= new Vector2(150f, 0f);
            _interaction.OnDrag(a);
            b.position += new Vector2(150f, 0f);
            _interaction.OnDrag(b);
            Assert.That(_camera.cockpitFieldOfView, Is.LessThan(fov));
            Assert.That(_camera.cockpitFieldOfView, Is.GreaterThanOrEqualTo(_camera.minCockpitFieldOfView));
            _interaction.OnPointerUp(a);
            _interaction.OnPointerUp(b);
            _interaction.OnPointerUp(c);
            Assert.IsTrue(_panel.IsOpen, "a pinch is never a tap");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_touch.joystick.IsEngaged, "the flying finger kept its control throughout");
            Assert.That(_session.aircraft.Controls.Pitch, Is.GreaterThan(0.3f), "and still pitches the aircraft");
            _touch.joystick.OnPointerUp(stick);
        }

        [UnityTest]
        public IEnumerator LosingAppFocus_CancelsCockpitGesturesWithoutATap()
        {
            yield return Build();
            var finger = Pointer(7, ScreenPointOf(CockpitItem.AirspeedIndicator));
            _interaction.OnPointerDown(finger);
            Assert.AreEqual(1, _interaction.ActivePointers);
            Assert.IsTrue(_touch.Ownership.IsOwnedBy(7, _interaction), "claimed in the shared table");

            _touch.Ownership.RevokeAll(); // what focus loss and pause do
            Assert.AreEqual(0, _interaction.ActivePointers);
            _interaction.OnPointerUp(finger);
            Assert.IsFalse(_panel.IsOpen, "a cancelled touch does not tap");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DragToLook_TurnsWithinLimits_IsRemembered_AndDoubleTapRecentres()
        {
            yield return Build();
            var finger = Pointer(8, EmptySky);
            _interaction.OnPointerDown(finger);
            finger.position += new Vector2(Screen.height * 0.3f, 0f);
            _interaction.OnDrag(finger);
            Assert.That(_camera.CockpitLook.x, Is.GreaterThan(20f), "drag right looks right");
            finger.position += new Vector2(Screen.height * 10f, 0f);
            _interaction.OnDrag(finger);
            Assert.AreEqual(_camera.cockpitYawLimits.y, _camera.CockpitLook.x, 1e-3f, "comfortable limit");
            _interaction.OnPointerUp(finger);
            Assert.IsFalse(_panel.IsOpen, "a look is not a tap");

            Vector2 look = _camera.CockpitLook;
            _camera.ToggleMode();
            yield return null;
            _camera.ToggleMode();
            yield return null;
            Assert.AreEqual(look, _camera.CockpitLook, "remembered across camera switches");

            Tap(EmptySky, 9);
            Tap(EmptySky, 9);
            Assert.IsTrue(_camera.IsRecentring);
            yield return new WaitForSeconds(0.6f);
            Assert.That(_camera.CockpitLook.magnitude, Is.LessThan(0.5f), "back to the resting view");
        }

        // ---- Systems: one state for cockpit, HUD and simulation -----------------------------

        [UnityTest]
        public IEnumerator Systems_AgreeBetweenContextualHud_Cockpit_AndSimulation()
        {
            yield return Build(withHud: true);
            var systems = _session.Systems;

            // Flaps from the contextual HUD: the same selector as the cockpit lever.
            int flapSlot = _hud.SlotOf(ContextAction.Flaps);
            Assert.That(flapSlot, Is.GreaterThanOrEqualTo(0), "flaps are offered now that they are modelled");
            _hud.railSlots[flapSlot].OnPointerClick(Pointer(50, Vector2.zero));
            Assert.IsTrue(_panel.IsShowingSystem(out var system));
            Assert.AreEqual(AircraftSystem.Flaps, system);
            Choose(2);
            Assert.AreEqual(2, systems.FlapDetent);
            var flapView = System.Array.Find(_rig.switchViews, v => v.system == AircraftSystem.Flaps);
            yield return null;
            Assert.AreEqual(2f / 3f, flapView.TargetPosition, 1e-4f, "the cockpit lever shows 20°");
            Assert.IsTrue(_session.Disclosure.IsActionEngaged(ContextAction.Flaps), "the HUD shows flaps out");
            yield return new WaitForSeconds(1f);
            float travelled = _session.Telemetry.FlapPosition;
            Assert.That(travelled, Is.GreaterThan(0.05f), "the simulated flaps are moving");
            Assert.That(travelled, Is.LessThanOrEqualTo(2f / 3f + 1e-3f));

            // Parking brake set in the cockpit, released from the HUD.
            Tap(ScreenPointOf(CockpitItem.ParkingBrakeHandle));
            Assert.IsTrue(_panel.IsShowingSystem(out system));
            Assert.AreEqual(AircraftSystem.ParkingBrake, system);
            Choose(1); // SET
            Assert.IsTrue(systems.ParkingBrakeSet);
            yield return null;
            int parkSlot = _hud.SlotOf(ContextAction.ParkingBrake);
            Assert.AreEqual("BRAKE SET", _hud.railSlots[parkSlot].label.text, "the HUD action shows the cockpit's change");
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1f, _session.aircraft.Controls.Brake, "and the aircraft is braking");

            _hud.railSlots[parkSlot].OnPointerClick(Pointer(51, Vector2.zero));
            Assert.IsFalse(systems.ParkingBrakeSet);
            yield return null;
            var brakeView = System.Array.Find(_rig.switchViews, v => v.system == AircraftSystem.ParkingBrake);
            Assert.AreEqual(0f, brakeView.TargetPosition, "the cockpit handle shows the HUD's change");
            Assert.AreEqual("BRAKE RELEASED", _panel.systemState.text, "and so does the open selector");
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, _session.aircraft.Controls.Brake);
        }

        [UnityTest]
        public IEnumerator EngineOffInFlight_NeedsASecondTap_AndRestarts()
        {
            yield return Build();
            var body = _session.aircraft.Body;
            body.isKinematic = true;
            body.position = new Vector3(0f, 300f, -500f);
            _session.aircraft.transform.position = new Vector3(0f, 300f, -500f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsFalse(_session.Telemetry.WeightOnWheels);
            _camera.SnapToTarget();

            Tap(ScreenPointOf(CockpitItem.IgnitionSwitch));
            Assert.IsTrue(_panel.IsShowingSystem(out var system));
            Assert.AreEqual(AircraftSystem.Ignition, system);
            Choose(0); // OFF
            Assert.IsTrue(_session.Systems.IgnitionOn, "one tap is not enough in flight");
            yield return null;
            Assert.AreEqual(0, _panel.PendingConfirmation);
            Assert.IsNotEmpty(_panel.confirmation.text);

            Choose(0);
            Assert.IsFalse(_session.Systems.IgnitionOn);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(EngineState.Off, _session.Telemetry.EngineState);

            Choose(1); // ON: starts without confirmation
            Assert.IsTrue(_session.Systems.IgnitionOn);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(EngineState.Starting, _session.Telemetry.EngineState);
        }

        [UnityTest]
        public IEnumerator Reset_RestoresSystemsAndClosesPanels_KeepingTheView()
        {
            yield return Build();
            _session.RequestSystemChange(new SystemChange(AircraftSystem.Flaps, 3));
            _session.RequestSystemChange(new SystemChange(AircraftSystem.LandingLight, 1));
            _camera.AddCockpitLook(30f, -5f);
            Tap(ScreenPointOf(CockpitItem.Altimeter));
            Assert.IsTrue(_panel.IsOpen);

            _session.ResetFlight();
            Assert.AreEqual(0, _session.Systems.FlapDetent);
            Assert.IsFalse(_session.Systems.LandingLightOn);
            Assert.IsTrue(_session.Systems.IgnitionOn);
            Assert.IsFalse(_panel.IsOpen);
            Assert.AreEqual(new Vector2(30f, -5f), _camera.CockpitLook, "the pilot's view direction is kept");
            yield return null;
        }
    }

    /// <summary>The focus panel at different landscape screen sizes: clear of the flight controls, readable, large targets.</summary>
    public class CockpitFocusPanelLayoutTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        static Rect WorldRect(Component component)
        {
            var corners = new Vector3[4];
            ((RectTransform)component.transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        [TestCase(1440f)]
        [TestCase(1920f)]
        [TestCase(2400f)]
        public void FocusPanel_FitsBetweenTheFlightControls(float width)
        {
            _root = new GameObject("PanelLayoutRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(_root.transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)canvasGo.transform;
            canvas.sizeDelta = new Vector2(width, 1080f);
            canvas.pivot = Vector2.zero;
            canvas.position = Vector3.zero;
            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(canvas, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var touch = TouchControlsFactory.Build(safeArea, font);
            var panel = CockpitFocusPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            var hud = ContextualHudFactory.Build(safeArea, font);
            Canvas.ForceUpdateCanvases();

            panel.ShowSystem(AircraftSystem.Flaps, "Flaps", "hint", new[] { "UP", "10", "20", "30" }, 4);
            panel.Layout();
            var controls = new[] { WorldRect(touch.joystick), WorldRect(touch.throttle), WorldRect(touch.brake), WorldRect(hud.systemsButton) };
            foreach (var card in new[] { panel.systemCard, panel.instrumentCard })
                foreach (var control in controls)
                    Assert.IsFalse(WorldRect(card).Overlaps(control), $"{card.name} overlaps a control at width {width}");
            for (int i = 0; i < 4; i++)
            {
                var option = WorldRect(panel.options[i]);
                Assert.That(option.height, Is.GreaterThanOrEqualTo(96f), "option buttons are large touch targets");
                Assert.That(option.width, Is.GreaterThanOrEqualTo(96f));
                Assert.IsTrue(WorldRect(panel.systemCard).Overlaps(option), "options stay on their card");
            }

            panel.ShowInstrument(InstrumentKind.Airspeed, "Airspeed indicator", "hint");
            panel.Layout();
            Assert.That(WorldRect(panel.instrumentCard).height, Is.GreaterThanOrEqualTo(width > 1500f ? 450f : 340f),
                "the enlarged instrument uses the free space (about a third to half of the screen height)");
        }
    }
}
