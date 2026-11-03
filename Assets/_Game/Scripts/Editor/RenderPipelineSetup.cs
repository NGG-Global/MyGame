using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Creates and assigns the mobile URP asset and the post-processing profile from
    /// code, so the project never depends on render settings someone clicked
    /// together in the Editor. The asset holds every feature that needs its own
    /// shader variants (HDR, soft shadows, two cascades, post-processing); the
    /// graphics presets only switch those features on and off at runtime on a copy.
    /// </summary>
    public static class RenderPipelineSetup
    {
        const string SettingsFolder = "Assets/_Game/Settings";
        const string UrpAssetPath = SettingsFolder + "/URP-Mobile.asset";
        const string UrpRendererPath = SettingsFolder + "/URP-Mobile_Renderer.asset";
        public const string PostProcessProfilePath = SettingsFolder + "/Graphics/PostProcessing.asset";
        /// <summary>URP's own post-processing resources (shaders and textures); the renderer needs them to run the volume effects.</summary>
        const string UrpPostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        /// <summary>Shadow distance of the asset: the presets never go beyond it.</summary>
        public const float MaxShadowDistance = 150f;

        public static void EnsureUrp()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            if (asset == null)
            {
                EditorFolders.Ensure(SettingsFolder);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, UrpRendererPath);
                }
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, UrpAssetPath);
                Debug.Log($"[OpenSkies] Created {UrpAssetPath}");
            }
            ApplyLook(asset, renderer);

            // Never override a pipeline the owner chose; only fill the empty slot.
            if (GraphicsSettings.defaultRenderPipeline == null) GraphicsSettings.defaultRenderPipeline = asset;
        }

        /// <summary>The project's rendering features, applied to the asset on every build so they live in code.</summary>
        static void ApplyLook(UniversalRenderPipelineAsset asset, UniversalRendererData renderer)
        {
            // HDR colour buffer: bloom and ACES tonemapping need values above 1 (the sun, the sea glint,
            // the lights). Battery Saver switches it off at runtime (a format choice, not a variant).
            asset.supportsHDR = true;
            asset.msaaSampleCount = 1;
            asset.renderScale = 1f;
            // Two cascades keep the aircraft's shadow sharp at the airfield and still reach the trees;
            // soft filtering is compiled in and the presets choose it per light.
            asset.shadowDistance = MaxShadowDistance;
            asset.shadowCascadeCount = 2;
            asset.cascade2Split = 0.22f;
            asset.mainLightShadowmapResolution = 2048;
            asset.supportsCameraDepthTexture = false;
            asset.supportsCameraOpaqueTexture = false;
            asset.colorGradingMode = ColorGradingMode.LowDynamicRange; // the LUT path that mobile GPUs handle well
            asset.colorGradingLutSize = 32;
            SetSerialized(asset, "m_SoftShadowsSupported", true);
            SetSerialized(asset, "m_AdditionalLightShadowsSupported", false);
            EditorUtility.SetDirty(asset);

            if (renderer != null)
            {
                if (renderer.postProcessData == null)
                {
                    var data = AssetDatabase.LoadAssetAtPath<PostProcessData>(UrpPostProcessDataPath);
                    if (data == null) Debug.LogWarning($"[OpenSkies] URP post-process data not found at {UrpPostProcessDataPath}; the volume effects will not run.");
                    renderer.postProcessData = data;
                }
                EditorUtility.SetDirty(renderer);
            }
        }

        /// <summary>Sets a serialised field that has no public setter; warns (and leaves it) if the field is not there in this URP version.</summary>
        static void SetSerialized(Object target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[OpenSkies] {target.name}: no serialised field '{field}' (URP version change?); left as is.");
                return;
            }
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The one global post-processing profile: ACES tonemapping, a little saturation and
        /// contrast, a soft vignette, restrained bloom for the sun, the sea glint and the lights.
        /// Created once; its values are re-applied on every build so the tuning lives here.
        /// </summary>
        public static VolumeProfile EnsurePostProcessProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProcessProfilePath);
            if (profile == null)
            {
                EditorFolders.Ensure(Path.GetDirectoryName(PostProcessProfilePath).Replace('\\', '/'));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostProcessProfilePath);
                Debug.Log($"[OpenSkies] Created {PostProcessProfilePath}");
            }

            var tonemapping = Component<Tonemapping>(profile);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            var colour = Component<ColorAdjustments>(profile);
            colour.postExposure.overrideState = true;
            colour.postExposure.value = 0.15f;
            colour.contrast.overrideState = true;
            colour.contrast.value = 8f;
            colour.saturation.overrideState = true;
            colour.saturation.value = 10f;

            var balance = Component<WhiteBalance>(profile);
            balance.temperature.overrideState = true;
            balance.temperature.value = 6f; // a touch warm: late-morning coastal light

            var vignette = Component<Vignette>(profile);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.22f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.5f;

            var bloom = Component<Bloom>(profile);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.05f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.35f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.65f;
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = false; // mobile: the cheap path

            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>The override of this type on the profile, added as a sub-asset when missing.</summary>
        static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) return existing;
            var component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }
    }
}
