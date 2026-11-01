using System;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Minimal flight HUD: the camera and RESET buttons and an optional development overlay.
    /// The camera button names the current view (CHASE or COCKPIT).
    /// It reads telemetry and raises requests; it never changes the flight.
    /// The overlay is development-only (Editor and development builds) and will
    /// never ship as player UI (see docs/GAME_DESIGN.md).
    /// </summary>
    public sealed class HudPresenter : MonoBehaviour
    {
        public Text developerReadout;
        public Button cameraButton;
        public Button resetButton;
        [Tooltip("Caption of the camera button; shows the current view.")]
        public Text cameraLabel;
        [Tooltip("Opacity groups of the session buttons (the player's HUD opacity).")]
        public CanvasGroup[] opacityGroups = new CanvasGroup[0];
        [Tooltip("Show the development overlay. Ignored in release builds, where it is always hidden.")]
        public bool showDeveloperOverlay = true;
        [Tooltip("The readout refreshes at this rate, not every frame.")]
        public float readoutRefreshSeconds = 0.25f;

        public event Action CameraToggleRequested;
        public event Action ResetRequested;

        const string ChaseLabel = "CAM\nCHASE";
        const string CockpitLabel = "CAM\nCOCKPIT";

        readonly TelemetryReadout _readout = new TelemetryReadout();
        float _nextRefresh;
        int _shownView = -1;

        public bool OverlayVisible => DisclosurePolicy.ShowDevelopmentOverlay(showDeveloperOverlay, Debug.isDebugBuild);
        public string CameraCaption => cameraLabel != null ? cameraLabel.text : null;

        void Awake()
        {
            // Runtime-generated sprites cannot be stored in a scene: the buttons get their shape here.
            StyleButton(cameraButton);
            StyleButton(resetButton);
        }

        static void StyleButton(Button button)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            if (image == null || image.sprite != null) return;
            HudStyle.Round(image, HudStyle.ControlRadius);
            image.color = Color.white; // tinted by the button's colour block
            var colours = button.colors;
            colours.normalColor = HudStyle.ControlSurface;
            colours.highlightedColor = HudStyle.ControlSurface;
            colours.selectedColor = HudStyle.ControlSurface;
            colours.pressedColor = HudStyle.ControlPressed;
            colours.fadeDuration = 0f; // feedback at once
            button.colors = colours;
        }

        /// <summary>Names the current view on the camera button.</summary>
        public void ShowView(bool cockpit)
        {
            int view = cockpit ? 1 : 0;
            if (view == _shownView || cameraLabel == null) return;
            _shownView = view;
            cameraLabel.text = cockpit ? CockpitLabel : ChaseLabel;
        }

        /// <summary>The player's HUD opacity for the session buttons.</summary>
        public void SetOpacity(float opacity)
        {
            for (int i = 0; i < opacityGroups.Length; i++)
                if (opacityGroups[i] != null) opacityGroups[i].alpha = Mathf.Clamp01(opacity);
        }

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
