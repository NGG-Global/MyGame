using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Assistance
{
    /// <summary>
    /// Assistance tuning in one asset, so it survives scene rebuilds (the scene
    /// builder creates it once and never overwrites it). Without an asset, the
    /// defaults in code are used.
    /// </summary>
    [CreateAssetMenu(menuName = "Open Skies/Flight Assist Configuration", fileName = "FlightAssistConfiguration")]
    public sealed class FlightAssistConfiguration : ScriptableObject
    {
        [Tooltip("Profile on first launch, before the player has chosen one.")]
        public FlightAssistProfile defaultProfile = FlightAssistProfile.Assisted;
        public ControlSensitivity defaultSensitivity = ControlSensitivity.Medium;
        public FlightAssistTuning tuning = new FlightAssistTuning();
    }
}
