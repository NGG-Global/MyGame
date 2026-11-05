using System;
using UnityEngine;

namespace OpenSkies.UI
{
    [Serializable]
    public sealed class EntryStationSettings
    {
        [Tooltip("Finger travel that moves the presentation one station, canvas units.")]
        public float stationWidth = 900f;
        [Tooltip("Release velocity (units per second) is projected this long ahead to choose the station.")]
        public float velocityProjectionSeconds = 0.2f;
        [Tooltip("Time to settle on a station after release, s.")]
        public float settleSeconds = 0.35f;
        [Tooltip("The presentation can be pulled this fraction of a station beyond the ends, compressed.")]
        [Range(0f, 0.5f)] public float overscroll = 0.25f;
    }

    /// <summary>
    /// The entry screen's three stations (First Flight, Free Flight, Landing
    /// Challenge) as a continuous position: a horizontal drag moves it, release
    /// settles on the nearest station with the release velocity taken into account,
    /// chips and keys jump to a station. The presentation (camera, text) reads
    /// <see cref="Position"/> continuously. Pure.
    /// </summary>
    public sealed class EntryStationModel
    {
        public const int Count = 3;

        readonly EntryStationSettings _s;
        float _position, _target, _from, _settleProgress = 1f;
        float _dragStartPosition, _dragStartX;
        bool _dragging;
        int _station;

        public EntryStationModel(EntryStationSettings settings = null)
        {
            _s = settings ?? new EntryStationSettings();
        }

        public EntryStationSettings Settings => _s;
        /// <summary>Continuous position in stations: 0 = the first station, 2 = the last.</summary>
        public float Position => _position;
        /// <summary>The station selected (rested on, or settling to).</summary>
        public int Station => _station;
        public bool IsDragging => _dragging;
        public bool IsSettling => _settleProgress < 1f;
        /// <summary>How far the presentation is from its station, 0-1 (for fading the description while moving).</summary>
        public float Displacement => Mathf.Clamp01(Mathf.Abs(_position - _station));

        public void Set(int station, bool instant)
        {
            _station = Mathf.Clamp(station, 0, Count - 1);
            if (instant) { _position = _station; _settleProgress = 1f; _dragging = false; }
            else SettleTo(_station);
        }

        public void BeginDrag(float fingerX)
        {
            _dragging = true;
            _dragStartPosition = _position;
            _dragStartX = fingerX;
            _settleProgress = 1f;
        }

        /// <summary>Dragging left shows the next station (the presentation follows the finger).</summary>
        public void Drag(float fingerX)
        {
            if (!_dragging || _s.stationWidth <= 0f) return;
            float raw = _dragStartPosition - (fingerX - _dragStartX) / _s.stationWidth;
            float max = Count - 1;
            if (raw < 0f) raw = -Compress(-raw);
            else if (raw > max) raw = max + Compress(raw - max);
            _position = raw;
        }

        float Compress(float beyond) => _s.overscroll * beyond / (beyond + _s.overscroll);

        /// <summary>
        /// Released with a horizontal velocity (units per second): settles on the nearest station ahead of
        /// the movement, between the two stations the finger left the presentation between. A flick carries
        /// it to the next station at most, never past one the finger did not reach.
        /// </summary>
        public int Release(float velocityX)
        {
            if (!_dragging) return _station;
            _dragging = false;
            float projected = _position - Mathf.Clamp(velocityX, -6000f, 6000f) * _s.velocityProjectionSeconds / Mathf.Max(1f, _s.stationWidth);
            int lower = Mathf.Clamp(Mathf.FloorToInt(_position), 0, Count - 1);
            int upper = Mathf.Clamp(Mathf.CeilToInt(_position), 0, Count - 1);
            SettleTo(Mathf.Clamp(Mathf.RoundToInt(projected), lower, upper));
            return _station;
        }

        public void Step(int direction) => SettleTo(Mathf.Clamp(_station + Math.Sign(direction), 0, Count - 1));

        void SettleTo(int station)
        {
            _station = station;
            _target = station;
            _from = _position;
            _settleProgress = _s.settleSeconds > 0f ? 0f : 1f;
            if (_settleProgress >= 1f) _position = _target;
        }

        public void Tick(float deltaTime)
        {
            if (_dragging || _settleProgress >= 1f) return;
            _settleProgress = Mathf.Min(1f, _settleProgress + deltaTime / Mathf.Max(0.01f, _s.settleSeconds));
            _position = Mathf.Lerp(_from, _target, HudEasing.EaseInOut(_settleProgress));
        }
    }
}
