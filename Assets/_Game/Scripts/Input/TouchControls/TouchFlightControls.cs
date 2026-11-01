using System.Text;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// Touch flight input: floating joystick (pitch/roll, left thumb), throttle
    /// slider and hold-to-brake button (right thumb). Implements
    /// <see cref="IFlightInputProvider"/>, so the aircraft never knows input came
    /// from touch.
    ///
    /// Robustness: every pointer is owned by exactly one control from press to
    /// release (<see cref="PointerOwnership"/>). Pointers whose finger or button
    /// is no longer down on the device (lost release events) are revoked each
    /// frame, and focus loss or pause revokes everything, so controls never
    /// stick. The throttle is never touched by revocation.
    /// </summary>
    public sealed class TouchFlightControls : MonoBehaviour, IFlightInputProvider
    {
        public FloatingJoystick joystick;
        public ThrottleSlider throttle;
        public HoldButton brake;
        public CanvasGroup controlsGroup;

        public TouchControlsVisibility visibility = TouchControlsVisibility.Auto;
        [Tooltip("Overall opacity of the touch controls.")]
        [Range(0.2f, 1f)] public float opacity = 0.9f;
        [Tooltip("The player's HUD opacity applied to the controls, never below this share (they must stay easy to find).")]
        [Range(0.5f, 1f)] public float minimumOpacityShare = 0.8f;

        [Header("Development")]
        public Text diagnostics;
        [Tooltip("Pointer owners and flight input values. Editor and development builds only.")]
        public bool showDiagnostics = true;
        public float diagnosticsRefreshSeconds = 0.1f;

        readonly PointerOwnership _ownership = new PointerOwnership();
        readonly StringBuilder _text = new StringBuilder(256);
        Vector2 _smoothedStick;
        FlightControls _lastControls;
        float _nextDiagnostics;
        float _opacityScale = 1f;
        bool _visible = true;

        public PointerOwnership Ownership => _ownership;
        public bool ControlsVisible => _visible;
        /// <summary>Opacity actually applied: the base opacity scaled by the player's HUD opacity (with a floor).</summary>
        public float EffectiveOpacity => opacity * _opacityScale;

        /// <summary>
        /// The player's HUD opacity (0.6-1). The controls follow it only part of the way,
        /// so they never become hard to find; touch behaviour is unchanged.
        /// </summary>
        public void SetOpacityScale(float hudOpacity)
        {
            float share = Mathf.Clamp(minimumOpacityShare, 0.5f, 1f);
            float t = Mathf.InverseLerp(0.6f, 1f, Mathf.Clamp01(hudOpacity));
            _opacityScale = Mathf.Lerp(share, 1f, t);
        }

        void Awake()
        {
            if (joystick != null) joystick.Initialize(_ownership);
            if (throttle != null) throttle.Initialize(_ownership);
            if (brake != null) brake.Initialize(_ownership);
            ApplyVisibility(force: true);
        }

        void Update()
        {
            ApplyVisibility(force: false);
            if (controlsGroup != null) controlsGroup.alpha = _visible ? EffectiveOpacity : 0f;
        }

        void LateUpdate()
        {
            RevokeLostPointers();
            UpdateDiagnostics();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) _ownership.RevokeAll();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) _ownership.RevokeAll();
        }

        void OnDisable()
        {
            _ownership.RevokeAll();
            _smoothedStick = Vector2.zero;
        }

        // ---- IFlightInputProvider -------------------------------------------------

        public void Contribute(ref FlightInputFrame frame, float deltaTime)
        {
            if (!_visible || joystick == null) return;

            Vector2 target = JoystickMath.Shape(joystick.Deflection, joystick.settings);
            _smoothedStick = JoystickMath.Smooth(_smoothedStick, target, joystick.settings.smoothingTime, deltaTime);
            JoystickMath.ToFlightAxes(_smoothedStick, out float pitch, out float roll);
            frame.Pitch += pitch;
            frame.Roll += roll;

            if (throttle != null) frame.ThrottleDelta += throttle.ConsumeDelta();
            if (brake != null && brake.IsHeld) frame.RequestBrake(1f);
        }

        public void ResetInput()
        {
            if (throttle != null) throttle.ClearPending();
            _smoothedStick = Vector2.zero;
        }

        // ---- Fed by the session ---------------------------------------------------

        /// <summary>Show the router's throttle on the slider (the router owns the value).</summary>
        public void ShowThrottle(float value)
        {
            if (throttle != null) throttle.ShowThrottle(value);
        }

        /// <summary>Brakes are offered only with weight on wheels.</summary>
        public void SetOnGround(bool onGround)
        {
            if (brake != null) brake.SetAvailable(onGround);
        }

        /// <summary>The merged commands actually sent to the aircraft, for diagnostics.</summary>
        public void ReportFlightControls(in FlightControls controls) => _lastControls = controls;

        // ---- Internals -------------------------------------------------------------

        void ApplyVisibility(bool force)
        {
            bool visible = DisclosurePolicy.ShowTouchControls(visibility, Application.isMobilePlatform, Application.isEditor, Touchscreen.current != null);
            if (!force && visible == _visible) return;
            _visible = visible;
            if (!visible) _ownership.RevokeAll();
            if (controlsGroup != null)
            {
                controlsGroup.blocksRaycasts = visible;
                controlsGroup.interactable = visible;
                controlsGroup.alpha = visible ? EffectiveOpacity : 0f;
            }
        }

        /// <summary>Safety net for release events that never arrive (focus changes, OS gestures, device removal).</summary>
        void RevokeLostPointers()
        {
            for (int i = _ownership.Count - 1; i >= 0; i--)
            {
                if (i >= _ownership.Count) continue; // a revocation can shrink the table
                bool alive;
                switch (_ownership.KindAt(i))
                {
                    case PointerKind.Touch: alive = IsTouchDown(_ownership.TouchIdAt(i)); break;
                    case PointerKind.MouseOrPen: alive = IsMouseOrPenDown(); break;
                    default: alive = true; break; // unknown source: trust the UI events
                }
                if (!alive) _ownership.Revoke(_ownership.PointerIdAt(i));
            }
        }

        static bool IsTouchDown(int touchId)
        {
            var screen = Touchscreen.current;
            if (screen == null) return false;
            var touches = screen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (touch.isInProgress && touch.touchId.ReadValue() == touchId) return true;
            }
            return false;
        }

        static bool IsMouseOrPenDown()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) return true;
            var pen = Pen.current;
            if (pen != null && pen.tip.isPressed) return true;
            return mouse == null && pen == null; // no device to check against
        }

        void UpdateDiagnostics()
        {
            if (diagnostics == null) return;
            bool show = DisclosurePolicy.ShowDevelopmentOverlay(showDiagnostics, Debug.isDebugBuild);
            if (diagnostics.enabled != show) diagnostics.enabled = show;
            if (!show || Time.unscaledTime < _nextDiagnostics) return;
            _nextDiagnostics = Time.unscaledTime + diagnosticsRefreshSeconds;

            // Development only, refreshed at 10 Hz: small allocations are acceptable here.
            _text.Clear().Append("POINTERS ");
            if (_ownership.Count == 0) _text.Append("none");
            for (int i = 0; i < _ownership.Count; i++)
            {
                if (i > 0) _text.Append("   ");
                _text.Append('#').Append(_ownership.PointerIdAt(i));
                switch (_ownership.KindAt(i))
                {
                    case PointerKind.Touch: _text.Append(" touch ").Append(_ownership.TouchIdAt(i)); break;
                    case PointerKind.MouseOrPen: _text.Append(" mouse/pen"); break;
                }
                _text.Append(" → ").Append(_ownership.OwnerAt(i).OwnerName);
            }
            _text.Append("\nINPUT  pitch ").Append(_lastControls.Pitch.ToString("+0.00;-0.00"))
                .Append("  roll ").Append(_lastControls.Roll.ToString("+0.00;-0.00"))
                .Append("  yaw ").Append(_lastControls.Yaw.ToString("+0.00;-0.00"))
                .Append("  throttle ").Append(Mathf.RoundToInt(_lastControls.Throttle * 100f)).Append('%')
                .Append("  brake ").Append(_lastControls.Brake > 0f ? "ON" : "off");
            diagnostics.text = _text.ToString();
        }
    }
}
