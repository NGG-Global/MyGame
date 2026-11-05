using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// A raycast target that draws nothing (the UI layer's own; layers do not share code). In its own
    /// file because Unity saves a component in a scene only when its class has a script file of the
    /// same name: declared inside another file, every touch area built by the scene builder became a
    /// missing script in the player ("The file 'level0' is corrupted").
    /// </summary>
    public sealed class RaycastOnlyHudGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
