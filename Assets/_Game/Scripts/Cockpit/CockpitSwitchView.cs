using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Cockpit
{
    /// <summary>
    /// Moves a cockpit control (toggle, handle, flap lever) to show the position
    /// held in the authoritative <see cref="AircraftSystems"/>, and lights its
    /// indicator when the system is on. It only reads; changes come from the
    /// focus panel, the contextual HUD or the keyboard, all through AircraftSystems.
    /// </summary>
    public sealed class CockpitSwitchView : MonoBehaviour
    {
        public AircraftSystem system;
        [Tooltip("The part that moves (toggle lever, handle, flap lever knob).")]
        public Transform moving;
        public Vector3 offLocalPosition;
        public Vector3 onLocalPosition;
        public Vector3 offLocalEuler;
        public Vector3 onLocalEuler;
        [Tooltip("Optional small light that is on while the system is on (or not at its first position).")]
        public Renderer indicator;
        public Color indicatorOff = new Color(0.15f, 0.15f, 0.15f, 1f);
        public Color indicatorOn = new Color(0.3f, 1f, 0.35f, 1f);
        [Tooltip("Time for the moving part to travel its full range, s.")]
        public float travelSeconds = 0.15f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColor = Shader.PropertyToID("_Color");

        AircraftSystems _systems;
        MaterialPropertyBlock _block;
        int _seenVersion = int.MinValue;
        float _target, _current;

        /// <summary>Position 0-1 the part is moving to (flap detent as a fraction).</summary>
        public float TargetPosition => _target;
        public float CurrentPosition => _current;
        public bool IndicatorLit { get; private set; }

        public void Bind(AircraftSystems systems)
        {
            _systems = systems;
            _seenVersion = int.MinValue;
            Refresh(snap: true);
        }

        void Update() => Refresh(snap: false);

        void Refresh(bool snap)
        {
            if (_systems == null) return;
            if (_systems.Version != _seenVersion)
            {
                _seenVersion = _systems.Version;
                int positions = _systems.PositionCount(system);
                int value = _systems.Get(system);
                _target = positions > 1 ? (float)value / (positions - 1) : 0f;
                SetIndicator(value > 0);
            }
            if (snap) _current = _target;
            else if (!Mathf.Approximately(_current, _target))
                _current = Mathf.MoveTowards(_current, _target, travelSeconds > 0f ? Time.deltaTime / travelSeconds : 1f);
            else return;

            if (moving == null) return;
            moving.localPosition = Vector3.Lerp(offLocalPosition, onLocalPosition, _current);
            moving.localRotation = Quaternion.Euler(Vector3.Lerp(offLocalEuler, onLocalEuler, _current));
        }

        void SetIndicator(bool on)
        {
            IndicatorLit = on;
            if (indicator == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            indicator.GetPropertyBlock(_block);
            Color c = on ? indicatorOn : indicatorOff;
            _block.SetColor(BaseColor, c);
            _block.SetColor(LegacyColor, c);
            indicator.SetPropertyBlock(_block);
        }
    }
}
