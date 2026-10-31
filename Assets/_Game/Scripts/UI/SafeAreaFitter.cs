using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// Keeps its RectTransform inside Screen.safeArea. Put all UI under one of these.
    /// Only touches the RectTransform when the safe area or screen size changes.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _lastSafeArea;
        Vector2Int _lastScreen;

        void Awake()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        void Update()
        {
            if (Screen.safeArea != _lastSafeArea || Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
                Apply();
        }

        void Apply()
        {
            _lastSafeArea = Screen.safeArea;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (!SafeAreaMath.TryGetAnchors(_lastSafeArea, Screen.width, Screen.height, out var min, out var max))
                return;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
