namespace OpenSkies.FlightState
{
    /// <summary>
    /// Extension point for optional flight assistance (coordination, attitude
    /// hold, stall protection). An assist only transforms pilot commands; it never
    /// touches the physics. No assists are implemented yet.
    /// </summary>
    public interface IFlightAssist
    {
        FlightControls Apply(in FlightControls pilot, in FlightTelemetry telemetry, float deltaTime);
    }

    /// <summary>Default: the pilot's commands pass through unchanged.</summary>
    public sealed class NoFlightAssist : IFlightAssist
    {
        public static readonly NoFlightAssist Instance = new NoFlightAssist();

        public FlightControls Apply(in FlightControls pilot, in FlightTelemetry telemetry, float deltaTime) => pilot;
    }
}
