using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Editor
{
    /// <summary>
    /// Headless build entry points for GameCI (unity-builder `buildMethod`).
    /// All project configuration that matters for a build is set here in code,
    /// so it never depends on someone clicking through the Editor.
    /// </summary>
    public static class BuildScript
    {
        const string ProductName = "MyGame";
        // PLACEHOLDER: replace before the first Play Store upload. It cannot change after publishing.
        const string AndroidIdentifier = "com.dor.mygame";
        // Google Play: from 31 Aug 2026 new apps and updates must target API 36 (Android 16).
        const int AndroidTargetApiLevel = 36;

        const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        const string SettingsFolder = "Assets/_Game/Settings";
        const string UrpAssetPath = SettingsFolder + "/URP-Mobile.asset";
        const string UrpRendererPath = SettingsFolder + "/URP-Mobile_Renderer.asset";

        public static void BuildWebGL() => Run(BuildTarget.WebGL, "Builds/WebGL", ConfigureWebGL);

        public static void BuildAndroid() => Run(BuildTarget.Android, $"Builds/Android/{ProductName}.apk", ConfigureAndroid);

        static void Run(BuildTarget target, string defaultPath, Action configure)
        {
            try
            {
                EnsureMainScene();
                EnsureUrp();
                ConfigureCommon();
                configure();
                AssetDatabase.SaveAssets();

                var options = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = GetArg("-customBuildPath") ?? defaultPath,
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                    options = BuildOptions.None,
                };

                Debug.Log($"[BuildScript] Building {target} to {options.locationPathName}");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[BuildScript] Result: {summary.result}, errors: {summary.totalErrors}, size: {summary.totalSize} bytes");

                if (summary.result != BuildResult.Succeeded)
                    Fail($"Build {summary.result}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(e.Message);
            }
        }

        static void Fail(string reason)
        {
            Debug.LogError($"[BuildScript] FAILED: {reason}");
            EditorApplication.Exit(1);
        }

        static void EnsureMainScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                // Intentionally empty: Bootstrap builds everything from code.
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException($"Could not save {ScenePath}");
                Debug.Log($"[BuildScript] Created {ScenePath}");
            }

            // Main first, keep any other scenes that are already registered.
            var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
        }

        static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return; // never override an existing choice

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (asset == null)
            {
                Directory.CreateDirectory(SettingsFolder);
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, UrpRendererPath);

                asset = UniversalRenderPipelineAsset.Create(renderer);
                // Mobile defaults: no HDR, no MSAA, native resolution, short shadows,
                // no extra depth/opaque copies. Post-processing data is left unset (off).
                asset.supportsHDR = false;
                asset.msaaSampleCount = 1;
                asset.renderScale = 1f;
                asset.shadowDistance = 20f;
                asset.supportsCameraDepthTexture = false;
                asset.supportsCameraOpaqueTexture = false;
                AssetDatabase.CreateAsset(asset, UrpAssetPath);
                Debug.Log($"[BuildScript] Created {UrpAssetPath}");
            }

            GraphicsSettings.defaultRenderPipeline = asset;
        }

        static void ConfigureCommon()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            var version = GetArg("-buildVersion");
            if (!string.IsNullOrEmpty(version) && version != "none")
                PlayerSettings.bundleVersion = version;
        }

        static void ConfigureAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidIdentifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)AndroidTargetApiLevel;
            EditorUserBuildSettings.buildAppBundle = false; // APK for sideloading playtests

            if (int.TryParse(GetArg("-androidVersionCode"), out var versionCode) && versionCode > 0)
                PlayerSettings.Android.bundleVersionCode = versionCode;
        }

        static void ConfigureWebGL()
        {
            // GitHub Pages cannot set Content-Encoding headers, so ship uncompressed files.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
        }

        static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name && !args[i + 1].StartsWith("-"))
                    return args[i + 1];
            return null;
        }
    }
}
