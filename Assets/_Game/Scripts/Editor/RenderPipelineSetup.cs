using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Creates and assigns the mobile URP asset from code, so the project never
    /// depends on render settings someone clicked together in the Editor.
    /// </summary>
    public static class RenderPipelineSetup
    {
        const string SettingsFolder = "Assets/_Game/Settings";
        const string UrpAssetPath = SettingsFolder + "/URP-Mobile.asset";
        const string UrpRendererPath = SettingsFolder + "/URP-Mobile_Renderer.asset";

        public static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return; // never override an existing choice

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (asset == null)
            {
                EditorFolders.Ensure(SettingsFolder);
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, UrpRendererPath);

                asset = UniversalRenderPipelineAsset.Create(renderer);
                // Mobile defaults: no HDR, no MSAA, native resolution, moderate shadow
                // distance, no extra depth/opaque copies. Post-processing data is left
                // unset (off) until a graphics-quality milestone.
                asset.supportsHDR = false;
                asset.msaaSampleCount = 1;
                asset.renderScale = 1f;
                asset.shadowDistance = 60f;
                // One cascade and a 1024 shadow map: the graphics presets change distance, resolution and
                // MSAA at runtime, never features that need other shader variants (cascades, soft shadows).
                asset.shadowCascadeCount = 1;
                asset.mainLightShadowmapResolution = 1024;
                asset.supportsCameraDepthTexture = false;
                asset.supportsCameraOpaqueTexture = false;
                AssetDatabase.CreateAsset(asset, UrpAssetPath);
                Debug.Log($"[OpenSkies] Created {UrpAssetPath}");
            }

            GraphicsSettings.defaultRenderPipeline = asset;
        }
    }
}
