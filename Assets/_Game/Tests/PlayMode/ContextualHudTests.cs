using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The contextual HUD built by its factory next to the touch controls, driven
    /// by synthetic disclosure states. Proves layout, accessibility of the flight
    /// controls, warnings in every profile, motion and rail behaviour. It does not
    /// prove how it looks on a device.
    /// </summary>
    public class ContextualHudTests
    {
        GameObject _root;
        TouchFlightControls _touch;
        ContextualHud _hud;
        RectTransform _canvas;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        /// <summary>World-space canvas of exactly width × 1080 units: screen coordinates equal world coordinates.</summary>
        void Build(float width = 1920f)
        {
            _root = new GameObject("HudTestRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(_root.transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _canvas = (RectTransform)canvasGo.transform;
            _canvas.sizeDelta = new Vector2(width, 1080f);
            _canvas.pivot = Vector2.zero;
            _canvas.position = Vector3.zero;

            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(_canvas, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _touch = TouchControlsFactory.Build(safeArea, font);
            _touch.visibility = TouchControlsVisibility.Always;
            _hud = ContextualHudFactory.Build(safeArea, font);
            _hud.SetMotionPreference(MotionPreference.Full);
            _hud.ConfigureActions(ContextualHud.ActionBit(ContextAction.ParkingBrake)
                                  | ContextualHud.ActionBit(ContextAction.ApproachGuide)
                                  | ContextualHud.ActionBit(ContextAction.Navigation));
            Canvas.ForceUpdateCanvases();
        }

        static Rect WorldRect(Component component)
        {
            var corners = new Vector3[4];
            ((RectTransform)component.transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        static DisclosureInput Input(FlightPhase phase, HudProfile profile, int warnings = 0, bool parkingBrake = false) => new DisclosureInput
        {
            Profile = profile,
            Phase = new FlightPhaseEstimate { Phase = phase, Candidate = phase, Confidence = 0.95f },
            WarningMask = warnings,
            OnGround = FlightPhases.IsGroundPhase(phase),
            HasRunway = true,
            DistanceToRunwayNm = 3f,
            Systems = new AircraftSystemsState { HasParkingBrake = true, ParkingBrakeSet = parkingBrake },
        };

        static RunwayRelation OnFinal => RunwayGeometry.Evaluate(new RunwayInfo(Vector3.zero, 0f, 1500f, 30f),
            new Vector3(20f, 150f, -3500f), 0f, 150f);

        [TestCase(1920f)] // 16:9 phone
        [TestCase(1440f)] // 4:3 tablet
        [TestCase(2400f)] // 20:9 phone
        public void Layout_NothingInteractiveOverlapsTheFlightControls(float width)
        {
            Build(width);
            var flightControls = new[] { WorldRect(_touch.joystick), WorldRect(_touch.throttle), WorldRect(_touch.brake) };
            var hudTargets = new List<Component>(_hud.railSlots) { _hud.systemsButton, _hud.profileButton, _hud.inspector };
            foreach (var target in hudTargets)
            {
                var rect = WorldRect(target);
                foreach (var control in flightControls)
                    Assert.IsFalse(rect.Overlaps(control), $"{target.name} overlaps a flight control at width {width}");
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0f), target.name + " inside the safe area");
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(width), target.name + " inside the safe area");
                Assert.That(rect.height, Is.GreaterThanOrEqualTo(96f), target.name + " touch target at least 96 units tall");
            }
            for (int i = 0; i < _hud.warningSlots.Length; i++)
                foreach (var control in flightControls)
                    Assert.IsFalse(WorldRect(_hud.warningSlots[i]).Overlaps(control), "warning banner over a flight control");
        }

        [UnityTest]
        public IEnumerator FlightControls_StayAccessibleAndInPlace_InEveryProfilePhaseAndWarning()
        {
            Build();
            yield return null;
            var joystick = WorldRect(_touch.joystick);
            var throttle = WorldRect(_touch.throttle);
            float opacity = _touch.controlsGroup.alpha;
            var disclosure = new DisclosureController();
            var warnings = new FlightWarningMonitor();

            foreach (HudProfile profile in new[] { HudProfile.Cinematic, HudProfile.Smart, HudProfile.Detailed })
            for (int p = 0; p < FlightPhases.Count; p++)
            {
                var phase = (FlightPhase)p;
                warnings.Update(new FlightTelemetry { StallWarning = !FlightPhases.IsGroundPhase(phase) }, 0.02f);
                disclosure.Update(Input(phase, profile, warnings.ActiveMask), 0.02f);
                _hud.Present(default, OnFinal, disclosure, warnings, profile);
                yield return null;

                string where = profile + " / " + phase;
                Assert.IsTrue(_touch.joystick.isActiveAndEnabled, where);
                Assert.IsTrue(_touch.throttle.isActiveAndEnabled, where);
                Assert.IsTrue(_touch.controlsGroup.blocksRaycasts && _touch.controlsGroup.interactable, where);
                Assert.AreEqual(opacity, _touch.controlsGroup.alpha, 1e-4f, where);
                Assert.AreEqual(joystick, WorldRect(_touch.joystick), where + ": joystick never moves");
                Assert.AreEqual(throttle, WorldRect(_touch.throttle), where + ": throttle never moves");
                Assert.IsTrue(_hud.systemsButton.isActiveAndEnabled, where + ": manual access to systems");
            }
        }

        [UnityTest]
        public IEnumerator Warning_OverridesCinematic()
        {
            Build();
            var disclosure = new DisclosureController();
            var warnings = new FlightWarningMonitor();
            var stalled = new FlightTelemetry { StallWarning = true, IndicatedAirspeedKnots = 48f };
            warnings.Update(stalled, 0.02f);
            disclosure.Update(Input(FlightPhase.Cruise, HudProfile.Cinematic, warnings.ActiveMask), 0.02f);

            float end = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < end)
            {
                _hud.Present(stalled, default, disclosure, warnings, HudProfile.Cinematic);
                yield return null;
            }

            Assert.AreEqual(1f, _hud.warningSlots[0].alpha, 1e-3f, "warning banner shown");
            Assert.AreEqual("STALL", _hud.warningLabels[0].text);
            Assert.IsFalse(_hud.warningSlots[0].blocksRaycasts, "a banner never takes touches");
            Assert.AreEqual(1f, _hud.dataItems[(int)HudElement.Airspeed].alpha, 1e-3f, "airspeed revealed by the stall warning");
            Assert.AreEqual(0f, _hud.dataItems[(int)HudElement.Altitude].alpha, 1e-3f, "routine information stays hidden");
            Assert.AreEqual("48", _hud.dataValues[(int)HudElement.Airspeed].text);
        }

        [Test]
        public void Fade_IsShortEasedAndRisesIntoPlace()
        {
            Build();
            var group = _hud.dataItems[0];
            var fade = new HudFade(group, interactive: false);
            var home = fade.Home;

            fade.SetTarget(1f, 0.2f, 10f);
            fade.Tick(0.1f);
            Assert.That(group.alpha, Is.InRange(0.05f, 0.95f), "half way through the fade");
            Assert.That(((RectTransform)group.transform).anchoredPosition.y, Is.LessThan(home.y), "rising into place");
            fade.Tick(0.1f);
            Assert.AreEqual(1f, group.alpha, 1e-5f);
            Assert.AreEqual(home, ((RectTransform)group.transform).anchoredPosition, "always ends at its fixed home");

            fade.SetTarget(0f, 0.2f, 10f);
            fade.Tick(0.2f);
            Assert.AreEqual(0f, group.alpha, 1e-5f);
            Assert.AreEqual(home, ((RectTransform)group.transform).anchoredPosition, "no movement when hiding");
        }

        [Test]
        public void ReducedMotion_NoMovementAndInstantFades()
        {
            Build();
            _hud.SetMotionPreference(MotionPreference.Reduced);
            Assert.IsTrue(_hud.ReducedMotion);
            var disclosure = new DisclosureController();
            disclosure.Update(Input(FlightPhase.Climb, HudProfile.Smart), 0.02f);
            var home = ((RectTransform)_hud.dataItems[0].transform).anchoredPosition;

            _hud.Present(default, default, disclosure, null, HudProfile.Smart);

            Assert.AreEqual(1f, _hud.dataItems[(int)HudElement.Airspeed].alpha, 1e-5f, "shown at once");
            Assert.AreEqual(home, ((RectTransform)_hud.dataItems[0].transform).anchoredPosition);
        }

        [Test]
        public void RailTap_RaisesTheAction_AndHiddenSlotsTakeNoTouches()
        {
            Build();
            var requested = new List<ContextAction>();
            _hud.ActionRequested += requested.Add;
            int parkSlot = _hud.SlotOf(ContextAction.ParkingBrake);
            Assert.AreEqual(0, parkSlot, "parking brake is the slot nearest the thumb");
            Assert.AreEqual(-1, _hud.SlotOf(ContextAction.Flaps), "no slot for a system the aircraft lacks");

            var disclosure = new DisclosureController();
            disclosure.Update(Input(FlightPhase.Parked, HudProfile.Smart), 0.02f);
            _hud.Present(default, default, disclosure, null, HudProfile.Smart);
            var group = _hud.railSlots[parkSlot].GetComponent<CanvasGroup>();
            Assert.IsTrue(group.blocksRaycasts, "visible: takes touches while it fades in");

            var button = _hud.railSlots[parkSlot];
            var pointer = new PointerEventData(EventSystem.current) { pointerId = 3 };
            button.OnPointerDown(pointer);
            button.OnPointerUp(pointer);
            button.OnPointerClick(pointer);
            CollectionAssert.AreEqual(new[] { ContextAction.ParkingBrake }, requested);

            int navSlot = _hud.SlotOf(ContextAction.Navigation);
            Assert.IsFalse(_hud.railSlots[navSlot].GetComponent<CanvasGroup>().blocksRaycasts, "hidden actions cannot be tapped");
        }

        [Test]
        public void PressedAction_IsNeverPulledFromUnderTheFinger()
        {
            Build();
            var disclosure = new DisclosureController();
            disclosure.Update(Input(FlightPhase.Cruise, HudProfile.Smart), 0.02f);
            _hud.Present(default, default, disclosure, null, HudProfile.Smart);
            var button = _hud.railSlots[_hud.SlotOf(ContextAction.Navigation)];
            var group = button.GetComponent<CanvasGroup>();
            Assert.IsTrue(group.blocksRaycasts, "navigation offered in cruise");
            button.OnPointerDown(new PointerEventData(EventSystem.current) { pointerId = 5 });

            for (int i = 0; i < 250; i++) // 5 s on approach: the controller no longer offers navigation
            {
                disclosure.Update(Input(FlightPhase.Approach, HudProfile.Smart), 0.02f);
                _hud.Present(default, default, disclosure, null, HudProfile.Smart);
            }
            Assert.IsFalse(disclosure.IsActionVisible(ContextAction.Navigation));
            Assert.IsTrue(group.blocksRaycasts, "still there while pressed");

            button.OnPointerUp(new PointerEventData(EventSystem.current) { pointerId = 5 });
            _hud.Present(default, default, disclosure, null, HudProfile.Smart);
            Assert.IsFalse(group.blocksRaycasts, "released: now it can go");
        }

        [Test]
        public void EngagedParkingBrake_ShowsItsStateInCinematic()
        {
            Build();
            var disclosure = new DisclosureController();
            disclosure.Update(Input(FlightPhase.Parked, HudProfile.Cinematic, parkingBrake: true), 0.02f);
            _hud.Present(default, default, disclosure, null, HudProfile.Cinematic);
            var button = _hud.railSlots[_hud.SlotOf(ContextAction.ParkingBrake)];
            Assert.IsTrue(button.GetComponent<CanvasGroup>().blocksRaycasts);
            Assert.IsTrue(button.Engaged);
            Assert.AreEqual("BRAKE SET", button.label.text);
        }

        [Test]
        public void ProfileButton_ShowsTheProfileAndRequestsTheNext()
        {
            Build();
            int requests = 0;
            _hud.ProfileCycleRequested += () => requests++;
            var disclosure = new DisclosureController();
            disclosure.Update(Input(FlightPhase.Cruise, HudProfile.Detailed), 0.02f);
            _hud.Present(default, default, disclosure, null, HudProfile.Detailed);
            Assert.AreEqual("HUD\nDETAILED", _hud.profileButton.label.text);
            Assert.IsFalse(_hud.systemsButton.Interactable, "SYSTEMS has nothing to add in Detailed");

            _hud.profileButton.OnPointerClick(new PointerEventData(EventSystem.current));
            Assert.AreEqual(1, requests);
        }
    }
}
