using OpenSkies.CameraSystems;
using OpenSkies.FlightInput;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.Simulation;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Composition root for one flight. Before each physics step it reads the
    /// control source (by default a <see cref="FlightInputRouter"/> merging
    /// keyboard/gamepad and touch), passes the commands through the optional
    /// flight assist and hands them to the aircraft. Each frame it handles session
    /// commands (reset, camera) and feeds telemetry to the HUD and touch controls.
    /// Layers only meet here.
    /// </summary>
    [DefaultExecutionOrder(-50)] // before AircraftController.FixedUpdate
    public sealed class FlightSession : MonoBehaviour
    {
        public AircraftController aircraft;
        public AircraftTelemetry telemetry;
        public CameraRig cameraRig;
        public HudPresenter hud;
        public TouchFlightControls touchControls;

        IFlightControlSource _controls;
        IFlightAssist _assist = NoFlightAssist.Instance;
        KeyboardGamepadInputProvider _devices;
        FlightInputRouter _router;

        public FlightTelemetry Telemetry => telemetry != null ? telemetry.Current : default;

        /// <summary>Replace the control source, e.g. with touch controls or a test script.</summary>
        public void SetControlSource(IFlightControlSource source) => _controls = source;

        /// <summary>Install an optional flight assist. Null restores direct control.</summary>
        public void SetFlightAssist(IFlightAssist assist) => _assist = assist ?? NoFlightAssist.Instance;

        public FlightInputRouter Router => _router;

        void Awake()
        {
            _devices = new KeyboardGamepadInputProvider();
            _router = new FlightInputRouter();
            _router.AddProvider(_devices);
            if (touchControls != null) _router.AddProvider(touchControls);
            if (_controls == null) _controls = _router;
        }

        void OnEnable()
        {
            if (hud != null)
            {
                hud.CameraToggleRequested += ToggleCamera;
                hud.ResetRequested += ResetFlight;
            }
        }

        void OnDisable()
        {
            if (hud != null)
            {
                hud.CameraToggleRequested -= ToggleCamera;
                hud.ResetRequested -= ResetFlight;
            }
        }

        void FixedUpdate()
        {
            if (aircraft == null) return;
            float dt = Time.fixedDeltaTime;
            var pilot = _controls.Read(dt);
            var commands = _assist.Apply(pilot, Telemetry, dt);
            aircraft.SetControls(commands);
            if (touchControls != null) touchControls.ReportFlightControls(commands);
        }

        void Update()
        {
            if (_devices.ResetPressedThisFrame()) ResetFlight();
            if (_devices.CameraTogglePressedThisFrame()) ToggleCamera();
            if (hud != null) hud.Present(Telemetry);
            if (touchControls != null)
            {
                touchControls.ShowThrottle(aircraft != null ? aircraft.Controls.Throttle : _router.Throttle);
                touchControls.SetOnGround(DisclosurePolicy.ShowBrakeControl(Telemetry));
            }
        }

        /// <summary>Aircraft back to the runway start at rest; throttle to idle; camera re-centred.</summary>
        public void ResetFlight()
        {
            if (aircraft != null) aircraft.ResetToStart();
            _controls.ResetControls();
            if (cameraRig != null) cameraRig.SnapToTarget();
        }

        void ToggleCamera()
        {
            if (cameraRig != null) cameraRig.ToggleMode();
        }
    }
}
