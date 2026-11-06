using System;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.Gestures
{
    /// <summary>
    /// A raycast target that draws nothing and accepts only the screen points its
    /// filter accepts: the open systems arc takes presses on its band and nowhere
    /// else, so a square rectangle around the handle never covers the throttle or
    /// the brake. Without a filter it behaves as a plain rectangle. In its own file
    /// because Unity saves a component in a scene only when its class has a script
    /// file of the same name.
    /// </summary>
    public sealed class HotspotShape : Graphic, ICanvasRaycastFilter
    {
        /// <summary>Screen point and event camera to "inside". Null: the whole rectangle.</summary>
        public Func<Vector2, Camera, bool> Filter { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) =>
            Filter == null || Filter(screenPoint, eventCamera);
    }
}
