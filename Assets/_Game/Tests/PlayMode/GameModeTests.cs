using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Gameplay;
using OpenSkies.Missions;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Game modes over a real <see cref="FlightSession"/> and Rigidbody aircraft: scenario
    /// starts in the air and on final, the tutorial never flying the aircraft, the pause
    /// menu, skipping, retries, the entry screen, remembered choices and the layout of the
    /// mission UI. EditMode tests cover the tutorial logic in depth.
    /// </summary>
    public class GameModeTests
    {
        static readonly Vector3 RunwayStart = new Vector3(0f, 0f, -710f);
        static readonly RunwayInfo Runway = new RunwayInfo(Vector3.zero, 0f, 1500f, 30f);

        GameObject _root;
        FlightSession _session;
        GameModeSession _modes;
        CameraRig _rig;
        MissionCard _card;
        CoachingPanel _coach;
        HudButton _menu, _skip;
        ModeSelectScreen _modeSelect;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_root != null) Object.Destroy(_root);
        }

        static PointerEventData Pointer() => new PointerEventData(EventSystem.current);

        static RectTransform SafeArea(Transform parent, float width, Vector3 position)
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)canvasGo.transform;
            canvas.sizeDelta = new Vector2(width, 1080f);
            canvas.pivot = Vector2.zero;
            canvas.position = position;
            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(canvas, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            return safeArea;
        }

        IEnumerator Build(bool withUi, bool showModeSelect = false)
        {
            _root = new GameObject("GameModeTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, RunwayStart, out var telemetry);
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);

            var cockpit = new GameObject("CockpitAnchor").transform;
            cockpit.SetParent(aircraft.transform, false);
            cockpit.localPosition = new Vector3(0f, 1.7f, 1f);
            var cameraGo = new GameObject("Camera", typeof(Camera));
            cameraGo.transform.SetParent(_root.transform, false);
            _rig = cameraGo.AddComponent<CameraRig>();
            _rig.target = aircraft.transform;
            _rig.cockpitAnchor = cockpit;

            if (withUi)
            {
                var safeArea = SafeArea(_root.transform, 1920f, new Vector3(0f, 0f, 5000f));
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                TouchControlsFactory.Build(safeArea, font).visibility = TouchControlsVisibility.Never;
                _coach = MissionUiFactory.BuildCoachingPanel(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
                _menu = MissionUiFactory.BuildMenuButton(safeArea, font);
                _skip = MissionUiFactory.BuildSkipButton(safeArea, font);
                _card = MissionUiFactory.BuildMissionCard(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset,
                    CockpitFocusPanelFactory.ColumnBottom, CockpitFocusPanelFactory.ColumnTop);
                _modeSelect = MissionUiFactory.BuildModeSelect(safeArea, font);
            }

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = _rig;
            _session.rememberHudPreferences = false; // never read or write the player's real preferences
            _session.homeRunway = Runway;
            sessionGo.SetActive(true);

            var modesGo = new GameObject("GameModes");
            modesGo.SetActive(false);
            modesGo.transform.SetParent(_root.transform, false);
            _modes = modesGo.AddComponent<GameModeSession>();
            _modes.flight = _session;
            _modes.rememberProgress = false;
            _modes.showModeSelectOnStart = showModeSelect;
            _modes.coach = _coach;
            _modes.card = _card;
            _modes.menuButton = _menu;
            _modes.skipButton = _skip;
            _modes.modeSelect = _modeSelect;
            modesGo.SetActive(true);
            if (withUi)
            {
                _card.SetReducedMotion(true);
                _coach.SetReducedMotion(true);
                _modeSelect.SetReducedMotion(true);
            }
            yield return null; // Start
            // The entry screen pauses the world (no physics steps until a mode is chosen).
            if (showModeSelect) yield return null;
            else yield return new WaitForFixedUpdate();
        }

        // ---- Scenario starts with the Rigidbody -------------------------------------------------------

        [UnityTest]
        public IEnumerator LandingChallenge_StartsEstablishedOnFinal_WithTheRigidbodyInitialised()
        {
            yield return Build(withUi: false);
            _modes.StartMode(GameMode.LandingChallenge);
            var body = _session.aircraft.Body;
            Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(25f), "the Rigidbody moves at approach speed from the first step");
            Assert.Less(body.linearVelocity.y, 0f, "descending");
            Assert.AreEqual(0f, body.position.x, 0.5f, "on the centreline");
            Assert.AreEqual(_session.Systems.FlapDetentCount - 1, _session.Systems.FlapDetent, "flaps set for landing");

            yield return new WaitForSeconds(1f);
            var t = _session.Telemetry;
            Assert.IsFalse(t.WeightOnWheels);
            Assert.That(t.FlapPosition, Is.GreaterThan(0.95f), "the flaps are out already, not travelling");
            Assert.That(t.EnginePower, Is.GreaterThan(0.05f), "the engine was started at approach power");
            Assert.That(t.IndicatedAirspeedKnots, Is.InRange(60f, 82f));
            Assert.That(t.VerticalSpeedFeetPerMinute, Is.InRange(-750f, -100f));
            Assert.That(t.HeightAboveGroundFeet, Is.InRange(380f, 650f));
            Assert.AreEqual(0f, Mathf.DeltaAngle(t.HeadingDegrees, 0f), 3f);
            Assert.Less(Mathf.Abs(t.BankDegrees), 5f);
            Assert.IsFalse(t.StallWarning);

            yield return new WaitForSeconds(5f);
            var phase = _session.PhaseEstimator.Phase;
            Assert.IsTrue(phase == FlightPhase.Approach || phase == FlightPhase.Landing, "recognised as an approach: " + phase);
            Assert.IsTrue(_session.Disclosure.IsVisible(HudElement.ApproachGuide), "the approach lights are shown");
            Assert.AreEqual(LandingChallenge.Status.Flying, _modes.Challenge.State);
            Assert.AreEqual(0, _session.Warnings.ActiveMask, "a stable start: no warning");
        }

        [UnityTest]
        public IEnumerator FreeFlight_InTheAir_StartsInSteadyLevelFlight()
        {
            yield return Build(withUi: false);
            _modes.StartMode(GameMode.FreeFlight, FreeFlightStart.Airborne);
            yield return new WaitForSeconds(0.5f);
            float altitude = _session.Telemetry.AltitudeFeet;
            yield return new WaitForSeconds(5f);
            var t = _session.Telemetry;
            Assert.IsFalse(t.WeightOnWheels);
            Assert.That(Mathf.Abs(t.AltitudeFeet - altitude), Is.LessThan(150f), "level: trimmed speed and level-flight power");
            Assert.That(t.IndicatedAirspeedKnots, Is.InRange(82f, 108f));
            Assert.IsFalse(t.StallWarning);

            _session.ResetFlight();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_session.Telemetry.WeightOnWheels, "RESET returns to the airborne start the player chose");
        }

        [UnityTest]
        public IEnumerator LandingChallenge_ResetIsARetry_FromFinalApproach()
        {
            yield return Build(withUi: false);
            _modes.StartMode(GameMode.LandingChallenge);
            Vector3 start = _session.aircraft.Body.position;
            yield return new WaitForSeconds(3f);
            Assert.That(Vector3.Distance(_session.aircraft.Body.position, start), Is.GreaterThan(50f));
            _session.ResetFlight();
            yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Distance(_session.aircraft.Body.position, start), Is.LessThan(5f), "back at the start of final");
            Assert.AreEqual(2, _modes.Challenge.Attempts);
            Assert.AreEqual(LandingChallenge.Status.Flying, _modes.Challenge.State);
        }

        // ---- The tutorial over the real session -------------------------------------------------------

        [UnityTest]
        public IEnumerator FirstFlight_NeverMovesAControl_AndKeepsProgressAcrossCameraChanges()
        {
            yield return Build(withUi: true);
            _modes.StartMode(GameMode.FirstFlight);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(TutorialStepId.Preparation, _modes.Tutorial.CurrentStep);
            Assert.AreEqual(0f, _session.aircraft.Controls.Throttle, "the tutorial never touches the throttle");
            Assert.AreEqual(0f, _session.aircraft.Controls.Pitch, 1e-4f, "...or the stick");
            Assert.That(_session.Telemetry.GroundspeedKnots, Is.LessThan(1f), "the aircraft waits for the player");
            Assert.IsTrue(_coach.Shown, "the first instruction is on screen");
            StringAssert.Contains("1/8", _coach.CurrentLabel);

            float before = _modes.Tutorial.Context.TimeInStep;
            _session.ToggleCamera();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(ViewMode.Cockpit, _session.CurrentView);
            Assert.AreEqual(TutorialStepId.Preparation, _modes.Tutorial.CurrentStep);
            Assert.That(_modes.Tutorial.Context.TimeInStep, Is.GreaterThan(before), "the same step carries on");
            _session.ToggleCamera();

            _session.SetAssistProfile(FlightAssistProfile.Manual);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(TutorialStepId.Preparation, _modes.Tutorial.CurrentStep, "a profile change keeps the progress");
            Assert.AreEqual(FirstFlightTutorial.Status.Running, _modes.Tutorial.State);
        }

        [UnityTest]
        public IEnumerator PauseMenu_FreezesTheFlight_AndResumeCarriesOn()
        {
            yield return Build(withUi: true);
            _modes.StartMode(GameMode.FirstFlight);
            yield return new WaitForSeconds(0.5f);
            _menu.OnPointerClick(Pointer());
            Assert.IsTrue(_modes.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(_card.IsModal, "the pause card dims and takes the touches");
            Assert.AreEqual(4, _card.ButtonCount, "resume, restart, skip, change mode");
            Assert.AreEqual(FirstFlightTutorial.Status.Paused, _modes.Tutorial.State);
            Vector3 position = _session.aircraft.Body.position;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(position, _session.aircraft.Body.position, "nothing moves while paused");

            _card.buttons[0].OnPointerClick(Pointer()); // RESUME
            Assert.IsFalse(_modes.IsPaused);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(_card.IsOpen);
            Assert.AreEqual(FirstFlightTutorial.Status.Running, _modes.Tutorial.State);
            Assert.AreEqual(TutorialStepId.Preparation, _modes.Tutorial.CurrentStep, "progress kept");
        }

        [UnityTest]
        public IEnumerator SkipTutorial_NeedsASecondTap_ThenFreeFlightCarriesOn()
        {
            yield return Build(withUi: true);
            _modes.StartMode(GameMode.FirstFlight);
            yield return new WaitForSeconds(0.5f);
            _skip.OnPointerClick(Pointer());
            Assert.AreEqual(GameMode.FirstFlight, _modes.Mode, "one tap only arms it");
            Assert.IsTrue(_skip.Engaged);
            _skip.OnPointerClick(Pointer());
            Assert.AreEqual(GameMode.FreeFlight, _modes.Mode);
            Assert.AreEqual(FirstFlightTutorial.Status.Skipped, _modes.Tutorial.State);
            Assert.AreEqual(TutorialStatus.Skipped, _modes.Progress.Tutorial);
            yield return null;
            Assert.IsFalse(_coach.Shown, "no more coaching");
            Assert.AreEqual(1f, Time.timeScale);
        }

        // ---- Entry screen ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EntryScreen_PausesTheWorld_AndStartsTheChosenMode()
        {
            yield return Build(withUi: true, showModeSelect: true);
            Assert.IsTrue(_modes.ModeSelectOpen);
            Assert.AreEqual(0f, Time.timeScale, "the world waits behind the entry screen");
            Assert.IsTrue(_modeSelect.firstFlightButton.Engaged, "the First Flight is recommended to a new player");

            _modeSelect.freeFlightButton.OnPointerClick(Pointer());
            Assert.IsTrue(_modeSelect.FreeFlightOptionsShown, "runway or in the air");
            _modeSelect.inTheAirButton.OnPointerClick(Pointer());
            Assert.IsFalse(_modes.ModeSelectOpen);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(GameMode.FreeFlight, _modes.Mode);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_session.Telemetry.WeightOnWheels, "started in the air");

            _modes.OpenMenu();
            _card.buttons[3].OnPointerClick(Pointer()); // CHANGE MODE
            Assert.IsTrue(_modes.ModeSelectOpen);
            _modeSelect.landingButton.OnPointerClick(Pointer());
            Assert.AreEqual(GameMode.LandingChallenge, _modes.Mode);
            Assert.AreEqual(LandingChallenge.Status.Flying, _modes.Challenge.State);
        }

        [UnityTest]
        public IEnumerator FreeFlight_DestinationFromTheMenu_PointsTheGuideAtALandmark()
        {
            yield return Build(withUi: true);
            _modes.StartMode(GameMode.FreeFlight, FreeFlightStart.Airborne);
            _modes.OpenMenu();
            _card.buttons[1].OnPointerClick(Pointer()); // GUIDE: next landmark
            Assert.IsTrue(_modes.Destination >= 0);
            _card.buttons[0].OnPointerClick(Pointer()); // RESUME
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var nav = _session.Navigation;
            Assert.IsTrue(nav.Valid);
            Assert.AreEqual(_modes.World[_modes.Destination].displayName, nav.Target.Name);
            yield return null;
            Assert.IsTrue(_session.Disclosure.IsVisible(HudElement.NavigationGuide), "the guide shows the chosen destination");
        }

        // ---- Remembered choices ---------------------------------------------------------------------------

        static void WithPrefsBackup(string[] keys, System.Action body)
        {
            var backup = new Dictionary<string, int>();
            foreach (var key in keys)
                if (PlayerPrefs.HasKey(key)) backup[key] = PlayerPrefs.GetInt(key);
            try { body(); }
            finally
            {
                foreach (var key in keys)
                {
                    if (backup.TryGetValue(key, out int value)) PlayerPrefs.SetInt(key, value);
                    else PlayerPrefs.DeleteKey(key);
                }
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void Progress_TutorialCompletion_PersistsInPlayerPrefs()
        {
            WithPrefsBackup(new[] { PlayerProgress.TutorialKey, PlayerProgress.BestLandingKey, PlayerProgress.LandingsKey }, () =>
            {
                PlayerPrefs.DeleteKey(PlayerProgress.TutorialKey);
                new PlayerProgress(new PlayerPrefsProgressStore()).MarkTutorialCompleted();
                Assert.AreEqual(TutorialStatus.Completed, new PlayerProgress(new PlayerPrefsProgressStore()).Tutorial);
            });
        }

        [Test]
        public void Preferences_CameraMode_IsRemembered()
        {
            WithPrefsBackup(new[] { "OpenSkies.View.CameraMode" }, () =>
            {
                HudPreferences.SaveCameraMode(CameraMode.Cockpit);
                Assert.AreEqual(CameraMode.Cockpit, HudPreferences.LoadCameraMode(CameraMode.Chase));
                PlayerPrefs.SetInt("OpenSkies.View.CameraMode", 7);
                Assert.AreEqual(CameraMode.Chase, HudPreferences.LoadCameraMode(CameraMode.Chase), "a bad value falls back");
            });
        }

        // ---- Layout -------------------------------------------------------------------------------------

        static Rect WorldRect(Component component)
        {
            var corners = new Vector3[4];
            ((RectTransform)component.transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        static RectTransform TopRight(RectTransform parent, string name, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(128f, 96f);
            return rect;
        }

        [TestCase(1440f)]
        [TestCase(1920f)]
        [TestCase(2400f)]
        public void MissionUi_StaysClearOfTheFlightControls_WithLargeTouchTargets(float width)
        {
            _root = new GameObject("MissionLayoutRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safeArea = SafeArea(_root.transform, width, Vector3.zero);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var touch = TouchControlsFactory.Build(safeArea, font);
            var hud = ContextualHudFactory.Build(safeArea, font);
            var coach = MissionUiFactory.BuildCoachingPanel(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            var menu = MissionUiFactory.BuildMenuButton(safeArea, font);
            var skip = MissionUiFactory.BuildSkipButton(safeArea, font);
            var card = MissionUiFactory.BuildMissionCard(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset,
                CockpitFocusPanelFactory.ColumnBottom, CockpitFocusPanelFactory.ColumnTop);
            var modeSelect = MissionUiFactory.BuildModeSelect(safeArea, font);
            // The scene's CAM and RESET buttons (built by the scene builder).
            var cam = TopRight(safeArea, "CAM", new Vector2(-24f, -16f));
            var reset = TopRight(safeArea, "RESET", new Vector2(-168f, -16f));
            Canvas.ForceUpdateCanvases();

            var controls = new List<Component> { touch.joystick, touch.throttle, touch.brake, hud.systemsButton, hud.profileButton, cam, reset };
            controls.AddRange(hud.railSlots);
            var mission = new List<Component> { coach, menu, skip };
            foreach (var item in mission)
            foreach (var control in controls)
                Assert.IsFalse(WorldRect(item).Overlaps(WorldRect(control)), $"{item.name} over {control.name} at width {width}");
            Assert.IsFalse(WorldRect(menu).Overlaps(WorldRect(skip)));
            Assert.That(WorldRect(coach).yMax, Is.LessThan(CockpitFocusPanelFactory.ColumnBottom * 1080f), "below the panel band and the instruments");
            foreach (var button in new[] { menu, skip, coach.button })
                Assert.That(WorldRect(button).height, Is.GreaterThanOrEqualTo(96f), button.name);

            card.Show("Paused", "First Flight, step 3 of 8: rotation", null, true, "RESUME", "RESTART FIRST FLIGHT", "SKIP TUTORIAL", "CHANGE MODE");
            card.Layout();
            Rect cardRect = WorldRect(card.card);
            foreach (var control in controls)
                Assert.IsFalse(cardRect.Overlaps(WorldRect(control)), $"card over {control.name} at width {width}");
            for (int i = 0; i < card.ButtonCount; i++)
            {
                Rect r = WorldRect(card.buttons[i]);
                Assert.That(r.height, Is.GreaterThanOrEqualTo(96f));
                Assert.IsTrue(cardRect.Contains(r.center), $"button {i} on its card at width {width}");
            }

            card.Show("Good landing: 78", "Touchdown 180 FPM: smooth\nOn the centreline, straight\nSpeed 52 KT: good (stall 46)\nNo bounce, kept straight",
                "Game score for practice, not a flight-training standard.", false, "RETRY", "FREE FLIGHT");
            card.Layout();
            Assert.That(WorldRect(card.card).height, Is.LessThanOrEqualTo((CockpitFocusPanelFactory.ColumnTop - CockpitFocusPanelFactory.ColumnBottom) * 1080f + 1f));

            var screenRect = WorldRect(safeArea);
            foreach (var button in new[] { modeSelect.freeFlightButton, modeSelect.firstFlightButton, modeSelect.landingButton, modeSelect.fromRunwayButton, modeSelect.inTheAirButton })
            {
                Rect r = WorldRect(button);
                Assert.That(r.height, Is.GreaterThanOrEqualTo(96f));
                Assert.IsTrue(screenRect.Contains(r.min) && screenRect.Contains(r.max), $"{button.name} on screen at width {width}");
            }
        }
    }
}
