using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.FlightInput
{
    /// <summary>
    /// The player's control sensitivity, applied to stick and rudder input from
    /// every device before any assistance, so it is independent of the assistance
    /// profile. Every curve is odd, smooth and monotonic, and keeps full authority:
    /// no input gives no command and full deflection gives full deflection.
    /// (Touch joystick calibration, its dead zone and curve, is separate and lives
    /// on the joystick.)
    /// </summary>
    public static class ControlResponse
    {
        /// <summary>Cubic share of the Low curve: 0.5·x + 0.5·x³, half the response near the centre.</summary>
        public const float LowExpo = 0.5f;
        /// <summary>Exponent of the High curve: 1 − (1 − x)^k, k times the response near the centre.</summary>
        public const float HighExponent = 1.6f;

        public static float Shape(float input, ControlSensitivity sensitivity)
        {
            float x = float.IsNaN(input) ? 0f : Mathf.Clamp(input, -1f, 1f);
            float a = Mathf.Abs(x);
            switch (sensitivity)
            {
                case ControlSensitivity.Low: a = (1f - LowExpo) * a + LowExpo * a * a * a; break;
                case ControlSensitivity.High: a = 1f - Mathf.Pow(1f - a, HighExponent); break;
                default: return x;
            }
            return x < 0f ? -a : a;
        }

        /// <summary>Pitch, roll and yaw shaped; throttle, brakes, flaps and ignition unchanged.</summary>
        public static FlightControls Apply(in FlightControls pilot, ControlSensitivity sensitivity)
        {
            if (sensitivity == ControlSensitivity.Medium) return pilot;
            return new FlightControls(Shape(pilot.Pitch, sensitivity), Shape(pilot.Roll, sensitivity), Shape(pilot.Yaw, sensitivity),
                pilot.Throttle, pilot.Brake, pilot.Flaps, pilot.Ignition);
        }
    }
}
