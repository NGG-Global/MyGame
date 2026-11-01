using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Audio;
using OpenSkies.Awareness;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Gameplay;
using OpenSkies.Missions;
using OpenSkies.Options;
using OpenSkies.Platform;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The M12 presentation layer with real components: settings screen layout and
    /// behaviour, the HUD at the largest allowed interface scale, pause and resume of
    /// the sound, options reaching the session, haptics limits, warnings never dimmed,
    /// and rendering choices that never touch the physics step. These prove wiring and
    /// layout; how it looks, sounds and feels needs the device checks in
    /// docs/PROTOTYPE_TEST_PLAN.md.
    /// </summary>
    public class PresentationTests
    {
        static readonly Vector3 RunwayStart = new Vector3(0f, 0f, -710f);
        static readonly RunwayInfo Runway = new RunwayInfo(Vector3.zero, 0f, 1500f, 30f);

        GameObject _root;
        FlightSession _session;
        GameModeSession _modes;
        PresentationSession _presentation;
        AudioManager _audio;
        SettingsScreen _settings;
        MissionCard _card;
        ContextualHud _hud;
        FakeHaptics _haptics;

        sealed class FakeHaptics : IHapticDevice
        {
            public readonly List<HapticEvent> Played = new List<HapticEvent>();
            public bool Supported => true;
            public void Play(HapticEvent haptic) => Played.Add(haptic);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (_root != null) Object.Destroy(_root);
        }

        static PointerEventData Pointer() => new PointerEventData(EventSystem.current);

        static Rect WorldRect(Component component)
        {
            var corners = new Vector3[4];
            ((RectTransform)component.transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>A world-space canvas of exactly width × height units at the origin (screen units = world units).</summary>
        static RectTransform SafeArea(Transform parent, float width, float height, Vector3 position)
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)canvasGo.transform;
            canvas.sizeDelta = new Vector2(width, height);
            canvas.pivot = Vector2.zero;
            canvas.position = position;
            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(canvas, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            return safeArea;
        }

        static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static UiScaleMath.Layout Layout => UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop);

        // ---- Layout at the interface scale limits -------------------------------------------------------

        [TestCase(4f / 3f, 1.1f)]
        [TestCase(16f / 10f, 1.1f)]
        [TestCase(16f / 9f, 1.1f)]
        [TestCase(20f / 9f, 1.1f)]
        [TestCase(4f / 3f, 0.9f)]
        [TestCase(20f / 9f, 0.9f)]
        public void Hud_AtTheEffectiveScale_NothingInteractiveOverlaps(float aspect, float requested)
        {
            float scale = UiScaleMath.Effective(requested, aspect, Layout);
            float height = UiScaleMath.ReferenceHeight / scale, width = aspect * height;
            _root = new GameObject("ScaleRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safe = SafeArea(_root.transform, width, height, Vector3.zero);
            var touch = TouchControlsFactory.Build(safe, Font);
            var hud = ContextualHudFactory.Build(safe, Font);
            var menu = MissionUiFactory.BuildMenuButton(safe, Font);
            var skip = MissionUiFactory.BuildSkipButton(safe, Font);
            var coach = MissionUiFactory.BuildCoachingPanel(safe, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            var look = CockpitFocusPanelFactory.BuildViewControls(safe, Font);
            Canvas.ForceUpdateCanvases();

            var flightControls = new List<Component> { touch.joystick, touch.throttle, touch.brake };
            var targets = new List<Component>(hud.railSlots) { hud.systemsButton, hud.profileButton, menu, skip, coach, look.lookForward };
            string at = $" at aspect {aspect:0.00}, scale {scale:0.000}";
            foreach (var target in targets)
            {
                var r = WorldRect(target);
                foreach (var control in flightControls)
                    Assert.IsFalse(r.Overlaps(WorldRect(control)), target.name + " over " + control.name + at);
                Assert.That(r.xMin, Is.GreaterThanOrEqualTo(-0.01f), target.name + " inside" + at);
                Assert.That(r.xMax, Is.LessThanOrEqualTo(width + 0.01f), target.name + " inside" + at);
                Assert.That(r.yMin, Is.GreaterThanOrEqualTo(-0.01f), target.name + " inside" + at);
                Assert.That(r.yMax, Is.LessThanOrEqualTo(height + 0.01f), target.name + " inside" + at);
            }
            // The rail never runs into the top bar, whatever the scale allows.
            foreach (var slot in hud.railSlots)
                Assert.IsFalse(WorldRect(slot).Overlaps(WorldRect(hud.profileButton)), "rail under the HUD profile button" + at);
            Assert.IsFalse(WorldRect(skip).Overlaps(WorldRect(menu)), at);
            // Warning banners sit between the joystick region and the rail.
            foreach (var warning in hud.warningSlots)
            {
                var w = WorldRect(warning);
                foreach (var control in flightControls) Assert.IsFalse(w.Overlaps(WorldRect(control)), "warning over " + control.name + at);
                foreach (var slot in hud.railSlots) Assert.IsFalse(w.Overlaps(WorldRect(slot)), "warning over the rail" + at);
            }
        }

        [TestCase(4f / 3f)]
        [TestCase(16f / 9f)]
        [TestCase(20f / 9f)]
        public void SettingsScreen_FitsWithLargeTargets_AtTheLargestScale(float aspect)
        {
            float scale = UiScaleMath.Effective(GameOptionsRules.MaxUiScale, aspect, Layout);
            float height = UiScaleMath.ReferenceHeight / scale, width = aspect * height;
            _root = new GameObject("SettingsRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safe = SafeArea(_root.transform, width, height, Vector3.zero);
            var settings = SettingsScreenFactory.Build(safe, Font);
            settings.SetReducedMotion(true);
            settings.Open(new SettingsView { Options = GameOptionsRules.Defaults, EffectiveUiScale = 1f, HapticsSupported = true, Fps60Supported = true });
            Canvas.ForceUpdateCanvases();

            var panel = WorldRect(settings.panel);
            Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(panel.xMax, Is.LessThanOrEqualTo(width));
            Assert.That(panel.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(panel.yMax, Is.LessThanOrEqualTo(height));

            var buttons = new List<HudButton>(settings.tabs) { settings.doneButton };
            foreach (var row in settings.rows)
            {
                buttons.AddRange(row.segments);
                if (row.IsStepper)
                {
                    buttons.Add(row.minus);
                    buttons.Add(row.plus);
                }
            }
            foreach (var button in buttons)
            {
                var r = WorldRect(button);
                Assert.That(r.height, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget - 0.01f), button.name);
                Assert.That(r.width, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget - 0.01f), button.name);
                Assert.IsTrue(panel.Contains(r.min) && panel.Contains(r.max), button.name + " on the panel");
            }
            // Rows on one page never overlap.
            for (int a = 0; a < settings.rows.Length; a++)
            for (int b = a + 1; b < settings.rows.Length; b++)
            {
                if (settings.rows[a].root.parent != settings.rows[b].root.parent) continue;
                Assert.IsFalse(WorldRect(settings.rows[a].root).Overlaps(WorldRect(settings.rows[b].root)), settings.rows[a].id + " / " + settings.rows[b].id);
            }
        }

        [UnityTest]
        public IEnumerator SettingsScreen_TabsShowOnePage_AndRaiseRequests()
        {
            _root = new GameObject("SettingsRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safe = SafeArea(_root.transform, 1920f, 1080f, Vector3.zero);
            var settings = SettingsScreenFactory.Build(safe, Font);
            settings.SetReducedMotion(true);
            var chosen = new List<(SettingId, int)>();
            var stepped = new List<(SettingId, int)>();
            settings.OptionChosen += (id, i) => chosen.Add((id, i));
            settings.Stepped += (id, d) => stepped.Add((id, d));
            var options = GameOptionsRules.Defaults;
            settings.Open(new SettingsView { Options = options, EffectiveUiScale = 1f, HudProfile = HudProfile.Detailed, HapticsSupported = false });
            yield return null;

            Assert.AreEqual(1f, settings.pages[(int)SettingsPage.Display].alpha);
            Assert.AreEqual(0f, settings.pages[(int)SettingsPage.Sound].alpha);
            Assert.IsTrue(settings.tabs[(int)SettingsPage.Display].Engaged, "the open tab is marked");
            Assert.IsTrue(settings.Row(SettingId.HudProfile).segments[1].Engaged, "DETAILED shown as the current profile");

            settings.tabs[(int)SettingsPage.Sound].OnPointerClick(Pointer());
            yield return null;
            Assert.AreEqual(SettingsPage.Sound, settings.Page);
            Assert.AreEqual(1f, settings.pages[(int)SettingsPage.Sound].alpha);
            Assert.AreEqual(0f, settings.pages[(int)SettingsPage.Display].alpha);
            Assert.IsFalse(settings.pages[(int)SettingsPage.Display].blocksRaycasts, "a hidden page takes no touches");

            settings.Row(SettingId.MasterVolume).plus.OnPointerClick(Pointer());
            settings.Row(SettingId.WarningVolume).minus.OnPointerClick(Pointer());
            Assert.Contains((SettingId.MasterVolume, 1), stepped);
            Assert.Contains((SettingId.WarningVolume, -1), stepped);
            Assert.AreEqual("80%", settings.Row(SettingId.MasterVolume).value.text);

            settings.tabs[(int)SettingsPage.Controls].OnPointerClick(Pointer());
            settings.Row(SettingId.AssistProfile).segments[2].OnPointerClick(Pointer());
            Assert.Contains((SettingId.AssistProfile, 2), chosen);
            Assert.IsFalse(settings.Row(SettingId.Haptics).segments[0].Interactable, "haptics cannot be switched on without a vibrator");
            Assert.IsNotEmpty(settings.Row(SettingId.Haptics).note.text, "and the screen says why");
        }

        [TestCase(1440f)]
        [TestCase(1920f)]
        [TestCase(2400f)]
        public void PauseCard_WithSettings_FitsTheColumn(float width)
        {
            _root = new GameObject("CardRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safe = SafeArea(_root.transform, width, 1080f, Vector3.zero);
            var card = MissionUiFactory.BuildMissionCard(safe, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset,
                CockpitFocusPanelFactory.ColumnBottom, CockpitFocusPanelFactory.ColumnTop);
            card.Show("Paused", "First Flight, step 3 of 8: rotation", null, true, true, "RESUME", "SETTINGS", "RESTART FIRST FLIGHT", "SKIP TUTORIAL",
                "CHANGE MODE");
            card.Layout();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(5, card.ButtonCount);
            var cardRect = WorldRect(card.card);
            Assert.That(cardRect.height, Is.LessThanOrEqualTo((CockpitFocusPanelFactory.ColumnTop - CockpitFocusPanelFactory.ColumnBottom) * 1080f + 1f));
            for (int i = 0; i < card.ButtonCount; i++)
            {
                var r = WorldRect(card.buttons[i]);
                Assert.That(r.height, Is.GreaterThanOrEqualTo(96f));
                Assert.IsTrue(cardRect.Contains(r.min) && cardRect.Contains(r.max), $"button {i} inside its card at width {width}");
                for (int j = 0; j < i; j++) Assert.IsFalse(r.Overlaps(WorldRect(card.buttons[j])), $"buttons {j} and {i}");
            }
            Assert.AreEqual(WorldRect(card.buttons[0]).yMax, WorldRect(card.buttons[1]).yMax, 0.01f, "RESUME and SETTINGS share a row");
        }

        // ---- HUD opacity and warnings -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator HudOpacity_DimsRoutineInformation_NeverWarnings()
        {
            _root = new GameObject("OpacityRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safe = SafeArea(_root.transform, 1920f, 1080f, Vector3.zero);
            var touch = TouchControlsFactory.Build(safe, Font);
            touch.visibility = TouchControlsVisibility.Always;
            var hud = ContextualHudFactory.Build(safe, Font);
            hud.SetMotionPreference(MotionPreference.Reduced);
            hud.SetOpacity(0.6f);
            touch.SetOpacityScale(0.6f);

            var disclosure = new DisclosureController();
            var warnings = new FlightWarningMonitor();
            var input = new DisclosureInput
            {
                Profile = HudProfile.Detailed,
                Phase = new FlightPhaseEstimate { Phase = FlightPhase.Cruise, Candidate = FlightPhase.Cruise, Confidence = 0.95f },
                HasRunway = true,
                DistanceToRunwayNm = 3f,
                Systems = new AircraftSystemsState { HasParkingBrake = true },
            };
            for (int i = 0; i < 80; i++)
            {
                warnings.Update(new FlightTelemetry { StallWarning = true, AltitudeFeet = 3000f, HeightAboveGroundFeet = 3000f }, 0.02f);
                input.WarningMask = warnings.ActiveMask;
                disclosure.Update(input, 0.02f);
                hud.Present(default, default, disclosure, warnings, HudProfile.Detailed);
                yield return null;
            }

            Assert.AreNotEqual(0, warnings.ActiveMask, "the stall warning is on");
            Assert.AreEqual(1f, hud.warningSlots[0].alpha, 1e-4f, "warnings are never dimmed");
            Assert.That(hud.dataItems[(int)HudElement.Airspeed].alpha, Is.InRange(0.01f, 0.6f + 1e-4f), "routine data follows the opacity");
            Assert.That(touch.EffectiveOpacity, Is.GreaterThanOrEqualTo(touch.opacity * touch.minimumOpacityShare - 1e-4f), "controls stay findable");
            Assert.IsNotNull(hud.warningIcons[0].sprite, "a severity symbol, not colour alone");
        }

        // ---- The presentation session over a real flight --------------------------------------------------

        IEnumerator BuildSession()
        {
            _root = new GameObject("PresentationRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, RunwayStart, out var telemetry);
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);

            var cockpit = new GameObject("CockpitAnchor").transform;
            cockpit.SetParent(aircraft.transform, false);
            cockpit.localPosition = new Vector3(0f, 1.7f, 1f);
            var cameraGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.transform.SetParent(_root.transform, false);
            var rig = cameraGo.AddComponent<CameraRig>();
            rig.target = aircraft.transform;
            rig.cockpitAnchor = cockpit;

            var safe = SafeArea(_root.transform, 1920f, 1080f, new Vector3(0f, 0f, 5000f));
            var touch = TouchControlsFactory.Build(safe, Font);
            touch.visibility = TouchControlsVisibility.Never;
            _hud = ContextualHudFactory.Build(safe, Font);
            _card = MissionUiFactory.BuildMissionCard(safe, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset,
                CockpitFocusPanelFactory.ColumnBottom, CockpitFocusPanelFactory.ColumnTop);
            var modeSelect = MissionUiFactory.BuildModeSelect(safe, Font);
            _settings = SettingsScreenFactory.Build(safe, Font);

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.cameraRig = rig;
            _session.touchControls = touch;
            _session.contextualHud = _hud;
            _session.rememberHudPreferences = false;
            _session.homeRunway = Runway;
            sessionGo.SetActive(true);

            var modesGo = new GameObject("GameModes");
            modesGo.SetActive(false);
            modesGo.transform.SetParent(_root.transform, false);
            _modes = modesGo.AddComponent<GameModeSession>();
            _modes.flight = _session;
            _modes.rememberProgress = false;
            _modes.showModeSelectOnStart = false;
            _modes.card = _card;
            _modes.modeSelect = modeSelect;
            modesGo.SetActive(true);

            _audio = new GameObject("Audio").AddComponent<AudioManager>();
            _audio.transform.SetParent(_root.transform, false);

            _haptics = new FakeHaptics();
            var presentationGo = new GameObject("Presentation");
            presentationGo.SetActive(false);
            presentationGo.transform.SetParent(_root.transform, false);
            _presentation = presentationGo.AddComponent<PresentationSession>();
            _presentation.flight = _session;
            _presentation.modes = _modes;
            _presentation.audioManager = _audio;
            _presentation.settings = _settings;
            _presentation.rememberOptions = false; // never read or write the player's real options
            _presentation.UseHapticDevice(_haptics);
            presentationGo.SetActive(true);

            _card.SetReducedMotion(true);
            _settings.SetReducedMotion(true);
            yield return null; // Start
            yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator Pause_SilencesTheFlight_AndResumeBringsItBack()
        {
            yield return BuildSession();
            _modes.StartMode(GameMode.FreeFlight);
            yield return new WaitForSeconds(0.5f); // real time, whatever the test frame rate
            Assert.IsFalse(_presentation.OutputPaused);
            Assert.IsFalse(AudioListener.pause);
            Assert.Greater(_audio.EngineLevel, 0.05f, "the engine idles audibly on the runway");

            _modes.OpenMenu();
            yield return null;
            Assert.IsTrue(_presentation.OutputPaused, "the menu pauses the flight's sound");
            Assert.IsTrue(AudioListener.pause);

            _modes.ResumeFromMenu();
            yield return null;
            Assert.IsFalse(_presentation.OutputPaused);
            Assert.IsFalse(AudioListener.pause);
        }

        [UnityTest]
        public IEnumerator EngineSound_FollowsPower_AndTheCockpitSoundsEnclosed()
        {
            yield return BuildSession();
            _modes.StartMode(GameMode.FreeFlight);
            yield return new WaitForSeconds(0.5f);
            float idlePitch = _audio.EnginePitch;
            float externalCutoff = _audio.CurrentAircraftCutoff;

            _session.Router.SetThrottle(1f);
            yield return new WaitForSeconds(3f);
            Assert.Greater(_audio.EnginePitch, idlePitch + 0.1f, "RPM rises with power");

            _session.ToggleCamera(); // cockpit
            yield return new WaitForSeconds(0.5f);
            Assert.Less(_audio.CurrentAircraftCutoff, externalCutoff * 0.5f, "the cabin muffles the engine and the wind");
            Assert.IsFalse(_audio.UsesMixer, "this test has no mixer: the fallback filters do the work");
        }

        [UnityTest]
        public IEnumerator RenderingChoices_NeverTouchThePhysicsStep()
        {
            yield return BuildSession();
            float step = Time.fixedDeltaTime;
            _presentation.OnOptionChosen(SettingId.Graphics, (int)GraphicsPreset.BatterySaver);
            _presentation.OnOptionChosen(SettingId.FrameRate, 1);
            yield return null;
            Assert.AreEqual(step, Time.fixedDeltaTime, "60 FPS does not change the 50 Hz physics");
            Assert.That(Application.targetFrameRate == 30 || Application.targetFrameRate == 60);
            _presentation.OnOptionChosen(SettingId.Graphics, (int)GraphicsPreset.HighQuality);
            _presentation.OnOptionChosen(SettingId.FrameRate, 0);
            yield return null;
            Assert.AreEqual(step, Time.fixedDeltaTime);
            Assert.AreEqual(30, Application.targetFrameRate);
            Assert.AreEqual(GraphicsPreset.HighQuality, _presentation.Graphics.Preset);
        }

        [UnityTest]
        public IEnumerator Settings_FromThePauseMenu_ReachTheSession_AndBackCloses()
        {
            yield return BuildSession();
            _modes.StartMode(GameMode.FreeFlight);
            yield return null;
            _modes.OpenMenu();
            _card.buttons[1].OnPointerClick(Pointer()); // SETTINGS
            yield return null;
            Assert.IsTrue(_settings.IsOpen);
            Assert.IsTrue(_modes.OverlayOpen, "MENU/Esc now closes the settings first");
            Assert.IsTrue(_modes.IsPaused, "the world stays paused underneath");

            _settings.tabs[(int)SettingsPage.Controls].OnPointerClick(Pointer());
            _settings.Row(SettingId.AssistProfile).segments[(int)FlightAssistProfile.Manual].OnPointerClick(Pointer());
            Assert.AreEqual(FlightAssistProfile.Manual, _session.AssistProfile, "one source of truth: the session's profile");

            _settings.tabs[(int)SettingsPage.Display].OnPointerClick(Pointer());
            _settings.Row(SettingId.HudOpacity).minus.OnPointerClick(Pointer());
            Assert.AreEqual(0.9f, _presentation.Options.HudOpacity, 1e-4f);
            Assert.AreEqual(0.9f, _hud.Opacity, 1e-4f, "applied at once");

            _settings.doneButton.OnPointerClick(Pointer());
            Assert.IsFalse(_settings.IsOpen);
            Assert.IsFalse(_modes.OverlayOpen);
            Assert.IsTrue(_modes.IsPaused, "back to the pause menu, still paused");
        }

        [UnityTest]
        public IEnumerator Haptics_ForSystemChanges_AndNeverWhenSwitchedOff()
        {
            yield return BuildSession();
            _modes.StartMode(GameMode.FreeFlight);
            yield return new WaitForFixedUpdate();
            _haptics.Played.Clear();

            _session.RequestSystemChange(new SystemChange(AircraftSystem.LandingLight, 1));
            Assert.Contains(HapticEvent.Selection, _haptics.Played, "a switch moved");

            _presentation.OnOptionChosen(SettingId.Haptics, 1); // OFF
            _haptics.Played.Clear();
            yield return new WaitForSecondsRealtime(0.3f);
            _session.RequestSystemChange(new SystemChange(AircraftSystem.LandingLight, 0));
            Assert.IsEmpty(_haptics.Played, "the player switched haptics off");
            Assert.IsFalse(_presentation.Options.Haptics);
        }

        [UnityTest]
        public IEnumerator Restart_NeverCountsAsATouchdown_AndSilencesTheOldFlight()
        {
            yield return BuildSession();
            _modes.StartMode(GameMode.FreeFlight, FreeFlightStart.Airborne);
            yield return new WaitForSeconds(1f);
            _haptics.Played.Clear();
            _session.ResetFlight(); // back to the airborne start
            _modes.StartMode(GameMode.FreeFlight); // and onto the runway: placed, not landed
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(_haptics.Played.Contains(HapticEvent.TouchdownSoft) || _haptics.Played.Contains(HapticEvent.TouchdownFirm),
                "placing the aircraft on the runway is not a touchdown");
        }
    }
}
