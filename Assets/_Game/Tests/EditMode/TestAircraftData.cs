using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>The prototype trainer's configuration for tests (default values).</summary>
    static class TestAircraftData
    {
        public static AircraftConfiguration Create() => ScriptableObject.CreateInstance<AircraftConfiguration>();

        public static void Destroy(AircraftConfiguration configuration)
        {
            if (configuration != null) Object.DestroyImmediate(configuration);
        }
    }
}
