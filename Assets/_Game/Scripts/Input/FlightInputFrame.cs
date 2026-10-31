namespace OpenSkies.FlightInput
{
    /// <summary>
    /// What all input providers asked for during one physics step, before the
    /// router turns it into <see cref="OpenSkies.FlightState.FlightControls"/>.
    /// Axes add up across providers; brake takes the strongest request; the
    /// throttle is changed by deltas because only the router owns its position.
    /// </summary>
    public struct FlightInputFrame
    {
        public float Pitch;          // + nose up
        public float Roll;           // + roll right
        public float Yaw;            // + right rudder
        public float ThrottleDelta;  // fraction of full travel, + opens
        public float Brake;          // 0-1

        public void RequestBrake(float amount)
        {
            if (amount > Brake) Brake = amount;
        }
    }

    /// <summary>A source of pilot input: keyboard/gamepad, touch controls, a test script.</summary>
    public interface IFlightInputProvider
    {
        /// <summary>Add this provider's input for one physics step.</summary>
        void Contribute(ref FlightInputFrame frame, float deltaTime);

        /// <summary>Drop any transient state (e.g. accumulated slider movement).</summary>
        void ResetInput();
    }
}
