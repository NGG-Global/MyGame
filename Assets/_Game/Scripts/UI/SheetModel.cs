using System;
using UnityEngine;

namespace OpenSkies.UI
{
    [Serializable]
    public sealed class SheetSettings
    {
        [Tooltip("Height of the first stop (resume, restart, change mode), canvas units.")]
        public float firstStop = 300f;
        [Tooltip("Height of the second stop (settings), canvas units; limited to the screen at runtime.")]
        public float secondStop = 860f;
        [Tooltip("Dragging below this share of the first stop and releasing dismisses the sheet (resumes).")]
        [Range(0.1f, 0.9f)] public float dismissShare = 0.55f;
        [Tooltip("Release velocity (units per second) is projected this long ahead to choose the stop.")]
        public float velocityProjectionSeconds = 0.18f;
        [Tooltip("The sheet can be pulled this far beyond its top stop, compressed.")]
        public float overTravel = 60f;
        [Tooltip("Time to settle on a stop after release, s.")]
        public float settleSeconds = HudStyle.SheetSettleSeconds;
    }

    /// <summary>
    /// A bottom sheet with progressive stops as data: closed, a first stop, a second
    /// stop. Dragging moves it continuously (with rubber-banding past the top), release
    /// settles on the nearest stop taking the release velocity into account, and a
    /// release below a share of the first stop dismisses it. Pure; the view only draws
    /// the height.
    /// </summary>
    public sealed class SheetModel
    {
        public const int Closed = 0;
        public const int FirstStop = 1;
        public const int SecondStop = 2;

        readonly SheetSettings _s;
        readonly float[] _stops = new float[3];
        float _height, _target, _settleFrom, _settleProgress = 1f;
        float _dragStartHeight, _dragStartY;
        bool _dragging;
        int _stop;

        public SheetModel(SheetSettings settings = null)
        {
            _s = settings ?? new SheetSettings();
            SetLimits(1080f);
        }

        public SheetSettings Settings => _s;
        /// <summary>Visible height now, canvas units.</summary>
        public float Height => _height;
        /// <summary>The stop the sheet rests on or is settling to.</summary>
        public int Stop => _stop;
        public bool IsDragging => _dragging;
        public bool IsSettling => _settleProgress < 1f;
        public bool IsOpen => _stop != Closed || _height > 0.5f;
        public float StopHeight(int stop) => _stops[Mathf.Clamp(stop, Closed, SecondStop)];
        /// <summary>0 at the first stop to 1 at the second, for revealing the second stop's content progressively.</summary>
        public float Depth => Mathf.InverseLerp(_stops[FirstStop], _stops[SecondStop], _height);

        /// <summary>The second stop never exceeds the screen height less a margin.</summary>
        public void SetLimits(float screenHeight)
        {
            _stops[Closed] = 0f;
            _stops[FirstStop] = Mathf.Max(96f, _s.firstStop);
            _stops[SecondStop] = Mathf.Clamp(_s.secondStop, _stops[FirstStop] + 96f, Mathf.Max(_stops[FirstStop] + 96f, screenHeight - 48f));
        }

        /// <summary>Opens straight to a stop (paused: the first stop).</summary>
        public void Open(int stop, bool instant)
        {
            _stop = Mathf.Clamp(stop, FirstStop, SecondStop);
            if (instant) { _height = _stops[_stop]; _settleProgress = 1f; }
            else SettleTo(_stop);
        }

        public void Close(bool instant)
        {
            _stop = Closed;
            _dragging = false;
            if (instant) { _height = 0f; _settleProgress = 1f; }
            else SettleTo(Closed);
        }

        public void BeginDrag(float fingerY)
        {
            _dragging = true;
            _dragStartHeight = _height;
            _dragStartY = fingerY;
            _settleProgress = 1f;
        }

        /// <summary>The sheet follows the finger: up is higher.</summary>
        public void Drag(float fingerY)
        {
            if (!_dragging) return;
            float raw = _dragStartHeight + (fingerY - _dragStartY);
            float top = _stops[SecondStop];
            if (raw > top)
            {
                float beyond = raw - top;
                raw = top + _s.overTravel * beyond / (beyond + _s.overTravel);
            }
            _height = Mathf.Max(0f, raw);
        }

        /// <summary>
        /// Released with a velocity (units per second, up positive): settles on a stop, or dismisses.
        /// The finger's own height decides which two stops are within reach (the ones it is between);
        /// the velocity, projected ahead, chooses between them. A flick never skips a stop, however
        /// fast: from the second stop a pull down reaches the first, and dismissal needs the finger
        /// itself below the first stop.
        /// </summary>
        public int Release(float velocity)
        {
            if (!_dragging) return _stop;
            _dragging = false;
            float projected = _height + Mathf.Clamp(velocity, -4000f, 4000f) * _s.velocityProjectionSeconds;
            int chosen;
            if (_height < _stops[FirstStop])
                chosen = projected < _stops[FirstStop] * _s.dismissShare ? Closed : FirstStop;
            else
                chosen = Mathf.Abs(projected - _stops[SecondStop]) < Mathf.Abs(projected - _stops[FirstStop]) ? SecondStop : FirstStop;
            SettleTo(chosen);
            return _stop;
        }

        /// <summary>Jumps to a stop (a tap on a section chip, a keyboard key).</summary>
        public void GoTo(int stop) => SettleTo(Mathf.Clamp(stop, Closed, SecondStop));

        void SettleTo(int stop)
        {
            _stop = stop;
            _target = _stops[stop];
            _settleFrom = _height;
            _settleProgress = _s.settleSeconds > 0f ? 0f : 1f;
            if (_settleProgress >= 1f) _height = _target;
        }

        /// <summary>Advances the settle animation (one soft overshoot, ease-out). With reduced motion pass a large step.</summary>
        public void Tick(float deltaTime)
        {
            if (_dragging || _settleProgress >= 1f) return;
            _settleProgress = Mathf.Min(1f, _settleProgress + deltaTime / Mathf.Max(0.01f, _s.settleSeconds));
            float t = _target > _settleFrom ? HudEasing.SoftSpring(_settleProgress, HudStyle.Overshoot) : HudEasing.EaseOut(_settleProgress);
            _height = Mathf.LerpUnclamped(_settleFrom, _target, t);
            if (_settleProgress >= 1f) _height = _target;
            if (_height < 0f) _height = 0f;
        }
    }
}
