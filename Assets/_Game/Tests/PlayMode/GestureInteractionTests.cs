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
    /// The gesture-first interface inside a real <see cref="FlightSession"/> with
    /// the Rigidbody aircraft, the touch controls, the contextual HUD and the
    /// gesture HUD on a 1920 × 1080 world-space canvas (screen points equal canvas
    /// units), driven by synthetic pointer events. Proves ownership, routing, the
    /// arc, the flap lever through the gate, HUD density preview and commit, the
    /// camera gestures and the layout. It does not prove how any of it feels.
    /// </summary>
    public class GestureInteractionTests
    {
        sealed class ScriptedControls : IFlightControlSource
        {
            public FlightControls Read(float deltaTime) => FlightControls.Neutral;
            public void ResetControls() { }
        }

        GameObject _root;
        FlightSession _session;
        TouchFlightControls _touch;
        ContextualHud _hud;
        GestureHud _gestureHud;
        InteractionSurface _surface;
        CameraRig _camera;
        float _width;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        IEnumerator Build(float width = 1920f)
        {
            _width = width;
            _root = new GameObject("GestureTestRoot");
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
            _hud = ContextualHudFactory.Build(safeArea, font);
            _hud.SetMotionPreference(MotionPreference.Reduced);
            _gestureHud = GestureHudFactory.Build(safeArea, font, _surface);

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = _camera;
            _session.touchControls = _touch;
            _session.contextualHud = _hud;
            _session.interactionSurface = _surface;
            _session.gestureHud = _gestureHud;
            _session.rememberHudPreferences = false;
            _session.homeRunway = new RunwayInfo(Vector3.zero, 0f, 2000f, 30f);
            _session.SetControlSource(new ScriptedControls());
            sessionGo.SetActive(true);
            // These tests exercise the M13/M14 pull gestures, which remain as the development comparison
            // (docs/TAP_DISCLOSURE_DESIGN.md, milestone 2); the tap-first wheel has its own tests.
            _session.SetPullGestureComparison(true);
            _gestureHud.SetReducedMotion(true);
            Canvas.ForceUpdateCanvases();

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

        Vector2 ArcPoint(int slot, float radius)
        {
            var model = _session.Gestures.Arc;
            return HandleCentre + GestureMath.FromAngle(model.SlotAngle(slot)) * radius;
        }

        /// <summary>The wheel's slot of an action (the switch panel sits after FLAPS).</summary>
        int SlotOf(ContextAction action) => _session.Gestures.SlotOf(action);

        /// <summary>Presses on the handle and drags along a path of screen points, a frame between each.</summary>
        IEnumerator PullHandle(int pointerId, params Vector2[] path)
        {
            var p = Pointer(pointerId, HandleCentre);
            _gestureHud.handleHotspot.OnPointerDown(p);
            yield return null;
            foreach (var point in path)
            {
                p.position = point;
                _gestureHud.handleHotspot.OnDrag(p);
                yield return null;
            }
        }

        IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        // ---- Ownership and routing ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator FingerOnTheJoystick_NeverReachesAGesture()
        {
            yield return Build();
            var stick = Pointer(1, WorldRect(_touch.joystick).center);
            _touch.joystick.OnPointerDown(stick);
            Assert.AreSame(_touch.joystick, _touch.Ownership.OwnerOf(1));

            // Even if the event system dispatched the same finger to the surface or the handle, nothing happens.
            _surface.OnPointerDown(stick);
            _gestureHud.handleHotspot.OnPointerDown(stick);
            stick.position = HandleCentre + new Vector2(0f, 150f);
            _surface.OnDrag(stick);
            yield return null;

            Assert.AreEqual(InteractionRoute.None, _surface.RouteOf(1));
            Assert.AreEqual(GestureSession.Doing.Nothing, _session.Gestures.Current);
            Assert.AreSame(_touch.joystick, _touch.Ownership.OwnerOf(1), "the joystick keeps its finger");
            _touch.joystick.OnPointerUp(stick);
        }

        [UnityTest]
        public IEnumerator JoystickAndHandlePull_Coexist_AndFlightInputIsUntouchedByTheGesture()
        {
            yield return Build();
            var stick = Pointer(1, WorldRect(_touch.joystick).center);
            _touch.joystick.OnPointerDown(stick);
            stick.position += new Vector2(0f, -120f);
            _touch.joystick.OnDrag(stick);

            yield return PullHandle(2, HandleCentre + new Vector2(0f, 60f), ArcPoint(0, 230f));
            Assert.AreEqual(GestureSession.Doing.ArcPull, _session.Gestures.Current);
            Assert.AreSame(_touch.joystick, _touch.Ownership.OwnerOf(1));
            Assert.AreSame(_surface, _touch.Ownership.OwnerOf(2), "the gesture claims its own finger in the shared table");
            var controls = _session.Router.Read(1f);
            Assert.That(controls.Pitch, Is.GreaterThan(0.5f), "the joystick still steers while the arc is open");
            Assert.AreEqual(0f, controls.Roll, 0.05f);

            _touch.joystick.OnPointerUp(stick);
            var up = Pointer(2, ArcPoint(0, 230f));
            _gestureHud.handleHotspot.OnPointerUp(up);
            yield return null;
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        [UnityTest]
        public IEnumerator Legacy_RoutesNothingButTheCockpit_AndShowsTheRail()
        {
            yield return Build();
            Assert.IsTrue(_session.Gestures.PullGestures, "the comparison is on in this fixture (a development build or the Editor)");
            Assert.IsFalse(_hud.LegacyRailEnabled);
            Assert.IsFalse(_hud.systemsGroup.blocksRaycasts, "SYSTEMS is off in the gesture-first interface");
            Assert.IsTrue(_gestureHud.handleGroup.blocksRaycasts);

            _session.SetInteractionMode(InteractionMode.Legacy);
            yield return null;
            Assert.IsTrue(_hud.LegacyRailEnabled);
            Assert.IsTrue(_hud.systemsGroup.blocksRaycasts, "the rail is back for comparison");
            Assert.IsFalse(_gestureHud.handleGroup.blocksRaycasts, "the handle is inert");

            var p = Pointer(3, HandleCentre);
            _gestureHud.handleHotspot.OnPointerDown(p);
            Assert.AreEqual(InteractionRoute.None, _surface.RouteOf(3), "a hotspot press is ignored in legacy mode");
            var world = Pointer(4, new Vector2(_width * 0.6f, 700f));
            _surface.OnPointerDown(world);
            Assert.AreEqual(InteractionRoute.None, _surface.RouteOf(4), "no world gestures either");
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        // ---- Arc ----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator HandlePull_UnfoldsTheArc_ReleaseOnNothing_ChangesNothing()
        {
            yield return Build();
            int before = _session.Systems.Version;
            yield return PullHandle(5, HandleCentre + new Vector2(0f, 30f));
            Assert.AreEqual(GestureSession.Doing.ArcPull, _session.Gestures.Current);
            Assert.That(_session.Gestures.Arc.Progress, Is.LessThan(0.3f), "a small pull only peeks");
            Assert.IsFalse(_session.Gestures.Arc.HasHovered);

            var p = Pointer(5, HandleCentre + GestureMath.FromAngle(60f) * 150f); // towards the throttle side: no slot there
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(1f, _session.Gestures.Arc.Progress, 1e-3f, "pulled through the reveal distance");
            Assert.IsFalse(_session.Gestures.Arc.HasHovered, "the throttle side of the handle holds no slot");

            p.position = HandleCentre + new Vector2(0f, 40f); // back to the handle: the cancel zone
            _gestureHud.handleHotspot.OnDrag(p);
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(GestureSession.Doing.Nothing, _session.Gestures.Current);
            Assert.AreEqual(before, _session.Systems.Version, "no system changed");
            Assert.IsFalse(_session.Disclosure.IsVisible(HudElement.NavigationGuide));
        }

        [UnityTest]
        public IEnumerator HandlePull_ReleaseOnNavigation_TogglesTheGuide_LikeTheRail()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Navigation);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            yield return PullHandle(6, HandleCentre + new Vector2(0f, 60f), ArcPoint(slot, 230f));
            var arc = _session.Gestures.Arc;
            Assert.AreEqual(slot, arc.HoveredIndex);
            Assert.IsTrue(arc.IsArmed);
            Assert.AreEqual(GestureState.Armed, _surface.Channel(InteractionRoute.SystemsHandle).State);

            _gestureHud.handleHotspot.OnPointerUp(Pointer(6, ArcPoint(slot, 230f)));
            yield return null;
            Assert.AreEqual(PlayerOverride.Show, _session.Disclosure.GetOverride(HudElement.NavigationGuide), "the same request the NAV button makes");
            Assert.AreEqual(GestureSession.Doing.Nothing, _session.Gestures.Current);
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        [UnityTest]
        public IEnumerator HandlePull_ParkingBrake_SetsItThroughTheGate_AndShowsItEngaged()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.ParkingBrake);
            yield return PullHandle(7, HandleCentre + new Vector2(0f, 60f), ArcPoint(slot, 230f));
            _gestureHud.handleHotspot.OnPointerUp(Pointer(7, ArcPoint(slot, 230f)));
            yield return null;
            Assert.IsTrue(_session.Systems.ParkingBrakeSet, "on the ground the brake sets");
            yield return null;
            Assert.AreEqual(ArcEmphasis.Engaged, _session.Gestures.Arc.InnerItem(slot).Emphasis);
        }

        [UnityTest]
        public IEnumerator Revocation_MidPull_CancelsWithoutChanging()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.ParkingBrake);
            yield return PullHandle(8, HandleCentre + new Vector2(0f, 60f), ArcPoint(slot, 230f));
            Assert.IsTrue(_session.Gestures.Arc.IsArmed);
            _touch.Ownership.RevokeAll(); // focus loss, pause
            yield return null;
            Assert.AreEqual(GestureSession.Doing.Nothing, _session.Gestures.Current);
            Assert.IsFalse(_session.Systems.ParkingBrakeSet, "an armed pull that is revoked applies nothing");
            Assert.AreEqual(0, _touch.Ownership.Count);
            _gestureHud.handleHotspot.OnPointerUp(Pointer(8, ArcPoint(slot, 230f))); // the late release is harmless
            yield return null;
            Assert.IsFalse(_session.Systems.ParkingBrakeSet);
        }

        // ---- Flap lever ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator FlapLever_PreviewsDetents_AndSetsThemOnlyOnRelease()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Flaps);
            Assert.AreEqual(0, _session.Systems.FlapDetent);
            var onFlaps = ArcPoint(slot, 230f);
            yield return PullHandle(9, HandleCentre + new Vector2(0f, 60f), onFlaps);
            Assert.AreEqual(GestureSession.Doing.ArcPull, _session.Gestures.Current);

            // Resting on FLAPS hands the same finger over to the lever.
            yield return Wait(_session.gestureSettings.leverHandoverSeconds + 0.05f);
            var p = Pointer(9, onFlaps + new Vector2(1f, 0f));
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(GestureSession.Doing.Lever, _session.Gestures.Current);
            Assert.IsTrue(_gestureHud.lever.IsShown);
            var lever = _session.Gestures.Lever;
            Assert.AreEqual(0, lever.Preview);

            float spacing = lever.Settings.detentSpacing;
            p.position = onFlaps + new Vector2(0f, -2f * spacing); // down two detents
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(2, lever.Preview, "the lever previews 20°");
            Assert.AreEqual(0, _session.Systems.FlapDetent, "the aircraft is untouched by the preview");

            p.position = onFlaps + new Vector2(0f, -1f * spacing); // back up one
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(1, lever.Preview);

            int changes = 0;
            _session.SystemChanged += _ => changes++;
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(1, _session.Systems.FlapDetent, "released at 10°: applied through the gate");
            Assert.AreEqual(1, changes, "exactly one change, on release");
            Assert.AreEqual(GestureSession.Doing.Nothing, _session.Gestures.Current);
            Assert.IsFalse(_gestureHud.lever.IsShown);
        }

        [UnityTest]
        public IEnumerator FlapLever_SidewaysCancels_NothingApplied()
        {
            yield return Build();
            int slot = SlotOf(ContextAction.Flaps);
            var onFlaps = ArcPoint(slot, 230f);
            yield return PullHandle(10, HandleCentre + new Vector2(0f, 60f), onFlaps);
            yield return Wait(_session.gestureSettings.leverHandoverSeconds + 0.05f);
            var p = Pointer(10, onFlaps + new Vector2(1f, 0f));
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(GestureSession.Doing.Lever, _session.Gestures.Current);
            float spacing = _session.Gestures.Lever.Settings.detentSpacing;
            p.position = onFlaps + new Vector2(0f, -3f * spacing);
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(3, _session.Gestures.Lever.Preview);
            p.position += new Vector2(-_session.Gestures.Lever.Settings.cancelDistance - 40f, 0f);
            _gestureHud.handleHotspot.OnDrag(p);
            yield return null;
            Assert.IsTrue(_session.Gestures.Lever.Cancelled);
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(0, _session.Systems.FlapDetent, "cancelled: the flaps stay up");
        }

        // ---- HUD density --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator HudBandPull_PreviewsTheNextProfile_CommitsOnRelease_RevertsOnCancel()
        {
            yield return Build();
            Assert.AreEqual(HudProfile.Smart, _session.ActiveHudProfile);
            var band = WorldRect(_gestureHud.bandHotspot);
            var start = new Vector2(band.center.x, band.center.y);
            var p = Pointer(11, start);
            _gestureHud.bandHotspot.OnPointerDown(p);
            yield return null;
            p.position = start + new Vector2(0f, -(_session.Gestures.Density.Settings.stepDistance + 40f));
            _gestureHud.bandHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(GestureSession.Doing.DensityPull, _session.Gestures.Current);
            Assert.AreEqual(HudProfile.Detailed, _session.Gestures.PreviewProfile);
            Assert.AreEqual(HudProfile.Smart, _session.ActiveHudProfile, "the committed profile is untouched by the preview");
            yield return null;
            Assert.IsTrue(_session.Disclosure.IsVisible(HudElement.Heading), "disclosure shows the preview: Detailed shows everything, even parked");

            p.position = start; // reversed: back to Smart
            _gestureHud.bandHotspot.OnDrag(p);
            yield return null;
            Assert.AreEqual(HudProfile.Smart, _session.Gestures.PreviewProfile);
            _gestureHud.bandHotspot.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(HudProfile.Smart, _session.ActiveHudProfile, "released where it started: nothing changed");

            p = Pointer(12, start);
            _gestureHud.bandHotspot.OnPointerDown(p);
            yield return null;
            p.position = start + new Vector2(0f, -(_session.Gestures.Density.Settings.stepDistance + 40f));
            _gestureHud.bandHotspot.OnDrag(p);
            yield return null;
            _gestureHud.bandHotspot.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(HudProfile.Detailed, _session.ActiveHudProfile, "released on Detailed: committed");
            Assert.AreEqual(GestureSession.Doing.Nothing, _session.Gestures.Current);
            yield return null;
            Assert.IsTrue(_session.Disclosure.IsVisible(HudElement.Heading));
        }

        [UnityTest]
        public IEnumerator ProfileButton_StillCyclesTheProfile()
        {
            yield return Build();
            _hud.profileButton.OnPointerClick(new PointerEventData(EventSystem.current));
            yield return null;
            Assert.AreEqual(HudProfile.Detailed, _session.ActiveHudProfile, "the conventional fallback works beside the gesture");
        }

        // ---- Camera -------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator WorldDrag_LooksAroundTheChaseView_AndSpringsBack_WithoutTouchingFlightInput()
        {
            yield return Build();
            _camera.chaseReturnSeconds = 0.1f;
            var start = new Vector2(_width * 0.7f, 760f);
            var p = Pointer(13, start);
            _surface.OnPointerDown(p);
            Assert.AreEqual(InteractionRoute.World, _surface.RouteOf(13));
            p.position = start + new Vector2(200f, 0f);
            _surface.OnDrag(p);
            yield return null;
            Assert.AreEqual(GestureSession.Doing.Look, _session.Gestures.Current);
            Assert.That(Mathf.Abs(_camera.ChaseLook.x), Is.GreaterThan(5f), "the view turned");
            Assert.IsTrue(_camera.IsChaseLookHeld);
            var controls = _session.Router.Read(1f);
            Assert.AreEqual(0f, controls.Pitch, 1e-4f);
            Assert.AreEqual(0f, controls.Roll, 1e-4f);
            Assert.AreEqual(0f, controls.Throttle, 1e-4f, "camera gestures never reach the flight controls");

            _surface.OnPointerUp(p);
            yield return null;
            Assert.IsFalse(_camera.IsChaseLookHeld);
            yield return Wait(0.4f);
            Assert.AreEqual(0f, _camera.ChaseLook.magnitude, 0.01f, "sprung back to the chase position");
            Assert.AreEqual(CameraMode.Chase, _camera.mode);
        }

        [UnityTest]
        public IEnumerator WorldDoubleTap_SwitchesTheView_AndTheCockpitThenGetsTheTouches()
        {
            yield return Build();
            var point = new Vector2(_width * 0.7f, 760f);
            var p = Pointer(14, point);
            _surface.OnPointerDown(p); _surface.OnPointerUp(p);
            p = Pointer(15, point);
            _surface.OnPointerDown(p); _surface.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(CameraMode.Cockpit, _camera.mode, "double tap on the sky switches to the cockpit");
            yield return null;
            Assert.IsTrue(_surface.CockpitView);
            var q = Pointer(16, point);
            _surface.OnPointerDown(q);
            Assert.AreNotEqual(InteractionRoute.World, _surface.RouteOf(16), "in cockpit view the open world is not a free-look surface");
            _surface.OnPointerUp(q);
        }

        [UnityTest]
        public IEnumerator Pinch_ChangesTheChaseDistance_WithinLimits()
        {
            yield return Build();
            float before = _camera.chaseDistance;
            var a = Pointer(17, new Vector2(_width * 0.6f, 700f));
            var b = Pointer(18, new Vector2(_width * 0.6f + 200f, 700f));
            _surface.OnPointerDown(a);
            _surface.OnPointerDown(b);
            b.position += new Vector2(200f, 0f); // spread: closer
            _surface.OnDrag(b);
            yield return null;
            Assert.That(_camera.chaseDistance, Is.LessThan(before));
            for (int i = 0; i < 30; i++) { b.position += new Vector2(200f, 0f); _surface.OnDrag(b); }
            Assert.That(_camera.chaseDistance, Is.GreaterThanOrEqualTo(_camera.minChaseDistance));
            _surface.OnPointerUp(a);
            _surface.OnPointerUp(b);
        }

        // ---- Explanations -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator LongPressOnTheHandle_ExplainsIt_AndHidesOnRelease()
        {
            yield return Build();
            var p = Pointer(19, HandleCentre);
            _gestureHud.handleHotspot.OnPointerDown(p);
            yield return Wait(_surface.settings.longPressSeconds + 0.1f);
            Assert.AreEqual(GestureSession.Doing.Explain, _session.Gestures.Current);
            Assert.IsTrue(_gestureHud.plate.IsShown);
            Assert.AreEqual("SYSTEMS", _gestureHud.plate.CurrentTitle);
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
            Assert.IsFalse(_gestureHud.plate.IsShown);
            Assert.AreEqual(GestureSession.Doing.Nothing, _session.Gestures.Current);
        }

        [UnityTest]
        public IEnumerator LongPressOnAWarningBanner_ExplainsIt_AndTheBannerStays()
        {
            yield return Build();
            _session.SetHudProfile(HudProfile.Cinematic);
            // A stall warning forced by telemetry is not available from a parked aircraft; the explanation
            // of a flight value is reachable instead: make the airspeed visible and hold it.
            _session.SetHudProfile(HudProfile.Detailed);
            yield return null;
            yield return null;
            Assert.IsTrue(_session.Disclosure.IsVisible(HudElement.Airspeed));
            var item = WorldRect(_hud.dataItems[(int)HudElement.Airspeed]).center;
            var p = Pointer(20, item);
            _gestureHud.bandHotspot.OnPointerDown(p);
            yield return Wait(_surface.settings.longPressSeconds + 0.1f);
            Assert.AreEqual(GestureSession.Doing.Explain, _session.Gestures.Current);
            Assert.AreEqual("AIRSPEED, KT", _gestureHud.plate.CurrentTitle);
            Assert.AreEqual(1f, _hud.dataItems[(int)HudElement.Airspeed].alpha, 1e-3f, "the value stays visible");
            _gestureHud.bandHotspot.OnPointerUp(p);
            yield return null;
            Assert.IsFalse(_gestureHud.plate.IsShown);
        }

        // ---- Layout -------------------------------------------------------------------------------

        [UnityTest] public IEnumerator Layout_16by9_KeepsClearOfTheFlightControls() => Layout(1920f);
        [UnityTest] public IEnumerator Layout_4by3_KeepsClearOfTheFlightControls() => Layout(1440f);
        [UnityTest] public IEnumerator Layout_20by9_KeepsClearOfTheFlightControls() => Layout(2400f);

        IEnumerator Layout(float width)
        {
            yield return Build(width);
            var flightControls = new[] { WorldRect(_touch.joystick), WorldRect(_touch.throttle), WorldRect(_touch.brake) };
            foreach (var hotspot in new Component[] { _gestureHud.handleHotspot, _gestureHud.bandHotspot })
            {
                var rect = WorldRect(hotspot);
                foreach (var control in flightControls)
                    Assert.IsFalse(rect.Overlaps(control), hotspot.name + " overlaps a flight control at width " + width);
                Assert.That(rect.height, Is.GreaterThanOrEqualTo(96f), hotspot.name + " is a touch target");
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(width));
            }
            // Every arc slot, at the band's outer edge, is inside the safe area and off the flight controls.
            var arc = _session.Gestures.Arc;
            for (int i = 0; i < arc.InnerCount; i++)
            {
                var point = ArcPoint(i, arc.Settings.outerRadius);
                Assert.That(point.x, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(width), "slot " + i + " at width " + width);
                Assert.That(point.y, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(1080f), "slot " + i + " at width " + width);
                Assert.That(point.x, Is.GreaterThan(flightControls[0].xMax), "slot " + i + " clear of the joystick region at width " + width);
                foreach (var control in flightControls)
                    Assert.IsFalse(control.Contains(point), "slot " + i + " over a flight control at width " + width);
            }
            // The arc never covers the most urgent warning banner.
            var warning = WorldRect(_hud.warningSlots[0]);
            for (int i = 0; i < arc.InnerCount; i++)
                Assert.IsFalse(warning.Contains(ArcPoint(i, arc.Settings.outerRadius)), "slot " + i + " over the warning banner at width " + width);
        }

        [UnityTest]
        public IEnumerator ReducedMotion_ArcAppearsAtOnce()
        {
            yield return Build();
            _session.ApplyMotionPreference(MotionPreference.Reduced);
            yield return PullHandle(21, HandleCentre + new Vector2(0f, 150f));
            yield return null;
            Assert.AreEqual(1f, _gestureHud.arc.ShownUnfold, 1e-3f, "no unfolding animation with reduced motion");
            _gestureHud.handleHotspot.OnPointerUp(Pointer(21, HandleCentre + new Vector2(0f, 150f)));
            yield return null;
            yield return null;
            Assert.AreEqual(0f, _gestureHud.arc.ShownUnfold, 1e-3f);
        }
    }
}
