using System;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Minimal flight HUD: a camera button and an optional development overlay.
    /// It reads telemetry and raises requests; it never changes the flight.
    /// The overlay is development-only (Editor and development builds) and will
    /// never ship as player UI (see docs/GAME_DESIGN.md).
    /// </summary>
    public sealed class HudPresenter : MonoBehaviour
    {
        public Text developerReadout;
        public Button cameraButton;
        public Button resetButton;
        [Tooltip("Show the development overlay. Ignored in release builds, where it is always hidden.")]
        public bool showDeveloperOverlay = true;
        [Tooltip("The readout refreshes at this rate, not every frame.")]
        public float readoutRefreshSeconds = 0.25f;

        public event Action CameraToggleRequested;
        public event Action ResetRequested;

        readonly TelemetryReadout _readout = new TelemetryReadout();
        float _nextRefresh;

        public bool OverlayVisible => DisclosurePolicy.ShowDevelopmentOverlay(showDeveloperOverlay, Debug.isDebugBuild);

        void OnEnable()
        {
            if (cameraButton != null) cameraButton.onClick.AddListener(RaiseCameraToggle);
            if (resetButton != null) resetButton.onClick.AddListener(RaiseReset);
            SyncOverlay();
        }

        void OnDisable()
        {
            if (cameraButton != null) cameraButton.onClick.RemoveListener(RaiseCameraToggle);
            if (resetButton != null) resetButton.onClick.RemoveListener(RaiseReset);
        }

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

        void RaiseCameraToggle() => CameraToggleRequested?.Invoke();
        void RaiseReset() => ResetRequested?.Invoke();
    }
}
