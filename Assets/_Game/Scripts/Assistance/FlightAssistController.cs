using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Assistance
{
    /// <summary>What the assistance is doing this step, for the HUD, the inspector and tests.</summary>
    public struct FlightAssistState
    {
        /// <summary>The selected profile (the controller may still be blending towards it).</summary>
        public FlightAssistProfile Profile;
        /// <summary>Progress of the blend towards <see cref="Profile"/>, 0-1.</summary>
        public float Blend;
        /// <summary>0 on the ground or below flying speed, 1 in flight; assistance scales with it.</summary>
        public float Authority;
        /// <summary>Pitch trim, fraction of elevator travel (shared by automatic and manual trim).</summary>
        public float Trim;
        public float PitchCorrection;
        public float RollCorrection;
        public float RudderCorrection;
        /// <summary>Highest nose-up elevator currently allowed (1 = no limit).</summary>
        public float NoseUpLimit;
        /// <summary>Stall prevention level, 0 (inactive) to 1 (tightest limit).</summary>
        public float StallProtection;
        public bool BankHeld;
        public float BankTarget;
        public bool TrimHeld;
        public float AngleOfAttackTarget;
    }

    /// <summary>
    /// The flight assistance layer: sits between the pilot's commands and the
    /// aircraft and only changes commands (<see cref="IFlightAssist"/>). The
    /// physics, the aircraft's constants and the telemetry are the same in every
    /// profile.
    ///
    /// How it behaves:
    /// - Corrections are added on top of the pilot's input and change at a limited
    ///   rate with limited authority. Full stick always reaches full deflection.
    /// - Damping and coordination fade out as the stick nears full deflection, so a
    ///   strong, deliberate input is never fought. Holds (bank, trim) act only with
    ///   the stick released.
    /// - It never moves the throttle and never flies a path or a heading: there is
    ///   no hidden autopilot.
    /// - It works only in the air at flying speed and fades in and out; a profile
    ///   change blends over a few seconds; one trim state is shared by every
    ///   profile, so changing profile never makes the aircraft pitch.
    /// - Stall prevention only limits nose-up elevator. When the aircraft cannot
    ///   be helped (too slow, engine off), it does what the physics says.
    ///
    /// Plain C#, allocation-free per step, deterministic.
    /// </summary>
    public sealed class FlightAssistController : IFlightAssist
    {
        readonly FlightAssistTuning _tuning;
        readonly FlightAssistGains _from = new FlightAssistGains();
        readonly FlightAssistGains _current = new FlightAssistGains();
        readonly FlightAssistGains _target = new FlightAssistGains();
        bool _standardAutoTrim = true, _assistedStallPrevention = true;
        AircraftEnvelope _envelope = AircraftEnvelope.PrototypeTrainer;
        FlightAssistProfile _profile;
        float _blend = 1f;

        float _authority;
        bool _engaged;
        float _trim, _pendingTransfer, _heldPitch, _trimInput;
        bool _pitchWasHeld;
        float _pitchFreeTime, _rollFreeTime;
        bool _hasAngleOfAttackTarget, _hasBankTarget;
        float _angleOfAttackRef, _angleOfAttackTarget, _bankTarget;
        float _pitchCorrection, _rollCorrection, _rudderCorrection, _noseUpLimit = 1f, _protection;
        float _lastAirspeed = float.NaN, _acceleration;

        public FlightAssistController(FlightAssistTuning tuning = null, FlightAssistProfile profile = FlightAssistProfile.Standard)
        {
            _tuning = tuning ?? new FlightAssistTuning();
            _profile = profile;
            _current.CopyFrom(Target());
        }

        public FlightAssistTuning Tuning => _tuning;
        /// <summary>Standard profile option: automatic pitch trim (on by default).</summary>
        public bool StandardAutoTrim => _standardAutoTrim;
        /// <summary>Assisted profile option: gentle stall prevention (on by default).</summary>
        public bool AssistedStallPrevention => _assistedStallPrevention;
        public FlightAssistProfile Profile => _profile;
        public AircraftEnvelope Envelope => _envelope;
        public float Trim => _trim;

        /// <summary>The aircraft's limits (critical angle of attack, zero-lift angle), from the aircraft data.</summary>
        public void SetEnvelope(in AircraftEnvelope envelope)
        {
            if (envelope.IsDefined) _envelope = envelope;
        }

        /// <summary>
        /// Selects a profile. In flight the behaviour blends from the current one over
        /// <see cref="FlightAssistTuning.profileBlendSeconds"/>; <paramref name="immediate"/>
        /// skips the blend (start of a flight, tests).
        /// </summary>
        public void SetProfile(FlightAssistProfile profile, bool immediate = false)
        {
            if (profile == _profile && (!immediate || _blend >= 1f)) return;
            _profile = profile;
            StartBlend(immediate);
        }

        /// <summary>The profiles' options. A change blends in like a profile change.</summary>
        public void SetOptions(bool standardAutoTrim, bool assistedStallPrevention)
        {
            if (standardAutoTrim == _standardAutoTrim && assistedStallPrevention == _assistedStallPrevention) return;
            _standardAutoTrim = standardAutoTrim;
            _assistedStallPrevention = assistedStallPrevention;
            StartBlend(immediate: false);
        }

        void StartBlend(bool immediate)
        {
            _from.CopyFrom(_current);
            _blend = immediate || _tuning.profileBlendSeconds <= 0f ? 1f : 0f;
            if (_blend >= 1f) _current.CopyFrom(Target());
        }

        /// <summary>The selected profile's gains with its options applied.</summary>
        FlightAssistGains Target()
        {
            _target.CopyFrom(_tuning.Gains(_profile));
            if (_profile == FlightAssistProfile.Standard && !_standardAutoTrim) _target.autoTrim = 0f;
            if (_profile == FlightAssistProfile.Assisted && !_assistedStallPrevention) _target.stallPrevention = 0f;
            return _target;
        }

        /// <summary>Manual trim input for the next steps: −1 nose down to +1 nose up (trim keys held).</summary>
        public void SetTrimInput(float input) => _trimInput = Mathf.Clamp(AssistMath.Finite(input), -1f, 1f);

        /// <summary>A new flight: trim neutral, nothing held or blending, assistance disengaged until airborne.</summary>
        public void Reset()
        {
            _blend = 1f;
            _current.CopyFrom(Target());
            _authority = 0f;
            _engaged = false;
            _trim = _pendingTransfer = _heldPitch = _trimInput = 0f;
            _pitchWasHeld = false;
            _pitchFreeTime = _rollFreeTime = 0f;
            _hasAngleOfAttackTarget = _hasBankTarget = false;
            _angleOfAttackRef = _angleOfAttackTarget = _bankTarget = 0f;
            _pitchCorrection = _rollCorrection = _rudderCorrection = 0f;
            _noseUpLimit = 1f;
            _protection = 0f;
            _lastAirspeed = float.NaN;
            _acceleration = 0f;
        }

        public FlightAssistState State => new FlightAssistState
        {
            Profile = _profile,
            Blend = _blend,
            Authority = _authority,
            Trim = _trim,
            PitchCorrection = _pitchCorrection,
            RollCorrection = _rollCorrection,
            RudderCorrection = _rudderCorrection,
            NoseUpLimit = _noseUpLimit,
            StallProtection = _protection,
            BankHeld = _hasBankTarget && _authority > 0f && _current.bankHold > 0f,
            BankTarget = _bankTarget,
            TrimHeld = _hasAngleOfAttackTarget && _authority > 0f && _current.autoTrim > 0f,
            AngleOfAttackTarget = _angleOfAttackTarget,
        };

        public FlightControls Apply(in FlightControls pilot, in FlightTelemetry telemetry, float deltaTime)
        {
            if (deltaTime > 0f) Step(pilot, telemetry, deltaTime);
            float pitch = Mathf.Min(AssistMath.Compose(pilot.Pitch, _trim + _pitchCorrection), _noseUpLimit);
            float roll = AssistMath.Compose(pilot.Roll, _rollCorrection);
            float yaw = AssistMath.Compose(pilot.Yaw, _rudderCorrection);
            return new FlightControls(pitch, roll, yaw, pilot.Throttle, pilot.Brake, pilot.Flaps, pilot.Ignition);
        }

        void Step(in FlightControls pilot, in FlightTelemetry t, float dt)
        {
            var k = _tuning;
            AdvanceBlend(dt);
            var g = _current;

            bool valid = Finite(t.IndicatedAirspeedKnots) && Finite(t.PitchDegrees) && Finite(t.TrueAirspeedKnots) && Finite(t.AngleOfAttackDegrees)
                         && Finite(t.SideslipDegrees) && Finite(t.BankDegrees) && Finite(t.PitchRateDegreesPerSecond)
                         && Finite(t.RollRateDegreesPerSecond) && Finite(t.YawRateDegreesPerSecond) && Finite(t.FlapPosition);
            // Engage after lift-off at flying speed; once engaged, stay engaged in the air down to a much lower
            // speed, so stall prevention is still there when the aircraft is slow (hysteresis).
            bool airborneNow = valid && !t.WeightOnWheels;
            _engaged = airborneNow && t.IndicatedAirspeedKnots >= (_engaged ? k.disengageAirspeedKnots : k.engageAirspeedKnots);
            bool flying = _engaged;
            float fade = flying ? k.engageSeconds : k.disengageSeconds;
            _authority = Mathf.MoveTowards(_authority, flying ? 1f : 0f, fade > 0f ? dt / fade : 1f);
            float a = _authority;

            float alpha = valid ? t.AngleOfAttackDegrees : 0f;
            float q = valid ? t.PitchRateDegreesPerSecond : 0f;
            float p = valid ? t.RollRateDegreesPerSecond : 0f;
            float r = valid ? t.YawRateDegreesPerSecond : 0f;
            float beta = valid ? t.SideslipDegrees : 0f;
            float bank = valid ? t.BankDegrees : 0f;
            float flaps = valid ? Mathf.Clamp01(t.FlapPosition) : 0f;
            float trueAirspeed = valid ? FlightUnits.KnotsToMetresPerSecond(t.TrueAirspeedKnots) : 0f;
            float critical = _envelope.CriticalAngleOfAttack(flaps);

            UpdateTrim(pilot.Pitch, alpha, q, bank, flaps, critical, valid && !t.WeightOnWheels, t.WeightOnWheels, a, g, dt);

            // Airspeed trend (kt/s), smoothed: the phugoid damper's input. Zero in any steady climb or descent.
            if (valid && !float.IsNaN(_lastAirspeed))
            {
                float raw = (t.IndicatedAirspeedKnots - _lastAirspeed) / dt;
                _acceleration += (raw - _acceleration) * Mathf.Min(1f, dt / Mathf.Max(k.accelerationSmoothingSeconds, 1e-3f));
            }
            _lastAirspeed = valid ? t.IndicatedAirspeedKnots : float.NaN;

            // Pitch: rate damping (short period) and speed-trend damping (phugoid: a little nose up while
            // gaining speed, nose down while losing it), faded by the pilot's own pitch input.
            float pitchWeight = AssistMath.OverrideWeight(pilot.Pitch, k.overrideStart, k.overrideEnd);
            float pitchTarget = a * pitchWeight * (-g.pitchDamping * q + g.speedDamping * _acceleration);
            pitchTarget = Mathf.Clamp(pitchTarget, -k.maxPitchCorrection, k.maxPitchCorrection);
            _pitchCorrection = AssistMath.RateLimit(_pitchCorrection, pitchTarget, k.correctionRate, dt);

            // Stall prevention: a limit on nose-up elevator, whatever the stick says (gentle, optional).
            // A steep nose-up attitude runs out of energy whatever the angle of attack: ease the nose down early.
            float protection = valid
                ? AssistMath.StallProtection(alpha, critical, q, k.stallAnticipationSeconds, k.stallProtectionStartMargin, k.stallProtectionFullMargin)
                : 0f;
            float attitude = valid ? AssistMath.AttitudeProtection(t.PitchDegrees, k.pitchProtectionStartDegrees, k.pitchProtectionFullDegrees) : 0f;
            float weight = g.stallPrevention * a;
            _protection = Mathf.Max(protection, attitude) * weight;
            float limitTarget = Mathf.Min(
                AssistMath.NoseUpLimit(protection * weight, k.stallProtectionMinimumElevator),
                AssistMath.NoseUpLimit(attitude * weight, k.pitchProtectionMinimumElevator));
            _noseUpLimit = AssistMath.RateLimit(_noseUpLimit, limitTarget, k.correctionRate, dt);

            // Roll: rate damping, and with the stick released a gentle bank hold.
            bool rollFree = Mathf.Abs(pilot.Roll) < k.releaseThreshold;
            if (!rollFree)
            {
                _rollFreeTime = 0f;
                _hasBankTarget = false;
            }
            else
            {
                _rollFreeTime += dt;
                if (!_hasBankTarget && valid && Captured(_rollFreeTime, p))
                {
                    _bankTarget = AssistMath.BankTarget(bank, k.levelBelowDegrees, k.maxHeldBankDegrees);
                    _hasBankTarget = true;
                }
            }
            float bankHold = _hasBankTarget
                ? Mathf.Clamp(k.bankGain * (_bankTarget - bank), -k.maxBankCorrection, k.maxBankCorrection) * g.bankHold
                : 0f;
            float rollWeight = AssistMath.OverrideWeight(pilot.Roll, k.overrideStart, k.overrideEnd);
            float rollTarget = Mathf.Clamp(a * (rollWeight * -g.rollDamping * p + bankHold), -k.maxRollCorrection, k.maxRollCorrection);
            _rollCorrection = AssistMath.RateLimit(_rollCorrection, rollTarget, k.correctionRate, dt);

            // Yaw: rudder towards zero sideslip, damping about the yaw rate the turn needs.
            float yawWeight = AssistMath.OverrideWeight(pilot.Yaw, k.overrideStart, k.overrideEnd);
            float turnRate = AssistMath.CoordinatedYawRate(bank, trueAirspeed);
            float rudderTarget = a * yawWeight * (g.coordination * k.sideslipGain * beta - g.yawDamping * (r - turnRate));
            rudderTarget = Mathf.Clamp(rudderTarget, -k.maxRudderCorrection, k.maxRudderCorrection);
            _rudderCorrection = AssistMath.RateLimit(_rudderCorrection, rudderTarget, k.correctionRate, dt);
        }

        void UpdateTrim(float pilotPitch, float alpha, float q, float bank, float flaps, float critical, bool airborne, bool onGround,
            float a, FlightAssistGains g, float dt)
        {
            var k = _tuning;
            bool pitchFree = Mathf.Abs(pilotPitch) < k.releaseThreshold;
            if (!pitchFree)
            {
                // Learn the stick the pilot holds steadily in settled flight (the trim the aircraft would
                // need). Strong inputs and pulls against stall prevention are manoeuvres, not trim: forget them.
                float learn = k.heldStickSeconds > 0f ? Mathf.Min(1f, dt / k.heldStickSeconds) : 1f;
                bool settled = Mathf.Abs(q) < k.captureRateDegreesPerSecond && Mathf.Abs(pilotPitch) <= k.overrideStart * 2f
                               && _protection <= 0f;
                _heldPitch += ((settled ? pilotPitch : 0f) - _heldPitch) * learn;
                _pitchFreeTime = 0f;
                _hasAngleOfAttackTarget = false;
            }
            else
            {
                if (_pitchWasHeld)
                {
                    // Just released: the automatic trim takes over the steady stick, smoothly.
                    _pendingTransfer += g.autoTrim * a * k.heldStickTransfer * _heldPitch;
                    _heldPitch = 0f;
                }
                _pitchFreeTime += dt;
            }
            _pitchWasHeld = !pitchFree;

            if (_pendingTransfer != 0f)
            {
                float step = Mathf.Clamp(_pendingTransfer, -k.correctionRate * dt, k.correctionRate * dt);
                _trim += step;
                _pendingTransfer -= step;
            }

            // The automatic trim never holds an angle of attack close to the stall.
            float highestTarget = critical - k.stallProtectionStartMargin - 1f;
            // Only from settled flight (never on a timeout): a reference taken mid-manoeuvre would hold the wrong speed.
            if (pitchFree && !_hasAngleOfAttackTarget && airborne && a > 0f && _pitchFreeTime >= k.captureDelaySeconds
                && Mathf.Abs(q) < k.captureRateDegreesPerSecond)
            {
                _angleOfAttackRef = Mathf.Min(alpha, highestTarget);
                _hasAngleOfAttackTarget = true;
            }

            if (_hasAngleOfAttackTarget && airborne)
            {
                float target = AssistMath.TurnAngleOfAttack(_angleOfAttackRef, _envelope.ZeroLiftAngleOfAttack(flaps), bank);
                _angleOfAttackTarget = Mathf.Min(Mathf.Lerp(_angleOfAttackRef, target, g.turnCompensation), highestTarget);
                float rate = Mathf.Clamp(k.trimGain * (_angleOfAttackTarget - alpha), -k.trimRate, k.trimRate);
                _trim += rate * g.autoTrim * a * dt;
            }
            else if (onGround && g.autoTrim > 0f)
            {
                _trim = Mathf.MoveTowards(_trim, 0f, k.trimRate * g.autoTrim * dt); // ready for the next take-off
            }

            _trim += _trimInput * k.manualTrimRate * dt;
            _trim = Mathf.Clamp(_trim, -k.trimAuthority, k.trimAuthority);
        }

        bool Captured(float freeTime, float rate) =>
            freeTime >= _tuning.captureTimeoutSeconds
            || (freeTime >= _tuning.captureDelaySeconds && Mathf.Abs(rate) < _tuning.captureRateDegreesPerSecond);

        void AdvanceBlend(float dt)
        {
            var target = Target();
            if (_blend >= 1f)
            {
                _current.CopyFrom(target); // live Inspector tuning applies at once
                return;
            }
            _blend = Mathf.Min(1f, _blend + dt / Mathf.Max(_tuning.profileBlendSeconds, 1e-3f));
            float s = _blend * _blend * (3f - 2f * _blend);
            FlightAssistGains.Lerp(_from, target, s, _current);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
