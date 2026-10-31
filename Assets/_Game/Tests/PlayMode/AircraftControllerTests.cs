using System.Collections;
using NUnit.Framework;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    public class AircraftControllerTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TestRoot");
            TestAircraftFactory.CreateGround(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator RestingOnRunway_SettlesOnWheelsWithoutDrifting()
        {
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, Vector3.zero, out var telemetry);
            yield return new WaitForSeconds(3f);

            Assert.IsTrue(aircraft.WeightOnWheels);
            Assert.That(aircraft.Body.linearVelocity.magnitude, Is.LessThan(0.05f), "should be at rest");
            Assert.That(aircraft.transform.position.y, Is.InRange(-0.3f, 0.05f), "sits on its suspension");
            Assert.That(Mathf.Abs(telemetry.Current.BankDegrees), Is.LessThan(1f));
            Assert.That(Mathf.Abs(telemetry.Current.PitchDegrees), Is.LessThan(5f));
        }

        [UnityTest]
        public IEnumerator FullThrottle_AcceleratesAlongRunway()
        {
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, Vector3.zero, out var telemetry);
            yield return new WaitForSeconds(1f);

            for (float t = 0f; t < 5f; t += Time.fixedDeltaTime)
            {
                aircraft.SetControls(new FlightControls(0f, 0f, 0f, 1f));
                yield return new WaitForFixedUpdate();
            }

            Assert.That(telemetry.Current.EnginePower, Is.GreaterThan(0.9f));
            Assert.That(aircraft.Thrust, Is.GreaterThan(0f));
            Assert.That(telemetry.Current.GroundspeedKnots, Is.GreaterThan(10f));
            Assert.That(aircraft.transform.position.z, Is.GreaterThan(5f), "moves towards the nose (+Z)");
            Assert.IsTrue(telemetry.Current.WeightOnWheels);
        }

        [UnityTest]
        public IEnumerator Reset_ClearsMotionAndControlState()
        {
            var start = new Vector3(0f, 0f, -100f);
            var aircraft = TestAircraftFactory.CreateAircraft(_root.transform, start, out _);
            yield return new WaitForFixedUpdate();

            aircraft.SetControls(new FlightControls(0.5f, 0.5f, 0.5f, 1f));
            aircraft.Body.linearVelocity = new Vector3(3f, 10f, 40f);
            aircraft.Body.angularVelocity = new Vector3(0.5f, -0.3f, 0.2f);
            for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
            Assert.That(aircraft.EnginePower, Is.GreaterThan(0f), "precondition: engine running");

            aircraft.ResetToStart();

            Assert.AreEqual(Vector3.zero, aircraft.Body.linearVelocity);
            Assert.AreEqual(Vector3.zero, aircraft.Body.angularVelocity);
            Assert.AreEqual(0f, aircraft.EnginePower);
            Assert.AreEqual(0f, aircraft.Controls.Throttle);
            Assert.AreEqual(0f, aircraft.Controls.Pitch);
            Assert.That(Vector3.Distance(aircraft.transform.position, start), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(aircraft.transform.rotation, Quaternion.identity), Is.LessThan(1e-3f));
        }
    }
}
