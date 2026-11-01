using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>What one telemetry sample is built from, in SI units and Unity axes.</summary>
    public struct TelemetrySource
    {
        /// <summary>World velocity of the aircraft, m/s.</summary>
        public Vector3 Velocity;
        /// <summary>Speed through the air, m/s (equal to the ground speed while no wind is modelled).</summary>
        public float TrueAirspeed;
        public Quaternion Rotation;
        /// <summary>Angular velocity in body axes, rad/s (Unity: +X nose down, +Y nose right, +Z roll left).</summary>
        public Vector3 BodyAngularVelocity;
        public AerodynamicForces Aerodynamics;
        /// <summary>The commands the aircraft is flying with.</summary>
        public FlightControls Controls;
        public float EnginePower;
        public EngineState EngineState;
        /// <summary>Actual flap position, 0-1.</summary>
        public float FlapPosition;
        public bool WeightOnWheels;
        public float AirDensity;
        /// <summary>Height of the aircraft reference point above sea level, m.</summary>
        public float Altitude;
        public float HeightAboveGround;
    }

    /// <summary>
    /// Builds the <see cref="FlightTelemetry"/> snapshot. Pure, so the game's
    /// <see cref="AircraftTelemetry"/> and test harnesses report exactly the same
    /// values from the same state.
    /// </summary>
    public static class TelemetryMath
    {
        const float StallWarningMinAirspeed = 5f; // m/s; no meaningful angle of attack below this

        public static FlightTelemetry Compose(in TelemetrySource s, AircraftConfiguration c)
        {
            float alphaDegrees = s.Aerodynamics.AngleOfAttack * Mathf.Rad2Deg;
            // Flaps lower the critical angle, so the warning moves with them.
            float criticalAngle = c.stallAngle + c.flapStallAngleChange * Mathf.Clamp01(s.FlapPosition);
            bool stallWarning = !s.WeightOnWheels
                                && s.TrueAirspeed > StallWarningMinAirspeed
                                && alphaDegrees >= criticalAngle - c.stallWarningMargin;
            Vector3 velocity = s.Velocity;
            Vector3 w = s.BodyAngularVelocity * Mathf.Rad2Deg;

            return new FlightTelemetry
            {
                IndicatedAirspeedKnots = FlightUnits.MetresPerSecondToKnots(Atmosphere.IndicatedFromTrueAirspeed(s.TrueAirspeed, s.AirDensity)),
                TrueAirspeedKnots = FlightUnits.MetresPerSecondToKnots(s.TrueAirspeed),
                GroundspeedKnots = FlightUnits.MetresPerSecondToKnots(new Vector2(velocity.x, velocity.z).magnitude),
                AltitudeFeet = FlightUnits.MetresToFeet(s.Altitude),
                HeightAboveGroundFeet = FlightUnits.MetresToFeet(s.HeightAboveGround),
                VerticalSpeedFeetPerMinute = FlightUnits.MetresPerSecondToFeetPerMinute(velocity.y),
                HeadingDegrees = AttitudeMath.HeadingDegrees(s.Rotation),
                PitchDegrees = AttitudeMath.PitchDegrees(s.Rotation),
                BankDegrees = AttitudeMath.BankDegrees(s.Rotation),
                AngleOfAttackDegrees = alphaDegrees,
                SideslipDegrees = s.Aerodynamics.Sideslip * Mathf.Rad2Deg,
                // Unity body axes to pilot convention: +X lowers the nose, +Z rolls left.
                PitchRateDegreesPerSecond = -w.x,
                RollRateDegreesPerSecond = -w.z,
                YawRateDegreesPerSecond = w.y,
                Throttle = s.Controls.Throttle,
                EnginePower = s.EnginePower,
                Brake = s.Controls.Brake,
                FlapPosition = s.FlapPosition,
                FlapDegrees = s.FlapPosition * c.MaxFlapDegrees,
                EngineState = s.EngineState,
                WeightOnWheels = s.WeightOnWheels,
                StallWarning = stallWarning,
            };
        }

        /// <summary>
        /// The aircraft's limits for assistance and guidance, from the same data the
        /// aerodynamics use. <paramref name="neverExceedKnots"/> comes from the warning
        /// settings until the aircraft data holds it.
        /// </summary>
        public static AircraftEnvelope Envelope(AircraftConfiguration c, float neverExceedKnots)
        {
            float slope = Mathf.Max(c.liftCurveSlope, 0.1f);
            return new AircraftEnvelope
            {
                CriticalAngleOfAttackClean = c.stallAngle,
                CriticalAngleOfAttackFullFlap = c.stallAngle + c.flapStallAngleChange,
                ZeroLiftAngleOfAttackClean = -c.liftCoefficientAtZeroAlpha / slope * Mathf.Rad2Deg,
                ZeroLiftAngleOfAttackFullFlap = -(c.liftCoefficientAtZeroAlpha + c.flapLiftIncrement) / slope * Mathf.Rad2Deg,
                StallSpeedCleanKnots = FlightUnits.MetresPerSecondToKnots(Aerodynamics.StallSpeed(c, 0f)),
                StallSpeedFullFlapKnots = FlightUnits.MetresPerSecondToKnots(Aerodynamics.StallSpeed(c, 1f)),
                NeverExceedSpeedKnots = neverExceedKnots,
                StallWarningMarginDegrees = c.stallWarningMargin,
            };
        }
    }
}
