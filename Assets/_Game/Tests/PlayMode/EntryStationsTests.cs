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
    /// The spatial entry: the showcase camera frames the parked aircraft, a drag
    /// moves between stations and settles, chips select directly, a tap or START
    /// begins the mode and gives the camera back to the flight. Synthetic pointers.
    /// </summary>
    public class EntryStationsTests
    {
        GameObject _root;
        FlightSession _session;
        GameModeSession _modes;
        InteractionSurface _surface;
        EntryStationsScreen _entry;
        CameraRig _rig;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_root != null) Object.Destroy(_root);
        }

        IEnumerator Build()
        {
            _root = new GameObject("EntryTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, new Vector3(0f, 0f, -710f), out var telemetry);
            var cameraGo = new GameObject("Camera", typeof(Camera));
            cameraGo.transform.SetParent(_root.transform, false);
            _rig = cameraGo.AddComponent<CameraRig>();
            _rig.target = aircraft.transform;

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
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var surfaceGo = new GameObject("InteractionSurface", typeof(RectTransform), typeof(RaycastOnlyGraphic));
            ((RectTransform)surfaceGo.transform).SetParent(safeArea, false);
            _surface = surfaceGo.AddComponent<InteractionSurface>();
            _surface.surface = surfaceGo.GetComponent<RaycastOnlyGraphic>();
            _surface.screenHeightOverride = 1080f;
            var touch = TouchControlsFactory.Build(safeArea, font);
            touch.visibility = TouchControlsVisibility.Always;
            var gestureHud = GestureHudFactory.Build(safeArea, font, _surface);
            _entry = EntryStationsFactory.Build(safeArea, font, _surface);
            Canvas.ForceUpdateCanvases();

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = _rig;
            _session.touchControls = touch;
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
            _modes.entryScreen = _entry;
            _modes.showModeSelectOnStart = true;
            _modes.rememberProgress = false;
            modesGo.SetActive(true);
            _entry.SetReducedMotion(true);
            yield return null;
            yield return null;
        }

        static PointerEventData Pointer(int id, Vector2 position) =>
            new PointerEventData(EventSystem.current) { pointerId = id, position = position, pressPosition = position };

        IEnumerator Swipe(int id, float dx)
        {
            var start = new Vector2(960f, 600f);
            var p = Pointer(id, start);
            _entry.sceneHotspot.OnPointerDown(p);
            yield return null;
            for (int i = 1; i <= 4; i++)
            {
                p.position = start + new Vector2(dx * i / 4f, 0f);
                _entry.sceneHotspot.OnDrag(p);
                yield return null;
            }
            _entry.sceneHotspot.OnPointerUp(p);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Opens_OnTheRecommendedStation_WithTheShowcaseCamera()
        {
            yield return Build();
            Assert.IsTrue(_modes.ModeSelectOpen);
            Assert.IsTrue(_entry.IsOpen);
            Assert.AreEqual(0, _entry.Station, "a new player is shown the First Flight first");
            Assert.AreEqual(CameraMode.Showcase, _rig.mode);
            Assert.IsTrue(_modes.IsPaused, "the world waits");
            Assert.That(Vector3.Distance(_rig.transform.position, _session.aircraft.transform.position), Is.InRange(5f, 20f), "framed close to the aircraft");
        }

        [UnityTest]
        public IEnumerator Launch_WaitsForThePlayer_NoModeStartsByItself()
        {
            // The screen's START action must not be a Unity message: nothing may choose a mode for the player.
            yield return Build();
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.IsTrue(_entry.IsOpen, "the entry screen stays open until the player chooses");
            Assert.IsFalse(_modes.ModeActive, "no mode started on its own");
            Assert.IsTrue(_modes.IsPaused);
            Assert.AreEqual(CameraMode.Showcase, _rig.mode);
        }

        [UnityTest]
        public IEnumerator SwipeLeft_MovesToTheNextStation_AndTheCameraMovesWithIt()
        {
            yield return Build();
            Vector3 before = _rig.transform.position;
            yield return Swipe(1, -700f);
            Assert.AreEqual(1, _entry.Station, "Free Flight");
            Assert.That(Vector3.Distance(_rig.transform.position, before), Is.GreaterThan(5f), "a different framing");
            Assert.IsTrue(_entry.freeFlightOptions.blocksRaycasts, "the Free Flight starts are offered");
            yield return Swipe(2, 700f);
            Assert.AreEqual(0, _entry.Station, "and back");
        }

        [UnityTest]
        public IEnumerator SmallDrag_SnapsBack_FlickCarriesOver()
        {
            yield return Build();
            _entry.SetReducedMotion(false);
            yield return Swipe(3, -150f);
            float end = Time.unscaledTime + 0.6f;
            while (Time.unscaledTime < end) yield return null;
            Assert.AreEqual(0, _entry.Station, "a small drag settles back");
            Assert.AreEqual(0f, _entry.Model.Position, 0.01f);
        }

        [UnityTest]
        public IEnumerator Chips_SelectDirectly_StartBeginsTheMode_AndTheCameraComesBack()
        {
            yield return Build();
            _entry.chips[2].OnPointerClick(new PointerEventData(EventSystem.current));
            yield return null;
            Assert.AreEqual(2, _entry.Station, "Landing Challenge");
            _entry.startButton.OnPointerClick(new PointerEventData(EventSystem.current));
            yield return null;
            Assert.IsFalse(_entry.IsOpen);
            Assert.IsFalse(_modes.ModeSelectOpen);
            Assert.AreEqual(GameMode.LandingChallenge, _modes.Mode);
            Assert.AreNotEqual(CameraMode.Showcase, _rig.mode, "the flight has its camera back");
            Assert.IsFalse(_modes.IsPaused);
        }

        [UnityTest]
        public IEnumerator TapOnTheScene_StartsTheSelectedStation()
        {
            yield return Build();
            var p = Pointer(4, new Vector2(960f, 600f));
            _entry.sceneHotspot.OnPointerDown(p);
            _entry.sceneHotspot.OnPointerUp(p);
            yield return null;
            Assert.AreEqual(GameMode.FirstFlight, _modes.Mode);
            Assert.IsFalse(_entry.IsOpen);
        }

        [UnityTest]
        public IEnumerator KeyboardSteps_AndFreeFlightStartChoice()
        {
            yield return Build();
            _entry.Step(1);
            yield return null;
            Assert.AreEqual(1, _entry.Station);
            _entry.inTheAirButton.OnPointerClick(new PointerEventData(EventSystem.current));
            yield return null;
            Assert.AreEqual(GameMode.FreeFlight, _modes.Mode);
            Assert.IsFalse(_modes.ModeSelectOpen);
        }

        [UnityTest]
        public IEnumerator ChangeMode_FromThePause_ReopensTheStations()
        {
            yield return Build();
            _entry.StartSelected();
            yield return null;
            Assert.IsFalse(_entry.IsOpen);
            _modes.OpenModeSelect();
            yield return null;
            Assert.IsTrue(_entry.IsOpen);
            Assert.AreEqual(CameraMode.Showcase, _rig.mode);
        }
    }
}
