using UnityEngine;

namespace OpenSkies.Gestures
{
    /// <summary>Screen-point tests against UI rectangles, for the hit tests transient surfaces register.</summary>
    public static class ScreenRects
    {
        /// <summary>Whether a screen point lies inside a rectangle on any canvas (overlay or camera).</summary>
        public static bool Contains(RectTransform rect, Vector2 screenPoint)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return false;
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera);
        }
    }
}
