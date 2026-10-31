using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenSkies.FlightInput
{
    /// <summary>
    /// Keyboard and gamepad flight input for the Editor, WebGL playtests and a
    /// paired controller. Works alongside touch controls through the router.
    ///
    /// Keyboard:
    ///   W / S          stick forward (nose down) / stick back (nose up)
    ///   A / D          roll left / roll right
    ///   Q / E          left / right rudder (also nosewheel steering)
    ///   Left Shift     throttle up (hold)
    ///   Left Ctrl      throttle down (hold)
    ///   Space          wheel brakes (hold)
    ///   R              reset to runway start
    ///   C              switch camera
    /// Gamepad: left stick pitch/roll (up = nose down), right stick X rudder,
    /// right/left trigger throttle up/down, south button (A/Cross) brakes,
    /// north button (Y/Triangle) camera, select/back reset.
    /// </summary>
    public sealed class KeyboardGamepadInputProvider : IFlightInputProvider
    {
        /// <summary>Throttle travel per second while a throttle key or trigger is held fully.</summary>
        public float ThrottleTravelPerSecond { get; set; } = 0.5f;

        public void Contribute(ref FlightInputFrame frame, float deltaTime)
        {
            float throttleDirection = 0f;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                frame.Pitch += Axis(keyboard.sKey.isPressed, keyboard.wKey.isPressed);
                frame.Roll += Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed);
                frame.Yaw += Axis(keyboard.eKey.isPressed, keyboard.qKey.isPressed);
                throttleDirection += Axis(keyboard.leftShiftKey.isPressed, keyboard.leftCtrlKey.isPressed);
                if (keyboard.spaceKey.isPressed) frame.RequestBrake(1f);
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 left = gamepad.leftStick.ReadValue();
                frame.Pitch -= left.y;
                frame.Roll += left.x;
                frame.Yaw += gamepad.rightStick.ReadValue().x;
                throttleDirection += gamepad.rightTrigger.ReadValue() - gamepad.leftTrigger.ReadValue();
                if (gamepad.buttonSouth.isPressed) frame.RequestBrake(1f);
            }

            if (deltaTime > 0f)
                frame.ThrottleDelta += Mathf.Clamp(throttleDirection, -1f, 1f) * ThrottleTravelPerSecond * deltaTime;
        }

        public void ResetInput() { }

        /// <summary>Session command, kept apart from flight input on purpose.</summary>
        public bool ResetPressedThisFrame()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) return true;
            var gamepad = Gamepad.current;
            return gamepad != null && gamepad.selectButton.wasPressedThisFrame;
        }

        /// <summary>Presentation command, kept apart from flight input on purpose.</summary>
        public bool CameraTogglePressedThisFrame()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame) return true;
            var gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonNorth.wasPressedThisFrame;
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
