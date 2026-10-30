using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// Code-first entry point. Runs on every launch regardless of which scene is
    /// loaded, so scenes can stay empty and nothing depends on scene YAML.
    /// </summary>
    public static class Bootstrap
    {
        // Unity 6's built-in runtime font for legacy uGUI Text.
        const string BuiltinFont = "LegacyRuntime.ttf";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ConfigureApp()
        {
            var config = GameConfig.Load();
            QualitySettings.vSyncCount = 0; // otherwise targetFrameRate is ignored on desktop/WebGL
            Application.targetFrameRate = config.targetFrameRate;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartGame() => EnsureGameRoot();

        /// <summary>Creates the game root, camera and HUD once. Safe to call repeatedly.</summary>
        public static GameRoot EnsureGameRoot()
        {
            if (GameRoot.Instance != null) return GameRoot.Instance;

            var config = GameConfig.Load();
            var rootObject = new GameObject("GameRoot");
            Object.DontDestroyOnLoad(rootObject);

            if (Camera.main == null) CreateCamera(rootObject.transform, config);
            var scoreLabel = CreateHud(rootObject.transform, config);

            var root = rootObject.AddComponent<GameRoot>();
            root.Init(config, scoreLabel);
            return root;
        }

        static void CreateCamera(Transform parent, GameConfig config)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 0f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = config.backgroundColor;
            camera.orthographic = true;
        }

        static Text CreateHud(Transform parent, GameConfig config)
        {
            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = config.referenceResolution;
            scaler.matchWidthOrHeight = 0f; // portrait: scale by width

            var safeGo = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            safeGo.transform.SetParent(canvasGo.transform, false);

            var labelGo = new GameObject("ScoreLabel", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(safeGo.transform, false);
            var rect = (RectTransform)labelGo.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 200f);

            var text = labelGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(BuiltinFont);
            text.fontSize = 120;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
