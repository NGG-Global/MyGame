using OpenSkies.Awareness;
using OpenSkies.Missions;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Turns a mission's start request into a physical aircraft state from the
    /// aircraft data: speeds, attitude, power and flaps follow the flight model,
    /// so every mode flies the same aircraft with the same physics.
    /// </summary>
    public static class MissionStarts
    {
        /// <summary>
        /// The aircraft state for a start, or false for a runway start (the aircraft's own
        /// reset pose). <paramref name="flapDetent"/> is the selector position to set.
        /// </summary>
        public static bool TryCreate(in MissionStart start, AircraftConfiguration c, in RunwayInfo runway, float aimPointMetres,
            LandingChallengeSettings approach, out AircraftStartState state, out int flapDetent)
        {
            state = default;
            flapDetent = 0;
            if (c == null) return false;
            switch (start.Kind)
            {
                case MissionStartKind.Airborne:
                    state = Airborne(c, start.Position, start.HeadingDegrees);
                    return true;
                case MissionStartKind.FinalApproach:
                    state = FinalApproach(c, start, runway, aimPointMetres, approach, out flapDetent);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Level, clean, at the stick-free trimmed speed with the power for level flight.</summary>
        public static AircraftStartState Airborne(AircraftConfiguration c, Vector3 position, float headingDegrees)
        {
            float altitude = Mathf.Max(position.y, 0f);
            float speed = FlightStartMath.StickFreeSpeed(c, 0f, altitude);
            var flight = FlightStartMath.Solve(c, speed, 0f, 0f, altitude);
            return FlightStartMath.Airborne(position, headingDegrees, flight, 0f);
        }

        /// <summary>
        /// Established on final: on the extended centreline at the start distance, on the
        /// glide path to the aim point, flaps set, at the stick-free speed for those flaps
        /// (kept between the configured multiples of the stall speed), power for the descent.
        /// </summary>
        public static AircraftStartState FinalApproach(AircraftConfiguration c, in MissionStart start, in RunwayInfo runway, float aimPointMetres,
            LandingChallengeSettings settings, out int flapDetent)
        {
            var s = settings ?? new LandingChallengeSettings();
            var detents = c.flapDetentDegrees != null && c.flapDetentDegrees.Length > 0 ? c.flapDetentDegrees : new[] { 0f };
            flapDetent = start.FlapDetent < 0 ? detents.Length - 1 : Mathf.Clamp(start.FlapDetent, 0, detents.Length - 1);
            float flaps = c.MaxFlapDegrees > 0f ? Mathf.Clamp01(detents[flapDetent] / c.MaxFlapDegrees) : 0f;

            float glidepath = start.GlidepathDegrees > 0f ? start.GlidepathDegrees : 3f;
            float distance = Mathf.Max(start.DistanceBeforeThresholdMetres, 200f);
            Vector3 position = NavigationMath.PointOnFinal(runway, start.LandingHeadingDegrees, distance);
            position.y = runway.centre.y + (distance + aimPointMetres) * Mathf.Tan(glidepath * Mathf.Deg2Rad);

            float stall = Aerodynamics.StallSpeed(c, flaps);
            float speed = Mathf.Clamp(FlightStartMath.StickFreeSpeed(c, flaps, position.y), stall * s.minSpeedFactor, stall * s.maxSpeedFactor);
            var flight = FlightStartMath.Solve(c, speed, -glidepath, flaps, position.y);
            return FlightStartMath.Airborne(position, start.LandingHeadingDegrees, flight, flaps);
        }
    }
}
