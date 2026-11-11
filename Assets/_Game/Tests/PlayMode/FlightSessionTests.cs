using System.Collections;
using NUnit.Framework;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput;
using OpenSkies.FlightState;
using OpenSkies.Gameplay;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    public class FlightSessionTests
    {
        sealed class ScriptedControls : IFlightControlSource
        {
            public FlightControls Controls = new FlightControls(0f, 0f, 0f, 1f);
            public int Resets;
            public FlightControls Read(float deltaTime) => Controls;
            public void ResetControls() => Resets++;
        }

        GameObject _root;
        CommandCluster _cluster;
        static readonly Vector3 StartPosition = new Vector3(0f, 0f, -500f);

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        FlightSession CreateSession(ScriptedControls controls, out CameraRig rig)
        {
            _root = new GameObject("TestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, StartPosition, out var telemetry);

            var cockpit = new GameObject("Cockpit").transform;
            cockpit.SetParent(aircraft.transform, false);
            cockpit.localPosition = new Vector3(0f, 1.7f, 1f);

            var cameraGo = new GameObject("Camera", typeof(Camera));
            cameraGo.transform.SetParent(_root.transform, false);
            rig = cameraGo.AddComponent<CameraRig>();
            rig.target = aircraft.transform;
            rig.cockpitAnchor = cockpit;

            // The top-right command cluster (camera, options with RESET), built before the session wakes.
            var hudGo = new GameObject("HUD", typeof(RectTransform));
            hudGo.transform.SetParent(_root.transform, false);
            _cluster = CommandClusterFactory.Build((RectTransform)hudGo.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

            var sessionGo = new GameObject("FlightSession");
            sessionGo.SetActive(false);
            sessionGo.transform.SetParent(_root.transform, false);
            var session = sessionGo.AddComponent<FlightSession>();
            session.aircraft = aircraft;
            session.telemetry = telemetry;
            session.cameraRig = rig;
            session.commandCluster = _cluster;
            session.SetControlSource(controls);
            sessionGo.SetActive(true);
            return session;
        }

        [UnityTest]
        public IEnumerator SessionDeliversControlsToAircraft()
        {
            var session = CreateSession(new ScriptedControls(), out _);
            yield return new WaitForSeconds(3f);

            Assert.AreEqual(1f, session.Telemetry.Throttle);
            Assert.That(session.Telemetry.GroundspeedKnots, Is.GreaterThan(5f));
        }

        [UnityTest]
        public IEnumerator SwitchingCamera_DoesNotInterruptControl()
        {
            var session = CreateSession(new ScriptedControls(), out var rig);
            yield return new WaitForSeconds(2f);
            float speedBefore = session.Telemetry.GroundspeedKnots;

            rig.ToggleMode();
            Assert.AreEqual(CameraMode.Cockpit, rig.mode);
            yield return new WaitForSeconds(1f);

            Assert.That(session.Telemetry.GroundspeedKnots, Is.GreaterThan(speedBefore), "aircraft must keep accelerating");
            Assert.AreEqual(1f, session.Telemetry.Throttle);
            // Coroutines resume before LateUpdate, so the camera may trail by one frame of
            // movement. The chase view is ~14 m away; the cockpit view is within 1 m.
            Assert.That(Vector3.Distance(rig.transform.position, rig.cockpitAnchor.position), Is.LessThan(1f));
        }

        [UnityTest]
        public IEnumerator ResetFlight_StopsAircraftAndResetsControls()
        {
            var controls = new ScriptedControls();
            var session = CreateSession(controls, out _);
            yield return new WaitForSeconds(2f);

            session.ResetFlight();

            Assert.AreEqual(1, controls.Resets);
            Assert.AreEqual(Vector3.zero, session.aircraft.Body.linearVelocity);
            Assert.That(Vector3.Distance(session.aircraft.transform.position, StartPosition), Is.LessThan(1e-3f));
        }

        [UnityTest]
        public IEnumerator ClusterCameraButton_SwitchesViewWithoutChangingThrottleOrFlight()
        {
            var session = CreateSession(new ScriptedControls(), out var rig);
            yield return new WaitForSeconds(2f);
            float speedBefore = session.Telemetry.GroundspeedKnots;

            _cluster.cameraButton.OnPointerClick(new PointerEventData(EventSystem.current)); // what a tap on the camera does
            Assert.AreEqual(CameraMode.Cockpit, rig.mode);
            Assert.AreEqual(1f, session.aircraft.Controls.Throttle, "camera switch must not touch throttle");

            yield return new WaitForSeconds(1f);
            Assert.That(session.Telemetry.GroundspeedKnots, Is.GreaterThan(speedBefore));

            _cluster.cameraButton.OnPointerClick(new PointerEventData(EventSystem.current));
            Assert.AreEqual(CameraMode.Chase, rig.mode);
            Assert.AreEqual(1f, session.aircraft.Controls.Throttle);
        }

        [UnityTest]
        public IEnumerator ClusterReset_BehindTheOptions_WithItsConfirmation_ResetsTheFlight()
        {
            var controls = new ScriptedControls();
            var session = CreateSession(controls, out _);
            yield return new WaitForSeconds(2f);
            Assert.That(session.aircraft.Body.linearVelocity.magnitude, Is.GreaterThan(1f), "precondition: moving");

            var tap = new PointerEventData(EventSystem.current);
            _cluster.optionsButton.OnPointerClick(tap); // RESET is behind the options trigger
            _cluster.resetButton.OnPointerClick(tap);
            Assert.AreEqual(0, controls.Resets, "the first tap only asks for a confirmation");
            yield return new WaitForSecondsRealtime(0.5f);
            _cluster.resetButton.OnPointerClick(tap); // the deliberate second tap

            Assert.AreEqual(1, controls.Resets);
            Assert.AreEqual(Vector3.zero, session.aircraft.Body.linearVelocity);
            Assert.AreEqual(0f, session.aircraft.EnginePower);
            Assert.That(Vector3.Distance(session.aircraft.transform.position, StartPosition), Is.LessThan(1e-3f));
        }

        [UnityTest]
        public IEnumerator Telemetry_ReportsGroundRollConsistently()
        {
            var session = CreateSession(new ScriptedControls(), out _);
            yield return new WaitForSeconds(3f);

            var t = session.Telemetry;
            Assert.IsTrue(t.WeightOnWheels);
            Assert.IsFalse(t.StallWarning, "no stall warning on the ground");
            Assert.That(t.TrueAirspeedKnots, Is.GreaterThan(5f));
            Assert.AreEqual(t.TrueAirspeedKnots, t.IndicatedAirspeedKnots, t.TrueAirspeedKnots * 0.01f, "IAS ≈ TAS at sea level");
            Assert.AreEqual(t.TrueAirspeedKnots, t.GroundspeedKnots, 0.5f, "no wind, rolling level");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, t.HeadingDegrees), 1f, "runway heading north");
            Assert.That(t.AltitudeFeet, Is.InRange(-1f, 1f));
            Assert.AreEqual(1f, t.Throttle);
            Assert.That(t.EnginePower, Is.GreaterThan(0.9f));

            session.ResetFlight();
            var after = session.telemetry.Sample();
            Assert.AreEqual(0f, after.TrueAirspeedKnots, 1e-3f);
            Assert.AreEqual(0f, after.EnginePower);
            Assert.AreEqual(0f, after.Throttle);
        }
    }
}
