using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput;
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
    /// The compact top-right command cluster (docs/TAP_DISCLOSURE_DESIGN.md, milestone 3) inside a
    /// real <see cref="FlightSession"/> with the Rigidbody aircraft, the touch controls, the
    /// contextual and gesture HUDs, the flight settings card, the cockpit focus panel and, where
    /// needed, the game modes (pause sheet, coaching, highlights), on a world-space canvas driven
    /// by synthetic pointers. Proves: the camera stays one tap away; HUD profiles are chosen
    /// directly; RESET cannot happen by accident; the panel opens and collapses by taps under
    /// the one coordinator; no command exists twice; the cluster is reachable and covers
    /// nothing on 4:3, 16:9 and 20:9 screens and inside a cut-out's safe area; a camera switch
    /// leaves the flying fingers alone; the tutorial's highlights follow the cluster. Not how
    /// any of it feels on a device.
    /// </summary>
    public class CommandClusterPlayModeTests
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
        CommandCluster _cluster;
        ControlHighlights _highlights;
        CoachingPanel _coach;
        PauseSheet _sheet;
        CameraRig _camera;
        ScriptedControls _controls;
        float _width;
        int _resets;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_root != null) Object.Destroy(_root);
        }

        static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static RectTransform NewCanvas(Transform parent, float width, float height, Vector3 position)
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)canvasGo.transform;
            canvas.sizeDelta = new Vector2(width, height);
            canvas.pivot = Vector2.zero;
            canvas.position = position;
            return canvas;
        }

        static RectTransform SafeArea(RectTransform canvas, Vector2 anchorMin, Vector2 anchorMax)
        {
            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(canvas, false);
            safeArea.anchorMin = anchorMin;
            safeArea.anchorMax = anchorMax;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            return safeArea;
        }

        IEnumerator Build(float width = 1920f, bool withModes = false, bool scripted = true)
        {
            _width = width;
            _resets = 0;
            _sheet = null; // the fixture is reused between tests
            _root = new GameObject("CommandClusterTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, new Vector3(0f, 0f, -500f), out var telemetry);
            var cockpit = new GameObject("CockpitAnchor").transform;
            cockpit.SetParent(aircraft.transform, false);
            cockpit.localPosition = new Vector3(0f, 1.7f, 1f);

            var cameraGo = new GameObject("Camera", typeof(Camera));
            cameraGo.transform.SetParent(_root.transform, false);
            _camera = cameraGo.AddComponent<CameraRig>();
            _camera.target = aircraft.transform;
            _camera.cockpitAnchor = cockpit;

            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var canvas = NewCanvas(_root.transform, width, 1080f, new Vector3(0f, 0f, 5000f));
            var safeArea = SafeArea(canvas, Vector2.zero, Vector2.one);

            var surfaceGo = new GameObject("InteractionSurface", typeof(RectTransform), typeof(RaycastOnlyGraphic));
            ((RectTransform)surfaceGo.transform).SetParent(safeArea, false);
            _surface = surfaceGo.AddComponent<InteractionSurface>();
            _surface.surface = surfaceGo.GetComponent<RaycastOnlyGraphic>();
            _surface.screenHeightOverride = 1080f;

            _touch = TouchControlsFactory.Build(safeArea, Font);
            _touch.visibility = TouchControlsVisibility.Always;
            var focus = CockpitFocusPanelFactory.Build(safeArea, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset, _surface);
            focus.SetReducedMotion(true);
            _settings = FlightSettingsPanelFactory.Build(safeArea, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            _settings.SetReducedMotion(true);
            _coach = MissionUiFactory.BuildCoachingPanel(safeArea, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            _coach.SetReducedMotion(true);
            _hud = ContextualHudFactory.Build(safeArea, Font);
            _hud.SetMotionPreference(MotionPreference.Reduced);
            _gestureHud = GestureHudFactory.Build(safeArea, Font, _surface);
            _cluster = CommandClusterFactory.Build(safeArea, Font);
            _cluster.settings.confirmWindowSeconds = 1.5f; // keeps the lapse test short; the minimum stays the game's
            _highlights = MissionUiFactory.BuildHighlights(safeArea);
            _highlights.SetReducedMotion(true);
            MissionCard card = null;
            if (withModes)
            {
                card = MissionUiFactory.BuildMissionCard(safeArea, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset,
                    CockpitFocusPanelFactory.ColumnBottom, CockpitFocusPanelFactory.ColumnTop);
                _sheet = PauseSheetFactory.Build(safeArea, Font, _surface);
            }
            UnityEngine.Canvas.ForceUpdateCanvases();

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = _camera;
            _session.touchControls = _touch;
            _session.contextualHud = _hud;
            _session.focusPanel = focus;
            _session.flightSettings = _settings;
            _session.interactionSurface = _surface;
            _session.gestureHud = _gestureHud;
            _session.commandCluster = _cluster;
            _session.rememberHudPreferences = false;
            _session.homeRunway = new RunwayInfo(Vector3.zero, 0f, 2000f, 30f);
            if (scripted)
            {
                _controls = new ScriptedControls();
                _session.SetControlSource(_controls);
            }
            sessionGo.SetActive(true);
            _gestureHud.SetReducedMotion(true);
            _session.ApplyMotionPreference(MotionPreference.Reduced);
            _session.ResetPerformed += () => _resets++;

            var modesGo = new GameObject("GameModes");
            modesGo.SetActive(false);
            modesGo.transform.SetParent(_root.transform, false);
            _modes = modesGo.AddComponent<GameModeSession>();
            _modes.flight = _session;
            _modes.coach = _coach;
            _modes.highlights = _highlights;
            _modes.card = card;
            _modes.pauseSheet = _sheet;
            _modes.showModeSelectOnStart = false;
            _modes.rememberProgress = false;
            modesGo.SetActive(true);
            if (_sheet != null) _sheet.SetReducedMotion(true);

            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate(); // the aircraft settles on its wheels
            yield return null;
            yield return null;
        }

        // ---- Helpers ------------------------------------------------------------------------------------

        static PointerEventData Pointer(int id, Vector2 position) =>
            new PointerEventData(EventSystem.current) { pointerId = id, position = position, pressPosition = position };

        static Rect WorldRect(Component component)
        {
            var corners = new Vector3[4];
            ((RectTransform)component.transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        CommandClusterSession Commands => _session.Commands;
        DisclosureCoordinator Coordinator => _session.Coordinator;
        Vector2 Sky => new Vector2(_width * 0.5f, 900f);

        /// <summary>A tap on a cluster control as uGUI delivers it: press, release, click, on one pointer.</summary>
        static IEnumerator Tap(HudButton button, int id)
        {
            var p = Pointer(id, WorldRect(button).center);
            button.OnPointerDown(p);
            button.OnPointerUp(p);
            button.OnPointerClick(p);
            yield return null;
        }

        /// <summary>A tap on the open surface (nothing drawn on top took it).</summary>
        IEnumerator TapSurface(int id, Vector2 point)
        {
            var p = Pointer(id, point);
            _surface.OnPointerDown(p);
            _surface.OnPointerUp(p);
            yield return null;
        }

        IEnumerator OpenOptions(int id = 300)
        {
            yield return Tap(_cluster.optionsButton, id);
            Assert.AreEqual(CommandLevel.Options, Commands.Level, "the trigger opens the options");
        }

        IEnumerator Moving()
        {
            _controls.Controls = new FlightControls(0f, 0f, 0f, 1f);
            yield return new WaitForSeconds(2f);
            Assert.That(_session.aircraft.Body.linearVelocity.magnitude, Is.GreaterThan(1f), "precondition: rolling");
        }

        // ---- Camera: one tap away ----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Camera_IsOneTapAway_FromEveryState_AndNamesTheView()
        {
            yield return Build();
            Assert.AreEqual(CameraMode.Chase, _camera.mode);
            Assert.AreEqual("CHASE", _cluster.cameraCaption.text);
            Assert.IsTrue(_cluster.rowGroup.blocksRaycasts && _cluster.rowGroup.alpha > 0.99f, "the primary row is always there");

            yield return Tap(_cluster.cameraButton, 300);
            Assert.AreEqual(CameraMode.Cockpit, _camera.mode, "one tap switches the view");
            Assert.AreEqual("COCKPIT", _cluster.cameraCaption.text, "and the button names it");

            // From the open options panel: still one tap, and the panel folds.
            yield return OpenOptions();
            yield return Tap(_cluster.cameraButton, 301);
            Assert.AreEqual(CameraMode.Chase, _camera.mode);
            Assert.IsFalse(Commands.IsOpen, "a camera switch folds the options");
            Assert.IsFalse(Coordinator.HasActive);

            // From the HUD selector, too.
            yield return OpenOptions();
            yield return Tap(_cluster.hudRow, 302);
            yield return Tap(_cluster.cameraButton, 303);
            Assert.AreEqual(CameraMode.Cockpit, _camera.mode);
            Assert.IsFalse(Commands.IsOpen);

            // In every HUD profile: the camera is never under HUD disclosure.
            foreach (var profile in new[] { HudProfile.Cinematic, HudProfile.Smart, HudProfile.Detailed })
            {
                _session.SetHudProfile(profile);
                yield return null;
                Assert.IsTrue(_cluster.rowGroup.alpha > 0.99f && _cluster.rowGroup.blocksRaycasts, profile + ": the camera stays");
                Assert.IsTrue(_cluster.cameraButton.Interactable);
            }
        }

        [UnityTest]
        public IEnumerator CameraSwitch_KeepsTheStickAndThrottleFingers_AndTheirInput()
        {
            yield return Build(scripted: false);
            var stickRect = WorldRect(_touch.joystick);
            var throttleRect = WorldRect(_touch.throttle);
            var stick = Pointer(1, new Vector2(stickRect.xMin + stickRect.width * 0.4f, stickRect.yMin + stickRect.height * 0.5f));
            var lever = Pointer(2, new Vector2(throttleRect.center.x, throttleRect.yMin + throttleRect.height * 0.3f));
            _touch.joystick.OnPointerDown(stick);
            _touch.throttle.OnPointerDown(lever);
            stick.position += new Vector2(0f, -200f);
            _touch.joystick.OnDrag(stick);
            lever.position += new Vector2(0f, 230f);
            _touch.throttle.OnDrag(lever);
            // The stick is smoothed over a few physics steps (JoystickMath.Smooth): let it settle before comparing.
            yield return new WaitForSeconds(0.5f);
            yield return new WaitForFixedUpdate();
            var pilotBefore = _session.PilotControls; // what the aircraft gets, sampled before anything reads the router itself
            var before = _session.Router.Read(1f);
            Assert.That(before.Pitch, Is.GreaterThan(0.5f), "precondition: the stick is held back");
            Assert.That(before.Throttle, Is.GreaterThan(0.3f), "precondition: the throttle is up");
            Assert.That(pilotBefore.Pitch, Is.GreaterThan(0.1f), "precondition: the aircraft gets the stick");
            Assert.AreEqual(2, _touch.Ownership.Count);

            yield return Tap(_cluster.cameraButton, 3);
            Assert.AreEqual(CameraMode.Cockpit, _camera.mode);
            yield return new WaitForFixedUpdate();
            var after = _session.Router.Read(1f);
            Assert.AreEqual(2, _touch.Ownership.Count, "both fingers are still owned by their controls");
            Assert.AreEqual(before.Pitch, after.Pitch, 1e-3f, "the stick input survives the camera switch");
            Assert.AreEqual(before.Throttle, after.Throttle, 1e-3f, "so does the throttle");
            Assert.AreEqual(pilotBefore.Pitch, _session.PilotControls.Pitch, 1e-3f, "and the aircraft keeps getting it");
            Assert.AreEqual(pilotBefore.Throttle, _session.PilotControls.Throttle, 1e-3f);

            // With the options open and back to the chase view: the same.
            yield return OpenOptions(4);
            yield return Tap(_cluster.cameraButton, 5);
            Assert.AreEqual(CameraMode.Chase, _camera.mode);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(before.Pitch, _session.Router.Read(1f).Pitch, 1e-3f);
            Assert.AreEqual(before.Throttle, _session.Router.Read(1f).Throttle, 1e-3f);
            Assert.AreEqual(InteractionRoute.None, _surface.RouteOf(1), "an owned finger is never routed");

            _touch.joystick.OnPointerUp(stick);
            _touch.throttle.OnPointerUp(lever);
            yield return null;
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        // ---- HUD profile: direct selection -------------------------------------------------------------

        [UnityTest]
        public IEnumerator HudProfiles_AreChosenDirectly_WithTheCurrentOneShown()
        {
            yield return Build();
            _controls.Controls = new FlightControls(0.1f, -0.2f, 0f, 0.7f);
            Assert.AreEqual(HudProfile.Smart, _session.ActiveHudProfile);

            yield return OpenOptions();
            Assert.AreEqual("SMART", _cluster.hudRowValue.text, "the HUD row shows the profile");
            yield return Tap(_cluster.hudRow, 301);
            Assert.AreEqual(CommandLevel.HudSelector, Commands.Level, "the HUD row opens the selector");
            Assert.AreEqual(2, Coordinator.Depth, "the selector is a child of the options");
            Assert.IsTrue(_cluster.selectorLevel.gameObject.activeInHierarchy);
            Assert.IsFalse(_cluster.optionsLevel.gameObject.activeInHierarchy, "in the panel's place, not beside it");
            Assert.AreEqual("HUD: SMART", _cluster.selectorTitle.text, "the current profile in words, not colour alone");
            for (int i = 0; i < CommandClusterModel.ProfileCount; i++)
                Assert.AreEqual(i == 1, _cluster.profileButtons[i].Engaged, "only the current profile is marked");

            yield return Tap(_cluster.profileButtons[2], 302); // DETAILED
            Assert.AreEqual(HudProfile.Detailed, _session.ActiveHudProfile, "one tap chooses it");
            Assert.IsFalse(Commands.IsOpen, "the choice is complete: the cluster folds");
            Assert.IsFalse(Coordinator.HasActive);

            // Straight to Cinematic: no cycling through the others.
            yield return OpenOptions();
            yield return Tap(_cluster.hudRow, 303);
            Assert.IsTrue(_cluster.profileButtons[2].Engaged);
            yield return Tap(_cluster.profileButtons[0], 304);
            Assert.AreEqual(HudProfile.Cinematic, _session.ActiveHudProfile);

            // The title row goes back one level and changes nothing.
            yield return OpenOptions();
            yield return Tap(_cluster.hudRow, 305);
            yield return Tap(_cluster.selectorBack, 306);
            Assert.AreEqual(CommandLevel.Options, Commands.Level);
            Assert.AreEqual(1, Coordinator.Depth);
            Assert.AreEqual(HudProfile.Cinematic, _session.ActiveHudProfile);
            Assert.AreEqual("CINEMATIC", _cluster.hudRowValue.text);

            // The keyboard's cycle (H) is kept.
            _session.CycleHudProfile();
            Assert.AreEqual(HudProfile.Smart, _session.ActiveHudProfile);

            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0.7f, _session.aircraft.Controls.Throttle, 1e-4f, "a HUD choice never touches flight input");
            Assert.AreEqual(-0.2f, _session.aircraft.Controls.Roll, 1e-4f);
        }

        // ---- RESET: never by accident ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Reset_CannotActivateAccidentally()
        {
            yield return Build();
            yield return Moving();

            // Hidden RESET: even a click delivered to it does nothing.
            yield return Tap(_cluster.resetButton, 300);
            Assert.AreEqual(0, _resets, "collapsed: RESET is not on screen and does nothing");

            yield return OpenOptions();
            yield return Tap(_cluster.resetButton, 301);
            Assert.AreEqual(0, _resets, "the first tap only asks for a confirmation");
            Assert.AreEqual(CommandConfirm.Reset, Commands.Model.Armed);
            Assert.IsTrue(_cluster.resetButton.Engaged);
            Assert.AreEqual(CommandClusterModel.ResetConfirmLabel, _cluster.resetButton.label.text, "the button says what the next tap does");

            yield return Tap(_cluster.resetButton, 302); // a double tap, within the bounce time
            Assert.AreEqual(0, _resets, "a double tap is never a confirmation");

            // Dismissing the menu (a tap outside) after arming: folded, nothing reset, nothing left armed.
            var p = Pointer(303, Sky);
            _surface.OnPointerDown(p);
            Assert.AreEqual(InteractionRoute.Dismiss, _surface.RouteOf(303), "the outside tap is the dismissal's, consumed");
            _surface.OnPointerUp(p);
            yield return null;
            Assert.IsFalse(Commands.IsOpen);
            Assert.AreEqual(CommandConfirm.None, Commands.Model.Armed);
            Assert.AreEqual(0, _resets);
            yield return OpenOptions();
            yield return Tap(_cluster.resetButton, 304);
            Assert.AreEqual(0, _resets, "after reopening, the first tap only arms again");

            // The trigger again (closing the menu) after arming.
            yield return Tap(_cluster.optionsButton, 305);
            Assert.IsFalse(Commands.IsOpen);
            Assert.AreEqual(0, _resets);

            // Another cluster replacing it after arming (the systems wheel).
            yield return OpenOptions();
            yield return Tap(_cluster.resetButton, 306);
            var h = Pointer(307, WorldRect(_gestureHud.handleHotspot).center);
            _gestureHud.handleHotspot.OnPointerDown(h);
            _gestureHud.handleHotspot.OnPointerUp(h);
            yield return null;
            Assert.IsTrue(_session.Gestures.ArcOpen);
            Assert.IsFalse(Commands.IsOpen, "the wheel replaced the options");
            Assert.AreEqual(CommandConfirm.None, Commands.Model.Armed);
            yield return TapSurface(308, Sky);

            // The window running out.
            yield return OpenOptions();
            yield return Tap(_cluster.resetButton, 309);
            yield return new WaitForSecondsRealtime(1.8f);
            Assert.AreEqual(CommandConfirm.None, Commands.Model.Armed, "the confirmation lapsed");
            Assert.AreEqual(CommandClusterModel.ResetLabel, _cluster.resetButton.label.text);
            yield return Tap(_cluster.resetButton, 310);
            Assert.AreEqual(0, _resets, "a late tap arms again; it never resets");
            Assert.That(_session.aircraft.Body.linearVelocity.magnitude, Is.GreaterThan(1f), "still rolling: nothing was reset");

            // The deliberate way: a second tap after a moment.
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Tap(_cluster.resetButton, 311);
            Assert.AreEqual(1, _resets, "confirmed");
            Assert.IsFalse(Commands.IsOpen, "the panel folded");
            Assert.That(_session.aircraft.Body.linearVelocity.magnitude, Is.LessThan(0.5f), "the aircraft is back at the start, at rest");
        }

        // ---- Opening and collapsing ----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator OptionsPanel_OpensAndCollapsesByTaps_UnderTheOneCoordinator()
        {
            yield return Build();
            Assert.IsFalse(_cluster.panelGroup.blocksRaycasts, "a folded panel intercepts nothing");
            Assert.IsFalse(_cluster.PanelShown);

            yield return OpenOptions();
            Assert.IsTrue(Coordinator.IsActive(Commands.OptionsLayer));
            Assert.IsTrue(_cluster.panelGroup.blocksRaycasts && _cluster.panelGroup.alpha > 0.99f, "shown at once with reduced motion");
            Assert.IsTrue(_cluster.optionsButton.Engaged, "the trigger shows that its panel is open");
            Assert.IsTrue(_cluster.lookForward.Suppressed, "the panel takes LOOK FWD's place");
            Assert.IsFalse(Coordinator.WantsOutsideTap(WorldRect(_cluster.panel).center), "a press on the panel is the panel's");
            Assert.IsTrue(Coordinator.WantsOutsideTap(Sky));

            yield return Tap(_cluster.optionsButton, 301);
            Assert.IsFalse(Commands.IsOpen, "the trigger again folds it");
            Assert.IsFalse(_cluster.panelGroup.blocksRaycasts);
            Assert.IsFalse(_cluster.lookForward.Suppressed);

            // Outside: consumed and folded, never a look.
            yield return OpenOptions();
            var p = Pointer(302, Sky);
            _surface.OnPointerDown(p);
            Assert.AreEqual(InteractionRoute.Dismiss, _surface.RouteOf(302));
            p.position += new Vector2(160f, 0f);
            _surface.OnDrag(p);
            yield return null;
            Assert.IsFalse(_camera.IsChaseLookHeld, "a drag that began as a dismissal never looks around");
            Assert.AreEqual(0f, _camera.ChaseLook.magnitude, 1e-3f, "the camera never moved");
            _surface.OnPointerUp(p);
            yield return null;
            Assert.IsTrue(Commands.IsOpen, "a drag is not a tap: nothing collapses");
            yield return TapSurface(303, Sky);
            Assert.IsFalse(Commands.IsOpen, "a tap outside folds it");

            // Opening the cluster closes any other cluster, and the other way round.
            _session.ToggleFlightSettings();
            Assert.IsTrue(_session.FlightSettingsOpen);
            yield return OpenOptions();
            Assert.IsFalse(_session.FlightSettingsOpen, "the flight settings gave way");
            var h = Pointer(304, WorldRect(_gestureHud.handleHotspot).center);
            _gestureHud.handleHotspot.OnPointerDown(h);
            _gestureHud.handleHotspot.OnPointerUp(h);
            yield return null;
            Assert.IsTrue(_session.Gestures.ArcOpen);
            Assert.IsFalse(Commands.IsOpen, "the wheel replaced the options");
            yield return OpenOptions();
            Assert.IsFalse(_session.Gestures.ArcOpen, "the options replaced the wheel");
            Assert.AreEqual(1, Coordinator.Depth);

            // A new flight and focus loss fold it.
            _session.ResetFlight();
            Assert.IsFalse(Commands.IsOpen, "a reset folds every transient");
            yield return OpenOptions();
            Coordinator.OnFocusLost();
            Assert.IsFalse(Commands.IsOpen, "focus loss folds it");
        }

        [UnityTest]
        public IEnumerator PauseAndSettings_FromTheCluster_FreezeTheFlightDeliberately()
        {
            yield return Build(withModes: true);
            yield return OpenOptions();
            yield return Tap(_cluster.pauseButton, 301);
            Assert.IsTrue(_modes.IsPaused, "pause pauses");
            Assert.IsTrue(_sheet.IsOpen);
            Assert.IsFalse(Commands.IsOpen, "and the options folded");
            _modes.ResumeFromMenu();
            yield return null;
            Assert.IsFalse(_modes.IsPaused);

            yield return OpenOptions();
            yield return Tap(_cluster.settingsButton, 302);
            Assert.IsTrue(_modes.IsPaused, "the settings live over a paused flight");
            Assert.IsTrue(_sheet.IsOpen);
            Assert.AreEqual(SheetModel.SecondStop, _sheet.Stop, "the sheet goes to its settings");
            Assert.IsFalse(Commands.IsOpen);

            // Paused: the trigger never opens the options over the pause.
            yield return Tap(_cluster.optionsButton, 303);
            Assert.IsFalse(Commands.IsOpen);
            _modes.ResumeFromMenu();
            yield return null;
            Assert.AreEqual(1f, Time.timeScale);
        }

        // ---- One of each command ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NoCommandIsDuplicated_AndTheOldTopBarIsGone()
        {
            yield return Build(withModes: true);
            var buttons = _root.GetComponentsInChildren<HudButton>(true);
            int camera = 0, reset = 0, pause = 0, look = 0, skip = 0, settings = 0, profileButtons = 0, menu = 0;
            foreach (var b in buttons)
            {
                switch (b.name)
                {
                    case "CameraButton": camera++; break;
                    case "ResetButton": reset++; break;
                    case "PauseButton": pause++; break;
                    case "LookForwardButton": look++; break;
                    case "SkipButton": skip++; break;
                    case "SettingsButton": settings++; break;
                    case "HudProfileButton": profileButtons++; break;
                    case "MenuButton": menu++; break;
                }
            }
            Assert.AreEqual(1, camera, "one camera button");
            Assert.AreEqual(1, reset, "one RESET");
            Assert.AreEqual(1, pause, "one pause");
            Assert.AreEqual(1, look, "one LOOK FWD");
            Assert.AreEqual(1, skip, "one SKIP TUTORIAL on the HUD (the pause menu's is a menu choice)");
            Assert.AreEqual(1, settings, "one SETTINGS on the HUD");
            Assert.AreEqual(0, profileButtons, "the cycling HUD profile button is gone");
            Assert.AreEqual(0, menu, "MENU became the cluster's pause");
            Assert.AreEqual(0, _root.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length, "the uGUI CAM and RESET buttons are gone");
            Assert.IsNull(typeof(ContextualHud).GetField("profileButton"), "retired, not hidden");
            Assert.IsNull(typeof(HudPresenter).GetField("resetButton"));
            Assert.IsNull(typeof(GameModeSession).GetField("menuButton"));

            // The collapsed cluster: exactly three permanent controls, nothing else in the row.
            Assert.AreEqual(3, _cluster.rowGroup.GetComponentsInChildren<HudButton>(true).Length, "camera, options, pause");
            // No X-shaped close control anywhere in the cluster.
            foreach (var text in _cluster.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                Assert.AreNotEqual("×", text.text.Trim());
                Assert.AreNotEqual("X", text.text.Trim());
                StringAssert.DoesNotContain("CLOSE", text.text);
            }
        }

        // ---- Reach, safe areas -------------------------------------------------------------------------------

        static IEnumerable<HudButton> ClusterTargets(CommandCluster cluster)
        {
            yield return cluster.cameraButton;
            yield return cluster.optionsButton;
            yield return cluster.pauseButton;
            yield return cluster.lookForward.lookForward;
            yield return cluster.hudRow;
            yield return cluster.resetButton;
            yield return cluster.settingsButton;
            yield return cluster.skipButton;
            yield return cluster.selectorBack;
            foreach (var b in cluster.profileButtons) yield return b;
        }

        [TestCase(4f / 3f, 1.1f)]
        [TestCase(16f / 9f, 1.1f)]
        [TestCase(20f / 9f, 1.1f)]
        [TestCase(16f / 9f, 0.9f)]
        [TestCase(20f / 9f, 0.9f)]
        public void Cluster_IsReachableOnAPhone_WithLargeTargets_AndCoversNothing(float aspect, float requested)
        {
            var layout = UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop);
            float scale = UiScaleMath.Effective(requested, aspect, layout);
            float height = UiScaleMath.ReferenceHeight / scale, width = aspect * height;
            _root = new GameObject("ReachRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safe = SafeArea(NewCanvas(_root.transform, width, height, Vector3.zero), Vector2.zero, Vector2.one);
            var touch = TouchControlsFactory.Build(safe, Font);
            var coach = MissionUiFactory.BuildCoachingPanel(safe, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            var hud = ContextualHudFactory.Build(safe, Font);
            var info = FlightInfoClusterFactory.Build(safe, Font);
            var cluster = CommandClusterFactory.Build(safe, Font);
            cluster.skipButton.gameObject.SetActive(true);
            cluster.selectorLevel.gameObject.SetActive(true);
            UnityEngine.Canvas.ForceUpdateCanvases();
            string at = $" at aspect {aspect:0.00}, scale {scale:0.000}";

            // SYSTEMS stands where the gesture HUD's handle is (GestureHudFactory.HandlePosition = ContextualHudFactory.RailBase).
            var keepClear = new List<Component> { touch.joystick, touch.throttle, touch.brake, hud.systemsButton, coach, info.capsuleButton, info.panel };
            keepClear.AddRange(hud.warningSlots);
            keepClear.AddRange(info.tiles);
            foreach (var target in ClusterTargets(cluster))
            {
                var r = WorldRect(target);
                Assert.That(r.height, Is.GreaterThanOrEqualTo(96f - 0.01f), target.name + " is a full touch target" + at);
                Assert.That(r.width, Is.GreaterThanOrEqualTo(96f - 0.01f), target.name + at);
                Assert.IsTrue(r.xMin >= -0.01f && r.yMin >= -0.01f && r.xMax <= width + 0.01f && r.yMax <= height + 0.01f, target.name + " on screen" + at);
                foreach (var other in keepClear)
                    Assert.IsFalse(r.Overlaps(WorldRect(other)), $"{target.name} over {other.name}{at}");
            }
            var panel = WorldRect(cluster.panel);
            foreach (var other in keepClear) Assert.IsFalse(panel.Overlaps(WorldRect(other)), $"the open panel over {other.name}{at}");
            // The legacy rail (development comparison) never meets the permanent controls; the transient panel may cover its top slot while open.
            foreach (var slot in hud.railSlots)
                foreach (var permanent in new HudButton[] { cluster.cameraButton, cluster.optionsButton, cluster.pauseButton, cluster.lookForward.lookForward })
                    Assert.IsFalse(WorldRect(slot).Overlaps(WorldRect(permanent)), $"{slot.name} under {permanent.name}{at}");
            Assert.IsFalse(WorldRect(cluster.cameraButton).Overlaps(WorldRect(cluster.optionsButton)), at);
            Assert.IsFalse(WorldRect(cluster.optionsButton).Overlaps(WorldRect(cluster.pauseButton)), at);
            // Reach: the camera in the top-right corner, the whole cluster in the right half above the throttle.
            var cameraRect = WorldRect(cluster.cameraButton);
            Assert.AreEqual(width - CommandClusterLayout.RightMargin, cameraRect.xMax, 0.01f, "the camera is in the corner" + at);
            Assert.AreEqual(height - CommandClusterLayout.TopMargin, cameraRect.yMax, 0.01f, at);
            Assert.That(panel.xMin, Is.GreaterThan(width * 0.5f), "right half" + at);
            Assert.That(panel.yMin, Is.GreaterThan(WorldRect(touch.throttle).yMax), "above the throttle" + at);
            // Visuals are smaller than their touch targets, and the pause is the quietest.
            float pauseVisual = cluster.pauseButton.background.rectTransform.rect.width;
            float optionsVisual = cluster.optionsButton.background.rectTransform.rect.width;
            float cameraVisual = cluster.cameraButton.background.rectTransform.rect.width;
            Assert.That(pauseVisual, Is.LessThan(optionsVisual));
            Assert.That(optionsVisual, Is.LessThan(cameraVisual));
            Assert.That(optionsVisual, Is.LessThan(WorldRect(cluster.optionsButton).width), "the touch target is larger than what is drawn");
        }

        [Test]
        public void SafeArea_ACutOutMovesTheClusterIn_WithTheSafeEdge()
        {
            _root = new GameObject("SafeAreaRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            // A 20:9 phone in landscape: a camera cut-out on the right and a status inset at the top.
            var canvas = NewCanvas(_root.transform, 2400f, 1080f, Vector3.zero);
            var safe = SafeArea(canvas, new Vector2(0.02f, 0f), new Vector2(0.955f, 0.97f));
            var cluster = CommandClusterFactory.Build(safe, Font);
            cluster.skipButton.gameObject.SetActive(true);
            cluster.selectorLevel.gameObject.SetActive(true);
            UnityEngine.Canvas.ForceUpdateCanvases();
            var safeRect = WorldRect(safe);
            foreach (var target in ClusterTargets(cluster))
            {
                var r = WorldRect(target);
                Assert.IsTrue(r.xMin >= safeRect.xMin - 0.01f && r.xMax <= safeRect.xMax + 0.01f && r.yMin >= safeRect.yMin - 0.01f && r.yMax <= safeRect.yMax + 0.01f,
                    target.name + " inside the safe area");
            }
            var camera = WorldRect(cluster.cameraButton);
            Assert.AreEqual(safeRect.xMax - CommandClusterLayout.RightMargin, camera.xMax, 0.01f, "measured from the safe edge, not the screen's");
            Assert.AreEqual(safeRect.yMax - CommandClusterLayout.TopMargin, camera.yMax, 0.01f);
            Assert.That(WorldRect(cluster.panel).xMax, Is.LessThanOrEqualTo(safeRect.xMax));
        }

        // ---- Tutorial highlights -------------------------------------------------------------------------------

        static int Index(CoachHighlight highlight)
        {
            for (int i = 0; i < ControlHighlights.TargetCount; i++)
                if ((int)highlight == 1 << i) return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator TutorialHighlights_FollowTheCluster_AndNeverBlockIt()
        {
            yield return Build(withModes: true);
            _modes.StartMode(GameMode.FirstFlight);
            yield return null;
            Assert.AreSame(_cluster.CameraVisual, _highlights.targets[Index(CoachHighlight.Camera)], "\"tap CAM\" frames the camera");
            Assert.AreSame(_cluster.OptionsVisual, _highlights.targets[Index(CoachHighlight.Reset)], "\"tap RESET\" frames the way to it while folded");
            Assert.IsTrue(Commands.Model.SkipAvailable, "SKIP TUTORIAL is offered while the First Flight runs");
            foreach (var frame in _highlights.frames) Assert.IsFalse(frame.raycastTarget, "a highlight never takes a touch");
            Assert.IsFalse(WorldRect(_coach).Overlaps(WorldRect(_cluster.panel)), "the coaching message and the panel never meet");

            yield return OpenOptions();
            yield return null;
            Assert.IsTrue(_cluster.skipButton.gameObject.activeInHierarchy);
            Assert.AreSame(_cluster.ResetVisual, _highlights.targets[Index(CoachHighlight.Reset)], "then RESET itself");

            // The frame shows on RESET while the panel shows it, and only then.
            _modes.enabled = false; // hold the coach's mask still for the check
            _highlights.Present(CoachHighlight.Reset | CoachHighlight.Camera);
            yield return null;
            Assert.IsTrue(_highlights.IsShowing(CoachHighlight.Reset));
            Assert.IsTrue(_highlights.IsShowing(CoachHighlight.Camera));
            Assert.IsTrue(WorldRect(_highlights.frames[Index(CoachHighlight.Reset)]).Contains(WorldRect(_cluster.ResetVisual).center));
            yield return Tap(_cluster.optionsButton, 310);
            yield return null;
            Assert.IsFalse(_highlights.IsShowing(CoachHighlight.Reset), "a folded panel hides its frame with it");
            Assert.IsTrue(_highlights.IsShowing(CoachHighlight.Camera), "the camera's frame stays");
            _modes.enabled = true;

            // SKIP TUTORIAL from the cluster, with its confirmation, ends the tutorial; the offer goes.
            yield return OpenOptions();
            yield return Tap(_cluster.skipButton, 311);
            Assert.AreEqual(GameMode.FirstFlight, _modes.Mode, "one tap only arms it");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Tap(_cluster.skipButton, 312);
            Assert.AreEqual(GameMode.FreeFlight, _modes.Mode);
            yield return null;
            Assert.IsFalse(Commands.Model.SkipAvailable);
        }
    }
}
