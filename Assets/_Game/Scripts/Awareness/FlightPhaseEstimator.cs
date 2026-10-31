using System;
using OpenSkies.FlightState;
using UnityEngine;
using static OpenSkies.Awareness.SoftLogic;

namespace OpenSkies.Awareness
{
    /// <summary>
    /// Infers the phase of flight from telemetry, the runway geometry and recent
    /// history. Deterministic: the same sequence of samples and time steps always
    /// gives the same phases, so it is tested with scripted telemetry.
    ///
    /// Each step:
    /// 1. Weight on wheels is debounced (lift-off and touchdown need a short
    ///    confirmation), and vertical speed and acceleration are smoothed.
    /// 2. Every phase gets a score from 0 to 1 from soft conditions (see
    ///    <see cref="FlightPhaseSettings"/> and docs/CONTEXTUAL_UI.md).
    /// 3. Anti-flicker: a challenger must beat the current phase by a margin
    ///    (hysteresis), keep that lead for its confirmation time, and the current
    ///    phase must have been held for a minimum time. Only a change between
    ///    ground and air skips the minimum time.
    ///
    /// Phases are not assumed to follow each other: any phase can follow any
    /// other if the telemetry supports it (go-around, touch-and-go, rejected
    /// take-off). Allocation-free after construction.
    /// </summary>
    public sealed class FlightPhaseEstimator
    {
        public const int HistoryCapacity = 8;
        const float Never = 1e9f;
        const float MetresPerNauticalMile = 1852f;
        const float TimeEpsilon = 1e-4f;

        readonly FlightPhaseSettings _settings;
        readonly float[] _scores = new float[FlightPhases.Count];
        readonly PhaseTransition[] _history = new PhaseTransition[HistoryCapacity];
        int _historyCount, _historyNext;

        // Debounced ground state and event history.
        bool _onGround;
        float _groundDisagreement;
        float _timeSinceLiftOff, _timeSinceTouchdown;
        bool _climbOut;          // after a lift-off or go-around, until climb, cruise, descent or approach
        float _climbOutTime;

        // Sanitised and smoothed signals.
        bool _hasSample;
        float _groundspeed, _verticalSpeed, _heightAboveGround, _throttle;
        float _smoothedVerticalSpeed, _smoothedAcceleration;

        // Committed state.
        bool _initialized;
        FlightPhase _phase, _previous, _candidate;
        PhaseTransitionReason _reason;
        float _timeInPhase, _candidateTime, _confidence, _clock;

        public FlightPhaseEstimator(FlightPhaseSettings settings = null)
        {
            _settings = settings ?? new FlightPhaseSettings();
            Reset();
        }

        public FlightPhaseSettings Settings => _settings;
        public FlightPhase Phase => _phase;
        public float Confidence => Mathf.Clamp01(_confidence);
        public bool OnGround => _onGround;
        /// <summary>Vertical speed after smoothing, fpm.</summary>
        public float SmoothedVerticalSpeed => _smoothedVerticalSpeed;
        /// <summary>Groundspeed change after smoothing, kt/s.</summary>
        public float SmoothedAcceleration => _smoothedAcceleration;
        /// <summary>Seconds since the last reset.</summary>
        public float Clock => _clock;
        /// <summary>Committed phase changes since the last reset, including the initial estimate.</summary>
        public int TransitionTotal { get; private set; }

        public FlightPhaseEstimate Current => new FlightPhaseEstimate
        {
            Phase = _phase,
            Confidence = Mathf.Clamp01(_confidence),
            TimeInPhase = _timeInPhase,
            Previous = _previous,
            Reason = _reason,
            Candidate = _candidate,
            CandidateProgress = _candidate == _phase ? 0f : Mathf.Clamp01(_candidateTime / Mathf.Max(_settings.ConfirmSeconds(_candidate), TimeEpsilon)),
            OnGround = _onGround,
        };

        /// <summary>Latest score of a phase, 0-1.</summary>
        public float Score(FlightPhase phase) => _scores[(int)phase];

        public int TransitionCount => _historyCount;

        /// <summary>Recent phase changes, newest first (index 0).</summary>
        public PhaseTransition GetTransition(int newestFirstIndex)
        {
            if (newestFirstIndex < 0 || newestFirstIndex >= _historyCount) throw new ArgumentOutOfRangeException(nameof(newestFirstIndex));
            int index = (_historyNext - 1 - newestFirstIndex + 2 * HistoryCapacity) % HistoryCapacity;
            return _history[index];
        }

        /// <summary>Forget everything. The next update commits an initial estimate without confirmation.</summary>
        public void Reset(bool assumeOnGround = true)
        {
            _onGround = assumeOnGround;
            _groundDisagreement = 0f;
            _timeSinceLiftOff = Never;
            _timeSinceTouchdown = Never;
            _climbOut = false;
            _climbOutTime = 0f;

            _hasSample = false;
            _groundspeed = _verticalSpeed = _heightAboveGround = _throttle = 0f;
            _smoothedVerticalSpeed = _smoothedAcceleration = 0f;

            _initialized = false;
            _phase = _previous = _candidate = assumeOnGround ? FlightPhase.Parked : FlightPhase.Cruise;
            _reason = PhaseTransitionReason.None;
            _timeInPhase = _candidateTime = _confidence = _clock = 0f;
            Array.Clear(_scores, 0, _scores.Length);
            _historyCount = _historyNext = 0;
            TransitionTotal = 0;
        }

        /// <summary>Advance by one telemetry sample.</summary>
        public void Update(in FlightTelemetry telemetry, in RunwayRelation runway, float deltaTime)
        {
            if (!(deltaTime > 0f) || float.IsInfinity(deltaTime)) return;
            _clock += deltaTime;
            ReadSignals(telemetry, deltaTime);
            UpdateGroundState(telemetry.WeightOnWheels, deltaTime);
            ComputeScores(runway);
            Decide(deltaTime);
        }

        void ReadSignals(in FlightTelemetry t, float dt)
        {
            float groundspeed = Mathf.Max(0f, Finite(t.GroundspeedKnots, _groundspeed));
            _verticalSpeed = Finite(t.VerticalSpeedFeetPerMinute, _verticalSpeed);
            _heightAboveGround = Mathf.Max(0f, Finite(t.HeightAboveGroundFeet, _heightAboveGround));
            _throttle = Mathf.Clamp01(Finite(t.Throttle, _throttle));

            if (!_hasSample)
            {
                _hasSample = true;
                _smoothedVerticalSpeed = _verticalSpeed;
                _smoothedAcceleration = 0f;
            }
            else
            {
                _smoothedVerticalSpeed += (_verticalSpeed - _smoothedVerticalSpeed) * Smoothing(dt, _settings.verticalSpeedSmoothingSeconds);
                float acceleration = (groundspeed - _groundspeed) / dt;
                _smoothedAcceleration += (acceleration - _smoothedAcceleration) * Smoothing(dt, _settings.accelerationSmoothingSeconds);
            }
            _groundspeed = groundspeed;
        }

        void UpdateGroundState(bool weightOnWheels, float dt)
        {
            if (_timeSinceLiftOff < Never) _timeSinceLiftOff += dt;
            if (_timeSinceTouchdown < Never) _timeSinceTouchdown += dt;
            if (_climbOut)
            {
                _climbOutTime += dt;
                if (_climbOutTime > _settings.initialClimbMaxSeconds) _climbOut = false;
            }

            if (weightOnWheels == _onGround)
            {
                _groundDisagreement = 0f;
                return;
            }
            _groundDisagreement += dt;
            float needed = weightOnWheels ? _settings.touchdownConfirmSeconds : _settings.liftOffConfirmSeconds;
            if (_groundDisagreement + TimeEpsilon < needed) return;

            _onGround = weightOnWheels;
            _groundDisagreement = 0f;
            if (_onGround)
            {
                _timeSinceTouchdown = 0f;
                _climbOut = false;
            }
            else
            {
                _timeSinceLiftOff = 0f;
                BeginClimbOut();
            }
        }

        void BeginClimbOut()
        {
            _climbOut = true;
            _climbOutTime = 0f;
        }

        void ComputeScores(in RunwayRelation runway)
        {
            var s = _settings;
            float ground = _onGround ? 1f : 0f;
            float air = 1f - ground;
            float gs = _groundspeed;
            float vs = _smoothedVerticalSpeed;
            float agl = _heightAboveGround;

            float takeoffPower = Ramp(_throttle, s.takeoffThrottle - s.throttleFade, s.takeoffThrottle);
            float climbPower = Ramp(_throttle, s.climbThrottle - s.throttleFade, s.climbThrottle);
            float accelerating = Ramp(_smoothedAcceleration, s.accelerationMin, s.accelerationFull);
            float decelerating = Ramp(_smoothedAcceleration, -s.accelerationMin, -s.accelerationFull);
            float stopped = Ramp(gs, s.movingGroundspeed, s.stoppedGroundspeed);
            float taxiSpeed = Ramp(gs, s.taxiMaxGroundspeed + s.taxiFadeGroundspeed, s.taxiMaxGroundspeed);
            float runwaySpeed = Ramp(gs, s.rolloutMinGroundspeed, s.rolloutFullGroundspeed);
            float takeoffRun = takeoffPower * Mathf.Max(accelerating, Ramp(gs, s.takeoffRunMinGroundspeed, s.takeoffRunFullGroundspeed));
            float landingWindow = Ramp(_timeSinceTouchdown, s.landingHoldSeconds, 0.5f * s.landingHoldSeconds);
            float onRunway = !runway.Valid || runway.OnRunway ? 1f : s.offRunwayTakeoffFactor;
            bool runwayContext = _timeSinceTouchdown <= s.rolloutContextSeconds
                                 || _phase == FlightPhase.TakeoffRoll || _phase == FlightPhase.Rollout || _phase == FlightPhase.Landing;

            // On the ground.
            _scores[(int)FlightPhase.Parked] = ground * stopped;
            _scores[(int)FlightPhase.Taxi] = ground * (1f - stopped) * taxiSpeed * (1f - takeoffPower);
            _scores[(int)FlightPhase.TakeoffRoll] = ground * takeoffRun * onRunway;
            _scores[(int)FlightPhase.Rollout] = ground * runwaySpeed * Mathf.Max(1f - takeoffPower, decelerating)
                                                * (runwayContext ? 1f : s.rolloutWithoutContextFactor) * (1f - landingWindow);

            // Landing: short final in the air, then the touchdown and the first seconds on the wheels.
            float goAround = Ramp(_throttle, s.goAroundThrottle - s.throttleFade, s.goAroundThrottle) * Ramp(vs, 0f, s.goAroundClimbRate);
            float departing = _climbOut ? climbPower : 0f;
            float notClimbingAway = Ramp(vs, 2f * s.approachMaxClimbRate, s.approachMaxClimbRate);
            float approachGeometry = ApproachGeometry(runway);
            float landingAir = air * ShortFinal(runway) * notClimbingAway * (1f - goAround) * (1f - departing);
            float landingGround = ground * landingWindow * (1f - takeoffRun);
            _scores[(int)FlightPhase.Landing] = Mathf.Max(landingAir, landingGround);

            // In the air.
            float approachPull = 1f - s.approachPreference * approachGeometry;
            bool onApproach = _phase == FlightPhase.Approach || _phase == FlightPhase.Landing;
            float climbOutContext = _climbOut ? 1f : (onApproach ? goAround : 0f);
            float belowInitialClimbTop = Ramp(agl, s.initialClimbTopFeet + s.initialClimbBandFeet, s.initialClimbTopFeet);
            float aboveInitialClimbTop = _climbOut ? 1f - belowInitialClimbTop : 1f;

            _scores[(int)FlightPhase.InitialClimb] = air * belowInitialClimbTop * climbPower
                                                     * Ramp(vs, -s.descentVerticalSpeed, 0f) * climbOutContext;
            _scores[(int)FlightPhase.Climb] = air * Ramp(vs, 0.5f * s.climbVerticalSpeed, s.climbVerticalSpeed) * aboveInitialClimbTop;
            _scores[(int)FlightPhase.Cruise] = air * Ramp(Mathf.Abs(vs), 2f * s.levelVerticalSpeed, s.levelVerticalSpeed)
                                               * Ramp(agl, s.cruiseMinHeightFeet / 3f, s.cruiseMinHeightFeet) * approachPull;
            _scores[(int)FlightPhase.Descent] = air * Ramp(vs, -0.5f * s.descentVerticalSpeed, -s.descentVerticalSpeed) * approachPull;
            _scores[(int)FlightPhase.Approach] = air * approachGeometry * notClimbingAway * (1f - goAround) * (1f - departing)
                                                 * (1f - 0.5f * landingAir);
        }

        /// <summary>0-1: inside the approach sector of the selected runway end, low and close enough, tracking towards it.</summary>
        float ApproachGeometry(in RunwayRelation r)
        {
            if (!r.Valid || r.AlongTrackMetres < -0.5f * r.RunwayLengthMetres) return 0f; // past the middle of the runway
            var s = _settings;
            float fade = 1f + s.approachFadeFraction;
            float distanceNm = r.DistanceToThresholdMetres / MetresPerNauticalMile;
            float heightFeet = r.HeightAboveThresholdMetres * FlightUnits.FeetPerMetre;
            float sectorHalfWidth = Mathf.Max(s.approachSectorMinHalfWidthMetres,
                Mathf.Max(0f, r.AlongTrackMetres) * Mathf.Tan(s.approachSectorHalfAngleDegrees * Mathf.Deg2Rad));
            return Ramp(distanceNm, fade * s.approachMaxDistanceNm, s.approachMaxDistanceNm)
                   * Ramp(heightFeet, fade * s.approachMaxHeightFeet, s.approachMaxHeightFeet)
                   * Ramp(Mathf.Abs(r.TrackErrorDegrees), fade * s.approachMaxTrackErrorDegrees, s.approachMaxTrackErrorDegrees)
                   * Ramp(Mathf.Abs(r.CrossTrackMetres), fade * sectorHalfWidth, sectorHalfWidth);
        }

        /// <summary>0-1: low over the last part of the approach or the runway, lined up with it.</summary>
        float ShortFinal(in RunwayRelation r)
        {
            if (!r.Valid || r.AlongTrackMetres < -r.RunwayLengthMetres) return 0f; // beyond the far end
            var s = _settings;
            float fade = 1f + s.landingFadeFraction;
            float heightFeet = r.HeightAboveThresholdMetres * FlightUnits.FeetPerMetre;
            float maxDistance = s.landingMaxDistanceNm * MetresPerNauticalMile;
            return Ramp(heightFeet, fade * s.landingHeightFeet, s.landingHeightFeet)
                   * Ramp(r.AlongTrackMetres, fade * maxDistance, maxDistance)
                   * Ramp(Mathf.Abs(r.CrossTrackMetres), fade * s.landingMaxCrossTrackMetres, s.landingMaxCrossTrackMetres)
                   * Ramp(Mathf.Abs(r.TrackErrorDegrees), fade * s.landingMaxTrackErrorDegrees, s.landingMaxTrackErrorDegrees);
        }

        void Decide(float dt)
        {
            if (!_initialized)
            {
                _initialized = true;
                Commit(BestScoringPhase(), PhaseTransitionReason.InitialEstimate, initial: true);
                return;
            }

            _timeInPhase += dt;
            int current = (int)_phase;
            _confidence += (_scores[current] - _confidence) * Smoothing(dt, _settings.confidenceSmoothingSeconds);

            // Hysteresis: the committed phase gets a head start of the switch margin.
            int leader = current;
            float leaderScore = _scores[current] + _settings.switchMargin;
            for (int i = 0; i < FlightPhases.Count; i++)
            {
                if (i != current && _scores[i] > leaderScore)
                {
                    leader = i;
                    leaderScore = _scores[i];
                }
            }

            if (leader == current)
            {
                _candidate = _phase;
                _candidateTime = 0f;
                return;
            }

            var challenger = (FlightPhase)leader;
            if (challenger != _candidate)
            {
                _candidate = challenger;
                _candidateTime = 0f;
            }
            _candidateTime += dt;

            // A phase that contradicts the (debounced) ground state is not held for its minimum time.
            bool contradicted = !IsConsistentWithGround(_phase, _onGround);
            bool heldLongEnough = contradicted || _timeInPhase + TimeEpsilon >= _settings.minimumPhaseSeconds;
            if (heldLongEnough && _candidateTime + TimeEpsilon >= _settings.ConfirmSeconds(challenger))
                Commit(challenger, ReasonFor(_phase, challenger), initial: false);
        }

        FlightPhase BestScoringPhase()
        {
            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < FlightPhases.Count; i++)
            {
                if (_scores[i] > bestScore)
                {
                    best = i;
                    bestScore = _scores[i];
                }
            }
            if (best >= 0) return (FlightPhase)best;
            return _onGround ? FlightPhase.Parked : FlightPhase.Cruise; // nothing fits: confidence will show 0
        }

        static bool IsConsistentWithGround(FlightPhase phase, bool onGround)
        {
            if (FlightPhases.IsGroundPhase(phase)) return onGround;
            if (FlightPhases.IsAirbornePhase(phase)) return !onGround;
            return true; // landing spans both
        }

        PhaseTransitionReason ReasonFor(FlightPhase from, FlightPhase to)
        {
            bool fromApproach = from == FlightPhase.Approach || from == FlightPhase.Landing;
            bool touchedDownInPhase = _timeSinceTouchdown <= _timeInPhase;
            switch (to)
            {
                case FlightPhase.Parked:
                    return PhaseTransitionReason.Stopped;
                case FlightPhase.Taxi:
                    if (from == FlightPhase.TakeoffRoll) return PhaseTransitionReason.TakeoffRejected;
                    return from == FlightPhase.Rollout || from == FlightPhase.Landing ? PhaseTransitionReason.SlowedToTaxi : PhaseTransitionReason.Taxiing;
                case FlightPhase.TakeoffRoll:
                    return from == FlightPhase.Landing || from == FlightPhase.Rollout ? PhaseTransitionReason.TouchAndGo : PhaseTransitionReason.TakeoffPower;
                case FlightPhase.InitialClimb:
                    if (from == FlightPhase.Rollout || (from == FlightPhase.Landing && touchedDownInPhase)) return PhaseTransitionReason.TouchAndGo;
                    if (fromApproach) return PhaseTransitionReason.GoAround;
                    return FlightPhases.IsGroundPhase(from) ? PhaseTransitionReason.LiftOff : PhaseTransitionReason.Climbing;
                case FlightPhase.Climb:
                    return fromApproach && !(from == FlightPhase.Landing && touchedDownInPhase) ? PhaseTransitionReason.GoAround : PhaseTransitionReason.Climbing;
                case FlightPhase.Cruise:
                    return PhaseTransitionReason.LevelFlight;
                case FlightPhase.Descent:
                    return PhaseTransitionReason.Descending;
                case FlightPhase.Approach:
                    return PhaseTransitionReason.ApproachGeometry;
                case FlightPhase.Landing:
                    return _onGround ? PhaseTransitionReason.Touchdown : PhaseTransitionReason.ShortFinal;
                case FlightPhase.Rollout:
                    return from == FlightPhase.TakeoffRoll ? PhaseTransitionReason.TakeoffRejected : PhaseTransitionReason.DeceleratingOnRunway;
                default:
                    return PhaseTransitionReason.None;
            }
        }

        void Commit(FlightPhase next, PhaseTransitionReason reason, bool initial)
        {
            var from = _phase;
            _previous = initial ? next : from;
            _phase = next;
            _reason = reason;
            _timeInPhase = 0f;
            _candidate = next;
            _candidateTime = 0f;
            _confidence = _scores[(int)next];

            if (next == FlightPhase.InitialClimb && !_climbOut) BeginClimbOut(); // go-around
            else if (next == FlightPhase.Climb || next == FlightPhase.Cruise || next == FlightPhase.Descent || next == FlightPhase.Approach)
                _climbOut = false;

            _history[_historyNext] = new PhaseTransition
            {
                From = initial ? next : from, To = next, Reason = reason, Time = _clock, Score = _scores[(int)next],
            };
            _historyNext = (_historyNext + 1) % HistoryCapacity;
            if (_historyCount < HistoryCapacity) _historyCount++;
            TransitionTotal++;
        }
    }
}
