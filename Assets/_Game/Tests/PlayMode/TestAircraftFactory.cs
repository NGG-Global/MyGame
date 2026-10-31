using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>Builds a minimal physics aircraft and ground for PlayMode tests.</summary>
    static class TestAircraftFactory
    {
        public const int AircraftLayer = 2; // Ignore Raycast, as in the prototype scene

        public static GameObject CreateGround(Transform parent)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestGround";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(200f, 1f, 2000f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f); // top surface at y = 0
            return ground;
        }

        /// <summary>Configured while inactive so Awake sees the configuration.</summary>
        public static AircraftController CreateAircraft(Transform parent, Vector3 position, out AircraftTelemetry telemetry)
        {
            var go = new GameObject("TestAircraft");
            go.SetActive(false);
            go.layer = AircraftLayer;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.identity);

            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.35f, 0.5f);
            box.size = new Vector3(1.2f, 1.3f, 4.8f);

            var controller = go.AddComponent<AircraftController>();
            controller.configuration = ScriptableObject.CreateInstance<AircraftConfiguration>();
            controller.drawForceGizmos = false;
            telemetry = go.AddComponent<AircraftTelemetry>();
            go.SetActive(true);
            return controller;
        }
    }
}
