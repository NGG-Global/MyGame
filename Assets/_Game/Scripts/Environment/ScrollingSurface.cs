using UnityEngine;

namespace OpenSkies.World
{
    /// <summary>
    /// Moves a surface's texture layers at a steady speed: the sea's two wave
    /// layers, the cloud deck drifting with the wind. It works on its own copy of
    /// the material (never the shared asset), sets two offsets per frame and
    /// allocates nothing. Visual only.
    /// </summary>
    public sealed class ScrollingSurface : MonoBehaviour
    {
        [Tooltip("Base map offset speed, in texture repeats per second.")]
        public Vector2 baseSpeed = new Vector2(0.01f, 0.004f);
        [Tooltip("Detail map offset speed, in texture repeats per second (0 if the material has no detail maps).")]
        public Vector2 detailSpeed = new Vector2(-0.02f, 0.03f);

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int DetailAlbedoMap = Shader.PropertyToID("_DetailAlbedoMap");

        Material _material;
        Vector2 _baseOffset, _detailOffset;
        bool _hasDetail;

        public Vector2 BaseOffset => _baseOffset;

        void Awake()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer == null) return;
            _material = renderer.material; // an instance of this renderer's material
            _hasDetail = _material.HasProperty(DetailAlbedoMap);
        }

        void Update() => Advance(Time.deltaTime);

        /// <summary>Advances the offsets by a time step (also used by tests).</summary>
        public void Advance(float deltaTime)
        {
            _baseOffset = Wrap(_baseOffset + baseSpeed * deltaTime);
            _detailOffset = Wrap(_detailOffset + detailSpeed * deltaTime);
            if (_material == null) return;
            _material.SetTextureOffset(BaseMap, _baseOffset);
            if (_hasDetail) _material.SetTextureOffset(DetailAlbedoMap, _detailOffset);
        }

        static Vector2 Wrap(Vector2 v) => new Vector2(Mathf.Repeat(v.x, 1f), Mathf.Repeat(v.y, 1f));

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
