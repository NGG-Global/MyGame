using UnityEngine;

namespace OpenSkies.Awareness
{
    /// <summary>
    /// Tuning for phase estimation, warnings and HUD disclosure in one asset, so
    /// it survives scene rebuilds (the scene builder creates it once and never
    /// overwrites it). Without an asset, the defaults in code are used.
    /// </summary>
    [CreateAssetMenu(menuName = "Open Skies/Flight Awareness Configuration", fileName = "FlightAwarenessConfiguration")]
    public sealed class FlightAwarenessConfiguration : ScriptableObject
    {
        public FlightPhaseSettings phase = new FlightPhaseSettings();
        public FlightWarningSettings warnings = new FlightWarningSettings();
        public DisclosureSettings disclosure = new DisclosureSettings();
    }
}
