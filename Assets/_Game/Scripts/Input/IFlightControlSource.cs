using OpenSkies.FlightState;

namespace OpenSkies.FlightInput
{
    /// <summary>
    /// Any producer of pilot commands: keyboard, gamepad, touch controls, a test
    /// script. The aircraft only ever sees <see cref="FlightControls"/>, so a new
    /// source never requires changes to the physics.
    /// </summary>
    public interface IFlightControlSource
    {
        /// <summary>Read commands for one physics step.</summary>
        FlightControls Read(float deltaTime);

        /// <summary>Return stateful controls (e.g. the throttle lever) to their start positions.</summary>
        void ResetControls();
    }
}
