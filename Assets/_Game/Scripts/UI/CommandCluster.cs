using System;
using OpenSkies.Awareness;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The compact top-right command cluster (docs/TAP_DISCLOSURE_DESIGN.md, milestone 3). It
    /// replaces the row of equal buttons (CAM, RESET, HUD profile, LOOK FWD, MENU, SKIP) with a
    /// hierarchy:
    /// - Primary, always shown: the camera (one tap switches the view; it names the current
    ///   view), the options trigger and pause (the quietest). No card behind them.
    /// - Contextual: LOOK FWD under the camera while the cockpit view is turned away.
    /// - Secondary, in the options panel: HUD, which opens the profile selector in the panel's
    ///   place (CINEMATIC, SMART, DETAILED, chosen directly).
    /// - Tertiary, smaller and quieter, in the panel's lower row: RESET and SKIP TUTORIAL (each
    ///   asks for a second tap) and SETTINGS.
    ///
    /// A view only: it raises requests and shows what the session tells it (<see cref="Present"/>).
    /// The session applies them through <c>FlightSession</c> and registers the panel with the
    /// disclosure coordinator (outside taps fold it; no close button). Visual sizes and touch
    /// sizes are independent: each control's touch area is a 96-unit invisible target around a
    /// smaller visual. Runtime-generated sprites are assigned here, never stored in the scene.
    /// </summary>
    public sealed class CommandCluster : MonoBehaviour
    {
        [Header("Primary row")]
        public HudButton cameraButton;
        public Image cameraIcon;
        public Text cameraCaption;
        public HudButton optionsButton;
        public Image optionsIcon;
        public HudButton pauseButton;
        public Image pauseIcon;
        [Tooltip("The primary row's opacity group (the player's HUD opacity).")]
        public CanvasGroup rowGroup;

        [Header("Contextual camera option")]
        [Tooltip("LOOK FWD under the camera; shown by the cockpit session while the view is turned away.")]
        public CockpitViewControls lookForward;

        [Header("Options panel (transient)")]
        public RectTransform panel;
        public CanvasGroup panelGroup;
        public Image panelBackground;
        public RectTransform optionsLevel;
        public RectTransform selectorLevel;
        public HudButton hudRow;
        public Text hudRowValue;
        public Image hudRowChevron;
        public HudButton resetButton;
        public HudButton settingsButton;
        public HudButton skipButton;
        public HudButton selectorBack;
        public Text selectorTitle;
        public Image selectorChevron;
        public HudButton[] profileButtons = new HudButton[CommandClusterModel.ProfileCount];
        public Image[] dividers = new Image[0];

        [Header("Look and timing")]
        public HudMotionSettings motion = new HudMotionSettings();
        public CommandClusterSettings settings = new CommandClusterSettings();

        public event Action CameraTapped;
        public event Action OptionsTapped;
        public event Action PauseTapped;
        public event Action HudRowTapped;
        public event Action SelectorBackTapped;
        /// <summary>A profile of the selector, by index into <see cref="CommandClusterModel.ProfileOrder"/>.</summary>
        public event Action<int> ProfileTapped;
        public event Action ResetTapped;
        public event Action SettingsTapped;
        public event Action SkipTapped;

        const int Unset = -1;

        HudFade _panelFade;
        bool _initialized, _reducedMotion;
        float _opacity = 1f;
        int _shownLevel = Unset, _shownProfile = Unset, _shownView = Unset, _shownSkip = Unset;

        /// <summary>The options panel is shown (or fading in) and takes touches.</summary>
        public bool PanelShown => _panelFade != null && _panelFade.IsShown;
        /// <summary>The level the view shows now.</summary>
        public CommandLevel ShownLevel => _shownLevel == Unset ? CommandLevel.Collapsed : (CommandLevel)_shownLevel;
        public bool ReducedMotion => _reducedMotion;

        /// <summary>What a coach's highlight frames: the visuals, not the larger touch areas.</summary>
        public RectTransform CameraVisual => VisualOf(cameraButton);
        public RectTransform OptionsVisual => VisualOf(optionsButton);
        public RectTransform ResetVisual => VisualOf(resetButton);

        static RectTransform VisualOf(HudButton button) =>
            button == null ? null : (button.background != null ? button.background.rectTransform : (RectTransform)button.transform);

        public void SetReducedMotion(bool reduced)
        {
            _reducedMotion = reduced;
            if (lookForward != null) lookForward.SetReducedMotion(reduced);
        }

        /// <summary>The player's HUD opacity on the primary row and LOOK FWD. The open panel stays at full strength (it carries text).</summary>
        public void SetOpacity(float opacity)
        {
            _opacity = Mathf.Clamp01(opacity);
            if (rowGroup != null) rowGroup.alpha = _opacity;
            if (lookForward != null) lookForward.SetOpacity(_opacity);
        }

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            AssignSprites();
            if (panelGroup != null) _panelFade = new HudFade(panelGroup, interactive: true);
            if (rowGroup != null) rowGroup.alpha = _opacity;

            // One subscription each, made once (no per-frame closures).
            if (cameraButton != null) cameraButton.Clicked += () => CameraTapped?.Invoke();
            if (optionsButton != null) optionsButton.Clicked += () => OptionsTapped?.Invoke();
            if (pauseButton != null) pauseButton.Clicked += () => PauseTapped?.Invoke();
            if (hudRow != null) hudRow.Clicked += () => HudRowTapped?.Invoke();
            if (selectorBack != null) selectorBack.Clicked += () => SelectorBackTapped?.Invoke();
            if (resetButton != null) resetButton.Clicked += () => ResetTapped?.Invoke();
            if (settingsButton != null) settingsButton.Clicked += () => SettingsTapped?.Invoke();
            if (skipButton != null) skipButton.Clicked += () => SkipTapped?.Invoke();
            for (int i = 0; i < profileButtons.Length; i++)
            {
                if (profileButtons[i] == null) continue;
                int index = i;
                profileButtons[i].Clicked += () => ProfileTapped?.Invoke(index);
            }
            if (optionsLevel != null) optionsLevel.gameObject.SetActive(true);
            if (selectorLevel != null) selectorLevel.gameObject.SetActive(false);
        }

        /// <summary>Generated sprites cannot be stored in a scene: the icons, the panel's shape and the chevrons get them here.</summary>
        void AssignSprites()
        {
            if (cameraIcon != null) cameraIcon.sprite = HudSprites.Icon(HudIcon.Camera);
            if (optionsIcon != null) optionsIcon.sprite = HudSprites.Icon(HudIcon.More);
            if (pauseIcon != null) pauseIcon.sprite = HudSprites.Icon(HudIcon.Pause);
            if (hudRowChevron != null) hudRowChevron.sprite = HudSprites.Arrow;
            if (selectorChevron != null) selectorChevron.sprite = HudSprites.Arrow;
            if (panelBackground != null && panelBackground.sprite == null) HudStyle.Round(panelBackground, HudStyle.PanelRadius);
        }

        /// <summary>
        /// Shows the model's level, the committed HUD profile, the current view and the session
        /// actions' captions. Idempotent and allocation-free; only what changed is touched.
        /// </summary>
        public void Present(CommandClusterModel model, HudProfile profile, bool cockpitView)
        {
            EnsureInitialized();
            if (model == null) return;

            int view = cockpitView ? 1 : 0;
            if (view != _shownView)
            {
                _shownView = view;
                if (cameraCaption != null) cameraCaption.text = CommandClusterModel.CameraLabel(cockpitView);
            }

            int level = (int)model.Level;
            if (level != _shownLevel)
            {
                bool open = model.Level != CommandLevel.Collapsed;
                // The content swaps in place (one footprint); folding keeps what was shown until it has faded.
                if (open)
                {
                    if (optionsLevel != null) optionsLevel.gameObject.SetActive(model.Level == CommandLevel.Options);
                    if (selectorLevel != null) selectorLevel.gameObject.SetActive(model.Level == CommandLevel.HudSelector);
                }
                _panelFade?.SetTarget(open ? 1f : 0f, motion.FadeSeconds(_reducedMotion), 0f);
                if (optionsButton != null) optionsButton.SetEngaged(open);
                if (lookForward != null) lookForward.SetSuppressed(open); // the panel takes LOOK FWD's place
                _shownLevel = level;
            }

            int shownProfile = CommandClusterModel.IndexOf(profile);
            if (shownProfile != _shownProfile)
            {
                _shownProfile = shownProfile;
                if (hudRowValue != null) hudRowValue.text = CommandClusterModel.ProfileLabel(profile);
                if (selectorTitle != null) selectorTitle.text = CommandClusterModel.SelectorTitle(profile);
                for (int i = 0; i < profileButtons.Length; i++)
                    if (profileButtons[i] != null) profileButtons[i].SetEngaged(i == shownProfile);
            }

            if (resetButton != null)
            {
                resetButton.SetLabel(model.ResetCaption);
                resetButton.SetEngaged(model.Armed == CommandConfirm.Reset);
            }
            int skip = model.SkipAvailable ? 1 : 0;
            if (skip != _shownSkip && skipButton != null)
            {
                _shownSkip = skip;
                skipButton.gameObject.SetActive(model.SkipAvailable);
            }
            if (skipButton != null && model.SkipAvailable)
            {
                skipButton.SetLabel(model.SkipCaption);
                skipButton.SetEngaged(model.Armed == CommandConfirm.SkipTutorial);
            }
        }

        void LateUpdate()
        {
            if (_initialized) _panelFade?.Tick(Time.unscaledDeltaTime);
        }
    }
}
