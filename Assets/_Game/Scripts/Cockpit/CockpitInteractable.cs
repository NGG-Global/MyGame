using UnityEngine;

namespace OpenSkies.Cockpit
{
    /// <summary>
    /// Something in the 3D cockpit the pilot can tap: an instrument (opens a large
    /// view) or a control (opens a touch-friendly selector; a tap never changes
    /// the system directly). The pick volume is larger than the visible part so
    /// taps need not be precise. No collider: picking is done by
    /// <see cref="CockpitPicking"/>, so the cockpit never affects the physics.
    /// </summary>
    public sealed class CockpitInteractable : MonoBehaviour
    {
        public CockpitItem item;
        [Tooltip("Accessible name: read by assistive technology and used as the focus view title.")]
        public string displayName;
        [Tooltip("Accessible hint: what it shows or does and how to use it.")]
        public string accessibilityHint;
        [Tooltip("Pick volume in metres along the object's own axes, centred on it.")]
        public Vector3 pickSize = new Vector3(0.08f, 0.08f, 0.06f);

        public bool IsInstrument => CockpitItems.IsInstrument(item);

        public PickBox WorldPickBox
        {
            get
            {
                var t = transform;
                return new PickBox
                {
                    Centre = t.position, Right = t.right, Up = t.up, Forward = t.forward, HalfExtents = pickSize * 0.5f,
                };
            }
        }

        /// <summary>Fills in the accessible name and hint from the item catalogue when they are empty.</summary>
        public void ApplyDefaultLabels()
        {
            if (string.IsNullOrEmpty(displayName)) displayName = CockpitItems.DisplayName(item);
            if (string.IsNullOrEmpty(accessibilityHint)) accessibilityHint = CockpitItems.AccessibilityHint(item);
        }

        void Awake() => ApplyDefaultLabels();
    }
}
