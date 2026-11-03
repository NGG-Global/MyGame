using System;
using OpenSkies.Gestures;
using OpenSkies.Missions;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The entry experience: the parked aircraft in its world, framed differently for
    /// each of three stations (First Flight, Free Flight, Landing Challenge). A
    /// horizontal drag moves the presentation continuously between stations and
    /// settles on the nearest; a tap on the scene or START begins; three chips name
    /// the stations and select them directly; arrow keys and a gamepad do the same.
    /// Draws what <see cref="EntryStationModel"/> says; the session poses the camera.
    /// </summary>
    public sealed class EntryStationsScreen : MonoBehaviour
    {
        public const int StationCount = EntryStationModel.Count;

        public CanvasGroup group;
        [Tooltip("The whole screen: drags move the stations, a tap starts (route Entry). Draws nothing.")]
        public GestureHotspot sceneHotspot;
        public Text title;
        public Text stationName;
        public Text description;
        public Text progress;
        public Image textPlate;
        [Tooltip("Index = station: FIRST FLIGHT, FREE FLIGHT, LANDING CHALLENGE.")]
        public HudButton[] chips = new HudButton[StationCount];
        public HudButton startButton;
        public HudButton settingsButton;
        [Tooltip("The two Free Flight starts, shown on the Free Flight station.")]
        public CanvasGroup freeFlightOptions;
        public HudButton fromRunwayButton;
        public HudButton inTheAirButton;
        public Text swipeHint;
        public EntryStationSettings settings = new EntryStationSettings();
        public HudMotionSettings motion = new HudMotionSettings();

        /// <summary>The player chose a mode (and, for Free Flight, where to start).</summary>
        public event Action<GameMode, FreeFlightStart> Chosen;
        public event Action SettingsRequested;
        /// <summary>The selected station changed (0 First Flight, 1 Free Flight, 2 Landing Challenge).</summary>
        public event Action<int> StationChanged;

        static readonly string[] Names = { "FIRST FLIGHT", "FREE FLIGHT", "LANDING CHALLENGE" };
        static readonly string[] Descriptions =
        {
            "Take-off, a short coastal flight and a landing, learnt by flying.",
            "Fly anywhere, at your own pace. The guide can lead you to a landmark.",
            "Start on final approach and land. Scored, with short feedback.",
        };

        EntryStationModel _model;
        HudFade _fade, _optionsFade;
        bool _initialized, _open, _reducedMotion;
        FreeFlightStart _freeStart = FreeFlightStart.Runway;
        int _shownStation = -1;
        readonly string[] _progress = new string[StationCount];
        float _lastDragX, _lastDragTime, _velocity;
        Vector2 _dragLocal;

        public EntryStationModel Model => _model;
        public bool IsOpen => _open;
        public int Station => _model != null ? _model.Station : 0;
        public FreeFlightStart FreeStart => _freeStart;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _model = new EntryStationModel(settings);
            if (group != null) _fade = new HudFade(group, interactive: true, initialAlpha: group.alpha);
            _open = group != null && group.alpha > 0f;
            if (freeFlightOptions != null) _optionsFade = new HudFade(freeFlightOptions, interactive: true);
            if (textPlate != null && textPlate.sprite == null) HudStyle.Round(textPlate, HudStyle.PanelRadius);
            for (int i = 0; i < chips.Length; i++)
            {
                if (chips[i] == null) continue;
                int station = i;
                chips[i].SetLabel(Names[i]);
                chips[i].Clicked += () => { if (_open) Select(station); };
            }
            if (startButton != null) startButton.Clicked += () => { if (_open) Start(); };
            if (settingsButton != null) settingsButton.Clicked += () => { if (_open) SettingsRequested?.Invoke(); };
            if (fromRunwayButton != null) fromRunwayButton.Clicked += () => { if (_open) { _freeStart = FreeFlightStart.Runway; Start(); } };
            if (inTheAirButton != null) inTheAirButton.Clicked += () => { if (_open) { _freeStart = FreeFlightStart.Airborne; Start(); } };
            if (title != null) title.text = "OPEN SKIES";
            Present(force: true);
        }

        /// <summary>Opens on a station with the player's progress lines (cached strings per station; null for none).</summary>
        public void Open(int station, string firstFlightProgress, string freeFlightProgress, string landingProgress)
        {
            EnsureInitialized();
            _open = true;
            _progress[0] = firstFlightProgress;
            _progress[1] = freeFlightProgress;
            _progress[2] = landingProgress;
            _model.Set(station, instant: true);
            _shownStation = -1;
            _fade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), 0f);
            Present(force: true);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        /// <summary>A chip, a key or a d-pad press: the station settles into view.</summary>
        public void Select(int station)
        {
            EnsureInitialized();
            _model.Set(station, instant: _reducedMotion);
            HudButton.Report(UiFeedback.Tick);
        }

        public void Step(int direction)
        {
            EnsureInitialized();
            int before = _model.Station;
            _model.Step(direction);
            if (_reducedMotion) _model.Set(_model.Station, instant: true);
            if (_model.Station != before) HudButton.Report(UiFeedback.Tick);
        }

        /// <summary>Starts the selected station's experience (Free Flight: the chosen start).</summary>
        public void Start()
        {
            if (!_open) return;
            switch (_model.Station)
            {
                case 0: Chosen?.Invoke(GameMode.FirstFlight, FreeFlightStart.Runway); break;
                case 1: Chosen?.Invoke(GameMode.FreeFlight, _freeStart); break;
                default: Chosen?.Invoke(GameMode.LandingChallenge, FreeFlightStart.Runway); break;
            }
        }

        /// <summary>The scene's gestures, forwarded by the session from the interaction surface (route Entry).</summary>
        public void OnGesture(in GestureEvent e)
        {
            if (!_open || _model == null) return;
            switch (e.Kind)
            {
                case GestureKind.Drag:
                    switch (e.Type)
                    {
                        case GestureEventType.Began:
                            _model.BeginDrag(LocalX(e.Origin));
                            _lastDragX = LocalX(e.Origin);
                            _lastDragTime = e.Time;
                            _velocity = 0f;
                            _model.Drag(LocalX(e.Position));
                            break;
                        case GestureEventType.Changed:
                        {
                            float x = LocalX(e.Position);
                            float dt = e.Time - _lastDragTime;
                            if (dt > 1e-4f) _velocity = Mathf.Lerp(_velocity, (x - _lastDragX) / dt, 0.5f);
                            _lastDragX = x;
                            _lastDragTime = e.Time;
                            _model.Drag(x);
                            break;
                        }
                        case GestureEventType.Completed:
                        case GestureEventType.Cancelled:
                        {
                            int before = _model.Station;
                            _model.Release(e.Type == GestureEventType.Completed ? _velocity : 0f);
                            if (_reducedMotion) _model.Set(_model.Station, instant: true);
                            if (_model.Station != before) HudButton.Report(UiFeedback.Tick);
                            break;
                        }
                    }
                    break;
                case GestureKind.Tap:
                    if (e.Type == GestureEventType.Completed && !_model.IsSettling) Start(); // a tap on the scene begins
                    break;
            }
        }

        float LocalX(Vector2 screenPoint)
        {
            var rect = (RectTransform)transform;
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, camera, out _dragLocal);
            return _dragLocal.x;
        }

        void Present(bool force)
        {
            if (_model == null) return;
            int station = _model.Station;
            if (station != _shownStation || force)
            {
                _shownStation = station;
                if (stationName != null) stationName.text = Names[station];
                if (description != null) description.text = Descriptions[station];
                if (progress != null)
                {
                    string line = _progress[station];
                    progress.text = line ?? string.Empty;
                    progress.enabled = !string.IsNullOrEmpty(line);
                }
                for (int i = 0; i < chips.Length; i++)
                    if (chips[i] != null) chips[i].SetEngaged(i == station);
                _optionsFade?.SetTarget(station == 1 ? 1f : 0f, motion.FadeSeconds(_reducedMotion), 0f);
                StationChanged?.Invoke(station);
            }
            // The description fades while the presentation is between stations.
            float alpha = 1f - 0.85f * _model.Displacement;
            SetAlpha(stationName, 0.95f * alpha);
            SetAlpha(description, 0.74f * alpha);
            SetAlpha(progress, 0.74f * alpha);
            if (fromRunwayButton != null) fromRunwayButton.SetEngaged(_freeStart == FreeFlightStart.Runway);
            if (inTheAirButton != null) inTheAirButton.SetEngaged(_freeStart == FreeFlightStart.Airborne);
        }

        static void SetAlpha(Text text, float alpha)
        {
            if (text == null) return;
            var c = text.color;
            c.a = alpha;
            text.color = c;
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            float dt = Time.unscaledDeltaTime;
            _fade?.Tick(dt);
            _optionsFade?.Tick(dt);
            _model.Tick(_reducedMotion ? 10f : dt);
            Present(force: false);
        }
    }
}
