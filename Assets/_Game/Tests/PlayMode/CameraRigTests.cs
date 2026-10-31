using System.Collections;
using NUnit.Framework;
using OpenSkies.CameraSystems;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    public class CameraRigTests
    {
        GameObject _root;
        Transform _target;
        Transform _cockpit;
        CameraRig _rig;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("CameraTestRoot");
            _target = new GameObject("Aircraft").transform;
            _target.SetParent(_root.transform, false);
            _target.SetPositionAndRotation(new Vector3(0f, 100f, 0f), Quaternion.Euler(0f, 90f, 0f));
            _cockpit = new GameObject("Cockpit").transform;
            _cockpit.SetParent(_target, false);
            _cockpit.localPosition = new Vector3(-0.3f, 1.9f, 0.9f);

            var cameraGo = new GameObject("Camera", typeof(Camera));
            cameraGo.SetActive(false);
            cameraGo.transform.SetParent(_root.transform, false);
            _rig = cameraGo.AddComponent<CameraRig>();
            _rig.target = _target;
            _rig.cockpitAnchor = _cockpit;
            cameraGo.SetActive(true);
        }

        [TearDown]
        public void TearDown() => Object.Destroy(_root);

        [Test]
        public void StartsInChaseViewBehindAndAboveTheAircraft()
        {
            Assert.AreEqual(CameraMode.Chase, _rig.mode);
            Vector3 p = _rig.transform.position;
            Assert.AreEqual(-_rig.chaseDistance, p.x, 1e-3f, "heading east, so behind is west");
            Assert.AreEqual(100f + _rig.chaseHeight, p.y, 1e-3f);
        }

        [Test]
        public void ToggleAlternatesAndSnapsToEachView()
        {
            _rig.ToggleMode();
            Assert.AreEqual(CameraMode.Cockpit, _rig.mode);
            Assert.That(Vector3.Distance(_rig.transform.position, _cockpit.position), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(_rig.transform.rotation, _cockpit.rotation), Is.LessThan(1e-2f));

            _rig.ToggleMode();
            Assert.AreEqual(CameraMode.Chase, _rig.mode);
            Assert.That(Vector3.Distance(_rig.transform.position, _cockpit.position), Is.GreaterThan(5f));
        }

        [UnityTest]
        public IEnumerator CockpitViewFollowsTheAircraftEveryFrame()
        {
            _rig.SetMode(CameraMode.Cockpit);
            _target.position += new Vector3(50f, 10f, 0f);
            _target.rotation = Quaternion.Euler(-5f, 120f, 20f);
            yield return null; // LateUpdate
            Assert.That(Vector3.Distance(_rig.transform.position, _cockpit.position), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(_rig.transform.rotation, _cockpit.rotation), Is.LessThan(1e-2f));
        }

        [UnityTest]
        public IEnumerator ChaseViewKeepsLevelHorizonWhenAircraftBanks()
        {
            _target.rotation = Quaternion.Euler(0f, 90f, -60f); // steep right bank
            _rig.SnapToTarget();
            yield return null;
            float roll = Mathf.DeltaAngle(0f, _rig.transform.eulerAngles.z); // 359.99° and 0° are the same
            Assert.AreEqual(0f, roll, 0.01f, "camera does not roll with the aircraft");
        }
    }
}
