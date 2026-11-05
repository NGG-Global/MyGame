using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Headless build entry points for GameCI (unity-builder `buildMethod`).
    /// All project configuration that matters for a build is set here in code,
    /// so it never depends on someone clicking through the Editor.
    /// </summary>
    public static class BuildScript
    {
        const string ProductName = "Open Skies"; // working title
        // PLACEHOLDER: replace before the first Play Store upload. It cannot change after publishing.
        const string AndroidIdentifier = "com.dor.openskies";
        // Google Play: from 31 Aug 2026 new apps and updates must target API 36 (Android 16).
        const int AndroidTargetApiLevel = 36;

        // ---- CI entry points (GameCI buildMethod). -developmentBuild enables the development overlays.

        public static void BuildWebGL() => Run(BuildTarget.WebGL, "Builds/WebGL", ConfigureWebGL, HasArg("-developmentBuild"));

        public static void BuildAndroid() => Run(BuildTarget.Android, "Builds/Android/OpenSkies.apk", ConfigureAndroid, HasArg("-developmentBuild"));

        // ---- Editor menu: the same configuration, so local builds match CI builds.

        [MenuItem("Open Skies/Build Android APK (Development)", false, 20)]
        static void BuildAndroidDevelopmentFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                Run(BuildTarget.Android, "Builds/Android/OpenSkies-dev.apk", ConfigureAndroid, development: true);
        }

        [MenuItem("Open Skies/Build Android APK (Release)", false, 21)]
        static void BuildAndroidReleaseFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                Run(BuildTarget.Android, "Builds/Android/OpenSkies.apk", ConfigureAndroid, development: false);
        }

        /// <summary>
        /// Applies the Android player settings without building, so Build Profiles >
        /// Build And Run uses the same configuration as CI.
        /// </summary>
        [MenuItem("Open Skies/Apply Android Player Settings", false, 40)]
        public static void ApplyAndroidSettings()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RenderPipelineSetup.EnsureUrp();
            EnsurePrototypeScene();
            ConfigureCommon();
            ConfigureAndroid();
            AssetDatabase.SaveAssets();
            Debug.Log("[BuildScript] Android player settings applied.");
        }

        static void Run(BuildTarget target, string defaultPath, Action configure, bool development)
        {
            try
            {
                RenderPipelineSetup.EnsureUrp();
                EnsurePrototypeScene();
                EnsureNoMissingScripts();
                ConfigureCommon();
                configure();
                AssetDatabase.SaveAssets();

                var options = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = GetArg("-customBuildPath") ?? defaultPath,
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                    // Development builds show the overlays (telemetry, touch diagnostics) on device.
                    options = development ? BuildOptions.Development : BuildOptions.None,
                };

                Debug.Log($"[BuildScript] Building {target} to {options.locationPathName}");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[BuildScript] Result: {summary.result}, errors: {summary.totalErrors}, size: {summary.totalSize} bytes");
                ReportForGameCi(summary);

                if (summary.result != BuildResult.Succeeded)
                    Fail($"Build {summary.result}");
                else if (!Application.isBatchMode)
                    EditorUtility.RevealInFinder(options.locationPathName);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(e.Message);
            }
        }

        /// <summary>
        /// The build results block and the "Build succeeded!" line that GameCI's build validation looks
        /// for; its own default build script prints them, so a custom buildMethod must print them itself,
        /// or every successful build is reported as "There was an error building the project". Plain
        /// console output: Unity appends a stack trace to Debug.Log lines in batch mode.
        /// </summary>
        static void ReportForGameCi(BuildSummary summary)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine();
            report.AppendLine("###########################");
            report.AppendLine("#      Build results      #");
            report.AppendLine("###########################");
            report.AppendLine();
            report.AppendLine($"Duration: {summary.totalTime}");
            report.AppendLine($"Warnings: {summary.totalWarnings}");
            report.AppendLine($"Errors: {summary.totalErrors}");
            report.AppendLine($"Size: {summary.totalSize} bytes");
            report.AppendLine();
            report.AppendLine(summary.result == BuildResult.Succeeded ? "Build succeeded!" : "Build failed!");
            Console.WriteLine(report.ToString());
        }

        static void Fail(string reason)
        {
            Debug.LogError($"[BuildScript] FAILED: {reason}");
            // Exit code for CI; never close an interactive Editor.
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else EditorUtility.DisplayDialog("Open Skies build failed", reason + "\n\nSee the Console for details.", "OK");
        }

        /// <summary>
        /// Reloads the prototype scene from disk and fails the build if any object in it has a missing
        /// script. A component whose class has no script file of the same name works while the scene is
        /// in memory but is saved without its script, and the player then reads the scene as corrupted
        /// ("The file 'level0' is corrupted! [Position out of bounds!]"). This also catches a scene saved
        /// before such a fix: rebuild it (Open Skies > Rebuild Flight Prototype Scene).
        /// </summary>
        static void EnsureNoMissingScripts()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // so the next line really reads the file
            var scene = EditorSceneManager.OpenScene(FlightPrototypeSceneBuilder.ScenePath, OpenSceneMode.Single);
            var broken = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    broken.Add(PathOf(t));
            if (broken.Count == 0) return;
            throw new BuildFailedException($"{broken.Count} object(s) in {FlightPrototypeSceneBuilder.ScenePath} have a missing script " +
                $"(a class without a script file of its own name, or a scene saved before a fix: rebuild it). First: {string.Join(", ", broken.GetRange(0, Math.Min(5, broken.Count)))}");
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static void EnsurePrototypeScene()
        {
            // A scene committed from an Editor is used as-is; CI only generates it when missing.
            if (!File.Exists(FlightPrototypeSceneBuilder.ScenePath))
                FlightPrototypeSceneBuilder.Build();
            else
                FlightPrototypeSceneBuilder.RegisterInBuildSettings();
        }

        static void ConfigureCommon()
        {
            PlayerSettings.productName = ProductName;
            // Linear lighting: physically plausible shading and the HDR/tonemapping path (owner decision with the visual upgrade).
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // CPU and GPU frame times for the performance monitor (FrameTimingManager); a very small cost.
            PlayerSettings.enableFrameTimingStats = true;
            // Landscape only, either way up.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            var version = GetArg("-buildVersion");
            if (!string.IsNullOrEmpty(version) && version != "none")
                PlayerSettings.bundleVersion = version;
        }

        static void ConfigureAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidIdentifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // ARM64 for phones; the CI launch test (development builds) adds x86_64 so the same APK runs natively on the Android emulator.
            PlayerSettings.Android.targetArchitectures = HasArg("-androidX86_64") ? AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64 : AndroidArchitecture.ARM64;
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

        static bool HasArg(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name && !args[i + 1].StartsWith("-"))
                    return args[i + 1];
            return null;
        }
    }
}
