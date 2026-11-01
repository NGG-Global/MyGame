using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// One short flying tip at the bottom of the free column (between the joystick
    /// area and the action rail), for the Assisted profile. It takes no touches,
    /// fades in place, and only changes text when the tip changes.
    /// </summary>
    public sealed class GuidanceLine : MonoBehaviour
    {
        public CanvasGroup group;
        public Text text;
        public Image backing;
        public HudMotionSettings motion = new HudMotionSettings();

        HudFade _fade;
        bool _reducedMotion;

        public bool Shown => _fade != null && _fade.IsShown;
        public string CurrentTip => text != null ? text.text : string.Empty;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_fade == null && group != null) _fade = new HudFade(group, interactive: false);
            if (backing != null && backing.sprite == null)
            {
                backing.sprite = HudSprites.RoundedRect;
                backing.type = Image.Type.Sliced;
            }
        }

        /// <summary>Shows a tip (a cached string), or hides the line for an empty one.</summary>
        public void Present(string tip)
        {
            EnsureInitialized();
            bool show = !string.IsNullOrEmpty(tip);
            if (show && text != null && !ReferenceEquals(text.text, tip)) text.text = tip;
            _fade?.SetTarget(show ? 1f : 0f, motion.FadeSeconds(_reducedMotion), 0f);
        }

        void LateUpdate() => _fade?.Tick(Time.unscaledDeltaTime);
    }
}
