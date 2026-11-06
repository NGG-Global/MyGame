using System.Collections;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Gameplay;
using OpenSkies.Gestures;
using OpenSkies.Missions;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The gesture-driven pause: a two-finger tap on the world pauses and raises the
    /// sheet; the sheet is dragged between its stops, reveals the embedded settings,
    /// and is dismissed by a drag down, the scrim or RESUME; the flight resumes with
    /// no stuck input. Synthetic pointers on a 1920 × 1080 world-space canvas.
    /// </summary>
    public class PauseSheetTests
    {
        GameObject _root;
        FlightSession _session;
        GameModeSession _modes;
        TouchFlightControls _touch;
        InteractionSurface _surface;
        PauseSheet _sheet;
        float _width;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_root != null) Object.Destroy(_root);
        }

        IEnumerator Build(float width = 1920f)
        {
            _width = width;
            _root = new GameObject("PauseSheetTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, new Vector3(0f, 0f, -710f), out var telemetry);
            var cameraGo = new GameObject("Camera", typeof(Camera));
            cameraGo.transform.SetParent(_root.transform, false);
            var rig = cameraGo.AddComponent<CameraRig>();
            rig.target = aircraft.transform;

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
            var hud = ContextualHudFactory.Build(safeArea, font);
            hud.SetMotionPreference(MotionPreference.Reduced);
            var gestureHud = GestureHudFactory.Build(safeArea, font, _surface);
            var card = MissionUiFactory.BuildMissionCard(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset,
                CockpitFocusPanelFactory.ColumnBottom, CockpitFocusPanelFactory.ColumnTop);
            _sheet = PauseSheetFactory.Build(safeArea, font, _surface);
            Canvas.ForceUpdateCanvases();

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = rig;
            _session.touchControls = _touch;
            _session.contextualHud = hud;
            _session.interactionSurface = _surface;
            _session.gestureHud = gestureHud;
            _session.rememberHudPreferences = false;
            _session.homeRunway = new RunwayInfo(Vector3.zero, 0f, 1500f, 30f);
            sessionGo.SetActive(true);

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
            gestureHud.SetReducedMotion(true);
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

        IEnumerator TwoFingerTapTheSky()
        {
            var sky = new Vector2(_width * 0.7f, 760f);
            var a = Pointer(1, sky);
            var b = Pointer(2, sky + new Vector2(120f, 0f));
            _surface.OnPointerDown(a);
            _surface.OnPointerDown(b);
            _surface.OnPointerUp(a);
            _surface.OnPointerUp(b);
            yield return null;
            yield return null;
        }

        Vector2 GripCentre => WorldRect(_sheet.grip).center;

        IEnumerator DragGrip(int id, float dy, float seconds = 0.2f)
        {
            var start = GripCentre;
            var p = Pointer(id, start);
            _sheet.grip.OnPointerDown(p);
            yield return null;
            int steps = 4;
            for (int i = 1; i <= steps; i++)
            {
                p.position = start + new Vector2(0f, dy * i / steps);
                _sheet.grip.OnDrag(p);
                yield return null;
            }
            _sheet.grip.OnPointerUp(p);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TwoFingerTapOnTheSky_PausesAndRaisesTheSheet_AtTheFirstStop()
        {
            yield return Build();
            Assert.IsFalse(_modes.IsPaused);
            yield return TwoFingerTapTheSky();
            Assert.IsTrue(_modes.IsPaused, "the flight froze");
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(_sheet.IsOpen);
            Assert.AreEqual(SheetModel.FirstStop, _sheet.Stop);
            Assert.That(_sheet.Model.Height, Is.GreaterThan(200f), "the sheet is up (reduced motion: at once)");
            Assert.IsFalse(_sheet.SettingsVisible);
            Assert.AreEqual(4, _sheet.ButtonCount, "RESUME, RESTART, the guide and CHANGE MODE for Free Flight");
        }

        [UnityTest]
        public IEnumerator JoystickAndThrottleTogether_NeverPause()
        {
            yield return Build();
            var stick = Pointer(3, WorldRect(_touch.joystick).center);
            var lever = Pointer(4, WorldRect(_touch.throttle).center);
            _touch.joystick.OnPointerDown(stick);
            _touch.throttle.OnPointerDown(lever);
            _surface.OnPointerDown(stick); // even if the event system dispatched them on
            _surface.OnPointerDown(lever);
            _touch.joystick.OnPointerUp(stick);
            _touch.throttle.OnPointerUp(lever);
            yield return null;
            Assert.IsFalse(_modes.IsPaused, "two flight-control fingers are never a pause");
        }

        [UnityTest]
        public IEnumerator PullingTheSheetUp_RevealsTheSettings_PullingDownDismisses()
        {
            yield return Build();
            yield return TwoFingerTapTheSky();
            Assert.IsTrue(_sheet.IsOpen);
            yield return DragGrip(5, 500f);
            Assert.AreEqual(SheetModel.SecondStop, _sheet.Stop, "pulled to the settings");
            Assert.IsTrue(_sheet.SettingsVisible);
            Assert.IsTrue(_sheet.settings.IsOpen);
            Assert.That(_sheet.secondStopGroup.alpha, Is.GreaterThan(0.9f));
            Assert.IsTrue(_modes.IsPaused, "still paused underneath");

            yield return DragGrip(6, -400f);
            Assert.AreEqual(SheetModel.FirstStop, _sheet.Stop, "back to the first stop");
            yield return DragGrip(7, -260f);
            Assert.IsFalse(_sheet.IsOpen, "dragged below the dismiss share: resumed");
            Assert.IsFalse(_modes.IsPaused);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(0, _touch.Ownership.Count, "no stuck pointers");
        }

        [UnityTest]
        public IEnumerator ScrimTapNeverResumes_TheResumeButtonDoes_AndTheFlightControlsWorkAgain()
        {
            yield return Build();
            yield return TwoFingerTapTheSky();
            _sheet.scrimButton.OnPointerClick(new PointerEventData(EventSystem.current));
            yield return null;
            Assert.IsTrue(_modes.IsPaused, "a tap on the dimmed world is consumed: resuming is deliberate (docs/TAP_DISCLOSURE_DESIGN.md)");
            Assert.IsTrue(_sheet.IsOpen);
            Assert.AreEqual(0f, Time.timeScale);

            _sheet.buttons[0].OnPointerClick(new PointerEventData(EventSystem.current)); // RESUME
            yield return null;
            Assert.IsFalse(_modes.IsPaused);
            var stick = Pointer(8, WorldRect(_touch.joystick).center);
            _touch.joystick.OnPointerDown(stick);
            stick.position += new Vector2(0f, -120f);
            _touch.joystick.OnDrag(stick);
            Assert.That(_session.Router.Read(1f).Pitch, Is.GreaterThan(0.5f), "the stick works after a resume");
            _touch.joystick.OnPointerUp(stick);
        }

        [UnityTest]
        public IEnumerator SheetButtons_ChangeMode_AndRestart_WorkLikeTheCard()
        {
            yield return Build();
            yield return TwoFingerTapTheSky();
            Assert.AreEqual(GameMode.FreeFlight, _modes.Mode);
            _sheet.buttons[1].OnPointerClick(new PointerEventData(EventSystem.current)); // RESTART ON THE RUNWAY
            yield return null;
            Assert.IsFalse(_modes.IsPaused);
            Assert.IsFalse(_sheet.IsOpen);
            Assert.IsTrue(_modes.ModeActive);
        }

        [UnityTest]
        public IEnumerator ReducedMotion_SheetAppearsAtItsStopAtOnce()
        {
            yield return Build();
            _sheet.SetReducedMotion(true);
            _modes.OpenMenu();
            yield return null;
            Assert.AreEqual(_sheet.Model.StopHeight(SheetModel.FirstStop), _sheet.Model.Height, 1e-3f);
            Assert.IsFalse(_sheet.Model.IsSettling);
        }

        [UnityTest]
        public IEnumerator Layout_SheetFitsTheScreen_AndItsButtonsAreTouchTargets()
        {
            yield return Build(1440f);
            _modes.OpenMenu();
            yield return null;
            yield return null;
            var sheet = WorldRect(_sheet.sheet);
            Assert.That(sheet.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(sheet.xMax, Is.LessThanOrEqualTo(1440f));
            for (int i = 0; i < _sheet.ButtonCount; i++)
            {
                var r = WorldRect(_sheet.buttons[i]);
                Assert.That(r.height, Is.GreaterThanOrEqualTo(96f), "button " + i);
                Assert.That(r.yMin, Is.GreaterThanOrEqualTo(0f), "button " + i + " above the bottom edge");
                Assert.That(r.yMax, Is.LessThanOrEqualTo(sheet.yMax + 1f), "button " + i + " inside the sheet");
            }
            Assert.That(_sheet.Model.StopHeight(SheetModel.SecondStop), Is.LessThanOrEqualTo(1080f - 48f));
        }
    }
}
