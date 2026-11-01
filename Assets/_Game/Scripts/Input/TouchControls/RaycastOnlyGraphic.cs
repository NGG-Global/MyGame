using UnityEngine.UI;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// A raycast target that draws nothing: the joystick region and the throttle's
    /// large touch area catch touches without a transparent quad being drawn over
    /// a large part of the screen every frame (overdraw on mobile GPUs).
    /// </summary>
    public sealed class RaycastOnlyGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
