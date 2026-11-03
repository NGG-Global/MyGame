using System.Collections;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.Cockpit;
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
    /// Direct manipulation of the cockpit inside a real <see cref="FlightSession"/>:
    /// the enlarged heading indicator sets the bug by a drag around it, the
    /// altimeter sets its subscale, the map pans, pinches and recentres, a flick on
    /// a cockpit switch toggles it through the gate, a drag on the cockpit flap
    /// lever uses the same lever as the arc, and a two-finger tap asks for the pause
    /// menu. Synthetic pointers on a 1920 × 1080 world-space canvas; feel is not proven.
    /// </summary>
    public class CockpitManipulationTests
    {
        sealed class ScriptedControls : IFlightControlSource
        {
            public FlightControls Read(float deltaTime) => FlightControls.Neutral;
            public void ResetControls() { }
        }

        GameObject _root;
        FlightSession _session;
        TouchFlightControls _touch;
        CockpitRig _rig;
        CockpitInteractionManager _interaction;
        CockpitFocusPanel _panel;
        GestureHud _gestureHud;
        InteractionSurface _surface;
        CameraRig _camera;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        IEnumerator Build()
        {
            _root = new GameObject("ManipulationTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, new Vector3(0f, 0f, -500f), out var telemetry);
            var eye = new GameObject("Eye").transform;
            eye.SetParent(aircraft.transform, false);
            eye.localPosition = new Vector3(-0.3f, 1.9f, 0.9f);
            eye.localRotation = Quaternion.Euler(CockpitLayout.RestingPitchDegrees, 0f, 0f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _rig = CockpitFactory.BuildInterior(eye, font);
            var lights = CockpitFactory.BuildExteriorLights(aircraft.transform);

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
            canvas.position = new Vector3(0f, 0f, 5000f);
            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(canvas, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;

            var surfaceGo = new GameObject("InteractionSurface", typeof(RectTransform), typeof(RaycastOnlyGraphic));
            ((RectTransform)surfaceGo.transform).SetParent(safeArea, false);
            _surface = surfaceGo.AddComponent<InteractionSurface>();
            _surface.surface = surfaceGo.GetComponent<RaycastOnlyGraphic>();
            _surface.screenHeightOverride = 1080f;
            var cockpitGo = new GameObject("CockpitInteraction", typeof(RectTransform));
            ((RectTransform)cockpitGo.transform).SetParent(safeArea, false);
            _interaction = cockpitGo.AddComponent<CockpitInteractionManager>();
            _interaction.surface = null;

            _touch = TouchControlsFactory.Build(safeArea, font);
            _touch.visibility = TouchControlsVisibility.Always;
            _panel = CockpitFocusPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset, _surface);
            var viewControls = CockpitFocusPanelFactory.BuildViewControls(safeArea, font);
            var hud = ContextualHudFactory.Build(safeArea, font);
            hud.SetMotionPreference(MotionPreference.Reduced);
            _gestureHud = GestureHudFactory.Build(safeArea, font, _surface);

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = _camera;
            _session.touchControls = _touch;
            _session.contextualHud = hud;
            _session.cockpit = _rig;
            _session.cockpitInteraction = _interaction;
            _session.focusPanel = _panel;
            _session.cockpitViewControls = viewControls;
            _session.aircraftLights = lights;
            _session.interactionSurface = _surface;
            _session.gestureHud = _gestureHud;
            _session.rememberHudPreferences = false;
            _session.homeRunway = new RunwayInfo(Vector3.zero, 0f, 2000f, 30f);
            _session.SetControlSource(new ScriptedControls());
            sessionGo.SetActive(true);
            _panel.SetReducedMotion(true);
            _gestureHud.SetReducedMotion(true);
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;
        }

        static PointerEventData Pointer(int id, Vector2 position) =>
            new PointerEventData(EventSystem.current) { pointerId = id, position = position, pressPosition = position };

        static Rect WorldRect(Component c)
        {
            var corners = new Vector3[4];
            ((RectTransform)c.transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        Vector2 ScreenPointOf(CockpitItem item) => _camera.ViewCamera.WorldToScreenPoint(_rig.Find(item).transform.position);

        /// <summary>A point on the enlarged face at a clockwise dial angle and a radius in design units (400 across).</summary>
        Vector2 OnFace(float clockwiseDegrees, float designRadius)
        {
            var rect = WorldRect(_panel.faceHost);
            float scale = rect.width / InstrumentFace.DesignSize;
            float a = clockwiseDegrees * Mathf.Deg2Rad;
            return rect.center + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * designRadius * scale;
        }

        IEnumerator OpenInstrument(InstrumentKind kind)
        {
            _session.Cockpit.OpenInstrument(kind);
            yield return null;
            yield return null;
            Assert.IsTrue(_panel.IsShowingInstrument(out var shown) && shown == kind);
        }

        IEnumerator DragOnFace(int id, Vector2 from, params Vector2[] path)
        {
            var p = Pointer(id, from);
            _panel.faceHotspot.OnPointerDown(p);
            yield return null;
            foreach (var point in path)
            {
                p.position = point;
                _panel.faceHotspot.OnDrag(p);
                yield return null;
            }
        }

        // ---- Heading bug --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator HeadingBug_DragAroundTheDial_PreviewsThenCommitsOnRelease()
        {
            yield return Build();
            yield return OpenInstrument(InstrumentKind.Heading);
            Assert.AreEqual(360, _session.Instruments.HeadingBugDegrees);
            int changes = 0;
            _session.InstrumentSettingChanged += _ => changes++;

            yield return DragOnFace(1, OnFace(0f, 120f), OnFace(30f, 120f), OnFace(60f, 120f), OnFace(90f, 120f));
            Assert.AreEqual(InstrumentManipulation.Doing.HeadingBug, _session.Manipulation.Current);
            Assert.AreEqual(90f, _session.Manipulation.Rotary.Preview, 1f, "a quarter turn clockwise previews 090");
            Assert.AreEqual(360, _session.Instruments.HeadingBugDegrees, "the bug itself is untouched until release");
            Assert.AreEqual(0, changes);
            Assert.AreEqual(InteractionRoute.Instrument, _surface.RouteOf(1));

            _panel.faceHotspot.OnPointerUp(Pointer(1, OnFace(90f, 120f)));
            yield return null;
            Assert.AreEqual(90, _session.Instruments.HeadingBugDegrees, "released: the bug is set");
            Assert.AreEqual(1, changes, "one change, on release");
            Assert.AreEqual(InstrumentManipulation.Doing.Nothing, _session.Manipulation.Current);
            Assert.IsTrue(_panel.IsShowingInstrument(out _), "the panel stays open: the bug can be adjusted again");
        }

        [UnityTest]
        public IEnumerator HeadingBug_MovingIntoTheCentre_Cancels()
        {
            yield return Build();
            yield return OpenInstrument(InstrumentKind.Heading);
            yield return DragOnFace(2, OnFace(0f, 120f), OnFace(45f, 120f), OnFace(90f, 120f), OnFace(90f, 10f));
            Assert.IsTrue(_session.Manipulation.Rotary.Cancelled);
            _panel.faceHotspot.OnPointerUp(Pointer(2, OnFace(90f, 10f)));
            yield return null;
            Assert.AreEqual(360, _session.Instruments.HeadingBugDegrees, "released in the centre: nothing changed");
        }

        [UnityTest]
        public IEnumerator HeadingBug_RevokedMidDrag_ChangesNothing()
        {
            yield return Build();
            yield return OpenInstrument(InstrumentKind.Heading);
            yield return DragOnFace(3, OnFace(0f, 120f), OnFace(60f, 120f));
            _touch.Ownership.RevokeAll();
            yield return null;
            Assert.AreEqual(InstrumentManipulation.Doing.Nothing, _session.Manipulation.Current);
            Assert.AreEqual(360, _session.Instruments.HeadingBugDegrees);
        }

        // ---- Altimeter subscale -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Baro_DragSetsTheSubscale_AndTheAltimeterReadsHigher_WhileTheAircraftStays()
        {
            yield return Build();
            yield return OpenInstrument(InstrumentKind.Altimeter);
            float trueAltitude = _session.Telemetry.AltitudeFeet;
            yield return DragOnFace(4, OnFace(0f, 120f), OnFace(30f, 120f), OnFace(60f, 120f), OnFace(90f, 120f), OnFace(120f, 120f));
            Assert.AreEqual(InstrumentManipulation.Doing.Baro, _session.Manipulation.Current);
            Assert.That(_session.Manipulation.Rotary.Preview, Is.GreaterThan(1013f));
            _panel.faceHotspot.OnPointerUp(Pointer(4, OnFace(120f, 120f)));
            yield return null;
            Assert.That(_session.Instruments.BaroHectopascals, Is.GreaterThan(1013));
            Assert.That(_session.IndicatedAltitudeFeet, Is.GreaterThan(trueAltitude + 100f), "a higher subscale reads higher");
            Assert.AreEqual(trueAltitude, _session.Telemetry.AltitudeFeet, 5f, "the aircraft did not move");
        }

        // ---- Map ----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Map_DragPans_PinchZooms_DoubleTapRecentres_AndFlightInputIsUntouched()
        {
            yield return Build();
            yield return OpenInstrument(InstrumentKind.Navigation);
            var centre = WorldRect(_panel.faceHost).center;
            yield return DragOnFace(5, centre, centre + new Vector2(60f, 0f), centre + new Vector2(120f, 0f));
            Assert.IsTrue(_session.Manipulation.Map.IsPanned);
            _panel.faceHotspot.OnPointerUp(Pointer(5, centre + new Vector2(120f, 0f)));
            yield return null;
            Assert.IsTrue(_session.Manipulation.Map.IsPanned, "the pan stays after release");

            var a = Pointer(6, centre + new Vector2(-40f, 0f));
            var b = Pointer(7, centre + new Vector2(40f, 0f));
            _panel.faceHotspot.OnPointerDown(a);
            _panel.faceHotspot.OnPointerDown(b);
            for (int i = 0; i < 6; i++) { b.position += new Vector2(60f, 0f); _panel.faceHotspot.OnDrag(b); }
            yield return null;
            Assert.That(_session.Manipulation.Map.RangeNm, Is.GreaterThan(0f), "pinching chose a range");
            _panel.faceHotspot.OnPointerUp(a);
            _panel.faceHotspot.OnPointerUp(b);

            var t1 = Pointer(8, centre);
            _panel.faceHotspot.OnPointerDown(t1); _panel.faceHotspot.OnPointerUp(t1);
            var t2 = Pointer(9, centre);
            _panel.faceHotspot.OnPointerDown(t2); _panel.faceHotspot.OnPointerUp(t2);
            yield return null;
            Assert.IsFalse(_session.Manipulation.Map.IsCustom, "double tap: back to the aircraft and the automatic range");
            var controls = _session.Router.Read(1f);
            Assert.AreEqual(0f, controls.Pitch, 1e-4f);
            Assert.AreEqual(0f, controls.Roll, 1e-4f);
        }

        // ---- Cockpit controls ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SwitchFlick_TogglesTheLandingLight_ThroughTheGate_AndAShortFlickSpringsBack()
        {
            yield return Build();
            Assert.IsFalse(_session.Systems.LandingLightOn);
            var start = ScreenPointOf(CockpitItem.LandingLightSwitch);
            var p = Pointer(10, start);
            _interaction.OnPointerDown(p);
            p.position = start + new Vector2(0f, 80f); // up: on
            _interaction.OnDrag(p);
            yield return null;
            Assert.IsTrue(_interaction.IsDraggingControl);
            Assert.IsFalse(_session.Systems.LandingLightOn, "a flick commits on release, not while moving");
            _interaction.OnPointerUp(p);
            yield return null;
            Assert.IsTrue(_session.Systems.LandingLightOn, "flicked up: on");

            var q = Pointer(11, start);
            _interaction.OnPointerDown(q);
            q.position = start + new Vector2(0f, -30f); // too short
            _interaction.OnDrag(q);
            _interaction.OnPointerUp(q);
            yield return null;
            Assert.IsTrue(_session.Systems.LandingLightOn, "a short flick changes nothing");
            Assert.IsFalse(_panel.IsOpen, "a flick never opens the selector");
        }

        [UnityTest]
        public IEnumerator IgnitionFlickInTheAir_WaitsForConfirmation_InTheSelector()
        {
            yield return Build();
            _session.StartFlight(MissionStarts.Airborne(_session.aircraft.configuration, new Vector3(0f, 460f, -2000f), 0f), 0);
            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsFalse(_session.Telemetry.WeightOnWheels);
            var start = ScreenPointOf(CockpitItem.IgnitionSwitch);
            var p = Pointer(12, start);
            _interaction.OnPointerDown(p);
            p.position = start + new Vector2(0f, -90f); // down: off
            _interaction.OnDrag(p);
            _interaction.OnPointerUp(p);
            yield return null;
            Assert.IsTrue(_session.Systems.IgnitionOn, "the engine stays on: a hazardous change waits");
            Assert.IsTrue(_panel.IsShowingSystem(out var system) && system == AircraftSystem.Ignition, "the selector opens with the confirmation");
            Assert.That(_panel.PendingConfirmation, Is.GreaterThanOrEqualTo(0));
        }

        [UnityTest]
        public IEnumerator CockpitFlapLever_Drag_UsesTheSharedLever_AndCommitsOnRelease()
        {
            yield return Build();
            var start = ScreenPointOf(CockpitItem.FlapSelector);
            var p = Pointer(13, start);
            _interaction.OnPointerDown(p);
            p.position = start + new Vector2(0f, -60f);
            _interaction.OnDrag(p);
            yield return null;
            Assert.IsTrue(_session.LeverInteraction.IsActive, "the same lever as the arc's");
            Assert.IsTrue(_gestureHud.lever.IsShown);
            float spacing = _session.LeverInteraction.Model.Settings.detentSpacing;
            p.position = start + new Vector2(0f, -2f * spacing);
            _interaction.OnDrag(p);
            yield return null;
            Assert.AreEqual(2, _session.LeverInteraction.Model.Preview);
            Assert.AreEqual(0, _session.Systems.FlapDetent, "preview only");
            _interaction.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(2, _session.Systems.FlapDetent, "released: set through the gate");
            Assert.IsFalse(_session.LeverInteraction.IsActive);
        }

        [UnityTest]
        public IEnumerator TwoFingerTapOnTheCockpit_AsksForPause_ButAPinchDoesNot()
        {
            yield return Build();
            int pauses = 0;
            _session.PauseRequested += () => pauses++;
            var sky = new Vector2(1300f, 900f);
            var a = Pointer(14, sky);
            var b = Pointer(15, sky + new Vector2(120f, 0f));
            _interaction.OnPointerDown(a);
            _interaction.OnPointerDown(b);
            _interaction.OnPointerUp(a);
            _interaction.OnPointerUp(b);
            yield return null;
            Assert.AreEqual(1, pauses);

            var c = Pointer(16, sky);
            var d = Pointer(17, sky + new Vector2(120f, 0f));
            _interaction.OnPointerDown(c);
            _interaction.OnPointerDown(d);
            d.position += new Vector2(80f, 0f);
            _interaction.OnDrag(d);
            _interaction.OnPointerUp(c);
            _interaction.OnPointerUp(d);
            yield return null;
            Assert.AreEqual(1, pauses, "a pinch is a pinch");
        }

        [UnityTest]
        public IEnumerator ManipulatingWhileFlying_TheJoystickKeepsItsFinger()
        {
            yield return Build();
            var stick = Pointer(18, WorldRect(_touch.joystick).center);
            _touch.joystick.OnPointerDown(stick);
            stick.position += new Vector2(0f, -100f);
            _touch.joystick.OnDrag(stick);
            yield return OpenInstrument(InstrumentKind.Heading);
            yield return DragOnFace(19, OnFace(0f, 120f), OnFace(45f, 120f));
            Assert.AreSame(_touch.joystick, _touch.Ownership.OwnerOf(18));
            Assert.AreSame(_surface, _touch.Ownership.OwnerOf(19));
            Assert.That(_session.Router.Read(1f).Pitch, Is.GreaterThan(0.5f), "still steering while the bug is adjusted");
            _panel.faceHotspot.OnPointerUp(Pointer(19, OnFace(45f, 120f)));
            _touch.joystick.OnPointerUp(stick);
            yield return null;
            Assert.AreEqual(45, _session.Instruments.HeadingBugDegrees);
        }

        [UnityTest]
        public IEnumerator EnlargedInstrument_EmergesFromThePanelInstrument_AndReturns()
        {
            yield return Build();
            _panel.SetReducedMotion(false);
            _panel.emergeSeconds = 0.2f;
            _session.Cockpit.OpenInstrument(InstrumentKind.Airspeed);
            yield return null;
            Assert.That(_panel.instrumentCard.localScale.x, Is.LessThan(0.9f), "starts small, at the cockpit instrument");
            float end = Time.unscaledTime + 0.5f;
            while (Time.unscaledTime < end) yield return null;
            Assert.AreEqual(1f, _panel.instrumentCard.localScale.x, 1e-3f, "settled at full size");
            Assert.AreEqual(Vector2.zero, _panel.instrumentCard.anchoredPosition);
            _session.Cockpit.Close();
            yield return null;
            yield return null;
            Assert.That(_panel.instrumentCard.localScale.x, Is.LessThan(1f), "returning into the cockpit instrument");
        }
    }
}
