using OpenSkies.Awareness;
using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>Progress towards one checkpoint. Values are stable.</summary>
    public enum WaypointState
    {
        Approaching = 0,
        /// <summary>Flown within the arrival radius, or close past it.</summary>
        Reached = 1,
        /// <summary>Flown past at a distance and now moving away: the player can turn back or carry on.</summary>
        Missed = 2,
    }

    /// <summary>
    /// Follows the aircraft towards a checkpoint. Reaching it is lenient: flying
    /// within the arrival radius counts, and so does passing within
    /// <see cref="TutorialSettings.passRadiusFactor"/> times that radius. A wide
    /// miss is reported, never punished: the mission decides how to recover
    /// (turn back, or carry on to the next checkpoint). Plain C#.
    /// </summary>
    public sealed class WaypointTracker
    {
        NavigationTarget _target;
        float _closest;
        float _distance;
        WaypointState _state;
        bool _active;

        public NavigationTarget Target => _target;
        public bool Active => _active;
        public WaypointState State => _state;
        public float DistanceMetres => _distance;
        public float ClosestMetres => _closest;

        public void Begin(in NavigationTarget target)
        {
            _target = target;
            _active = target.IsDefined;
            _closest = float.MaxValue;
            _distance = float.MaxValue;
            _state = WaypointState.Approaching;
        }

        public void Stop()
        {
            _active = false;
            _state = WaypointState.Approaching;
        }

        /// <summary>Updates with the aircraft position; returns the state.</summary>
        public WaypointState Update(Vector3 position, TutorialSettings settings)
        {
            if (!_active) return _state;
            Vector3 offset = _target.Position - position;
            offset.y = 0f;
            _distance = offset.magnitude;
            if (_distance < _closest) _closest = _distance;
            if (_state == WaypointState.Reached) return _state;

            float radius = Mathf.Max(_target.ArrivalRadiusMetres, 1f);
            if (_distance <= radius) _state = WaypointState.Reached;
            else if (_distance > _closest + settings.missedMarginMetres * 0.33f && _closest <= radius * settings.passRadiusFactor)
                _state = WaypointState.Reached; // passed close by: that counts
            else if (_distance > _closest + settings.missedMarginMetres && _closest < float.MaxValue)
                _state = WaypointState.Missed;
            else if (_state == WaypointState.Missed && _distance < _closest + settings.missedMarginMetres * 0.5f)
                _state = WaypointState.Approaching; // turned back towards it
            return _state;
        }
    }
}
