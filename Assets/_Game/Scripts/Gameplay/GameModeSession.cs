using OpenSkies.Awareness;
using OpenSkies.Missions;
using OpenSkies.Simulation;
using OpenSkies.UI;
using OpenSkies.World;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Game modes on top of one <see cref="FlightSession"/>: the entry screen,
    /// Free Flight (runway or airborne), the First Flight tutorial and the Landing
    /// Challenge, the pause menu, mission results and local progress. Every mode
    /// flies the same aircraft with the same physics and assistance: a mode only
    /// chooses where the flight starts, where RESET returns to, what the
    /// navigation guide points at, which guides it asks the HUD for, and what the
    /// coach says. It never sends a flight control.
    ///
    /// Runs after the flight session at the physics rate (it reads this step's
    /// phase of flight and warnings) and per frame for presentation.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public sealed class GameModeSession : MonoBehaviour
    {
        public FlightSession flight;
        [Tooltip("Mission tuning. The defaults in code are used when empty.")]
        public MissionConfiguration configuration;

        [Header("UI")]
        public ModeSelectScreen modeSelect;
        public MissionCard card;
        public CoachingPanel coach;
        public ControlHighlights highlights;
        public CheckpointMarker marker;
        public HudButton menuButton;
        public HudButton skipButton;

        [Header("World")]
        [Tooltip("Landmark lights that flash while their landmark is the active checkpoint.")]
        public LandmarkBeacon[] beacons = new LandmarkBeacon[0];

        [Header("Session")]
        [Tooltip("Show the entry screen when the scene starts (tests start a mode directly).")]
        public bool showModeSelectOnStart = true;
        [Tooltip("Keep progress (First Flight, best landing) between sessions (PlayerPrefs). Tests keep it in memory.")]
        public bool rememberProgress = true;
        [Tooltip("A first tap on SKIP TUTORIAL asks for a second within this time, s.")]
        public float skipConfirmSeconds = 3f;

        const string Footnote = "Game score for practice, not a flight-training standard.";
        const string SkipLabel = "SKIP\nTUTORIAL";
        const string SkipConfirmLabel = "TAP AGAIN\nTO SKIP";

        /// <summary>A mission result waiting on the card (it comes back after the pause menu).</summary>
        enum OpenResult
        {
            None = 0,
            Landing = 1,
            FirstFlight = 2,
        }

        enum CardAction
        {
            None = 0,
            Resume,
            Restart,
            SkipTutorial,
            ChangeMode,
            CycleDestination,
            FreeFlightHere,
            LandingChallenge,
        }

        MissionConfiguration _config;
        LandmarkRegistry _world;
        TrainingSpeeds _speeds;
        PlayerProgress _progress;
        FirstFlightTutorial _tutorial;
        LandingChallenge _challenge;
        GameMode _mode;
        FreeFlightStart _freeStart;
        bool _initialized, _modeActive, _paused, _resultShown, _skipArmed;
        float _resumeTimeScale = 1f, _skipArmedAt;
        int _checkpointVersion = -1, _destination = -1, _cueSource = -1;
        OpenResult _openResult;
        string _resultTitle, _resultBody;
        readonly CardAction[] _cardActions = new CardAction[MissionCard.MaxButtons];
        HudFade _skipFade;
        string[] _destinationLabels;
        string _airfieldDestinationLabel;

        public GameMode Mode => _mode;
        public bool ModeActive => _modeActive;
        public bool IsPaused => _paused;
        public FirstFlightTutorial Tutorial => _tutorial;
        public LandingChallenge Challenge => _challenge;
        public PlayerProgress Progress => _progress;
        public TrainingSpeeds Speeds => _speeds;
        public LandmarkRegistry World => _world;
        public bool ModeSelectOpen => modeSelect != null && modeSelect.IsOpen;
        /// <summary>Free Flight destination: a landmark index, or -1 for the airfield (guide on request).</summary>
        public int Destination => _destination;

        // ---- Set-up ---------------------------------------------------------------------------------

        void Start() => EnsureInitialized();

        /// <summary>Builds the missions from the aircraft data. Runs in Start, after every Awake.</summary>
        public void EnsureInitialized()
        {
            if (_initialized || flight == null) return;
            _initialized = true;
            _config = configuration != null ? configuration : ScriptableObject.CreateInstance<MissionConfiguration>();
            _world = PrototypeWorld.CreateRegistry();

            var aircraftData = flight.aircraft != null ? flight.aircraft.configuration : null;
            var envelope = flight.Envelope;
            float liftOff = OpenSkies.FlightState.FlightUnits.KnotsToMetresPerSecond(envelope.StallSpeedCleanKnots * _config.tutorial.liftOffFactor);
            float takeoffRun = aircraftData != null ? FlightStartMath.TakeoffGroundRunMetres(aircraftData, liftOff) : float.NaN;
            _speeds = TrainingSpeeds.From(envelope, float.IsInfinity(takeoffRun) ? float.NaN : takeoffRun, _config.tutorial);

            var runway = flight.homeRunway;
            _tutorial = new FirstFlightTutorial(_config.tutorial, _speeds, _world, runway, _config.landingChallenge, _config.landingScoring);
            _challenge = new LandingChallenge(_config.landingChallenge, _config.landingScoring, _speeds, runway);
            _progress = new PlayerProgress(rememberProgress ? new PlayerPrefsProgressStore() : new MemoryProgressStore());

            _airfieldDestinationLabel = "GUIDE: AIRFIELD";
            _destinationLabels = new string[_world.Count];
            for (int i = 0; i < _world.Count; i++) _destinationLabels[i] = "GUIDE: " + _world[i].displayName;

            bool reduced = flight.contextualHud != null && flight.contextualHud.ReducedMotion;
            if (coach != null)
            {
                coach.SetReducedMotion(reduced);
                coach.DetailRequested += OnDetailRequested;
            }
            if (card != null)
            {
                card.SetReducedMotion(reduced);
                card.ButtonClicked += OnCardButton;
            }
            if (modeSelect != null)
            {
                modeSelect.SetReducedMotion(reduced);
                modeSelect.Chosen += OnModeChosen;
            }
            if (highlights != null) highlights.SetReducedMotion(reduced);
            if (menuButton != null) menuButton.Clicked += ToggleMenu;
            if (skipButton != null)
            {
                var group = skipButton.GetComponent<CanvasGroup>();
                if (group != null) _skipFade = new HudFade(group, interactive: true);
                skipButton.Clicked += OnSkipClicked;
            }
            flight.ResetPerformed += OnFlightReset;
            ResolveHighlightTargets();

            if (showModeSelectOnStart) OpenModeSelect();
            else
            {
                if (modeSelect != null) modeSelect.Close();
                _mode = GameMode.FreeFlight;
                _modeActive = true;
            }
        }

        /// <summary>The controls a coaching message can point at, from the HUD the session already has.</summary>
        void ResolveHighlightTargets()
        {
            if (highlights == null) return;
            var touch = flight.touchControls;
            if (touch != null)
            {
                if (touch.joystick != null) highlights.SetTarget(CoachHighlight.Stick, touch.joystick.visualRoot);
                if (touch.throttle != null) highlights.SetTarget(CoachHighlight.Throttle, touch.throttle.track != null ? touch.throttle.track : (RectTransform)touch.throttle.transform);
                if (touch.brake != null) highlights.SetTarget(CoachHighlight.Brake, (RectTransform)touch.brake.transform);
            }
            var hud = flight.hud;
            if (hud != null)
            {
                if (hud.cameraButton != null) highlights.SetTarget(CoachHighlight.Camera, (RectTransform)hud.cameraButton.transform);
                if (hud.resetButton != null) highlights.SetTarget(CoachHighlight.Reset, (RectTransform)hud.resetButton.transform);
            }
            var contextual = flight.contextualHud;
            if (contextual != null)
            {
                int slot = contextual.SlotOf(ContextAction.Flaps);
                if (slot >= 0 && contextual.railSlots[slot] != null) highlights.SetTarget(CoachHighlight.Flaps, (RectTransform)contextual.railSlots[slot].transform);
            }
        }

        void OnDisable()
        {
            // Never leave the game frozen (scene change, tests).
            if (_paused) Time.timeScale = _resumeTimeScale;
            _paused = false;
        }

        void OnDestroy()
        {
            if (flight != null) flight.ResetPerformed -= OnFlightReset;
        }

        void OnApplicationPause(bool paused)
        {
            // Mobile: coming back from another app finds the flight paused, not crashed.
            if (paused && Application.isMobilePlatform && _modeActive && !_paused && !ModeSelectOpen) OpenMenu();
        }

        // ---- Modes ----------------------------------------------------------------------------------

        /// <summary>The entry screen, over a paused view of the world.</summary>
        public void OpenModeSelect()
        {
            EnsureInitialized();
            StopMissions();
            _modeActive = false;
            Freeze();
            if (modeSelect == null) return;
            string firstFlight = _progress.Tutorial == TutorialStatus.Completed ? "Completed. Fly it again any time"
                : _progress.RecommendFirstFlight ? "Recommended: take-off, a short coastal flight, landing"
                : "Take-off, a short coastal flight and a landing";
            string landing = _progress.HasBestLanding ? "Best score " + _progress.BestLandingScore + " of 100"
                : "Start on final approach and land";
            modeSelect.Open(_progress.RecommendFirstFlight, firstFlight, landing);
        }

        void OnModeChosen(GameMode mode, FreeFlightStart start)
        {
            if (modeSelect != null) modeSelect.Close();
            Unfreeze();
            StartMode(mode, start);
        }

        /// <summary>Starts a mode from its beginning (also used by tests and the menu).</summary>
        public void StartMode(GameMode mode, FreeFlightStart start = FreeFlightStart.Runway)
        {
            EnsureInitialized();
            if (ModeSelectOpen) modeSelect.Close();
            if (_paused) Unfreeze();
            StopMissions();
            _mode = mode;
            _freeStart = start;
            _modeActive = true;
            switch (mode)
            {
                case GameMode.FirstFlight:
                    _tutorial.Start();
                    flight.ClearResetStart();
                    flight.StartOnRunway();
                    _checkpointVersion = _tutorial.CheckpointVersion;
                    break;
                case GameMode.LandingChallenge:
                    StartChallengeAttempt();
                    break;
                default:
                    _destination = -1;
                    if (start == FreeFlightStart.Airborne)
                    {
                        var free = _config.freeFlight;
                        var state = MissionStarts.Airborne(flight.aircraft.configuration, free.airbornePosition, free.airborneHeadingDegrees);
                        flight.StartFlight(state, 0);
                        flight.SetResetStart(state, 0);
                    }
                    else
                    {
                        flight.ClearResetStart();
                        flight.StartOnRunway();
                    }
                    break;
            }
            SyncGuidance();
        }

        void StartChallengeAttempt()
        {
            _challenge.Start();
            _resultShown = false;
            _openResult = OpenResult.None;
            if (card != null) card.Close();
            if (MissionStarts.TryCreate(_challenge.StartState, flight.aircraft.configuration, flight.homeRunway, AimPoint, _config.landingChallenge,
                    out var state, out int detent))
            {
                flight.StartFlight(state, detent);
                flight.SetResetStart(state, detent);
            }
        }

        /// <summary>Carries on flying from here in Free Flight (after a result or a skipped tutorial). RESET returns to the runway.</summary>
        public void ContinueInFreeFlight()
        {
            StopMissions();
            _mode = GameMode.FreeFlight;
            _freeStart = FreeFlightStart.Runway;
            _modeActive = true;
            _destination = -1;
            flight.ClearResetStart();
            SyncGuidance();
        }

        /// <summary>The player knows how to fly: the tutorial ends at once, the flight carries on.</summary>
        public void SkipTutorial()
        {
            if (_mode != GameMode.FirstFlight || !_tutorial.IsActive) return;
            _progress.MarkTutorialSkipped();
            if (_paused) Unfreeze();
            ContinueInFreeFlight();
        }

        void StopMissions()
        {
            if (_tutorial != null && _tutorial.IsActive) _tutorial.Skip();
            if (_challenge != null) _challenge.Stop();
            _resultShown = false;
            _openResult = OpenResult.None;
            _skipArmed = false;
            if (card != null) card.Close();
            if (coach != null) coach.Hide();
            _cueSource = -1;
            if (highlights != null) highlights.Present(CoachHighlight.None);
            if (flight != null)
            {
                flight.ClearNavigationTarget();
                flight.SetMissionGuidance(0, 0);
                flight.SuppressFlyingTips = false;
            }
            SetActiveBeacon(null);
        }

        float AimPoint => flight.Disclosure != null ? flight.Disclosure.Settings.aimPointMetres : 150f;

        // ---- Physics rate: missions read this step's awareness -------------------------------------

        void FixedUpdate()
        {
            if (!_initialized || !_modeActive || flight == null || flight.PhaseEstimator == null) return;
            if (_mode == GameMode.FirstFlight && _tutorial.IsActive)
            {
                var input = BuildInput();
                _tutorial.Update(input, Time.fixedDeltaTime);
            }
            else if (_mode == GameMode.LandingChallenge && _challenge.IsActive)
            {
                var input = BuildInput();
                _challenge.Update(input, Time.fixedDeltaTime);
            }
            SyncGuidance();
        }

        /// <summary>What the missions see this step (all value types: no allocation).</summary>
        public MissionInput BuildInput() => new MissionInput
        {
            Telemetry = flight.Telemetry,
            Phase = flight.PhaseEstimator.Current,
            WarningMask = flight.Warnings.ActiveMask,
            Runway = flight.Runway,
            Position = flight.AircraftPosition,
            Pilot = flight.PilotControls,
            Profile = flight.AssistProfile,
            View = flight.CurrentView,
            TouchControls = flight.touchControls != null && flight.touchControls.ControlsVisible,
            ParkingBrakeSet = flight.Systems.ParkingBrakeSet,
            FlapSelection = flight.Systems.FlapSelection,
        };

        /// <summary>Navigation target, requested guides and the RESET checkpoint for the active mode.</summary>
        void SyncGuidance()
        {
            if (flight == null) return;
            if (_modeActive && _mode == GameMode.FirstFlight && _tutorial.IsActive)
            {
                if (_tutorial.HasNavigationTarget) flight.SetNavigationTarget(_tutorial.NavigationTarget);
                else flight.ClearNavigationTarget();
                flight.SetMissionGuidance(_tutorial.RequestedGuides, _tutorial.RequestedActions);
                if (_tutorial.CheckpointVersion != _checkpointVersion)
                {
                    _checkpointVersion = _tutorial.CheckpointVersion;
                    if (MissionStarts.TryCreate(_tutorial.Checkpoint, flight.aircraft.configuration, flight.homeRunway, AimPoint, _config.landingChallenge,
                            out var state, out int detent))
                        flight.SetResetStart(state, detent);
                    else flight.ClearResetStart();
                }
            }
            else if (_modeActive && _mode == GameMode.LandingChallenge && _challenge.IsActive)
            {
                if (_challenge.HasNavigationTarget) flight.SetNavigationTarget(_challenge.NavigationTarget);
                else flight.ClearNavigationTarget();
                flight.SetMissionGuidance(_challenge.RequestedGuides, 0);
            }
            else if (_modeActive && _mode == GameMode.FreeFlight && _destination >= 0)
            {
                var landmark = _world[_destination];
                flight.SetNavigationTarget(new NavigationTarget(landmark.displayName, landmark.position, landmark.arrivalRadiusMetres,
                    landmark.kind != LandmarkKind.Airfield));
                flight.SetMissionGuidance(HudElements.Bit(HudElement.NavigationGuide), 0);
            }
            else
            {
                flight.ClearNavigationTarget();
                flight.SetMissionGuidance(0, 0);
            }
        }

        // ---- Frame: input, results and presentation ----------------------------------------------

        void Update()
        {
            if (!_initialized) return;
            if (flight.Devices != null && flight.Devices.MenuPressedThisFrame()) ToggleMenu();

            if (_modeActive && _mode == GameMode.FirstFlight && _tutorial.State == FirstFlightTutorial.Status.Completed && !_resultShown)
                ShowFirstFlightComplete();
            if (_modeActive && _mode == GameMode.LandingChallenge && _challenge.State == LandingChallenge.Status.Result && !_resultShown)
                ShowLandingResult();

            PresentCoaching();
            PresentNavigationAids();
            PresentSkip();
            if (card != null && !card.IsModal)
            {
                bool otherPanel = flight.FlightSettingsOpen || (flight.Cockpit != null && flight.Cockpit.IsPanelOpen);
                card.SetSuppressed(otherPanel);
            }
        }

        void PresentCoaching()
        {
            int source = -1;
            CoachingCue cue = default;
            if (_modeActive && _mode == GameMode.FirstFlight && _tutorial.IsActive)
            {
                source = 0;
                cue = _tutorial.Cue;
            }
            else if (_modeActive && _mode == GameMode.LandingChallenge && _challenge.IsActive)
            {
                source = 1;
                cue = _challenge.Cue;
            }
            if (source != _cueSource)
            {
                _cueSource = source;
                if (coach != null) coach.Hide(); // versions are per mission: start afresh
            }
            if (coach != null && source >= 0) coach.Present(cue);
            if (highlights != null) highlights.Present(source >= 0 && cue.Visible && !_paused ? cue.Highlights : CoachHighlight.None);
            // The tutorial's coaching replaces the Assisted flying tips; the challenge's only while it speaks.
            flight.SuppressFlyingTips = source == 0 || (source == 1 && cue.Visible);
        }

        void PresentNavigationAids()
        {
            string landmarkId = null;
            if (_modeActive && _mode == GameMode.FirstFlight) landmarkId = _tutorial.ActiveLandmarkId;
            else if (_modeActive && _mode == GameMode.FreeFlight && _destination >= 0 && _world[_destination].kind != LandmarkKind.Airfield)
                landmarkId = _world[_destination].id;
            SetActiveBeacon(landmarkId);

            if (marker == null) return;
            var nav = flight.Navigation;
            bool show = landmarkId != null && nav.Valid && nav.Target.IsLandmark && !ModeSelectOpen;
            marker.Present(show, nav.Target.Position, nav.Target.Name, nav.DistanceMetres);
        }

        void SetActiveBeacon(string landmarkId)
        {
            for (int i = 0; i < beacons.Length; i++)
                if (beacons[i] != null) beacons[i].SetActive(landmarkId != null && beacons[i].landmarkId == landmarkId);
        }

        void PresentSkip()
        {
            if (skipButton == null) return;
            bool show = _modeActive && _mode == GameMode.FirstFlight && _tutorial.IsActive && !_paused;
            if (_skipArmed && (!show || Time.unscaledTime - _skipArmedAt > skipConfirmSeconds))
            {
                _skipArmed = false;
                skipButton.SetLabel(SkipLabel);
                skipButton.SetEngaged(false);
            }
            if (_skipFade != null)
            {
                _skipFade.SetTarget(show ? 1f : 0f, 0.2f, 0f);
                _skipFade.Tick(Time.unscaledDeltaTime);
            }
        }

        void OnSkipClicked()
        {
            if (_mode != GameMode.FirstFlight || !_tutorial.IsActive) return;
            if (!_skipArmed)
            {
                // A second tap confirms: an accidental brush never ends the tutorial.
                _skipArmed = true;
                _skipArmedAt = Time.unscaledTime;
                skipButton.SetLabel(SkipConfirmLabel);
                skipButton.SetEngaged(true);
                return;
            }
            _skipArmed = false;
            skipButton.SetLabel(SkipLabel);
            skipButton.SetEngaged(false);
            SkipTutorial();
        }

        void OnDetailRequested()
        {
            if (_mode == GameMode.FirstFlight && _tutorial.IsActive) _tutorial.ToggleDetail();
            else if (_mode == GameMode.LandingChallenge && _challenge.IsActive) _challenge.ToggleDetail();
        }

        // ---- RESET ----------------------------------------------------------------------------------

        /// <summary>The player's RESET has put the aircraft back at the mode's start or checkpoint.</summary>
        void OnFlightReset()
        {
            if (!_modeActive) return;
            if (_mode == GameMode.FirstFlight && _tutorial.IsActive) _tutorial.OnFlightReset();
            else if (_mode == GameMode.LandingChallenge)
            {
                // RESET in the challenge is a retry from final approach (the reset start is the challenge start).
                _challenge.Start();
                _resultShown = false;
                _openResult = OpenResult.None;
                if (card != null && !card.IsModal) card.Close();
            }
            else if (card != null && !card.IsModal)
            {
                _openResult = OpenResult.None;
                card.Close();
            }
        }

        // ---- Pause menu -----------------------------------------------------------------------------

        public void ToggleMenu()
        {
            if (ModeSelectOpen) return;
            if (_paused) ResumeFromMenu();
            else OpenMenu();
        }

        /// <summary>Pauses the flight and shows the menu for the current mode.</summary>
        public void OpenMenu()
        {
            EnsureInitialized();
            if (ModeSelectOpen || card == null) return;
            Freeze();
            switch (_mode)
            {
                case GameMode.FirstFlight when _tutorial.IsActive:
                    SetActions(CardAction.Resume, CardAction.Restart, CardAction.SkipTutorial, CardAction.ChangeMode);
                    card.Show("Paused", "First Flight, step " + (_tutorial.StepIndex + 1) + " of " + _tutorial.StepCount + ": "
                                        + _tutorial.Step.Title.ToLowerInvariant(), null, true,
                        "RESUME", "RESTART FIRST FLIGHT", "SKIP TUTORIAL", "CHANGE MODE");
                    break;
                case GameMode.LandingChallenge:
                    SetActions(CardAction.Resume, CardAction.Restart, CardAction.ChangeMode, CardAction.None);
                    card.Show("Paused", "Landing Challenge", null, true, "RESUME", "RETRY", "CHANGE MODE");
                    break;
                default:
                    SetActions(CardAction.Resume, CardAction.CycleDestination, CardAction.Restart, CardAction.ChangeMode);
                    card.Show("Paused", "Free Flight. The guide can lead you to a landmark.", null, true,
                        "RESUME", DestinationLabel(), _freeStart == FreeFlightStart.Airborne ? "RESTART IN THE AIR" : "RESTART ON THE RUNWAY",
                        "CHANGE MODE");
                    break;
            }
        }

        public void ResumeFromMenu()
        {
            if (card != null && card.IsModal) card.Close();
            Unfreeze();
            // A mission result that was waiting comes back (it is not scored again).
            if (_openResult != OpenResult.None) ShowResultCard();
        }

        void SetActions(CardAction a, CardAction b, CardAction c, CardAction d)
        {
            _cardActions[0] = a;
            _cardActions[1] = b;
            _cardActions[2] = c;
            _cardActions[3] = d;
        }

        void OnCardButton(int index)
        {
            var action = index >= 0 && index < _cardActions.Length ? _cardActions[index] : CardAction.None;
            switch (action)
            {
                case CardAction.Resume:
                    ResumeFromMenu();
                    break;
                case CardAction.Restart:
                    if (card != null) card.Close();
                    if (_mode == GameMode.LandingChallenge)
                    {
                        Unfreeze();
                        _modeActive = true;
                        StartChallengeAttempt();
                    }
                    else StartMode(_mode, _freeStart);
                    break;
                case CardAction.SkipTutorial:
                    if (card != null) card.Close();
                    SkipTutorial();
                    break;
                case CardAction.ChangeMode:
                    if (card != null) card.Close();
                    OpenModeSelect();
                    break;
                case CardAction.CycleDestination:
                    CycleDestination();
                    if (card != null) card.SetButtonLabel(index, DestinationLabel());
                    break;
                case CardAction.FreeFlightHere:
                    if (card != null) card.Close();
                    ContinueInFreeFlight();
                    break;
                case CardAction.LandingChallenge:
                    if (card != null) card.Close();
                    StartMode(GameMode.LandingChallenge);
                    break;
            }
        }

        /// <summary>Free Flight: airfield (guide on request), then each landmark, then back.</summary>
        public void CycleDestination()
        {
            int next = _destination + 1;
            while (next < _world.Count && _world[next].kind == LandmarkKind.Airfield) next++;
            _destination = next < _world.Count ? next : -1;
            SyncGuidance();
        }

        string DestinationLabel() => _destination >= 0 ? _destinationLabels[_destination] : _airfieldDestinationLabel;

        void Freeze()
        {
            if (_paused) return;
            _paused = true;
            _resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            _tutorial?.Pause();
            if (flight.touchControls != null) flight.touchControls.Ownership.RevokeAll();
            flight.CloseFlightSettings();
            flight.Cockpit?.Close();
        }

        void Unfreeze()
        {
            if (!_paused) return;
            _paused = false;
            Time.timeScale = _resumeTimeScale;
            _tutorial?.Resume();
            // Nothing dragged while paused may reach the aircraft (the throttle lever keeps its position).
            if (flight.touchControls != null) flight.touchControls.ResetInput();
        }

        // ---- Results --------------------------------------------------------------------------------

        /// <summary>Scores and records the landing once, then shows it.</summary>
        void ShowLandingResult()
        {
            _resultShown = true;
            var result = _challenge.Result;
            bool best = !result.Crashed && _progress.RecordLanding(result.Total);
            _resultTitle = result.Crashed ? "Landing: out of control" : LandingScoring.GradeLabel(result.Grade) + " landing: " + result.Total;
            _resultBody = (best ? "New best score!\n" : string.Empty) + LandingScoring.Summary(result, _config.landingScoring);
            _openResult = OpenResult.Landing;
            ShowResultCard();
        }

        /// <summary>Records the completion once, then shows it.</summary>
        void ShowFirstFlightComplete()
        {
            _resultShown = true;
            _progress.MarkTutorialCompleted();
            flight.ClearResetStart(); // the lesson is over: RESET goes back to the runway
            var landing = _tutorial.LastLanding;
            _resultTitle = "First flight complete";
            _resultBody = "Take-off, coastal flight and landing: done.\n"
                          + "Landing: " + LandingScoring.GradeLabel(landing.Grade).ToLowerInvariant() + ", " + landing.Total + " points\n"
                          + LandingScoring.Summary(landing, _config.landingScoring);
            _openResult = OpenResult.FirstFlight;
            ShowResultCard();
        }

        void ShowResultCard()
        {
            if (card == null) return;
            if (_openResult == OpenResult.Landing)
            {
                SetActions(CardAction.Restart, CardAction.FreeFlightHere, CardAction.None, CardAction.None);
                card.Show(_resultTitle, _resultBody, Footnote, false, "RETRY", "FREE FLIGHT");
            }
            else if (_openResult == OpenResult.FirstFlight)
            {
                SetActions(CardAction.FreeFlightHere, CardAction.LandingChallenge, CardAction.None, CardAction.None);
                card.Show(_resultTitle, _resultBody, Footnote, false, "FREE FLIGHT", "LANDINGS");
            }
        }

        /// <summary>The result card's text (for tests and the inspector).</summary>
        public string ResultTitle => _openResult != OpenResult.None ? _resultTitle : null;
    }
}
