using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// Scales content designed at a fixed size (instrument faces are built at
    /// 400 × 400 units) to the largest square that fits this rectangle, whenever
    /// its size changes (screen size, orientation, safe area). Children keep
    /// their layout; only their scale changes.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class FitToSquare : MonoBehaviour
    {
        public float designSize = 400f;

        public float CurrentScale { get; private set; } = 1f;

        void OnEnable() => Fit();

        void OnRectTransformDimensionsChange() => Fit();

        public void Fit()
        {
            var rect = (RectTransform)transform;
            Vector2 size = rect.rect.size;
            float side = Mathf.Min(size.x, size.y);
            if (side <= 0f || designSize <= 0f) return;
            CurrentScale = side / designSize;
            for (int i = 0; i < rect.childCount; i++)
                rect.GetChild(i).localScale = new Vector3(CurrentScale, CurrentScale, 1f);
        }
    }
}
