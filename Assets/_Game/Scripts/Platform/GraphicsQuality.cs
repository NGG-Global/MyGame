using OpenSkies.Options;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenSkies.Platform
{
    /// <summary>What a graphics preset changes. Rendering only: nothing here can reach the flight model or the physics step.</summary>
    public struct GraphicsProfile
    {
        /// <summary>Fraction of the screen resolution the 3D view renders at (the UI is always native).</summary>
        public float RenderScale;
        public bool Shadows;
        /// <summary>Main light shadow distance, m.</summary>
        public float ShadowDistance;
        public int ShadowResolution;
        /// <summary>1 (off), 2 or 4.</summary>
        public int Msaa;
        /// <summary>LOD bias: scales the distance at which detail (trees, small objects) is drawn or culled.</summary>
        public float LodBias;
        /// <summary>Camera far clip, m. Fog hides the edge.</summary>
        public float ViewDistance;
    }

    /// <summary>
    /// The three graphics presets. Values are first estimates for mid-range Android
    /// phones, to be confirmed by measurements on devices (docs/MOBILE_PERFORMANCE_REPORT.md).
    /// </summary>
    public static class GraphicsPresets
    {
        public static GraphicsProfile For(GraphicsPreset preset)
        {
            switch (preset)
            {
                case GraphicsPreset.BatterySaver:
                    return new GraphicsProfile
                    {
                        RenderScale = 0.75f, Shadows = false, ShadowDistance = 0f, ShadowResolution = 512, Msaa = 1, LodBias = 0.6f,
                        ViewDistance = 4500f,
                    };
                case GraphicsPreset.HighQuality:
                    return new GraphicsProfile
                    {
                        RenderScale = 1f, Shadows = true, ShadowDistance = 120f, ShadowResolution = 2048, Msaa = 2, LodBias = 1.5f,
                        ViewDistance = 7000f,
                    };
                default:
                    return new GraphicsProfile
                    {
                        RenderScale = 0.9f, Shadows = true, ShadowDistance = 60f, ShadowResolution = 1024, Msaa = 1, LodBias = 1f,
                        ViewDistance = 6000f,
                    };
            }
        }

        public static string Label(GraphicsPreset preset)
        {
            switch (preset)
            {
                case GraphicsPreset.BatterySaver: return "BATTERY SAVER";
                case GraphicsPreset.HighQuality: return "HIGH QUALITY";
                default: return "BALANCED";
            }
        }
    }

    /// <summary>
    /// Applies graphics presets at runtime. URP settings change on a runtime copy of
    /// the project's pipeline asset (assigned as the quality level's pipeline), so the
    /// asset on disk is never modified, and the previous pipeline is restored on
    /// <see cref="Dispose"/>. Features that change shader variants (cascades, soft
    /// shadows, HDR) are left as the project asset sets them, because variants the
    /// build stripped cannot be switched on at runtime. Without URP (some test
    /// set-ups) only the engine-wide settings apply.
    /// </summary>
    public sealed class GraphicsQuality : System.IDisposable
    {
        readonly Light _sun;
        readonly Camera _camera;
        readonly LightShadows _sunShadows;
        readonly float _lodBias;
        RenderPipelineAsset _previousOverride;
        UniversalRenderPipelineAsset _runtimeAsset;
        bool _overrideInstalled;

        public GraphicsPreset Preset { get; private set; } = GraphicsPreset.Balanced;
        public GraphicsProfile Profile { get; private set; } = GraphicsPresets.For(GraphicsPreset.Balanced);
        public bool HasUrp => _runtimeAsset != null;

        /// <param name="sun">The scene's main light (its shadows switch with the preset); may be null.</param>
        /// <param name="camera">The view camera (its far clip follows the preset); may be null.</param>
        public GraphicsQuality(Light sun, Camera camera)
        {
            _sun = sun;
            _camera = camera;
            _sunShadows = sun != null ? sun.shadows : LightShadows.None;
            if (_sunShadows == LightShadows.None) _sunShadows = LightShadows.Hard;
            _lodBias = QualitySettings.lodBias;
        }

        public void Apply(GraphicsPreset preset)
        {
            Preset = preset;
            var p = GraphicsPresets.For(preset);
            Profile = p;
            EnsureRuntimeAsset();
            if (_runtimeAsset != null)
            {
                _runtimeAsset.renderScale = p.RenderScale;
                _runtimeAsset.shadowDistance = p.Shadows ? p.ShadowDistance : 0f;
                _runtimeAsset.mainLightShadowmapResolution = p.ShadowResolution;
                _runtimeAsset.msaaSampleCount = p.Msaa;
            }
            QualitySettings.lodBias = p.LodBias;
            if (_sun != null) _sun.shadows = p.Shadows ? _sunShadows : LightShadows.None;
            if (_camera != null) _camera.farClipPlane = p.ViewDistance;
        }

        void EnsureRuntimeAsset()
        {
            if (_overrideInstalled) return;
            _overrideInstalled = true;
            _previousOverride = QualitySettings.renderPipeline;
            var source = (_previousOverride != null ? _previousOverride : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (source == null) return;
            _runtimeAsset = Object.Instantiate(source);
            _runtimeAsset.name = source.name + " (runtime)";
            _runtimeAsset.hideFlags = HideFlags.DontSave;
            QualitySettings.renderPipeline = _runtimeAsset;
        }

        /// <summary>Puts back the project's pipeline asset, the LOD bias and the sun's shadows (scene unload, quitting, tests).</summary>
        public void Dispose()
        {
            if (_runtimeAsset != null)
            {
                if (QualitySettings.renderPipeline == _runtimeAsset) QualitySettings.renderPipeline = _previousOverride;
                Object.Destroy(_runtimeAsset);
                _runtimeAsset = null;
            }
            _overrideInstalled = false;
            if (_sun != null) _sun.shadows = _sunShadows;
            QualitySettings.lodBias = _lodBias; // project settings are not changed by playing
        }
    }

    /// <summary>Frame-rate targets. The physics step is never changed with them.</summary>
    public static class FrameRatePolicy
    {
        public const int Baseline = 30;
        public const int High = 60;

        /// <summary>60 needs a display that refreshes at least about 60 times a second.</summary>
        public static bool Supports60(float refreshRateHz) => float.IsNaN(refreshRateHz) || refreshRateHz <= 0f || refreshRateHz >= 59f;

        public static int Target(FrameRatePreference preference, float refreshRateHz) =>
            preference == FrameRatePreference.Fps60 && Supports60(refreshRateHz) ? High : Baseline;

        /// <summary>The display's refresh rate, or 0 when unknown.</summary>
        public static float DisplayRefreshRate()
        {
            var ratio = Screen.currentResolution.refreshRateRatio;
            double value = ratio.value;
            return double.IsNaN(value) || double.IsInfinity(value) ? 0f : (float)value;
        }

        /// <summary>Sets the frame-rate target. vSync stays off so the target applies on every platform.</summary>
        public static int Apply(FrameRatePreference preference)
        {
            int target = Target(preference, DisplayRefreshRate());
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = target;
            return target;
        }
    }
}
