using System.Collections;
using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightInput;
using OpenSkies.FlightState;
using OpenSkies.Gameplay;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The disclosure system inside a real <see cref="FlightSession"/> with the
    /// Rigidbody aircraft: phase detection from physics telemetry, HUD profile
    /// changes during flight, the parking brake and reset.
    /// </summary>
    public class FlightSessionDisclosureTests
    {
        sealed class ScriptedControls : IFlightControlSource
        {
            public float Throttle;
            public FlightControls Read(float deltaTime) => new FlightControls(0f, 0f, 0f, Throttle);
            public void ResetControls() => Throttle = 0f;
        }

        /// <summary>Opens the router's throttle lever every step, like holding a throttle key.</summary>
        sealed class ThrottleUp : IFlightInputProvider
        {
            public void Contribute(ref FlightInputFrame frame, float deltaTime) => frame.ThrottleDelta += 1f;
            public void ResetInput() { }
        }

        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        FlightSession CreateSession(IFlightControlSource controls, bool withHud)
        {
            _root = new GameObject("DisclosureTestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, new Vector3(0f, 0f, -500f), out var telemetry);

            ContextualHud hud = null;
            if (withHud)
            {
                new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);
                var canvasGo = new GameObject("Canvas", typeof(Canvas));
                canvasGo.transform.SetParent(_root.transform, false);
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var canvas = (RectTransform)canvasGo.transform;
                canvas.sizeDelta = new Vector2(1920f, 1080f);
                var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
                safeArea.SetParent(canvas, false);
                safeArea.anchorMin = Vector2.zero;
                safeArea.anchorMax = Vector2.one;
                safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
                hud = ContextualHudFactory.Build(safeArea, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                hud.motionPreference = MotionPreference.Full;
            }

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            var session = sessionGo.AddComponent<FlightSession>();
            session.aircraft = aircraft;
            session.telemetry = telemetry;
            session.contextualHud = hud;
            session.rememberHudPreferences = false; // tests never write player preferences
            session.homeRunway = new RunwayInfo(Vector3.zero, 0f, 2000f, 30f);
            if (controls != null) session.SetControlSource(controls);
            sessionGo.SetActive(true);
            return session;
        }

        [UnityTest]
        public IEnumerator Session_DetectsParkedThenTakeoffRoll_FromPhysicsTelemetry()
        {
            var controls = new ScriptedControls();
            var session = CreateSession(controls, withHud: false);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(FlightPhase.Parked, session.PhaseEstimator.Phase);
            Assert.IsTrue(session.PhaseEstimator.OnGround);
            Assert.IsTrue(session.Runway.OnRunway);

            controls.Throttle = 1f;
            yield return new WaitForSeconds(4f);
            Assert.AreEqual(FlightPhase.TakeoffRoll, session.PhaseEstimator.Phase);
            Assert.AreEqual(PhaseTransitionReason.TakeoffPower, session.PhaseEstimator.Current.Reason);
            Assert.AreEqual(0, session.Warnings.ActiveCount, "no warnings on a normal take-off roll");
            Assert.That(session.Telemetry.HeightAboveGroundFeet, Is.InRange(0f, 1f), "radio altitude on the runway");
        }

        [UnityTest]
        public IEnumerator SwitchingHudProfiles_DuringFlight_NeverTouchesTheControls()
        {
            var controls = new ScriptedControls { Throttle = 1f };
            var session = CreateSession(controls, withHud: true);
            yield return new WaitForSeconds(3f);

            foreach (var profile in new[] { HudProfile.Detailed, HudProfile.Cinematic, HudProfile.Smart })
            {
                float speedBefore = session.Telemetry.GroundspeedKnots;
                session.SetHudProfile(profile);
                Assert.AreEqual(profile, session.ActiveHudProfile);
                Assert.AreEqual(1f, session.aircraft.Controls.Throttle, "a profile change never touches the throttle");
                yield return new WaitForSeconds(0.5f);
                Assert.That(session.Telemetry.GroundspeedKnots, Is.GreaterThan(speedBefore), profile + ": still accelerating");
                Assert.AreEqual(1f, session.aircraft.Controls.Throttle);
            }

            // The HUD button cycles profiles the same way.
            session.contextualHud.profileButton.OnPointerClick(new PointerEventData(EventSystem.current));
            Assert.AreEqual(FlightSession.NextProfile(HudProfile.Smart), session.ActiveHudProfile);
            Assert.AreEqual(1f, session.aircraft.Controls.Throttle);
        }

        [UnityTest]
        public IEnumerator SmartHud_ShowsAirspeedOnTheTakeoffRoll()
        {
            var controls = new ScriptedControls();
            var session = CreateSession(controls, withHud: true);
            session.SetHudProfile(HudProfile.Smart);
            yield return new WaitForSeconds(2f);
            var airspeed = session.contextualHud.dataItems[(int)HudElement.Airspeed];
            Assert.AreEqual(0f, airspeed.alpha, 1e-3f, "nothing to read while parked");

            controls.Throttle = 1f;
            yield return new WaitForSeconds(4f);
            Assert.AreEqual(FlightPhase.TakeoffRoll, session.PhaseEstimator.Phase);
            Assert.AreEqual(1f, airspeed.alpha, 1e-3f, "airspeed shown for the take-off roll");
        }

        [UnityTest]
        public IEnumerator ParkingBrake_HoldsAgainstFullPower_AndReleases()
        {
            var session = CreateSession(null, withHud: false); // default router: devices, parking brake
            yield return new WaitForSeconds(1f);
            session.ToggleParkingBrake();
            Assert.IsTrue(session.Systems.ParkingBrakeSet);

            session.Router.AddProvider(new ThrottleUp());
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(1f, session.aircraft.Controls.Throttle, 1e-3f);
            Assert.AreEqual(1f, session.aircraft.Controls.Brake, "parking brake applies full brakes through the router");
            Assert.That(session.Telemetry.GroundspeedKnots, Is.LessThan(1f), "held at full power");

            session.ToggleParkingBrake();
            Assert.IsFalse(session.Systems.ParkingBrakeSet);
            yield return new WaitForSeconds(3f);
            Assert.That(session.Telemetry.GroundspeedKnots, Is.GreaterThan(5f), "rolls once released");
        }

        [UnityTest]
        public IEnumerator ParkingBrake_CannotBeSetInTheAir()
        {
            var session = CreateSession(null, withHud: false);
            yield return new WaitForFixedUpdate();
            // Lift the aircraft clear of the ground; it is airborne for a moment.
            session.aircraft.Body.position += Vector3.up * 30f;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(session.Telemetry.WeightOnWheels);
            session.ToggleParkingBrake();
            Assert.IsFalse(session.Systems.ParkingBrakeSet);
        }

        [UnityTest]
        public IEnumerator ResetFlight_ResetsAwarenessAndReleasesTheParkingBrake()
        {
            var controls = new ScriptedControls { Throttle = 1f };
            var session = CreateSession(controls, withHud: true);
            yield return new WaitForSeconds(4f);
            Assert.AreEqual(FlightPhase.TakeoffRoll, session.PhaseEstimator.Phase);

            session.ResetFlight();
            Assert.AreEqual(FlightPhase.Parked, session.PhaseEstimator.Phase);
            Assert.AreEqual(0, session.PhaseEstimator.TransitionCount);
            Assert.IsFalse(session.Systems.ParkingBrakeSet);
            Assert.AreEqual(0f, session.Telemetry.GroundspeedKnots, 1e-3f, "telemetry refreshed at once");
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(FlightPhase.Parked, session.PhaseEstimator.Phase, "no stale phase after reset");
        }
    }
}
