using UnityEngine;

namespace OpenSkies.Cockpit
{
    /// <summary>
    /// A steady frame on the instrument panel around the cockpit item whose
    /// enlarged view or selector is open, so the pilot can see which physical
    /// instrument or switch the panel refers to (also when the selector was
    /// opened from the contextual HUD). It outlines the item's pick area, which
    /// also shows how forgiving the touch target is. It only shows a selection;
    /// it never changes a system. No flashing, no animation.
    /// </summary>
    public sealed class CockpitSelectionView : MonoBehaviour
    {
        [Tooltip("The frame, a child of the instrument panel's world-space canvas. Hidden when nothing is selected.")]
        public RectTransform frame;
        [Tooltip("Extra space around the item's pick area, canvas units.")]
        public float margin = 12f;

        CockpitInteractable _selected;

        /// <summary>The item currently framed, or null.</summary>
        public CockpitInteractable Selected => _selected;
        public bool IsShown => frame != null && frame.gameObject.activeSelf;

        /// <summary>Frames <paramref name="target"/>, or hides the frame when it is null. Cheap when nothing changes.</summary>
        public void Show(CockpitInteractable target)
        {
            if (frame == null) return;
            if (target == null)
            {
                Hide();
                return;
            }
            if (ReferenceEquals(target, _selected) && frame.gameObject.activeSelf) return;
            _selected = target;

            // The frame lies on the panel canvas, whose plane the pick box faces: map the
            // item's centre and its pick extents (metres, along its own axes) into canvas units.
            var canvas = (RectTransform)frame.parent;
            var t = target.transform;
            Vector3 centre = canvas.InverseTransformPoint(t.position);
            float width = canvas.InverseTransformVector(t.right * target.pickSize.x).magnitude;
            float height = canvas.InverseTransformVector(t.up * target.pickSize.y).magnitude;
            frame.anchoredPosition = new Vector2(centre.x, centre.y);
            frame.sizeDelta = new Vector2(width + 2f * margin, height + 2f * margin);
            if (!frame.gameObject.activeSelf) frame.gameObject.SetActive(true);
        }

        public void Hide()
        {
            _selected = null;
            if (frame != null && frame.gameObject.activeSelf) frame.gameObject.SetActive(false);
        }
    }
}
