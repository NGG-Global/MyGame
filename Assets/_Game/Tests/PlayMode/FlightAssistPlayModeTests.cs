using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Assistance;
using OpenSkies.Awareness;
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
    /// Flight assistance in a real <see cref="FlightSession"/>: the Rigidbody
    /// aircraft in flight with profile changes under load, sensitivity, the
    /// settings card, the flying tips, layout and the remembered choices. The
    /// EditMode scenarios cover flight behaviour in depth; these confirm it with
    /// the physics engine and the real wiring.
    /// </summary>
    public class FlightAssistPlayModeTests
    {
        sealed class ScriptedControls : IFlightControlSource
        {
            public FlightControls Controls = new FlightControls(0f, 0f, 0f, 0f);
            public FlightControls Read(float deltaTime) => Controls;
            public void ResetControls() => Controls = FlightControls.Neutral;
        }

        GameObject _root;
        FlightSession _session;
        ScriptedControls _controls;
        ContextualHud _hud;
        FlightSettingsPanel _settings;
        GuidanceLine _guidance;
        CockpitFocusPanel _focus;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        static PointerEventData Pointer() => new PointerEventData(EventSystem.current);

        IEnumerator Build(Vector3 position, bool withUi)
        {
            _root = new GameObject("AssistTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, position, out var telemetry);
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);

            if (withUi)
            {
                var safeArea = SafeArea(_root.transform, 1920f, new Vector3(0f, 0f, 5000f));
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                TouchControlsFactory.Build(safeArea, font).visibility = TouchControlsVisibility.Never;
                _focus = CockpitFocusPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
                _settings = FlightSettingsPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
                _guidance = FlightSettingsPanelFactory.BuildGuidanceLine(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
                _hud = ContextualHudFactory.Build(safeArea, font);
                _hud.SetMotionPreference(MotionPreference.Reduced);
            }

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            _session = sessionGo.AddComponent<FlightSession>();
            _session.aircraft = aircraft;
            _session.telemetry = telemetry;
            _session.rememberHudPreferences = false; // never read or write the player's real preferences
            _session.homeRunway = new RunwayInfo(Vector3.zero, 0f, 2000f, 30f);
            _session.contextualHud = _hud;
            _session.focusPanel = _focus;
            _session.flightSettings = _settings;
            _session.guidanceLine = _guidance;
            _controls = new ScriptedControls();
            _session.SetControlSource(_controls);
            sessionGo.SetActive(true);
            if (withUi)
            {
                _settings.SetReducedMotion(true);
                _guidance.SetReducedMotion(true);
                _focus.SetReducedMotion(true);
            }
            yield return new WaitForFixedUpdate();
        }

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

        /// <summary>Puts the Rigidbody aircraft into level flight at about 94 kt (its hands-off trim speed).</summary>
        void Launch()
        {
            var body = _session.aircraft.Body;
            var pose = Quaternion.Euler(-1.9f, 0f, 0f); // nose up by the trim angle of attack
            body.rotation = pose;
            _session.aircraft.transform.rotation = pose;
            body.linearVelocity = new Vector3(0f, 0f, 48.5f);
            body.angularVelocity = Vector3.zero;
        }

        // ---- In flight with the physics engine --------------------------------------------------------

        [UnityTest]
        public IEnumerator InFlight_ProfileChangesUnderLoad_AreSmooth_AndManualIsDirect()
        {
            yield return Build(new Vector3(0f, 600f, -500f), withUi: false);
            Launch();
            _controls.Controls = new FlightControls(0f, 0f, 0f, 0.5f);
            Assert.AreEqual(FlightAssistProfile.Assisted, _session.AssistProfile, "first launch default");

            // Hands off in Assisted: steady.
            for (int i = 0; i < 250; i++) yield return new WaitForFixedUpdate();
            var t = _session.Telemetry;
            Assert.AreEqual(1f, _session.Assistance.State.Authority, 1e-4f, "engaged in flight");
            Assert.That(Mathf.Abs(t.BankDegrees), Is.LessThan(3f));
            Assert.That(t.IndicatedAirspeedKnots, Is.InRange(80f, 110f));

            // Roll into a turn, let go, and change profile every 2 s under load.
            var sequence = new[] { FlightAssistProfile.Manual, FlightAssistProfile.Standard, FlightAssistProfile.Assisted, FlightAssistProfile.Manual };
            float maxStep = 0f, maxPitchRate = 0f, maxRollRate = 0f;
            var previous = _session.aircraft.Controls;
            for (int i = 0; i < 75 + 100 * sequence.Length; i++)
            {
                bool rolling = i < 75;
                _controls.Controls = new FlightControls(0.06f, rolling ? 0.4f : 0f, 0f, 0.7f);
                if (i >= 75 && (i - 75) % 100 == 0) _session.SetAssistProfile(sequence[(i - 75) / 100]);
                yield return new WaitForFixedUpdate();
                var c = _session.aircraft.Controls;
                Assert.IsFalse(float.IsNaN(c.Pitch) || float.IsNaN(c.Roll) || float.IsNaN(c.Yaw));
                if (i > 77) // after the pilot's own roll-out step
                    maxStep = Mathf.Max(maxStep, Mathf.Max(Mathf.Abs(c.Pitch - previous.Pitch), Mathf.Max(Mathf.Abs(c.Roll - previous.Roll), Mathf.Abs(c.Yaw - previous.Yaw))));
                if (i > 100) // once the pilot's roll has settled
                {
                    maxPitchRate = Mathf.Max(maxPitchRate, Mathf.Abs(_session.Telemetry.PitchRateDegreesPerSecond));
                    maxRollRate = Mathf.Max(maxRollRate, Mathf.Abs(_session.Telemetry.RollRateDegreesPerSecond));
                }
                previous = c;
            }
            Assert.That(maxStep, Is.LessThan(0.04f), "no command jump at any profile change");
            Assert.That(maxPitchRate, Is.LessThan(8f), "no pitch jolt");
            Assert.That(maxRollRate, Is.LessThan(8f), "no roll jolt");

            // Fully in Manual now: roll and rudder are exactly the pilot's (pitch carries only the kept trim).
            for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(1f, _session.Assistance.State.Blend, 1e-6f);
            Assert.AreEqual(0f, _session.aircraft.Controls.Roll, 1e-6f);
            Assert.AreEqual(0f, _session.aircraft.Controls.Yaw, 1e-6f);
            Assert.AreEqual(AssistMath.Compose(0.06f, _session.Assistance.Trim), _session.aircraft.Controls.Pitch, 1e-5f);
            Assert.AreEqual(0.7f, _session.aircraft.Controls.Throttle, 1e-6f, "assistance never moves the throttle");
        }

        [UnityTest]
        public IEnumerator Sensitivity_ShapesThePilotsInput_InEveryProfile()
        {
            yield return Build(new Vector3(0f, 0f, -500f), withUi: false);
            _controls.Controls = new FlightControls(0.3f, -0.3f, 0f, 0f);
            foreach (FlightAssistProfile profile in System.Enum.GetValues(typeof(FlightAssistProfile)))
            {
                _session.SetAssistProfile(profile);
                foreach (ControlSensitivity sensitivity in System.Enum.GetValues(typeof(ControlSensitivity)))
                {
                    _session.SetSensitivity(sensitivity);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    float expected = ControlResponse.Shape(0.3f, sensitivity);
                    Assert.AreEqual(expected, _session.aircraft.Controls.Pitch, 1e-5f, $"{profile}, {sensitivity}: on the ground the shaped input goes straight through");
                    Assert.AreEqual(-expected, _session.aircraft.Controls.Roll, 1e-5f);
                }
            }
        }

        // ---- Settings card, rail action, tips ---------------------------------------------------------

        [UnityTest]
        public IEnumerator SettingsCard_FromTheRail_ChangesProfileSensitivityAndOptions()
        {
            yield return Build(new Vector3(0f, 0f, -500f), withUi: true);
            yield return null;
            int slot = _hud.SlotOf(ContextAction.Assistance);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0), "ASSIST is on the rail");
            _hud.railSlots[slot].OnPointerClick(Pointer());
            Assert.IsTrue(_settings.IsOpen);
            yield return null;
            Assert.IsTrue(_settings.profiles[(int)FlightAssistProfile.Assisted].Engaged);
            Assert.AreEqual(2, _settings.VisibleOptionCount, "Assisted: stall aid and tips");
            Assert.IsTrue(_settings.options[0].Engaged && _settings.options[1].Engaged, "both on by default");

            _settings.profiles[(int)FlightAssistProfile.Standard].OnPointerClick(Pointer());
            yield return null;
            Assert.AreEqual(FlightAssistProfile.Standard, _session.AssistProfile);
            Assert.AreEqual(FlightAssistProfile.Standard, _session.Assistance.Profile);
            Assert.AreEqual(1, _settings.VisibleOptionCount, "Standard: automatic trim");
            Assert.AreEqual(FlightSettingOption.AutoTrim, _settings.OptionMeaning(0));
            _settings.options[0].OnPointerClick(Pointer());
            yield return null;
            Assert.IsFalse(_session.Assistance.StandardAutoTrim);
            Assert.IsFalse(_settings.options[0].Engaged);

            _settings.profiles[(int)FlightAssistProfile.Manual].OnPointerClick(Pointer());
            yield return null;
            Assert.AreEqual(0, _settings.VisibleOptionCount);
            Assert.IsNotEmpty(_settings.optionNote.text, "Manual explains where the trim is");

            _settings.sensitivities[(int)ControlSensitivity.High].OnPointerClick(Pointer());
            yield return null;
            Assert.AreEqual(ControlSensitivity.High, _session.Sensitivity);
            Assert.IsTrue(_settings.sensitivities[(int)ControlSensitivity.High].Engaged);

            // One panel at a time in the free column.
            _hud.railSlots[_hud.SlotOf(ContextAction.Flaps)].OnPointerClick(Pointer());
            Assert.IsTrue(_focus.IsOpen);
            Assert.IsFalse(_settings.IsOpen, "the flap selector closes the settings");
            _hud.railSlots[slot].OnPointerClick(Pointer());
            Assert.IsTrue(_settings.IsOpen);
            Assert.IsFalse(_focus.IsOpen, "and the settings close the flap selector");
            _settings.close.OnPointerClick(Pointer());
            Assert.IsFalse(_settings.IsOpen);
            Assert.AreEqual(0f, _session.aircraft.Controls.Throttle, "settings never touch the flight controls");
        }

        [UnityTest]
        public IEnumerator Tips_ShowOnlyInAssisted()
        {
            yield return Build(new Vector3(0f, 0f, -500f), withUi: true);
            yield return new WaitForSeconds(2f); // parked on the runway, phase settles (advice shows for 8 s)
            Assert.IsTrue(_guidance.Shown, "Assisted, parked on the runway at idle: a take-off tip");
            Assert.AreEqual(_session.Guidance.Text(GuidanceHint.TakeOffPower), _guidance.CurrentTip);

            _session.SetAssistProfile(FlightAssistProfile.Standard);
            yield return null;
            Assert.IsFalse(_guidance.Shown, "no tips in Standard");
            _session.SetAssistProfile(FlightAssistProfile.Assisted);
            _session.ToggleFlightOption(FlightSettingOption.Guidance);
            yield return null;
            Assert.IsFalse(_session.GuidanceEnabled);
            Assert.IsFalse(_guidance.Shown, "and none when the player turned them off");
        }

        [Test]
        public void Preferences_RememberTheFlightSettings()
        {
            var saved = new FlightPreferences.Settings
            {
                Profile = FlightAssistProfile.Manual, Sensitivity = ControlSensitivity.Low,
                StandardAutoTrim = false, AssistedStallPrevention = false, Guidance = false,
            };
            var fallback = new FlightPreferences.Settings
            {
                Profile = FlightAssistProfile.Assisted, Sensitivity = ControlSensitivity.Medium,
                StandardAutoTrim = true, AssistedStallPrevention = true, Guidance = true,
            };
            string[] keys =
            {
                "OpenSkies.Flight.AssistProfile", "OpenSkies.Flight.Sensitivity", "OpenSkies.Flight.StandardAutoTrim",
                "OpenSkies.Flight.AssistedStallPrevention", "OpenSkies.Flight.Guidance",
            };
            var backup = new Dictionary<string, int>();
            foreach (var key in keys)
                if (PlayerPrefs.HasKey(key)) backup[key] = PlayerPrefs.GetInt(key);
            try
            {
                FlightPreferences.Save(saved);
                var loaded = FlightPreferences.Load(fallback);
                Assert.AreEqual(FlightAssistProfile.Manual, loaded.Profile);
                Assert.AreEqual(ControlSensitivity.Low, loaded.Sensitivity);
                Assert.IsFalse(loaded.StandardAutoTrim || loaded.AssistedStallPrevention || loaded.Guidance);
                PlayerPrefs.SetInt(keys[0], 99);
                Assert.AreEqual(FlightAssistProfile.Assisted, FlightPreferences.Load(fallback).Profile, "a bad value falls back");
            }
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

        // ---- Layout -----------------------------------------------------------------------------------

        static Rect WorldRect(Component component)
        {
            var corners = new Vector3[4];
            ((RectTransform)component.transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        [TestCase(1440f)]
        [TestCase(1920f)]
        [TestCase(2400f)]
        public void SettingsCardAndTips_FitBetweenTheFlightControls(float width)
        {
            _root = new GameObject("AssistLayoutRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
            var safeArea = SafeArea(_root.transform, width, Vector3.zero);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var touch = TouchControlsFactory.Build(safeArea, font);
            var settings = FlightSettingsPanelFactory.Build(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            var tips = FlightSettingsPanelFactory.BuildGuidanceLine(safeArea, font, TouchControlsFactory.JoystickRegionRight, ContextualHudFactory.RailLeftInset);
            var hud = ContextualHudFactory.Build(safeArea, font);
            Canvas.ForceUpdateCanvases();
            settings.Open(new FlightSettingsState { Profile = FlightAssistProfile.Assisted, Sensitivity = ControlSensitivity.Medium });
            settings.Layout();

            var controls = new List<Component> { touch.joystick, touch.throttle, touch.brake, hud.systemsButton, hud.profileButton };
            controls.AddRange(hud.railSlots);
            Rect card = WorldRect(settings.card);
            foreach (var control in controls)
            {
                Assert.IsFalse(card.Overlaps(WorldRect(control)), $"settings card over {control.name} at width {width}");
                Assert.IsFalse(WorldRect(tips).Overlaps(WorldRect(control)), $"tips over {control.name} at width {width}");
            }
            var buttons = new List<HudButton>(settings.profiles);
            buttons.AddRange(settings.sensitivities);
            buttons.AddRange(settings.options);
            buttons.Add(settings.close);
            foreach (var button in buttons)
            {
                Rect r = WorldRect(button);
                Assert.That(r.height, Is.GreaterThanOrEqualTo(96f), $"{button.name}: large touch target");
                Assert.That(r.width, Is.GreaterThanOrEqualTo(96f), $"{button.name}: large touch target at width {width}");
                Assert.IsTrue(card.Contains(r.center), $"{button.name} on its card");
            }
            Assert.That(card.height, Is.LessThanOrEqualTo(0.52f * 1080f + 1f), "fits the free column");
            Assert.That(WorldRect(tips).yMax, Is.LessThan(CockpitFocusPanelFactory.ColumnBottom * 1080f), "tips stay below the panels");
        }
    }
}
