using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The development telemetry overlay. The session buttons it used to carry (CAM and RESET)
    /// are part of the top-right command cluster now (<see cref="CommandCluster"/>).
    /// It reads telemetry and never changes the flight. The overlay is development-only
    /// (Editor and development builds) and will never ship as player UI (see docs/GAME_DESIGN.md).
    /// </summary>
    public sealed class HudPresenter : MonoBehaviour
    {
        public Text developerReadout;
        [Tooltip("Show the development overlay. Ignored in release builds, where it is always hidden.")]
        public bool showDeveloperOverlay = true;
        [Tooltip("The readout refreshes at this rate, not every frame.")]
        public float readoutRefreshSeconds = 0.25f;

        readonly TelemetryReadout _readout = new TelemetryReadout();
        float _nextRefresh;

        public bool OverlayVisible => DisclosurePolicy.ShowDevelopmentOverlay(showDeveloperOverlay, Debug.isDebugBuild);

        void OnEnable() => SyncOverlay();

        public void Present(in FlightTelemetry telemetry)
        {
            if (developerReadout == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + readoutRefreshSeconds;
            if (!SyncOverlay()) return;
            if (_readout.Update(telemetry, out var text)) developerReadout.text = text;
        }

        /// <summary>Applies the disclosure policy; returns whether the overlay is shown.</summary>
        bool SyncOverlay()
        {
            bool visible = OverlayVisible;
            if (developerReadout != null && developerReadout.enabled != visible) developerReadout.enabled = visible;
            return visible;
        }
    }
}
