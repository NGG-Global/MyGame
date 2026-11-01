using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// An understated checkpoint symbol: a small diamond over the landmark the
    /// mission is heading for, with its name and distance, drawn only while the
    /// landmark is in front of the camera and inside the screen. No rings in the
    /// sky; the navigation guide's arrow covers the other directions. Takes no
    /// touches. Runs after the camera has moved, so it does not lag behind.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CheckpointMarker : MonoBehaviour
    {
        public Camera viewCamera;
        [Tooltip("The safe area the marker moves in.")]
        public RectTransform area;
        public RectTransform marker;
        public CanvasGroup group;
        public Text nameText;
        public Text distanceText;
        [Tooltip("The symbol sits this far above the landmark's base, m.")]
        public float heightAboveLandmark = 40f;
        [Tooltip("Hidden this close to the screen edge, canvas units.")]
        public float edgeMargin = 40f;
        [Tooltip("Opacity of the marker.")]
        [Range(0.2f, 1f)] public float opacity = 0.85f;

        static readonly NumberStrings NauticalMiles = new NumberStrings(0, 999, v => NumberStrings.Tenths(v) + " NM");

        bool _requested, _visible;
        Vector3 _world;
        int _shownTenths = int.MinValue;

        public bool Visible => _visible;

        /// <summary>Asks for the marker over a world position (call every frame; strings must be cached).</summary>
        public void Present(bool show, Vector3 worldPosition, string landmarkName, float distanceMetres)
        {
            _requested = show;
            if (!show) return;
            _world = worldPosition;
            if (nameText != null && !ReferenceEquals(nameText.text, landmarkName)) nameText.text = landmarkName ?? string.Empty;
            int tenths = Mathf.Clamp(Mathf.RoundToInt(distanceMetres / 185.2f), 0, 9999);
            if (distanceText != null && tenths != _shownTenths)
            {
                _shownTenths = tenths;
                distanceText.text = NauticalMiles.Get(tenths);
            }
        }

        void Awake()
        {
            var symbol = marker != null ? marker.GetComponent<Image>() : null;
            if (symbol != null && symbol.sprite == null) symbol.sprite = HudSprites.Diamond;
            SetVisible(false);
        }

        void LateUpdate()
        {
            bool visible = false;
            if (_requested && viewCamera != null && area != null && marker != null)
            {
                Vector3 screen = viewCamera.WorldToScreenPoint(_world + Vector3.up * heightAboveLandmark);
                if (screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out var local))
                {
                    Rect r = area.rect;
                    visible = local.x > r.xMin + edgeMargin && local.x < r.xMax - edgeMargin
                              && local.y > r.yMin + edgeMargin && local.y < r.yMax - edgeMargin;
                    if (visible) marker.anchoredPosition = local - r.center;
                }
            }
            SetVisible(visible);
        }

        void SetVisible(bool visible)
        {
            if (visible == _visible && group != null && (group.alpha > 0f) == visible) return;
            _visible = visible;
            if (group != null)
            {
                group.alpha = visible ? opacity : 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
        }
    }
}
