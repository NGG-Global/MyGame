namespace OpenSkies.FlightState
{
    /// <summary>
    /// How much the flight assistance layer helps the pilot. Values are stable
    /// (they are saved in the player's preferences).
    /// </summary>
    public enum FlightAssistProfile
    {
        /// <summary>Strong damping, bank and trim hold, coordination, gentle stall prevention, guidance.</summary>
        Assisted = 0,
        /// <summary>Light damping, coordination, optional automatic trim. No auto-levelling.</summary>
        Standard = 1,
        /// <summary>Direct control: the pilot's commands reach the aircraft unchanged (apart from manual trim).</summary>
        Manual = 2,
    }

    /// <summary>Response of the pilot's stick and rudder, independent of the assistance profile. Values are stable.</summary>
    public enum ControlSensitivity
    {
        /// <summary>Softer around the centre for fine control; full deflection still gives full authority.</summary>
        Low = 0,
        /// <summary>Linear: the command equals the input.</summary>
        Medium = 1,
        /// <summary>Quicker: full authority is reached before full deflection.</summary>
        High = 2,
    }

    public static class FlightAssistProfiles
    {
        public const int Count = 3;

        public static string Label(FlightAssistProfile profile)
        {
            switch (profile)
            {
                case FlightAssistProfile.Assisted: return "ASSISTED";
                case FlightAssistProfile.Standard: return "STANDARD";
                default: return "MANUAL";
            }
        }

        /// <summary>One line for the selector: what the profile does.</summary>
        public static string Description(FlightAssistProfile profile)
        {
            switch (profile)
            {
                case FlightAssistProfile.Assisted:
                    return "Steadier: holds bank and trim when you let go, keeps turns coordinated, eases off before a stall, shows tips.";
                case FlightAssistProfile.Standard:
                    return "Responsive: light damping and turn coordination, automatic trim if you want it. No auto-levelling.";
                default:
                    return "Direct control: your inputs reach the aircraft unchanged. Rudder and trim are yours. Warnings stay on.";
            }
        }

        /// <summary>Assisted, then Standard, then Manual.</summary>
        public static FlightAssistProfile Next(FlightAssistProfile profile) =>
            profile == FlightAssistProfile.Assisted ? FlightAssistProfile.Standard
            : profile == FlightAssistProfile.Standard ? FlightAssistProfile.Manual : FlightAssistProfile.Assisted;

        public static bool IsValid(int value) => value >= 0 && value < Count;

        public static string SensitivityLabel(ControlSensitivity sensitivity)
        {
            switch (sensitivity)
            {
                case ControlSensitivity.Low: return "LOW";
                case ControlSensitivity.High: return "HIGH";
                default: return "MEDIUM";
            }
        }
    }

    /// <summary>
    /// The aircraft's aerodynamic limits that assistance and guidance need, taken
    /// from the same aircraft data the simulation uses (so they can never
    /// disagree). Angles of attack in degrees, speeds in indicated knots at sea
    /// level, 1 g. Values with flaps interpolate between clean and full flap.
    /// </summary>
    public struct AircraftEnvelope
    {
        public float CriticalAngleOfAttackClean;
        public float CriticalAngleOfAttackFullFlap;
        /// <summary>Angle of attack at which the wing gives no lift (negative for a cambered wing).</summary>
        public float ZeroLiftAngleOfAttackClean;
        public float ZeroLiftAngleOfAttackFullFlap;
        public float StallSpeedCleanKnots;
        public float StallSpeedFullFlapKnots;
        public float NeverExceedSpeedKnots;
        /// <summary>The stall warning comes on this many degrees before the critical angle.</summary>
        public float StallWarningMarginDegrees;

        public bool IsDefined => CriticalAngleOfAttackClean > 0f;

        /// <summary>Critical angle of attack at a flap position (0 up, 1 full).</summary>
        public float CriticalAngleOfAttack(float flaps) =>
            Lerp(CriticalAngleOfAttackClean, CriticalAngleOfAttackFullFlap, flaps);

        public float ZeroLiftAngleOfAttack(float flaps) =>
            Lerp(ZeroLiftAngleOfAttackClean, ZeroLiftAngleOfAttackFullFlap, flaps);

        /// <summary>1 g stall speed (indicated) at a flap position; approximate between the two ends.</summary>
        public float StallSpeedKnots(float flaps) => Lerp(StallSpeedCleanKnots, StallSpeedFullFlapKnots, flaps);

        /// <summary>
        /// Values for the prototype trainer's default configuration, for tests and as a
        /// fallback. The session builds the real envelope from the aircraft data.
        /// </summary>
        public static AircraftEnvelope PrototypeTrainer => new AircraftEnvelope
        {
            CriticalAngleOfAttackClean = 15f, CriticalAngleOfAttackFullFlap = 13f,
            ZeroLiftAngleOfAttackClean = -3.5f, ZeroLiftAngleOfAttackFullFlap = -9.3f,
            StallSpeedCleanKnots = 51f, StallSpeedFullFlapKnots = 46f, NeverExceedSpeedKnots = 160f, StallWarningMarginDegrees = 3f,
        };

        static float Lerp(float a, float b, float t)
        {
            t = float.IsNaN(t) ? 0f : (t < 0f ? 0f : (t > 1f ? 1f : t));
            return a + (b - a) * t;
        }
    }
}
