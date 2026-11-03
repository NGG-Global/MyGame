using OpenSkies.Gestures;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// The feel of the gesture language in one asset, so it survives scene rebuilds
    /// and can be tuned on a device without touching code: recognition thresholds,
    /// the arc's geometry, the lever's detents, the HUD density steps, the rotary
    /// adjustments and the map view, the pause sheet and the entry stations. The
    /// scene builder creates it once and never overwrites it; without an asset the
    /// defaults in code are used. The player's own gesture options (speed,
    /// sensitivity) scale these at runtime; they never change the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Open Skies/Interaction Configuration", fileName = "InteractionConfiguration")]
    public sealed class InteractionConfiguration : ScriptableObject
    {
        public GestureSettings gestures = new GestureSettings();
        public SystemsArcSettings arc = new SystemsArcSettings();
        public FlapsLeverSettings lever = new FlapsLeverSettings();
        public HudDensitySettings density = new HudDensitySettings();
        public RotarySettings rotary = new RotarySettings();
        public MapViewSettings mapView = new MapViewSettings();
        public SheetSettings sheet = new SheetSettings();
        public EntryStationSettings entry = new EntryStationSettings();

        /// <summary>
        /// The recognition thresholds with the player's gesture speed applied: a relaxed
        /// player gets longer holds and dwells and shorter pulls; a quick one the opposite.
        /// Returns a new settings object; the asset is untouched.
        /// </summary>
        public GestureSettings TunedGestures(float timeScale, float distanceScale)
        {
            var g = gestures.Sanitized();
            timeScale = Mathf.Clamp(timeScale, 0.5f, 2f);
            distanceScale = Mathf.Clamp(distanceScale, 0.5f, 2f);
            g.longPressSeconds *= timeScale;
            g.dwellSeconds *= timeScale;
            g.pullRevealDistance *= distanceScale;
            g.pullPeekDistance *= distanceScale;
            return g.Sanitized();
        }
    }
}
