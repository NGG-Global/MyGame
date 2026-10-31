using UnityEngine;

namespace OpenSkies.FlightState
{
    /// <summary>
    /// One step of pilot commands, independent of any input device. Produced by
    /// an input source (keyboard, touch, test script), optionally modified by a
    /// flight assist, consumed by the aircraft.
    ///
    /// Directions:
    /// Pitch +1 = stick back (nose up), -1 = stick forward (nose down).
    /// Roll  +1 = stick right (roll right), -1 = stick left.
    /// Yaw   +1 = right rudder pedal (nose right; also steers the nosewheel right).
    /// Throttle 0 = idle, 1 = full power. Brake 0 = off, 1 = full wheel brakes.
    /// Flaps 0 = up, 1 = fully extended (the selected position; the flaps travel to it).
    /// Ignition on = the engine may run (switching it on from off starts the engine).
    /// </summary>
    public readonly struct FlightControls
    {
        public readonly float Pitch;
        public readonly float Roll;
        public readonly float Yaw;
        public readonly float Throttle;
        public readonly float Brake;
        public readonly float Flaps;
        public readonly bool Ignition;

        public FlightControls(float pitch, float roll, float yaw, float throttle, float brake = 0f, float flaps = 0f, bool ignition = true)
        {
            Pitch = Clamp(pitch, -1f, 1f);
            Roll = Clamp(roll, -1f, 1f);
            Yaw = Clamp(yaw, -1f, 1f);
            Throttle = Clamp(throttle, 0f, 1f);
            Brake = Clamp(brake, 0f, 1f);
            Flaps = Clamp(flaps, 0f, 1f);
            Ignition = ignition;
        }

        /// <summary>Stick centred, idle, brakes off, flaps up, ignition on.</summary>
        public static FlightControls Neutral => new FlightControls(0f, 0f, 0f, 0f, 0f, 0f, true);

        // NaN from a bad input source must never reach the physics.
        static float Clamp(float value, float min, float max) => float.IsNaN(value) ? 0f : Mathf.Clamp(value, min, max);
    }
}
