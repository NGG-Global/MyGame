using UnityEngine.UI;

namespace OpenSkies.Cockpit
{
    /// <summary>
    /// A raycast target that draws nothing: the full-screen surface behind every
    /// control that receives the touches nothing else took. No geometry means no
    /// full-screen overdraw on mobile GPUs.
    /// </summary>
    public sealed class TouchSurfaceGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
