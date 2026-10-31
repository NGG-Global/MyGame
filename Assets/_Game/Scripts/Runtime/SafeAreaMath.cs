using UnityEngine;

namespace Game
{
    /// <summary>
    /// Converts Screen.safeArea (pixels) into normalized RectTransform anchors.
    /// </summary>
    public static class SafeAreaMath
    {
        public static bool TryGetAnchors(Rect safeArea, int screenWidth, int screenHeight,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return false;
            }

            anchorMin = new Vector2(
                Mathf.Clamp01(safeArea.xMin / screenWidth),
                Mathf.Clamp01(safeArea.yMin / screenHeight));
            anchorMax = new Vector2(
                Mathf.Clamp01(safeArea.xMax / screenWidth),
                Mathf.Clamp01(safeArea.yMax / screenHeight));
            return true;
        }
    }
}
