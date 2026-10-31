using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Cockpit
{
    /// <summary>
    /// Exterior lights driven by the authoritative <see cref="AircraftSystems"/>:
    /// the landing light (a spot light and its lens) and the navigation lights
    /// (red left wingtip, green right wingtip, white tail). Placeholder visuals:
    /// unlit spheres, no flares or strobes yet.
    /// </summary>
    public sealed class AircraftLightsView : MonoBehaviour
    {
        public Light landingLight;
        public Renderer[] landingLightLenses = new Renderer[0];
        public Renderer[] navigationLights = new Renderer[0];

        AircraftSystems _systems;
        int _seenVersion = int.MinValue;

        public bool LandingLightShown { get; private set; }
        public bool NavigationLightsShown { get; private set; }

        public void Bind(AircraftSystems systems)
        {
            _systems = systems;
            _seenVersion = int.MinValue;
            Refresh();
        }

        void Update() => Refresh();

        void Refresh()
        {
            if (_systems == null || _systems.Version == _seenVersion) return;
            _seenVersion = _systems.Version;
            LandingLightShown = _systems.LandingLightOn;
            NavigationLightsShown = _systems.NavigationLightsOn;
            if (landingLight != null) landingLight.enabled = LandingLightShown;
            for (int i = 0; i < landingLightLenses.Length; i++)
                if (landingLightLenses[i] != null) landingLightLenses[i].enabled = LandingLightShown;
            for (int i = 0; i < navigationLights.Length; i++)
                if (navigationLights[i] != null) navigationLights[i].enabled = NavigationLightsShown;
        }
    }
}
