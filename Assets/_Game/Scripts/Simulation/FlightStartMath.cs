using UnityEngine;

namespace OpenSkies.Simulation
{
    /// <summary>
    /// Where and how a flight begins: on the ground at rest, or in the air with a
    /// Rigidbody velocity, attitude, power and flap position that match a steady
    /// flight condition. World space, SI units.
    /// </summary>
    public struct AircraftStartState
    {
        /// <summary>Aircraft root (wheel-contact level) position, world, m.</summary>
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>World velocity, m/s.</summary>
        public Vector3 Velocity;
        /// <summary>Throttle lever and settled engine power, 0-1.</summary>
        public float Throttle;
        /// <summary>Actual flap position, 0 (up) to 1 (full), already travelled.</summary>
        public float Flaps;
        public bool Airborne;
    }

    /// <summary>A steady, unaccelerated flight condition of the aircraft.</summary>
    public struct SteadyFlight
    {
        /// <summary>True airspeed, m/s.</summary>
        public float Airspeed;
        /// <summary>Angle of attack, radians.</summary>
        public float AngleOfAttack;
        /// <summary>Throttle that balances drag and the weight component along the path, 0-1.</summary>
        public float Throttle;
        /// <summary>Flight path angle, radians (negative = descending).</summary>
        public float FlightPath;
        /// <summary>False when the condition needs more than full power or an angle of attack near the stall.</summary>
        public bool Feasible;
    }

    /// <summary>
    /// Pure functions that put the aircraft into a believable steady state for a
    /// scenario start (in the air, on final approach), and estimate performance
    /// figures from the aircraft data, so missions never hard-code numbers that
    /// could disagree with the flight model.
    /// </summary>
    public static class FlightStartMath
    {
        const float Gravity = 9.81f;

        /// <summary>
        /// The speed at which the aircraft flies with the stick released at neutral
        /// trim (elevator 0) for a flap position: where the pitch moment balances.
        /// Indicated (sea-level equivalent) speed, m/s.
        /// </summary>
        public static float StickFreeSpeed(AircraftConfiguration c, float flaps, float altitudeMetres = 0f)
        {
            flaps = Mathf.Clamp01(flaps);
            float alpha = StickFreeAngleOfAttack(c, flaps);
            float cl = Aerodynamics.LiftCoefficient(alpha, flaps, c, out _);
            float density = Atmosphere.Density(altitudeMetres);
            return Mathf.Sqrt(2f * c.mass * Gravity / (density * c.wingArea * Mathf.Max(cl, 0.1f)));
        }

        /// <summary>Angle of attack where the pitch moment is zero with the elevator neutral, radians.</summary>
        public static float StickFreeAngleOfAttack(AircraftConfiguration c, float flaps)
        {
            float stability = Mathf.Min(c.pitchStability, -1e-3f); // stable aircraft only
            return -(c.pitchMomentAtZeroAlpha + c.flapPitchMoment * Mathf.Clamp01(flaps)) / stability;
        }

        /// <summary>
        /// Steady flight at a true airspeed on a flight path angle (degrees, negative
        /// descending): lift balances the weight across the path, thrust balances drag
        /// and the weight along it.
        /// </summary>
        public static SteadyFlight Solve(AircraftConfiguration c, float airspeed, float flightPathDegrees, float flaps, float altitudeMetres)
        {
            flaps = Mathf.Clamp01(flaps);
            float gamma = flightPathDegrees * Mathf.Deg2Rad;
            float density = Atmosphere.Density(altitudeMetres);
            airspeed = Mathf.Max(airspeed, 1f);
            float qS = 0.5f * density * airspeed * airspeed * c.wingArea;
            float weight = c.mass * Gravity;

            float slope = Mathf.Max(c.liftCurveSlope, 0.1f);
            float clZero = c.liftCoefficientAtZeroAlpha + c.flapLiftIncrement * flaps;
            float alpha = 0f, throttle = 0f, thrust = 0f;
            // Two passes: the thrust line along the nose adds a little lift at the angle of attack.
            for (int pass = 0; pass < 2; pass++)
            {
                float clNeeded = (weight * Mathf.Cos(gamma) - thrust * Mathf.Sin(alpha)) / qS;
                alpha = (clNeeded - clZero) / slope;
                float cl = Aerodynamics.LiftCoefficient(alpha, flaps, c, out float stallDepth);
                float drag = qS * Aerodynamics.DragCoefficient(cl, alpha, stallDepth, flaps, c);
                thrust = Mathf.Max(0f, (drag + weight * Mathf.Sin(gamma)) / Mathf.Max(Mathf.Cos(alpha), 0.5f));
                throttle = thrust / Mathf.Max(EngineModel.ThrustAt(1f, airspeed, c), 1f);
            }

            float critical = (c.stallAngle + c.flapStallAngleChange * flaps - c.stallWarningMargin) * Mathf.Deg2Rad;
            return new SteadyFlight
            {
                Airspeed = airspeed,
                AngleOfAttack = alpha,
                Throttle = Mathf.Clamp01(throttle),
                FlightPath = gamma,
                Feasible = throttle <= 1f && alpha < critical,
            };
        }

        /// <summary>
        /// An airborne start in a steady condition: the aircraft at <paramref name="position"/>
        /// flying along <paramref name="headingDegrees"/> (0 = +Z, 90 = +X), wings level,
        /// nose at the angle of attack above the flight path, engine settled, flaps already out.
        /// </summary>
        public static AircraftStartState Airborne(Vector3 position, float headingDegrees, in SteadyFlight flight, float flaps)
        {
            float pitchUp = (flight.AngleOfAttack + flight.FlightPath) * Mathf.Rad2Deg;
            // Unity: yaw about +Y, then pitch about the local +X (positive pitches the nose down).
            var rotation = Quaternion.Euler(-pitchUp, headingDegrees, 0f);
            float heading = headingDegrees * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(heading) * Mathf.Cos(flight.FlightPath), Mathf.Sin(flight.FlightPath),
                Mathf.Cos(heading) * Mathf.Cos(flight.FlightPath));
            return new AircraftStartState
            {
                Position = position,
                Rotation = rotation,
                Velocity = direction * flight.Airspeed,
                Throttle = flight.Throttle,
                Flaps = Mathf.Clamp01(flaps),
                Airborne = true,
            };
        }

        /// <summary>
        /// Ground run to reach <paramref name="liftOffSpeed"/> (m/s) at full power on a
        /// level, dry runway at sea level, from the thrust, drag and rolling resistance in
        /// the aircraft data (wing at its ground angle, no wind). An estimate for
        /// guidance, not a certified take-off distance; the offline validation of the
        /// default trainer gave about 340 m (docs/FLIGHT_MODEL.md).
        /// </summary>
        public static float TakeoffGroundRunMetres(AircraftConfiguration c, float liftOffSpeed, float groundAngleOfAttackDegrees = 0f)
        {
            const float dt = 0.05f;
            float alpha = groundAngleOfAttackDegrees * Mathf.Deg2Rad;
            float density = Atmosphere.SeaLevelDensity;
            float weight = c.mass * Gravity;
            float v = 0f, distance = 0f;
            for (int i = 0; i < 4000 && v < liftOffSpeed; i++) // 200 s cap: an aircraft that cannot accelerate
            {
                float qS = 0.5f * density * v * v * c.wingArea;
                float cl = Aerodynamics.LiftCoefficient(alpha, 0f, c, out float stallDepth);
                float lift = qS * cl;
                float drag = qS * Aerodynamics.DragCoefficient(cl, alpha, stallDepth, 0f, c);
                float rolling = c.rollingResistance * Mathf.Max(0f, weight - lift);
                float thrust = EngineModel.ThrustAt(1f, v, c);
                float acceleration = (thrust - drag - rolling) / c.mass;
                if (acceleration <= 0f) return float.PositiveInfinity;
                v += acceleration * dt;
                distance += v * dt;
            }
            return v >= liftOffSpeed ? distance : float.PositiveInfinity;
        }
    }
}
