using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenSkies.Platform
{
    /// <summary>
    /// The last few launch messages: breadcrumbs (bootstrap, scene loaded, first
    /// frames) and every error or exception, oldest first, capped so the overlay
    /// stays readable. Plain C#, EditMode-tested.
    /// </summary>
    public sealed class LaunchLog
    {
        readonly string[] _lines;
        readonly StringBuilder _text = new StringBuilder(1024);
        string _cached = string.Empty;
        int _count, _next;
        bool _textValid;

        public LaunchLog(int capacity = 12)
        {
            _lines = new string[Math.Max(1, capacity)];
        }

        public int Count => _count;
        public int Capacity => _lines.Length;
        /// <summary>True once an error, assertion or exception was recorded.</summary>
        public bool HasErrors { get; private set; }

        public void Add(string line, bool isError = false)
        {
            if (string.IsNullOrEmpty(line)) return;
            _lines[_next] = line;
            _next = (_next + 1) % _lines.Length;
            if (_count < _lines.Length) _count++;
            if (isError) HasErrors = true;
            _textValid = false;
        }

        /// <summary>The kept lines, oldest first, one per line (the same string until the log changes).</summary>
        public string Text
        {
            get
            {
                if (_textValid) return _cached;
                _text.Clear();
                int start = _count < _lines.Length ? 0 : _next;
                for (int i = 0; i < _count; i++)
                {
                    if (i > 0) _text.Append('\n');
                    _text.Append(_lines[(start + i) % _lines.Length]);
                }
                _cached = _text.ToString();
                _textValid = true;
                return _cached;
            }
        }
    }

    /// <summary>
    /// Development builds only: shows the launch sequence and every error or
    /// exception on screen (IMGUI, so it needs nothing from the scene) and appends
    /// the same lines to openskies-launch.log in persistentDataPath, flushed per
    /// line. Installed before the first scene loads, so a scene that fails to start
    /// still reports why. A native crash cannot be caught here: an empty overlay
    /// followed by the app closing means the fault is below managed code, and only
    /// the device log (adb logcat) can name it. Never present in release builds and
    /// never installed in the Editor.
    /// </summary>
    public sealed class LaunchDiagnostics : MonoBehaviour
    {
        const float BreadcrumbsShownSeconds = 20f;
        const int MaxMessageLength = 600;

        static LaunchDiagnostics _instance;

        readonly LaunchLog _log = new LaunchLog();
        StreamWriter _file;
        GUIStyle _style;
        Texture2D _background;
        readonly GUIContent _content = new GUIContent();
        string _shown = string.Empty;
        int _frames;
        bool _dismissed;

        public static LaunchLog Log => _instance != null ? _instance._log : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!Debug.isDebugBuild || Application.isEditor || _instance != null) return;
            var go = new GameObject("LaunchDiagnostics");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LaunchDiagnostics>();
            _instance.Record("bootstrap " + Application.version + " " + Application.unityVersion + " on " + SystemInfo.operatingSystem
                             + ", " + SystemInfo.graphicsDeviceType + ", " + SystemInfo.systemMemorySize + " MB");
        }

        void Awake()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "openskies-launch.log");
                _file = new StreamWriter(path, false);
                _file.WriteLine("--- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ---");
                _file.Flush();
            }
            catch (Exception)
            {
                _file = null; // the overlay still works
            }
            Application.logMessageReceived += OnLogMessage;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _file?.Dispose();
            _file = null;
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            _frames++;
            if (_frames == 1 || _frames == 10 || _frames == 60 || _frames == 600) Record("frame " + _frames + " at " + Time.realtimeSinceStartup.ToString("0.0") + " s");
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Record("scene loaded: " + scene.name);

        void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            string text = type + ": " + condition;
            if (!string.IsNullOrEmpty(stackTrace))
            {
                // The first frame of the trace names the line; the overlay has no room for more.
                int end = stackTrace.IndexOf('\n');
                text += " @ " + (end > 0 ? stackTrace.Substring(0, end) : stackTrace);
            }
            if (text.Length > MaxMessageLength) text = text.Substring(0, MaxMessageLength);
            Record(text, isError: true);
        }

        void Record(string line, bool isError = false)
        {
            _log.Add(line, isError);
            if (_file == null) return;
            try
            {
                _file.WriteLine(line);
                _file.Flush(); // the app can die at any moment
            }
            catch (Exception)
            {
                _file = null;
            }
        }

        void OnGUI()
        {
            if (_dismissed) return;
            bool show = _log.HasErrors || Time.realtimeSinceStartup < BreadcrumbsShownSeconds;
            if (!show || _log.Count == 0) return;
            if (_style == null)
            {
                _background = new Texture2D(1, 1);
                _background.SetPixel(0, 0, new Color(0.1f, 0f, 0f, 0.85f));
                _background.Apply();
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(12, Screen.height / 48),
                    wordWrap = true,
                    richText = false,
                    alignment = TextAnchor.UpperLeft,
                };
                _style.normal.textColor = Color.white;
                _style.normal.background = _background;
                _style.padding = new RectOffset(16, 16, 12, 12);
            }
            if (!ReferenceEquals(_shown, _log.Text))
            {
                _shown = _log.Text;
                _content.text = _shown;
            }
            float width = Screen.width * 0.7f;
            float height = Mathf.Min(Screen.height * 0.6f, _style.CalcHeight(_content, width));
            var rect = new Rect(Screen.height * 0.04f, Screen.height * 0.04f, width, height);
            GUI.Label(rect, _content, _style);
            // Two taps on the panel hide it for this session (development only; nothing here is a control of the game).
            if (Event.current.type == EventType.MouseDown && Event.current.clickCount >= 2 && rect.Contains(Event.current.mousePosition)) _dismissed = true;
        }
    }
}
