using UnityEngine;

namespace OpenSkies.Assistance
{
    /// <summary>Pure functions behind the assistance controller. Angles in degrees, stick values −1..1.</summary>
    public static class AssistMath
    {
        /// <summary>
        /// How much stabilisation remains on an axis for a stick deflection: 1 near
        /// the centre, fading smoothly to 0 by <paramref name="end"/>, so a strong,
        /// deliberate input is never fought.
        /// </summary>
        public static float OverrideWeight(float stick, float start, float end)
        {
            float a = Mathf.Abs(Finite(stick));
            if (end <= start) return a < start ? 1f : 0f;
            float t = Mathf.Clamp01((a - start) / (end - start));
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Puts the pilot's stick on top of a base command (trim and corrections)
        /// while keeping full authority: centre gives the base, full deflection
        /// still gives ±1, and the result is continuous and monotonic in the stick.
        /// </summary>
        public static float Compose(float stick, float baseCommand)
        {
            float s = Mathf.Clamp(Finite(stick), -1f, 1f);
            float b = Mathf.Clamp(Finite(baseCommand), -0.95f, 0.95f);
            return s >= 0f ? b + s * (1f - b) : b + s * (1f + b);
        }

        /// <summary>Moves towards a target by at most <paramref name="ratePerSecond"/>·dt.</summary>
        public static float RateLimit(float current, float target, float ratePerSecond, float deltaTime) =>
            Mathf.MoveTowards(current, Finite(target, current), Mathf.Max(0f, ratePerSecond) * Mathf.Max(0f, deltaTime));

        /// <summary>
        /// Bank to hold once the stick is released: level below <paramref name="levelBelow"/>,
        /// the current bank up to <paramref name="maxHeld"/>, and <paramref name="maxHeld"/>
        /// (same side) when steeper, so a steep bank eases back instead of tightening.
        /// </summary>
        public static float BankTarget(float bank, float levelBelow, float maxHeld)
        {
            bank = Finite(bank);
            float a = Mathf.Abs(bank);
            if (a < levelBelow) return 0f;
            return a <= maxHeld ? bank : Mathf.Sign(bank) * maxHeld;
        }

        /// <summary>
        /// Body yaw rate of a coordinated turn at a bank angle and true airspeed (m/s):
        /// g·sin(bank)/V, deg/s. Yaw damping acts on the difference, so it never resists a turn.
        /// </summary>
        public static float CoordinatedYawRate(float bankDegrees, float trueAirspeed)
        {
            if (trueAirspeed < 1f) return 0f;
            return 9.81f * Mathf.Sin(Finite(bankDegrees) * Mathf.Deg2Rad) / trueAirspeed * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Angle of attack a held turn needs to keep the same vertical lift: the lift
        /// coefficient (proportional to α − α₀) grows by 1/cos(bank). The bank is capped
        /// at 60° (2 g) so the target stays sensible.
        /// </summary>
        public static float TurnAngleOfAttack(float angleOfAttack, float zeroLiftAngle, float bankDegrees)
        {
            float bank = Mathf.Min(Mathf.Abs(Finite(bankDegrees)), 60f) * Mathf.Deg2Rad;
            float loadFactor = 1f / Mathf.Cos(bank);
            return zeroLiftAngle + (Finite(angleOfAttack) - zeroLiftAngle) * loadFactor;
        }

        /// <summary>
        /// Stall prevention level 0-1 from the margin to the critical angle of attack
        /// (with a pitch-rate lead): 0 at or beyond <paramref name="startMargin"/>, 1 at
        /// <paramref name="fullMargin"/> or closer.
        /// </summary>
        public static float StallProtection(float angleOfAttack, float criticalAngle, float pitchRate, float anticipationSeconds,
            float startMargin, float fullMargin)
        {
            float predicted = Finite(angleOfAttack) + Mathf.Max(0f, Finite(pitchRate)) * Mathf.Max(0f, anticipationSeconds);
            float margin = criticalAngle - predicted;
            if (startMargin <= fullMargin) return margin <= fullMargin ? 1f : 0f;
            return Mathf.Clamp01((startMargin - margin) / (startMargin - fullMargin));
        }

        /// <summary>
        /// Pitch-attitude protection level 0-1: 0 up to <paramref name="startDegrees"/> nose up,
        /// 1 at <paramref name="fullDegrees"/> or more. A steep climb runs out of airspeed whatever
        /// the angle of attack, so this eases the nose down before the energy is gone.
        /// </summary>
        public static float AttitudeProtection(float pitchDegrees, float startDegrees, float fullDegrees)
        {
            if (fullDegrees <= startDegrees) return Finite(pitchDegrees) >= startDegrees ? 1f : 0f;
            return Mathf.Clamp01((Finite(pitchDegrees) - startDegrees) / (fullDegrees - startDegrees));
        }

        /// <summary>Highest nose-up elevator allowed at a protection level: 1 (no limit) down to the minimum.</summary>
        public static float NoseUpLimit(float protection, float minimumElevator) =>
            Mathf.Lerp(1f, minimumElevator, Mathf.Clamp01(protection));

        public static float Finite(float value, float fallback = 0f) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
