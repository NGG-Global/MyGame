using OpenSkies.Cockpit;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>References to the parts of the 3D cockpit, built by <see cref="CockpitFactory"/>.</summary>
    public sealed class CockpitRig : MonoBehaviour
    {
        public Canvas instrumentPanel;
        [Tooltip("Index = InstrumentKind.")]
        public InstrumentFace[] instruments = new InstrumentFace[CockpitFocusPanel.InstrumentCount];
        public CockpitInteractable[] interactables = new CockpitInteractable[0];
        public CockpitSwitchView[] switchViews = new CockpitSwitchView[0];
        [Tooltip("Frame around the item whose enlarged view or selector is open.")]
        public CockpitSelectionView selection;

        public CockpitInteractable Find(CockpitItem item)
        {
            for (int i = 0; i < interactables.Length; i++)
                if (interactables[i] != null && interactables[i].item == item) return interactables[i];
            return null;
        }
    }
}
