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
    ///   P              parking brake (set on the ground / release)
    ///   H              next HUD profile (Cinematic, Smart, Detailed)
    ///   F3             development HUD inspector
    ///   [ / ]          flaps up / down one detent
    ///   L / N          landing light / navigation lights
    ///   V              cockpit view: look forward again
    ///   - / =          cockpit view: wider / narrower field of view (hold)
    ///   M              next flight assistance profile (Assisted, Standard, Manual)
    ///   Home / End     pitch trim nose down / nose up (hold)
    /// Gamepad: left stick pitch/roll (up = nose down), right stick X rudder,
    /// right/left trigger throttle up/down, south button (A/Cross) brakes,
    /// north button (Y/Triangle) camera, select/back reset, d-pad up/down pitch
    /// trim nose down/up.
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

        /// <summary>Session command: toggle the parking brake.</summary>
        public bool ParkingBrakeTogglePressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.pKey.wasPressedThisFrame;
        }

        /// <summary>Presentation command: next HUD profile.</summary>
        public bool HudProfileCyclePressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.hKey.wasPressedThisFrame;
        }

        /// <summary>Development command: show or hide the HUD inspector.</summary>
        public bool InspectorTogglePressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.f3Key.wasPressedThisFrame;
        }

        /// <summary>Session command: -1 flaps up one detent, +1 down one detent, 0 none.</summary>
        public int FlapStepPressedThisFrame()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return 0;
            return (keyboard.rightBracketKey.wasPressedThisFrame ? 1 : 0) - (keyboard.leftBracketKey.wasPressedThisFrame ? 1 : 0);
        }

        public bool LandingLightTogglePressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.lKey.wasPressedThisFrame;
        }

        public bool NavigationLightsTogglePressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.nKey.wasPressedThisFrame;
        }

        /// <summary>Presentation command: cockpit view looks forward again.</summary>
        /// <summary>Session command: next assistance profile.</summary>
        public bool AssistProfileCyclePressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.mKey.wasPressedThisFrame;
        }

        /// <summary>Pitch trim input while held: −1 nose down, +1 nose up (Home/End, gamepad d-pad down/up = nose up/down).</summary>
        public float PitchTrimAxis()
        {
            float axis = 0f;
            var keyboard = Keyboard.current;
            if (keyboard != null) axis += Axis(keyboard.endKey.isPressed, keyboard.homeKey.isPressed);
            var gamepad = Gamepad.current;
            if (gamepad != null) axis += Axis(gamepad.dpad.down.isPressed, gamepad.dpad.up.isPressed);
            return Mathf.Clamp(axis, -1f, 1f);
        }

        public bool RecentreViewPressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.vKey.wasPressedThisFrame;
        }

        /// <summary>Presentation command while held: +1 widens the cockpit field of view, -1 narrows it.</summary>
        public float FieldOfViewAxis()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return 0f;
            return Axis(keyboard.minusKey.isPressed, keyboard.equalsKey.isPressed);
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
