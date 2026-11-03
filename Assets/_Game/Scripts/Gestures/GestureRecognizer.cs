using UnityEngine;

namespace OpenSkies.Gestures
{
    /// <summary>
    /// One gesture at a time from up to two pointers, as an explicit state
    /// machine: Idle, Candidate, Peek, ActiveManipulation, Armed, then Completed
    /// or Cancelled. It is fed raw pointer samples (press, move, release, revoke,
    /// tick) with explicit times, knows nothing about Unity events or rendering,
    /// and reports through a fixed-size event queue, so it is deterministic and
    /// allocation-free and can be tested without an engine.
    ///
    /// Distances are compared as fractions of the screen height
    /// (<see cref="SetScreenHeight"/>). A consumer can refine arming with
    /// <see cref="SetTargetArmed"/> (for example an arc segment is under the finger)
    /// and cancel with <see cref="Cancel"/>. Armed never means applied: the consumer
    /// acts only on a Completed event.
    /// </summary>
    public sealed class GestureRecognizer
    {
        public const int NoPointer = int.MinValue;
        const int QueueCapacity = 32;

        readonly GestureSettings _s;
        readonly GestureMask _mask;
        readonly GestureEvent[] _queue = new GestureEvent[QueueCapacity];
        int _head, _count;
        float _unit = 1080f;

        // Primary pointer.
        int _primary = NoPointer;
        Vector2 _origin, _position, _previous, _lastStep;
        float _pressTime, _lastMoveTime, _now;
        Vector2 _velocity;
        float _maxDistance;      // fraction of the screen height
        bool _inert;             // the finger left over from a two-finger gesture: does nothing until it lifts

        // Secondary pointer.
        int _secondary = NoPointer;
        Vector2 _secondOrigin, _secondPosition;
        float _secondPressTime, _secondMaxDistance;
        float _pinchDistance;
        bool _twoFingerCandidate;

        // Gesture.
        GestureState _state;
        GestureKind _kind;
        float _progress;
        SwipeDirection _direction;
        CancelReason _cancel;
        bool _targetArmed, _hasTargetArming;

        // Taps.
        float _lastTapTime = float.NegativeInfinity;
        Vector2 _lastTapPosition;

        // Dwell.
        Vector2 _dwellAnchor;
        float _dwellStart;
        bool _dwelling;
        int _dwellCount;

        public GestureRecognizer(GestureSettings settings, GestureMask mask)
        {
            _s = (settings ?? new GestureSettings()).Sanitized();
            _mask = mask;
        }

        public GestureSettings Settings => _s;
        public GestureMask Mask => _mask;
        public GestureState State => _state;
        public GestureKind Kind => _kind;
        /// <summary>Pull: 0 at the peek distance to 1 at the reveal distance. Long press: hold time over the threshold. Dwell: rest time over the threshold.</summary>
        public float Progress => _progress;
        public SwipeDirection Direction => _direction;
        public CancelReason LastCancel => _cancel;
        public bool IsTracking => _primary != NoPointer;
        public bool HasSecondPointer => _secondary != NoPointer;
        public int PointerCount => (_primary != NoPointer ? 1 : 0) + (_secondary != NoPointer ? 1 : 0);
        public int PrimaryPointer => _primary;
        public Vector2 Origin => _origin;
        public Vector2 Position => _position;
        /// <summary>Position minus origin, pixels.</summary>
        public Vector2 Delta => _position - _origin;
        /// <summary>Smoothed finger velocity, pixels per second.</summary>
        public Vector2 Velocity => _velocity;
        public bool IsDwelling => _dwelling;
        public int DwellCount => _dwellCount;
        public bool IsArmed => _state == GestureState.Armed;
        public int PendingEvents => _count;
        /// <summary>Seconds the primary finger has been down.</summary>
        public float HeldSeconds => _primary != NoPointer ? _now - _pressTime : 0f;
        /// <summary>Time since the finger last moved past the dwell slop (for views that show dwell progress).</summary>
        public float RestSeconds => _primary != NoPointer ? _now - _dwellStart : 0f;

        public void SetScreenHeight(float pixels)
        {
            if (pixels > 1f) _unit = pixels;
        }

        public bool TryDequeue(out GestureEvent e)
        {
            if (_count == 0)
            {
                e = default;
                return false;
            }
            e = _queue[_head];
            _head = (_head + 1) % QueueCapacity;
            _count--;
            return true;
        }

        public void ClearEvents()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// A consumer knows whether releasing would hit something (an arc segment, a
        /// lever detent). While set, it decides arming instead of the pull distance.
        /// </summary>
        public void SetTargetArmed(bool armed)
        {
            if (_hasTargetArming && _targetArmed == armed) return;
            _hasTargetArming = true;
            _targetArmed = armed;
            RefreshPullState();
        }

        public void ClearTargetArming()
        {
            if (!_hasTargetArming) return;
            _hasTargetArming = false;
            _targetArmed = false;
            RefreshPullState();
        }

        /// <summary>The consumer ends the gesture without completing it; the pointer stays inert until it lifts.</summary>
        public void Cancel(CancelReason reason = CancelReason.Interrupted)
        {
            if (_primary == NoPointer) return;
            if (_state != GestureState.Cancelled && _state != GestureState.Completed) EndCancelled(reason);
            _inert = true;
        }

        // ---- Samples ----------------------------------------------------------------------------

        /// <summary>A pointer went down. Returns false if the recognizer has no use for it (a third finger).</summary>
        public bool Press(int pointerId, Vector2 position, float time)
        {
            _now = time;
            if (pointerId == NoPointer) return false;
            if (_primary == NoPointer)
            {
                BeginPrimary(pointerId, position, time);
                return true;
            }
            if (pointerId == _primary) return false;
            if (_secondary != NoPointer || _inert) return false;
            if ((_mask & (GestureMask.Pinch | GestureMask.TwoFingerTap)) == 0) return false;
            // A second finger joins only before the first one has become a long press or a pull.
            if (_state != GestureState.Candidate && _kind != GestureKind.Drag) return false;

            _secondary = pointerId;
            _secondOrigin = _secondPosition = position;
            _secondPressTime = time;
            _secondMaxDistance = 0f;
            _pinchDistance = Vector2.Distance(_position, position);
            _twoFingerCandidate = (_mask & GestureMask.TwoFingerTap) != 0 && _state == GestureState.Candidate
                                  && time - _pressTime <= _s.secondFingerWindowSeconds && _maxDistance <= _s.tapSlop;
            if (_kind == GestureKind.Drag)
            {
                // The drag ends where the second finger lands; a pinch may follow.
                Enqueue(GestureEventType.Completed, GestureKind.Drag);
                _kind = GestureKind.None;
                _state = GestureState.Candidate;
            }
            return true;
        }

        public void Move(int pointerId, Vector2 position, float time)
        {
            _now = time;
            if (pointerId == _secondary)
            {
                _secondMaxDistance = Mathf.Max(_secondMaxDistance, Vector2.Distance(position, _secondOrigin) / _unit);
                _secondPosition = position;
                UpdatePinch();
                return;
            }
            if (pointerId != _primary) return;

            float dt = time - _lastMoveTime;
            Vector2 step = position - _position;
            if (dt > 0f) _velocity = GestureMath.Smooth(_velocity, step / dt, _s.velocitySmoothingSeconds, dt);
            _previous = _position;
            _position = position;
            _lastStep = step;
            _lastMoveTime = time;
            float distance = Vector2.Distance(position, _origin) / _unit;
            _maxDistance = Mathf.Max(_maxDistance, distance);

            if (_secondary != NoPointer)
            {
                UpdatePinch();
                return;
            }
            if (_inert || _state == GestureState.Cancelled || _state == GestureState.Completed) return;

            UpdateDwellAnchor(position, time);

            switch (_kind)
            {
                case GestureKind.None:
                    if (distance > Slop()) BeginMovement(distance);
                    break;
                case GestureKind.LongPress:
                    if (distance <= _s.tapSlop) break;
                    // A long press that starts moving becomes the route's movement gesture (a held
                    // handle that is then pulled unfolds the arc); without one it is simply cancelled.
                    if ((_mask & (GestureMask.Pull | GestureMask.Drag | GestureMask.Swipe)) == 0)
                    {
                        EndCancelled(CancelReason.MovedOut);
                        break;
                    }
                    var cancelled = NewEvent(GestureEventType.Cancelled, GestureKind.LongPress);
                    cancelled.Reason = CancelReason.MovedOut;
                    Push(cancelled);
                    _kind = GestureKind.None;
                    _state = GestureState.Candidate;
                    _progress = 0f;
                    BeginMovement(distance);
                    break;
                case GestureKind.Pull:
                    UpdatePull(distance);
                    break;
                case GestureKind.Drag:
                    Enqueue(GestureEventType.Changed, GestureKind.Drag);
                    break;
            }
        }

        public void Release(int pointerId, float time)
        {
            _now = time;
            if (pointerId == _secondary)
            {
                EndSecondary(time, revoked: false);
                return;
            }
            if (pointerId != _primary) return;

            if (_secondary != NoPointer)
            {
                // The primary lifted first: the two-finger gesture ends; the other finger is now inert.
                EndTwoFinger(time, revoked: false);
                _primary = _secondary;
                _position = _secondPosition;
                _secondary = NoPointer;
                _inert = true;
                return;
            }

            if (!_inert && _state != GestureState.Cancelled && _state != GestureState.Completed) FinishPrimary(time);
            ResetPrimary();
        }

        /// <summary>The pointer was taken away (focus loss, lost release, pause): nothing completes.</summary>
        public void Revoke(int pointerId)
        {
            if (pointerId == _secondary)
            {
                EndSecondary(_now, revoked: true);
                return;
            }
            if (pointerId != _primary) return;
            if (_secondary != NoPointer)
            {
                EndTwoFinger(_now, revoked: true);
                _secondary = NoPointer;
            }
            if (!_inert && _state != GestureState.Cancelled && _state != GestureState.Completed) EndCancelled(CancelReason.Revoked);
            ResetPrimary();
        }

        /// <summary>Time passes without a sample: long presses and dwells are decided here.</summary>
        public void Tick(float time)
        {
            _now = time;
            if (_primary == NoPointer || _inert) return;
            if (_secondary != NoPointer) return;

            if (_kind == GestureKind.None && _state == GestureState.Candidate && (_mask & GestureMask.LongPress) != 0)
            {
                float held = time - _pressTime;
                _progress = Mathf.Clamp01(held / _s.longPressSeconds);
                if (held >= _s.longPressSeconds && _maxDistance <= _s.tapSlop)
                {
                    _kind = GestureKind.LongPress;
                    _state = GestureState.Armed;
                    _progress = 1f;
                    Enqueue(GestureEventType.Began, GestureKind.LongPress);
                    Enqueue(GestureEventType.Armed, GestureKind.LongPress);
                }
                return;
            }

            if ((_mask & GestureMask.Dwell) != 0 && (_kind == GestureKind.Pull || _kind == GestureKind.Drag) && !_dwelling
                && time - _dwellStart >= _s.dwellSeconds)
            {
                _dwelling = true;
                _dwellCount++;
                Enqueue(GestureEventType.Dwell, GestureKind.Dwell);
            }
        }

        // ---- Internals --------------------------------------------------------------------------

        void BeginPrimary(int pointerId, Vector2 position, float time)
        {
            _primary = pointerId;
            _origin = _position = _previous = position;
            _lastStep = Vector2.zero;
            _pressTime = _lastMoveTime = time;
            _velocity = Vector2.zero;
            _maxDistance = 0f;
            _inert = false;
            _state = GestureState.Candidate;
            _kind = GestureKind.None;
            _progress = 0f;
            _direction = SwipeDirection.None;
            _cancel = CancelReason.None;
            _targetArmed = false;
            _hasTargetArming = false;
            _dwellAnchor = position;
            _dwellStart = time;
            _dwelling = false;
            _dwellCount = 0;
            _twoFingerCandidate = false;
        }

        void ResetPrimary()
        {
            _primary = NoPointer;
            _inert = false;
            _state = GestureState.Idle;
            _kind = GestureKind.None;
            _progress = 0f;
            _dwelling = false;
            _hasTargetArming = false;
            _targetArmed = false;
        }

        float Slop()
        {
            // The movement that starts a pull or a drag; taps and long presses use the tap slop.
            if ((_mask & GestureMask.Pull) != 0) return _s.pullPeekDistance;
            if ((_mask & (GestureMask.Drag | GestureMask.Swipe)) != 0) return _s.dragThreshold;
            return _s.tapSlop;
        }

        void BeginMovement(float distance)
        {
            if ((_mask & GestureMask.Pull) != 0)
            {
                _kind = GestureKind.Pull;
                _state = GestureState.Peek;
                Enqueue(GestureEventType.Began, GestureKind.Pull);
                UpdatePull(distance);
                return;
            }
            if ((_mask & (GestureMask.Drag | GestureMask.Swipe)) != 0)
            {
                _kind = GestureKind.Drag;
                _state = GestureState.ActiveManipulation;
                _lastStep = _position - _origin; // the whole movement so far: no dead jump
                Enqueue(GestureEventType.Began, GestureKind.Drag);
                Enqueue(GestureEventType.Changed, GestureKind.Drag);
                return;
            }
            // Only taps or long presses were wanted: moving ends the candidate.
            EndCancelled(CancelReason.MovedOut);
        }

        void UpdatePull(float distance)
        {
            float span = _s.pullRevealDistance - _s.pullPeekDistance;
            _progress = Mathf.Clamp01((distance - _s.pullPeekDistance) / span);
            RefreshPullState();
            Enqueue(GestureEventType.Changed, GestureKind.Pull);
        }

        void RefreshPullState()
        {
            if (_kind != GestureKind.Pull || _state == GestureState.Cancelled || _state == GestureState.Completed) return;
            float distance = Vector2.Distance(_position, _origin) / _unit;
            bool revealed = _state == GestureState.Peek
                ? distance >= _s.pullRevealDistance
                : distance >= _s.pullRevealDistance - _s.pullHysteresis;
            bool armed = _hasTargetArming ? _targetArmed : revealed;
            var next = armed ? GestureState.Armed : (revealed ? GestureState.ActiveManipulation : GestureState.Peek);
            if (next == _state) return;
            bool wasArmed = _state == GestureState.Armed;
            _state = next;
            if (armed && !wasArmed) Enqueue(GestureEventType.Armed, GestureKind.Pull);
            else if (!armed && wasArmed) Enqueue(GestureEventType.Disarmed, GestureKind.Pull);
        }

        void UpdateDwellAnchor(Vector2 position, float time)
        {
            if (Vector2.Distance(position, _dwellAnchor) / _unit <= _s.dwellSlop) return;
            _dwellAnchor = position;
            _dwellStart = time;
            if (!_dwelling) return;
            _dwelling = false;
            Enqueue(GestureEventType.DwellEnded, GestureKind.Dwell);
        }

        void UpdatePinch()
        {
            float distance = Vector2.Distance(_position, _secondPosition);
            if (_kind == GestureKind.None)
            {
                if ((_mask & GestureMask.Pinch) == 0) return;
                if (Mathf.Abs(distance - _pinchDistance) / _unit < _s.pinchMinChange) return;
                _kind = GestureKind.Pinch;
                _state = GestureState.ActiveManipulation;
                _twoFingerCandidate = false;
                Enqueue(GestureEventType.Began, GestureKind.Pinch);
            }
            if (_kind != GestureKind.Pinch) return;
            float delta = (distance - _pinchDistance) / _unit;
            _pinchDistance = distance;
            if (Mathf.Abs(delta) < 1e-6f) return;
            var e = NewEvent(GestureEventType.Changed, GestureKind.Pinch);
            e.PinchDelta = delta;
            Push(e);
        }

        void EndSecondary(float time, bool revoked)
        {
            EndTwoFinger(time, revoked);
            _secondary = NoPointer;
            _inert = true; // the remaining finger does nothing more (as the cockpit's pinch rule)
        }

        void EndTwoFinger(float time, bool revoked)
        {
            if (_kind == GestureKind.Pinch)
            {
                _state = revoked ? GestureState.Cancelled : GestureState.Completed;
                _cancel = revoked ? CancelReason.Revoked : CancelReason.None;
                var e = NewEvent(revoked ? GestureEventType.Cancelled : GestureEventType.Completed, GestureKind.Pinch);
                e.Reason = _cancel;
                Push(e);
                return;
            }
            bool tap = !revoked && _twoFingerCandidate
                       && time - _secondPressTime <= _s.twoFingerTapMaxSeconds && time - _pressTime <= _s.twoFingerTapMaxSeconds + _s.secondFingerWindowSeconds
                       && _maxDistance <= _s.tapSlop && _secondMaxDistance <= _s.tapSlop;
            if (tap)
            {
                _kind = GestureKind.TwoFingerTap;
                _state = GestureState.Completed;
                Enqueue(GestureEventType.Completed, GestureKind.TwoFingerTap);
            }
            else if (_state == GestureState.Candidate)
            {
                _state = GestureState.Cancelled;
                _cancel = revoked ? CancelReason.Revoked : CancelReason.Incomplete;
            }
            _twoFingerCandidate = false;
        }

        void FinishPrimary(float time)
        {
            float held = time - _pressTime;
            switch (_kind)
            {
                case GestureKind.None:
                    if (held <= _s.tapMaxSeconds && _maxDistance <= _s.tapSlop) CompleteTap(time);
                    else EndCancelled(CancelReason.Incomplete);
                    break;
                case GestureKind.LongPress:
                    _state = GestureState.Completed;
                    Enqueue(GestureEventType.Completed, GestureKind.LongPress);
                    break;
                case GestureKind.Pull:
                    if (_state == GestureState.Armed)
                    {
                        _state = GestureState.Completed;
                        Enqueue(GestureEventType.Completed, GestureKind.Pull);
                    }
                    else EndCancelled(CancelReason.ReleasedUnarmed);
                    break;
                case GestureKind.Drag:
                    _state = GestureState.Completed;
                    Enqueue(GestureEventType.Completed, GestureKind.Drag);
                    if ((_mask & GestureMask.Swipe) != 0) ReportSwipe(held);
                    break;
            }
        }

        void CompleteTap(float time)
        {
            bool doubleTap = (_mask & GestureMask.DoubleTap) != 0
                             && time - _lastTapTime <= _s.doubleTapSeconds
                             && Vector2.Distance(_position, _lastTapPosition) / _unit <= _s.doubleTapSlop;
            if (doubleTap)
            {
                _lastTapTime = float.NegativeInfinity;
                _kind = GestureKind.DoubleTap;
                _state = GestureState.Completed;
                Enqueue(GestureEventType.Completed, GestureKind.DoubleTap);
                return;
            }
            _lastTapTime = time;
            _lastTapPosition = _position;
            if ((_mask & GestureMask.Tap) == 0)
            {
                EndCancelled(CancelReason.Incomplete);
                return;
            }
            _kind = GestureKind.Tap;
            _state = GestureState.Completed;
            Enqueue(GestureEventType.Completed, GestureKind.Tap);
        }

        void ReportSwipe(float held)
        {
            float distance = Vector2.Distance(_position, _origin) / _unit;
            if (distance < _s.swipeMinDistance || held > _s.swipeMaxSeconds) return;
            if (_velocity.magnitude / _unit < _s.swipeMinVelocity) return;
            var direction = GestureMath.Direction(_position - _origin, _s.directionToleranceDegrees);
            if (direction == SwipeDirection.None) return;
            _direction = direction;
            var e = NewEvent(GestureEventType.Completed, GestureKind.Swipe);
            e.Direction = direction;
            Push(e);
        }

        void EndCancelled(CancelReason reason)
        {
            var kind = _kind;
            _state = GestureState.Cancelled;
            _cancel = reason;
            if (_dwelling)
            {
                _dwelling = false;
                Enqueue(GestureEventType.DwellEnded, GestureKind.Dwell);
            }
            var e = NewEvent(GestureEventType.Cancelled, kind);
            e.Reason = reason;
            Push(e);
        }

        void Enqueue(GestureEventType type, GestureKind kind) => Push(NewEvent(type, kind));

        GestureEvent NewEvent(GestureEventType type, GestureKind kind) => new GestureEvent
        {
            Kind = kind,
            Type = type,
            Origin = _origin,
            Position = _position,
            Delta = _position - _origin,
            Step = _lastStep,
            Progress = _progress,
            Direction = _direction,
            Reason = CancelReason.None,
            Time = _now,
        };

        void Push(in GestureEvent e)
        {
            if (_count == QueueCapacity)
            {
                // Overflow: drop the oldest. Consumers drain every frame, so this never happens in practice.
                _head = (_head + 1) % QueueCapacity;
                _count--;
            }
            _queue[(_head + _count) % QueueCapacity] = e;
            _count++;
        }
    }
}
