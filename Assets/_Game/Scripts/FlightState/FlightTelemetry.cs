namespace OpenSkies.FlightState
{
    /// <summary>
    /// Snapshot of the aircraft in pilot units, sampled once per physics step.
    /// It is a value type: readers get a copy and cannot affect the simulation.
    /// </summary>
    public struct FlightTelemetry
    {
        /// <summary>Approximate indicated airspeed: true airspeed scaled by √(ρ/ρ₀) (ISA, no instrument errors).</summary>
        public float IndicatedAirspeedKnots;
        /// <summary>Speed through the air. No wind is modelled yet.</summary>
        public float TrueAirspeedKnots;
        /// <summary>Horizontal speed over the ground.</summary>
        public float GroundspeedKnots;
        /// <summary>Height of the aircraft reference point (wheel contact level) above sea level. The prototype ground is at sea level.</summary>
        public float AltitudeFeet;
        /// <summary>
        /// Height of the aircraft reference point above the ground directly below
        /// (radio-altimeter style, never negative). Beyond the radio-altimeter
        /// range, or with no ground below, it falls back to height above sea level.
        /// </summary>
        public float HeightAboveGroundFeet;
        public float VerticalSpeedFeetPerMinute;
        /// <summary>Yaw angle as a compass heading, 0-360. True heading: magnetic variation is not modelled.</summary>
        public float HeadingDegrees;
        /// <summary>Nose up positive.</summary>
        public float PitchDegrees;
        /// <summary>Roll angle, right wing down positive.</summary>
        public float BankDegrees;
        /// <summary>Angle between the airflow and the aircraft's longitudinal axis, nose above flight path positive.</summary>
        public float AngleOfAttackDegrees;
        /// <summary>Airflow from the right positive.</summary>
        public float SideslipDegrees;
        /// <summary>Body-axis rotation rate about the wing axis, nose up positive (rate gyro).</summary>
        public float PitchRateDegreesPerSecond;
        /// <summary>Body-axis rotation rate about the longitudinal axis, right wing down positive.</summary>
        public float RollRateDegreesPerSecond;
        /// <summary>Body-axis rotation rate about the vertical axis, nose right positive.</summary>
        public float YawRateDegreesPerSecond;
        /// <summary>Throttle lever position, 0-1.</summary>
        public float Throttle;
        /// <summary>Engine output, 0-1. Lags the throttle.</summary>
        public float EnginePower;
        public float Brake;
        /// <summary>Actual flap position, 0 (up) to 1 (fully extended). Lags the selector.</summary>
        public float FlapPosition;
        public float FlapDegrees;
        public EngineState EngineState;
        public bool WeightOnWheels;
        public bool StallWarning;
    }
}
