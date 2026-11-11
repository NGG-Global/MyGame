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
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The top-left flight information cluster (docs/TAP_DISCLOSURE_DESIGN.md, milestone 4) inside a
    /// real <see cref="FlightSession"/> with the Rigidbody aircraft, the touch controls, the
    /// contextual and gesture HUDs, the command cluster, the flight settings card and the cockpit
    /// focus panel, on a world-space canvas driven by synthetic pointers. Proves: tap to expand,
    /// tap a reading to focus and explain, tap outside to collapse (consumed), another cluster
    /// replaces it, no X; every profile collapses independently and the profile never changes;
    /// phase changes and warnings never open or fold it; warnings stay visible and surface their
    /// reading; the cockpit view starts quiet; a requested navigation guide survives collapsing;
    /// the readings are the session's telemetry; the flying fingers keep their input; nothing
    /// overlaps or clips on 4:3, 16:10, 16:9, 19.5:9 and 20:9 at the largest allowed scale, and
    /// inside a cut-out's safe area. Not how any of it looks or feels on a device.
    /// </summary>
    public class FlightInfoPlayModeTests
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
        CommandCluster _cluster;
        FlightInfoCluster _info;
        CameraRig _camera;
        ScriptedControls _controls;
        float _width;

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

        IEnumerator Build(float width = 1920f, bool scripted = true)
        {
            _width = width;
            _root = new GameObject("FlightInfoTestRoot");
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
            var settings = FlightSettingsPanelFactory.Build(safeArea, Font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            settings.SetReducedMotion(true);
            _hud = ContextualHudFactory.Build(safeArea, Font);
            _hud.SetMotionPreference(MotionPreference.Reduced);
            _gestureHud = GestureHudFactory.Build(safeArea, Font, _surface);
            _info = FlightInfoClusterFactory.Build(safeArea, Font);
            _cluster = CommandClusterFactory.Build(safeArea, Font);
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
            _session.flightInfo = _info;
            _session.focusPanel = focus;
            _session.flightSettings = settings;
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

        /// <summary>A tap on a button as uGUI delivers it: press, release, click, on one pointer.</summary>
        static IEnumerator Tap(HudButton button, int id)
        {
            Assert.IsTrue(button.isActiveAndEnabled, button.name + " is there to tap");
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

        IEnumerator Frames(int count = 3)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        FlightInfoSession Info => _session.FlightInfo;
        DisclosureCoordinator Coordinator => _session.Coordinator;
        Vector2 Sky => new Vector2(_width * 0.5f, 900f);

        IEnumerator Expand(int id = 400)
        {
            yield return Tap(_info.capsuleButton, id);
            Assert.AreEqual(FlightInfoLevel.Expanded, Info.Level, "a tap on the capsule opens the panel");
        }

        static void AssertNoCloseControl(Component root)
        {
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                Assert.AreNotEqual("×", text.text.Trim());
                Assert.AreNotEqual("X", text.text.Trim());
                StringAssert.DoesNotContain("CLOSE", text.text);
            }
        }

        // ---- Interactions --------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TapExpands_TapAReadingFocusesAndExplains_TapOutsideCollapses_WithoutAnX()
        {
            yield return Build();
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "the capsule is the resting state");
            Assert.IsTrue(_info.CapsuleShown && _info.capsuleGroup.blocksRaycasts, "the trigger is always there");
            Assert.IsFalse(_info.PanelShown);
            Assert.IsFalse(_info.panelGroup.blocksRaycasts, "a folded panel takes no touches");

            yield return Expand();
            yield return null;
            Assert.IsTrue(_info.PanelShown && _info.panelGroup.blocksRaycasts);
            Assert.IsFalse(_info.CapsuleShown, "the panel takes the capsule's place");
            Assert.AreEqual(Info.PanelLayer, Coordinator.ActiveRoot, "a root surface of the one coordinator");
            for (int i = 0; i < FlightInfoCluster.TileCount; i++)
            {
                Assert.IsTrue(_info.tiles[i].isActiveAndEnabled, "every reading is on the panel");
                Assert.AreNotEqual("-", _info.tileValues[i].text, "with its value");
            }
            Assert.IsTrue(_info.navigationTile.isActiveAndEnabled, "the destination's details");

            yield return Tap(_info.tiles[(int)HudElement.Airspeed], 401);
            Assert.AreEqual(FlightInfoLevel.Focused, Info.Level);
            Assert.AreEqual(HudElement.Airspeed, Info.Focus);
            Assert.AreEqual(2, Coordinator.Depth, "the focus is a child of the panel");
            Assert.IsTrue(_info.focusButton.isActiveAndEnabled && _info.explanationButton.isActiveAndEnabled, "enlarged and explained in the panel, no other window");
            Assert.AreEqual("AIRSPEED, KT", _info.focusTitle.text);
            StringAssert.Contains("wing", _info.explanationText.text);
            yield return null;
            Assert.AreEqual(_info.tileValues[(int)HudElement.Airspeed].text, _info.focusValue.text, "the enlarged value is the tile's value");
            Assert.IsTrue(_info.tiles[(int)HudElement.Airspeed].Engaged, "the focused tile is marked");
            Assert.IsFalse(_info.navigationTile.gameObject.activeSelf, "the lower row is the focus's");

            yield return Tap(_info.tiles[(int)HudElement.Altitude], 402);
            Assert.AreEqual(HudElement.Altitude, Info.Focus, "another reading moves the focus");
            Assert.IsFalse(_info.tiles[(int)HudElement.Airspeed].Engaged);
            yield return Tap(_info.explanationButton, 403);
            Assert.AreEqual(FlightInfoLevel.Expanded, Info.Level, "the detail goes back to the panel");
            Assert.AreEqual(1, Coordinator.Depth);

            yield return Tap(_info.tiles[(int)HudElement.Heading], 404);
            yield return TapSurface(405, Sky);
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "a tap outside folds everything");
            Assert.IsFalse(Coordinator.HasActive);
            Assert.AreEqual(DisclosureCollapseReason.OutsideTap, Coordinator.LastReasonOf(Info.PanelLayer));
            Assert.AreEqual(GestureSession.Doing.Nothing, _session.Gestures.Current, "the dismissing tap was consumed: no free look, nothing underneath");
            yield return null;
            Assert.IsTrue(_info.CapsuleShown);

            // The trigger again folds it, from the panel and from a focus.
            yield return Expand(406);
            yield return Tap(_info.tiles[(int)HudElement.Groundspeed], 407);
            yield return Tap(_info.triggerButton, 408);
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level);
            Assert.AreEqual(DisclosureCollapseReason.Trigger, Coordinator.LastReasonOf(Info.PanelLayer));
            AssertNoCloseControl(_info);
        }

        [UnityTest]
        public IEnumerator AnotherCluster_ReplacesIt_AndItReplacesThem()
        {
            yield return Build();
            yield return Expand();
            yield return Tap(_cluster.optionsButton, 410);
            Assert.IsTrue(_session.Commands.IsOpen, "the command options open");
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "and replace the flight information");
            Assert.AreEqual(DisclosureCollapseReason.Replaced, Coordinator.LastReasonOf(Info.PanelLayer));

            yield return Tap(_info.capsuleButton, 411);
            Assert.AreEqual(FlightInfoLevel.Expanded, Info.Level);
            Assert.IsFalse(_session.Commands.IsOpen, "the flight information replaces the command options");

            var handle = WorldRect(_gestureHud.handleHotspot).center;
            var p = Pointer(412, handle);
            _gestureHud.handleHotspot.OnPointerDown(p);
            _gestureHud.handleHotspot.OnPointerUp(p);
            yield return null;
            Assert.IsTrue(_session.Gestures.ArcOpen, "a tap on SYSTEMS opens the wheel");
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "the wheel replaces the flight information");

            yield return Tap(_info.capsuleButton, 413);
            Assert.IsFalse(_session.Gestures.ArcOpen, "and the flight information folds the wheel");
            Assert.AreEqual(FlightInfoLevel.Expanded, Info.Level);

            // The HUD band's pull is the development comparison's: in the tap-first interface the cluster owns the top left.
            Assert.IsFalse(_gestureHud.BandActive);
            Assert.IsFalse(_gestureHud.bandGroup.blocksRaycasts);
            Assert.Greater(_info.transform.GetSiblingIndex(), _gestureHud.transform.GetSiblingIndex(), "the cluster draws over the band");
        }

        // ---- Profiles, phases, warnings ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator EveryProfile_CollapsesIndependently_AndTheProfileNeverChanges()
        {
            yield return Build();
            foreach (var profile in new[] { HudProfile.Cinematic, HudProfile.Smart, HudProfile.Detailed })
            {
                _session.SetHudProfile(profile);
                yield return Frames();
                yield return Tap(_info.capsuleButton, 420);
                Assert.AreEqual(FlightInfoLevel.Expanded, Info.Level, profile + ": expands");
                Assert.AreEqual(profile, _session.ActiveHudProfile);
                yield return Tap(_info.triggerButton, 421);
                Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, profile + ": collapses");
                Assert.AreEqual(profile, _session.ActiveHudProfile, profile + ": collapsing never changes the profile");
            }

            // A profile change (the H key, the band) while it is open leaves it open. (The command cluster's selector is
            // another cluster: opening it replaces this one, which is the replacement rule, not the profile.)
            yield return Expand(422);
            foreach (var profile in new[] { HudProfile.Cinematic, HudProfile.Detailed, HudProfile.Smart })
            {
                _session.SetHudProfile(profile);
                yield return Frames();
                Assert.AreEqual(FlightInfoLevel.Expanded, Info.Level, profile + ": a profile change never folds the panel");
            }

            // Detailed at rest: a compact capsule, more on expansion.
            yield return Tap(_info.triggerButton, 423);
            _session.SetHudProfile(HudProfile.Detailed);
            yield return Frames();
            Assert.AreEqual(FlightInfoModel.MaxCapsuleReadings, Info.Model.CapsuleCount);
            Assert.That(WorldRect(_info.capsuleButton).width, Is.LessThan(WorldRect(_info.panel).width), "the capsule is narrower than the panel");
            Assert.AreEqual("IAS KT", _info.cellLabels[0].text);
        }

        [UnityTest]
        public IEnumerator PhaseChanges_NeverExpandACollapsedCluster_NorFoldAnOpenOne()
        {
            yield return Build();
            yield return new WaitForSeconds(2f); // the estimate's confidence builds up (an uncertain phase shows a safe fallback)
            Assert.AreEqual(FlightPhase.Parked, _session.PhaseEstimator.Current.Phase);
            Assert.AreEqual(0, Info.Model.CapsuleCount, "Smart, parked: just the trigger");

            _controls.Controls = new FlightControls(0f, 0f, 0f, 1f);
            float end = Time.time + 10f; // a phase is held at least 2 s, then the next one is confirmed
            while (Time.time < end && _session.PhaseEstimator.Current.Phase == FlightPhase.Parked) yield return null;
            Assert.AreNotEqual(FlightPhase.Parked, _session.PhaseEstimator.Current.Phase, "precondition: the phase changed");
            yield return Frames();
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "a phase change never opens the cluster");
            Assert.Greater(Info.Model.CapsuleCount, 0, "the capsule's content follows the phase instead");

            yield return Expand(430);
            var phase = _session.PhaseEstimator.Current.Phase;
            end = Time.time + 6f;
            while (Time.time < end && _session.PhaseEstimator.Current.Phase == phase) yield return null;
            yield return Frames();
            Assert.AreEqual(FlightInfoLevel.Expanded, Info.Level, "nor folds an open one");
            _controls.Controls = FlightControls.Neutral;
        }

        [UnityTest]
        public IEnumerator Warnings_StayVisible_AndAStallSurfacesTheAirspeedInTheCollapsedCapsule()
        {
            _root = new GameObject("WarningRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safe = SafeArea(NewCanvas(_root.transform, 1920f, 1080f, Vector3.zero), Vector2.zero, Vector2.one);
            var hud = ContextualHudFactory.Build(safe, Font);
            hud.SetMotionPreference(MotionPreference.Reduced);
            var info = FlightInfoClusterFactory.Build(safe, Font);
            info.SetReducedMotion(true);
            UnityEngine.Canvas.ForceUpdateCanvases();

            var disclosure = new DisclosureController();
            var warnings = new FlightWarningMonitor();
            var model = new FlightInfoModel();
            var stalled = new FlightTelemetry { StallWarning = true, IndicatedAirspeedKnots = 48f, AltitudeFeet = 1500f };
            var input = new DisclosureInput
            {
                Profile = HudProfile.Cinematic,
                Phase = new FlightPhaseEstimate { Phase = FlightPhase.Cruise, Candidate = FlightPhase.Cruise, Confidence = 0.95f },
                HasRunway = true,
                DistanceToRunwayNm = 20f,
            };
            float end = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < end)
            {
                warnings.Update(stalled, 0.02f);
                input.WarningMask = warnings.ActiveMask;
                disclosure.Update(input, 0.02f);
                model.Compose(disclosure, warnings.ActiveMask, false, false, false);
                info.Present(model, stalled, default, default, disclosure.Settings);
                hud.Present(disclosure, warnings, HudProfile.Cinematic);
                yield return null;
            }

            Assert.AreEqual(FlightInfoLevel.Collapsed, model.Level, "a warning never opens the cluster");
            Assert.AreEqual(1f, hud.warningSlots[0].alpha, 1e-3f, "the banner is shown, in Cinematic");
            Assert.AreEqual("STALL", hud.warningLabels[0].text);
            Assert.IsTrue(info.cells[0].gameObject.activeInHierarchy, "the collapsed capsule surfaces the airspeed");
            Assert.AreEqual("IAS KT", info.cellLabels[0].text);
            Assert.AreEqual("48", info.cellValues[0].text);
            Assert.AreEqual(info.alertValueColor, info.cellValues[0].color, "in the alert colour");
            Assert.IsFalse(info.cells[1].gameObject.activeInHierarchy, "routine information stays quiet");

            model.Expand();
            end = Time.realtimeSinceStartup + 0.2f;
            while (Time.realtimeSinceStartup < end)
            {
                info.Present(model, stalled, default, default, disclosure.Settings);
                hud.Present(disclosure, warnings, HudProfile.Cinematic);
                yield return null;
            }
            Assert.AreEqual(1f, hud.warningSlots[0].alpha, 1e-3f, "the open panel never hides or dims a warning");
            Assert.AreEqual(info.alertValueColor, info.tileValues[(int)HudElement.Airspeed].color);
            var panel = WorldRect(info.panel);
            foreach (var slot in hud.warningSlots) Assert.IsFalse(panel.Overlaps(WorldRect(slot)), "the panel never covers a banner");
            Assert.IsFalse(hud.warningSlots[0].blocksRaycasts, "a banner never takes touches");
        }

        // ---- Cockpit, navigation, telemetry ------------------------------------------------------------

        [UnityTest]
        public IEnumerator CockpitView_StartsQuiet_WithoutDuplicates_AndStaysAvailableOnDemand()
        {
            yield return Build();
            _session.SetHudProfile(HudProfile.Detailed);
            yield return Frames();
            Assert.AreEqual(3, Info.Model.CapsuleCount, "chase view, Detailed: three readings at rest");
            yield return Expand();

            yield return Tap(_cluster.cameraButton, 440);
            Assert.AreEqual(CameraMode.Cockpit, _camera.mode);
            yield return Frames();
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "the cockpit view starts quiet");
            Assert.AreEqual(DisclosureCollapseReason.Owner, Coordinator.LastReasonOf(Info.PanelLayer));
            Assert.AreEqual(0, Info.Model.CapsuleCount, "the instruments show the readings: the capsule does not repeat them");
            for (int i = 0; i < FlightInfoCluster.CellCount; i++) Assert.IsFalse(_info.cells[i].gameObject.activeInHierarchy);
            Assert.IsTrue(_info.CapsuleShown, "the trigger stays");

            yield return Expand(441);
            yield return null;
            Assert.IsTrue(_info.tiles[(int)HudElement.Airspeed].isActiveAndEnabled, "on demand, the readings are one tap away");
            yield return Tap(_cluster.cameraButton, 442);
            yield return Frames();
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "any camera change folds it");
        }

        [UnityTest]
        public IEnumerator NavigationRequested_SurvivesCollapsing_AndStaysInTheCapsule()
        {
            yield return Build();
            Assert.AreEqual(FlightInfoModel.NoElement, Info.Model.Chip, "Smart offers navigation on request");
            _session.PerformContextAction(ContextAction.Navigation);
            yield return Frames();
            Assert.AreEqual(HudElement.NavigationGuide, Info.Model.Chip);
            Assert.IsTrue(_info.chip.gameObject.activeInHierarchy && _info.chipNavigation.gameObject.activeInHierarchy, "the destination indicator in the capsule");
            Assert.AreEqual("AIRFIELD", _info.chipLabel.text);

            for (int round = 0; round < 2; round++)
            {
                yield return Expand(450 + round * 4);
                yield return Tap(_info.navigationTile, 451 + round * 4);
                Assert.AreEqual(HudElement.NavigationGuide, Info.Focus, "the guide can be focused and explained");
                Assert.IsTrue(_info.navigationTile.isActiveAndEnabled, "it keeps its own tile");
                Assert.AreEqual(WorldRect(_info.explanationButton).xMin, WorldRect(_info.approachTile).xMin, 1f, "its explanation takes the other half of the row");
                yield return TapSurface(452 + round * 4, Sky);
                Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level);
                yield return Frames();
                Assert.AreEqual(PlayerOverride.Show, _session.Disclosure.GetOverride(HudElement.NavigationGuide), "collapsing never touches the player's choice");
                Assert.IsTrue(_session.Disclosure.IsVisible(HudElement.NavigationGuide));
                Assert.IsTrue(_info.chip.gameObject.activeInHierarchy, "the capsule keeps the destination");
            }
        }

        [UnityTest]
        public IEnumerator Readings_AreTheSessionsTelemetry_AndTheAltimetersAltitude()
        {
            yield return Build();
            _session.SetHudProfile(HudProfile.Detailed);
            _session.RequestInstrumentSetting(InstrumentSetting.BaroHectopascals, InstrumentSettings.StandardPressureHectopascals + 10);
            yield return Expand();
            yield return new WaitForSecondsRealtime(0.4f);

            var shown = _session.Telemetry;
            shown.AltitudeFeet = _session.IndicatedAltitudeFeet;
            for (int i = 0; i < FlightInfoCluster.TileCount; i++)
            {
                var element = (HudElement)i;
                Assert.AreEqual(FlightReadouts.Text(element, FlightReadouts.Quantise(element, shown)), _info.tileValues[i].text, element + " is the session's value");
            }
            Assert.AreNotEqual("0", _info.tileValues[(int)HudElement.Altitude].text, "the altitude follows the altimeter's subscale, as the cockpit shows it");

            var nav = _session.Navigation;
            Assert.IsTrue(nav.Valid);
            Assert.AreEqual(FlightReadouts.NauticalMiles(FlightReadouts.Tenths(nav.DistanceMetres)), _info.navigationDistance.text);
            Assert.AreEqual(FlightReadouts.Bearing(FlightReadouts.CompassDegrees(nav.BearingDegrees)), _info.navigationBearing.text);

            yield return Tap(_info.triggerButton, 460);
            yield return new WaitForSecondsRealtime(0.3f);
            for (int c = 0; c < Info.Model.CapsuleCount; c++)
            {
                var element = Info.Model.CapsuleElement(c);
                Assert.AreEqual(_info.tileValues[(int)element].text, _info.cellValues[c].text, "the capsule and the panel show one value");
            }
        }

        // ---- Flight input, pause, reset --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator FlyingFingers_KeepTheirInput_WhileTheClusterOpensFocusesAndFolds()
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
            yield return new WaitForSeconds(0.5f); // the stick is smoothed over a few physics steps
            yield return new WaitForFixedUpdate();
            var before = _session.Router.Read(1f);
            Assert.That(before.Pitch, Is.GreaterThan(0.5f), "precondition: the stick is held back");
            Assert.That(before.Throttle, Is.GreaterThan(0.3f), "precondition: the throttle is up");
            Assert.AreEqual(2, _touch.Ownership.Count);

            yield return Tap(_info.capsuleButton, 3);
            yield return Tap(_info.tiles[(int)HudElement.VerticalSpeed], 4);
            _touch.joystick.OnDrag(stick); // still flying with the panel open and focused
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(FlightInfoLevel.Focused, Info.Level, "flying never folds it: a flight-control finger is never an outside tap");
            Assert.AreEqual(2, _touch.Ownership.Count, "both fingers are still owned by their controls");
            Assert.AreEqual(InteractionRoute.None, _surface.RouteOf(1), "an owned finger is never routed");
            yield return TapSurface(5, Sky);
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level);
            yield return new WaitForFixedUpdate();
            var after = _session.Router.Read(1f);
            Assert.AreEqual(before.Pitch, after.Pitch, 1e-3f, "the stick input survives");
            Assert.AreEqual(before.Throttle, after.Throttle, 1e-3f, "so does the throttle");
            Assert.AreEqual(2, _touch.Ownership.Count);

            _touch.joystick.OnPointerUp(stick);
            _touch.throttle.OnPointerUp(lever);
            yield return null;
            Assert.AreEqual(0, _touch.Ownership.Count);
        }

        [UnityTest]
        public IEnumerator PauseResetAndFocusLoss_FoldIt_AndAFrozenFlightNeverOpensIt()
        {
            yield return Build();
            yield return Expand();
            Time.timeScale = 0f;
            yield return null;
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "a frozen flight folds it");
            yield return Tap(_info.capsuleButton, 470);
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "and it never opens over a frozen flight");
            Time.timeScale = 1f;
            yield return null;

            yield return Expand(471);
            _session.ResetFlight();
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "a new flight folds it");
            Assert.AreEqual(DisclosureCollapseReason.Reset, Coordinator.LastReasonOf(Info.PanelLayer));

            yield return Expand(472);
            Coordinator.OnFocusLost();
            Assert.AreEqual(FlightInfoLevel.Collapsed, Info.Level, "leaving the app folds it");
        }

        // ---- Layout -----------------------------------------------------------------------------------

        static IEnumerable<HudButton> Targets(FlightInfoCluster info)
        {
            yield return info.capsuleButton;
            yield return info.triggerButton;
            foreach (var tile in info.tiles) yield return tile;
            yield return info.navigationTile;
            yield return info.approachTile;
            yield return info.focusButton;
            yield return info.explanationButton;
        }

        /// <summary>A model with the widest capsule (three readings and the navigation chip) and both guides on the panel.</summary>
        static FlightInfoModel WidestModel(out DisclosureController disclosure)
        {
            disclosure = new DisclosureController();
            var input = new DisclosureInput
            {
                Profile = HudProfile.Detailed,
                Phase = new FlightPhaseEstimate { Phase = FlightPhase.Cruise, Candidate = FlightPhase.Cruise, Confidence = 0.95f },
                HasRunway = true,
                HasNavigationTarget = true,
                DistanceToRunwayNm = 20f,
            };
            disclosure.Update(input, 0.02f);
            var model = new FlightInfoModel();
            model.Compose(disclosure, 0, true, true, true);
            return model;
        }

        [TestCase(4f / 3f)]
        [TestCase(16f / 10f)]
        [TestCase(16f / 9f)]
        [TestCase(19.5f / 9f)]
        [TestCase(20f / 9f)]
        public void Layout_AtTheLargestAllowedScale_NothingOverlapsOrClips(float aspect)
        {
            var layout = UiScaleMath.HudLayout(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaTop, TouchControlsFactory.JoystickRegionTop);
            float scale = UiScaleMath.Effective(1.1f, aspect, layout);
            float height = UiScaleMath.ReferenceHeight / scale, width = aspect * height;
            _root = new GameObject("LayoutRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safe = SafeArea(NewCanvas(_root.transform, width, height, Vector3.zero), Vector2.zero, Vector2.one);
            var surfaceGo = new GameObject("InteractionSurface", typeof(RectTransform), typeof(RaycastOnlyGraphic));
            ((RectTransform)surfaceGo.transform).SetParent(safe, false);
            var surface = surfaceGo.AddComponent<InteractionSurface>();
            var touch = TouchControlsFactory.Build(safe, Font);
            var hud = ContextualHudFactory.Build(safe, Font);
            var gestures = GestureHudFactory.Build(safe, Font, surface);
            var info = FlightInfoClusterFactory.Build(safe, Font);
            var cluster = CommandClusterFactory.Build(safe, Font);
            info.SetReducedMotion(true);
            var model = WidestModel(out var disclosure);
            info.Present(model, new FlightTelemetry { IndicatedAirspeedKnots = 112f, AltitudeFeet = 12000f, VerticalSpeedFeetPerMinute = -1500f }, default, default,
                disclosure.Settings);
            // Every lower-row control at once, for the geometry (the view shows at most two of them together).
            foreach (var b in new HudButton[] { info.navigationTile, info.approachTile, info.focusButton, info.explanationButton }) b.gameObject.SetActive(true);
            UnityEngine.Canvas.ForceUpdateCanvases();
            string at = $" at aspect {aspect:0.00}, scale {scale:0.000}";

            var keepClear = new List<Component> { touch.joystick, touch.throttle, touch.brake, gestures.handleHotspot, cluster.cameraButton, cluster.optionsButton,
                cluster.pauseButton, cluster.lookForward.lookForward, cluster.panel };
            keepClear.AddRange(hud.warningSlots);
            var capsule = WorldRect(info.capsuleButton);
            Assert.AreEqual(FlightInfoLayout.CapsuleHit(3, true).width, capsule.width, 0.01f, "the widest capsule" + at);
            foreach (var target in Targets(info))
            {
                var r = WorldRect(target);
                Assert.That(r.height, Is.GreaterThanOrEqualTo(96f - 0.01f), target.name + " is a full touch target" + at);
                Assert.That(r.width, Is.GreaterThanOrEqualTo(96f - 0.01f), target.name + at);
                Assert.IsTrue(r.xMin >= -0.01f && r.yMin >= -0.01f && r.xMax <= width + 0.01f && r.yMax <= height + 0.01f, target.name + " on screen" + at);
                foreach (var other in keepClear) Assert.IsFalse(r.Overlaps(WorldRect(other)), $"{target.name} over {other.name}{at}");
            }
            var panel = WorldRect(info.panel);
            foreach (var other in keepClear) Assert.IsFalse(panel.Overlaps(WorldRect(other)), $"the open panel over {other.name}{at}");
            Assert.That(panel.yMin, Is.GreaterThan(WorldRect(touch.joystick).yMax), "above the joystick region" + at);
            Assert.That(panel.xMax, Is.LessThan(WorldRect(cluster.pauseButton).xMin), "left of the command cluster" + at);
            // Tiles never overlap each other; the capsule's cells stay inside the pill.
            for (int i = 0; i < info.tiles.Length; i++)
                for (int j = i + 1; j < info.tiles.Length; j++)
                    Assert.IsFalse(WorldRect(info.tiles[i]).Overlaps(WorldRect(info.tiles[j])), at);
            var pill = WorldRect(info.capsuleButton.background);
            for (int c = 0; c < model.CapsuleCount; c++) AssertInside(WorldRect(info.cells[c]), pill, "cell " + c + at);
            AssertInside(WorldRect(info.chip), pill, "the chip" + at);
            // No clipped text: each value fits its plate (when the font's metrics are available to the test).
            AssertFits(info.tileValues[(int)HudElement.Altitude], WorldRect(info.tiles[0].background).width - 8f, "12000 in a tile" + at);
            AssertFits(info.tileValues[(int)HudElement.VerticalSpeed], WorldRect(info.tiles[0].background).width - 8f, "-1500 in a tile" + at);
            AssertFits(info.cellValues[1], FlightInfoLayout.CellWidth - 8f, "12000 in the capsule" + at);
            info.chipLabel.text = "LIGHTHOUSE";
            AssertFits(info.chipLabel, FlightInfoLayout.ChipWidth - 60f, "the longest destination in the chip" + at);
        }

        [Test]
        public void SafeArea_ACutOutMovesTheClusterIn_WithTheSafeEdge()
        {
            _root = new GameObject("SafeAreaRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            // A 20:9 phone in landscape: a camera cut-out on the left and a status inset at the top.
            var canvas = NewCanvas(_root.transform, 2400f, 1080f, Vector3.zero);
            var safe = SafeArea(canvas, new Vector2(0.045f, 0f), new Vector2(0.98f, 0.97f));
            var info = FlightInfoClusterFactory.Build(safe, Font);
            UnityEngine.Canvas.ForceUpdateCanvases();
            var safeRect = WorldRect(safe);
            foreach (var target in Targets(info))
            {
                var r = WorldRect(target);
                Assert.IsTrue(r.xMin >= safeRect.xMin - 0.01f && r.xMax <= safeRect.xMax + 0.01f && r.yMin >= safeRect.yMin - 0.01f && r.yMax <= safeRect.yMax + 0.01f,
                    target.name + " inside the safe area");
            }
            var trigger = WorldRect(info.triggerButton);
            Assert.AreEqual(safeRect.xMin + FlightInfoLayout.LeftMargin, trigger.xMin, 0.01f, "measured from the safe edge, not the screen's");
            Assert.AreEqual(safeRect.yMax - FlightInfoLayout.TopMargin, trigger.yMax, 0.01f);
            Assert.AreEqual(trigger.xMin, WorldRect(info.capsuleButton).xMin, 0.01f, "the capsule and the panel share the corner");
        }

        // ---- Helpers ------------------------------------------------------------------------------------------

        static void AssertInside(Rect inner, Rect outer, string what)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 0.01f), what);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 0.01f), what);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 0.01f), what);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 0.01f), what);
        }

        /// <summary>The text's natural width fits; skipped where the test player has no font metrics (it then reports 0).</summary>
        static void AssertFits(Text text, float width, string what)
        {
            float preferred = text.preferredWidth;
            if (preferred <= 0f) return;
            Assert.That(preferred, Is.LessThanOrEqualTo(width), what + ": \"" + text.text + "\"");
        }
    }
}
