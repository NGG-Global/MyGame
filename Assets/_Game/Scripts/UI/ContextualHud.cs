using System;
using OpenSkies.Awareness;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Presents the parts of the contextual HUD decided by <see cref="DisclosureController"/>
    /// that are not the flight information: the warning banners, and the contextual action
    /// rail with SYSTEMS (the legacy interface). The flight data and the approach and
    /// navigation guides are the top-left flight information cluster
    /// (<see cref="FlightInfoCluster"/>, docs/TAP_DISCLOSURE_DESIGN.md, milestone 4). It only
    /// reads state and raises requests; it never changes the flight, and it never moves
    /// anything: every element has one fixed home and disclosure only fades it in or out.
    ///
    /// Built in code by <see cref="ContextualHudFactory"/>; steady flight allocates nothing.
    /// </summary>
    public sealed class ContextualHud : MonoBehaviour
    {
        public const int WarningSlotCount = FlightWarnings.Count;
        public const int RailSlotCount = ContextActions.Count;

        [Header("Warnings, most urgent first")]
        public CanvasGroup[] warningSlots = new CanvasGroup[WarningSlotCount];
        public Image[] warningBackgrounds = new Image[WarningSlotCount];
        public Text[] warningLabels = new Text[WarningSlotCount];
        [Tooltip("Severity symbol on each banner (octagon: warning, triangle: caution), so severity is never colour alone.")]
        public Image[] warningIcons = new Image[WarningSlotCount];

        [Header("Action rail and HUD buttons")]
        [Tooltip("Fixed slots, bottom up. Each available action keeps one slot for the whole session.")]
        public HudButton[] railSlots = new HudButton[RailSlotCount];
        [Tooltip("Always visible: opens every available action (manual access to aircraft systems).")]
        public HudButton systemsButton;
        [Tooltip("Opacity group of SYSTEMS (the player's HUD opacity). The HUD profile is chosen in the command cluster.")]
        public CanvasGroup systemsGroup;

        [Header("Development")]
        public DisclosureInspector inspector;

        [Header("Look and motion")]
        public HudMotionSettings motion = new HudMotionSettings();
        public MotionPreference motionPreference = MotionPreference.FollowSystem;
        public Color warningColor = HudStyle.Warning;
        public Color cautionColor = HudStyle.Caution;
        public Color warningTextColor = Color.white;
        public Color cautionTextColor = HudStyle.TextOnAccent;

        public event Action<ContextAction> ActionRequested;
        public event Action SystemsToggleRequested;

        const int NoValue = int.MinValue;

        HudFade[] _warningFades;
        HudFade[] _railFades;
        readonly ContextAction[] _slotAction = new ContextAction[RailSlotCount];
        readonly bool[] _slotUsed = new bool[RailSlotCount];
        readonly int[] _slotWarning = new int[WarningSlotCount];
        float _opacity = 1f;
        bool _initialized, _actionsConfigured, _reducedMotion;
        bool _legacyRail = true;

        public bool ReducedMotion => _reducedMotion;
        /// <summary>SYSTEMS and the action rail are shown (legacy interface). Off: the gesture-first handle and arc replace them.</summary>
        public bool LegacyRailEnabled => _legacyRail;

        /// <summary>The warning shown in a banner slot (a <see cref="FlightWarning"/> value), or -1.</summary>
        public int WarningInSlot(int slot) => slot >= 0 && slot < WarningSlotCount && _slotWarning[slot] != NoValue ? _slotWarning[slot] : -1;

        /// <summary>
        /// Shows or hides SYSTEMS and the rail. The warnings (and the flight information
        /// cluster) are unaffected: they belong to both interfaces.
        /// </summary>
        public void SetLegacyRailEnabled(bool enabled)
        {
            EnsureInitialized();
            _legacyRail = enabled;
            if (systemsGroup != null)
            {
                systemsGroup.alpha = enabled ? _opacity : 0f;
                systemsGroup.blocksRaycasts = enabled;
                systemsGroup.interactable = enabled;
            }
        }
        /// <summary>The player's HUD opacity (the buttons; warnings are never dimmed).</summary>
        public float Opacity => _opacity;

        /// <summary>The action shown in a rail slot, if the slot is in use.</summary>
        public bool TryGetSlotAction(int slot, out ContextAction action)
        {
            bool used = slot >= 0 && slot < RailSlotCount && _slotUsed[slot];
            action = used ? _slotAction[slot] : default;
            return used;
        }

        /// <summary>Rail slot of an action, or -1 if the aircraft does not have it.</summary>
        public int SlotOf(ContextAction action)
        {
            for (int i = 0; i < RailSlotCount; i++)
                if (_slotUsed[i] && _slotAction[i] == action) return i;
            return -1;
        }

        public static int ActionBit(ContextAction action) => 1 << (int)action;
        public static int AllActions => (1 << ContextActions.Count) - 1;

        void Awake() => EnsureInitialized();

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) RefreshMotionPreference(); // the player may have changed the system setting
        }

        /// <summary>
        /// Assigns the aircraft's actions to rail slots, bottom up in
        /// <see cref="ContextAction"/> order. Slots never change afterwards, so an
        /// action is always in the same place. Unused slots stay hidden.
        /// </summary>
        public void ConfigureActions(int availableMask)
        {
            _actionsConfigured = true; // before initialisation, which would otherwise configure every action
            EnsureInitialized();
            int slot = 0;
            for (int a = 0; a < ContextActions.Count && slot < RailSlotCount; a++)
            {
                if ((availableMask & (1 << a)) == 0) continue;
                var action = (ContextAction)a;
                _slotAction[slot] = action;
                _slotUsed[slot] = true;
                if (railSlots[slot] != null) railSlots[slot].SetLabel(ActionLabel(action, false));
                slot++;
            }
            for (; slot < RailSlotCount; slot++) _slotUsed[slot] = false;
        }

        /// <summary>
        /// Dims the HUD buttons (the rail, SYSTEMS) to the player's opacity, 0.6-1. Warning
        /// banners always stay at full strength. The flight information cluster has its own.
        /// </summary>
        public void SetOpacity(float opacity)
        {
            EnsureInitialized();
            _opacity = Mathf.Clamp(opacity, 0.2f, 1f);
            for (int i = 0; i < RailSlotCount; i++) _railFades[i]?.SetOpacityScale(_opacity);
            if (systemsGroup != null) systemsGroup.alpha = _legacyRail ? _opacity : 0f;
        }

        public void SetMotionPreference(MotionPreference preference)
        {
            motionPreference = preference;
            RefreshMotionPreference();
        }

        void RefreshMotionPreference() =>
            _reducedMotion = SystemMotion.Resolve(motionPreference, motionPreference == MotionPreference.FollowSystem && SystemMotion.PrefersReducedMotion());

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _warningFades = new HudFade[WarningSlotCount];
            for (int i = 0; i < WarningSlotCount; i++)
            {
                if (warningSlots[i] != null) _warningFades[i] = new HudFade(warningSlots[i], interactive: false);
                _slotWarning[i] = NoValue;
            }

            _railFades = new HudFade[RailSlotCount];
            for (int i = 0; i < RailSlotCount; i++)
            {
                var slot = railSlots[i];
                if (slot == null) continue;
                var group = slot.GetComponent<CanvasGroup>();
                if (group == null) group = slot.gameObject.AddComponent<CanvasGroup>();
                _railFades[i] = new HudFade(group, interactive: true);
                int index = i; // one closure per slot, created once
                slot.Clicked += () => OnRailSlotClicked(index);
            }
            if (systemsButton != null) systemsButton.Clicked += OnSystemsClicked;

            AssignSprites();
            RefreshMotionPreference();
            if (!_actionsConfigured) ConfigureActions(AllActions);
        }

        /// <summary>Runtime-generated sprites cannot be stored in a scene, so they are assigned here.</summary>
        void AssignSprites()
        {
            for (int k = 0; k < warningBackgrounds.Length; k++)
                if (warningBackgrounds[k] != null && warningBackgrounds[k].sprite == null) HudStyle.Round(warningBackgrounds[k], HudStyle.BannerRadius);
        }

        /// <summary>
        /// Updates the banners (most urgent first) and the rail from the disclosure controller's
        /// decisions. Call once per frame; animation runs in LateUpdate.
        /// </summary>
        public void Present(DisclosureController disclosure, FlightWarningMonitor warnings, HudProfile profile)
        {
            if (disclosure == null) return;
            EnsureInitialized();
            float fade = motion.FadeSeconds(_reducedMotion);
            float slide = motion.Slide(_reducedMotion);
            PresentWarnings(warnings, fade);
            PresentRail(disclosure, profile, fade, slide);
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _warningFades.Length; i++) _warningFades[i]?.Tick(dt);
            for (int i = 0; i < _railFades.Length; i++) _railFades[i]?.Tick(dt);
        }

        // ---- Warnings ---------------------------------------------------------------

        void PresentWarnings(FlightWarningMonitor warnings, float fade)
        {
            int count = warnings != null ? warnings.ActiveCount : 0;
            float fadeIn = motion.WarningFadeSeconds(_reducedMotion);
            for (int k = 0; k < WarningSlotCount; k++)
            {
                var slotFade = _warningFades[k];
                if (slotFade == null) continue;
                if (k >= count)
                {
                    slotFade.SetTarget(0f, fade, 0f); // keeps its last text while it fades
                    continue;
                }

                var warning = warnings.ActiveAt(k);
                if (_slotWarning[k] != (int)warning)
                {
                    _slotWarning[k] = (int)warning;
                    bool red = FlightWarnings.Severity(warning) == WarningSeverity.Warning;
                    if (warningLabels[k] != null)
                    {
                        warningLabels[k].text = FlightWarnings.Label(warning);
                        warningLabels[k].color = red ? warningTextColor : cautionTextColor;
                    }
                    if (warningBackgrounds[k] != null) warningBackgrounds[k].color = red ? warningColor : cautionColor;
                    var icon = k < warningIcons.Length ? warningIcons[k] : null;
                    if (icon != null)
                    {
                        icon.sprite = red ? HudSprites.WarningIcon : HudSprites.CautionIcon;
                        icon.color = red ? warningTextColor : cautionTextColor;
                    }
                }
                slotFade.SetTarget(1f, fadeIn, 0f); // appears in place, never slides
            }
        }

        // ---- Action rail ------------------------------------------------------------

        void PresentRail(DisclosureController disclosure, HudProfile profile, float fade, float slide)
        {
            for (int s = 0; s < RailSlotCount; s++)
            {
                var slotFade = _railFades[s];
                var button = railSlots[s];
                if (slotFade == null || button == null) continue;
                if (!_slotUsed[s] || (!_legacyRail && !button.IsPressed))
                {
                    slotFade.SetTarget(0f, _slotUsed[s] ? fade : 0f, 0f);
                    continue;
                }

                var action = _slotAction[s];
                bool engaged = disclosure.IsActionEngaged(action);
                // Never pull a button away from under a finger that is pressing it.
                bool visible = disclosure.IsActionVisible(action) || button.IsPressed;
                slotFade.SetTarget(visible ? 1f : 0f, fade, slide);
                button.SetInteractable(disclosure.IsActionEnabled(action));
                button.SetEngaged(engaged);
                button.SetLabel(ActionLabel(action, engaged));
            }

            if (systemsButton != null)
            {
                bool useful = DisclosureController.SystemsToggleUseful(profile);
                systemsButton.SetInteractable(useful);
                systemsButton.SetEngaged(useful && disclosure.RailExpanded);
            }
        }

        void OnRailSlotClicked(int slot)
        {
            if (_slotUsed[slot]) ActionRequested?.Invoke(_slotAction[slot]);
        }

        void OnSystemsClicked() => SystemsToggleRequested?.Invoke();

        public static string ActionLabel(ContextAction action, bool engaged)
        {
            switch (action)
            {
                case ContextAction.ParkingBrake: return engaged ? "BRAKE SET" : "PARK BRAKE";
                case ContextAction.Flaps: return "FLAPS";
                case ContextAction.ApproachGuide: return "APPROACH";
                case ContextAction.Navigation: return "NAV";
                case ContextAction.Assistance: return "ASSIST";
                default: return "?";
            }
        }
    }
}
