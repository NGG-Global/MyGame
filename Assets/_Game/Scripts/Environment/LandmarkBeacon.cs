using UnityEngine;

namespace OpenSkies.World
{
    /// <summary>
    /// The light on a landmark (lighthouse lantern, harbour light). It flashes
    /// slowly while the landmark is the active checkpoint of a mission, and is
    /// dark otherwise: a believable navigation aid instead of a floating marker.
    /// Visual only; it never affects the flight. Allocation-free.
    /// </summary>
    public sealed class LandmarkBeacon : MonoBehaviour
    {
        [Tooltip("Landmark id in the registry (PrototypeWorld).")]
        public string landmarkId;
        [Tooltip("The lit lens, enabled while the light is on.")]
        public Renderer lens;
        [Tooltip("One flash cycle, s.")]
        public float periodSeconds = 2f;
        [Tooltip("Fraction of the cycle the light is on.")]
        [Range(0.05f, 1f)] public float onFraction = 0.35f;

        bool _active;

        public bool Active => _active;
        public bool LightOn => lens != null && lens.enabled;

        public void SetActive(bool active)
        {
            if (active == _active) return;
            _active = active;
            if (!active && lens != null) lens.enabled = false;
        }

        void Awake()
        {
            if (lens != null) lens.enabled = false;
        }

        void Update()
        {
            if (!_active || lens == null) return;
            bool on = FlashOn(Time.time, periodSeconds, onFraction);
            if (lens.enabled != on) lens.enabled = on;
        }

        /// <summary>Whether a light with this period and duty cycle is on at a time.</summary>
        public static bool FlashOn(float time, float periodSeconds, float onFraction)
        {
            if (!(periodSeconds > 0f)) return true;
            float phase = time / periodSeconds;
            phase -= Mathf.Floor(phase);
            return phase < onFraction;
        }
    }
}
