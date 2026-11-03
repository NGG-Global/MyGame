using OpenSkies.Awareness;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// The HUD density scale that appears while the player pulls down from the
    /// HUD band: three stops (CINEMATIC, SMART, DETAILED), a marker that follows
    /// the finger continuously, the previewed stop in cyan and the committed one
    /// ringed, and a note saying what releasing does. Draws what
    /// <see cref="HudDensityModel"/> says; the profile is changed only by the session.
    /// </summary>
    public sealed class HudDensityLadder : MonoBehaviour
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image plate;
        public Image rail;
        public Image[] stops = new Image[HudDensityModel.LevelCount];
        public Text[] labels = new Text[HudDensityModel.LevelCount];
        public Image committedRing;
        public Image marker;
        public Text note;
        public HudDensitySettings settings = new HudDensitySettings();
        public HudMotionSettings motion = new HudMotionSettings();

        [Header("Look")]
        public Color railColor = HudStyle.Hairline;
        public Color stopColor = HudStyle.TextSecondary;
        public Color previewColor = HudStyle.Selected;
        public Color committedColor = HudStyle.TextPrimary;
        public float plateWidth = 320f;
        public float platePadding = 56f;
        public float labelOffset = 36f;

        const string NoChange = "Release: HUD unchanged";
        const string ReleaseToApply = "Release to apply";
        static readonly string[] Names = { "CINEMATIC", "SMART", "DETAILED" };

        HudFade _fade;
        float _markerY;
        bool _reducedMotion, _initialized;
        float _opacity = 1f;

        public bool IsShown => _fade != null && _fade.IsShown;

        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        public void SetOpacity(float opacity)
        {
            _opacity = Mathf.Clamp01(opacity);
            _fade?.SetOpacityScale(_opacity);
        }

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (group != null) _fade = new HudFade(group, interactive: false);
            _fade?.SetOpacityScale(_opacity);
            if (plate != null && plate.sprite == null) HudStyle.Round(plate, HudStyle.PanelRadius);
            for (int i = 0; i < stops.Length; i++)
            {
                if (stops[i] != null && stops[i].sprite == null) stops[i].sprite = HudSprites.Disc;
                if (labels[i] != null) labels[i].text = Names[i];
            }
            if (marker != null && marker.sprite == null) marker.sprite = HudSprites.Disc;
            if (committedRing != null && committedRing.sprite == null) committedRing.sprite = HudSprites.Ring;
        }

        /// <summary>Level 0 (Cinematic) is at the top; each level is one step down, matching the pull.</summary>
        float LevelY(int level) => -level * settings.stepDistance;

        /// <summary>The ladder appears with the committed stop at the finger (canvas-local point in the parent of <see cref="root"/>).</summary>
        public void Show(HudDensityModel model, Vector2 localOrigin, Rect bounds)
        {
            EnsureInitialized();
            if (root == null || model == null) return;
            int committed = HudDensityModel.LevelOf(model.Committed);
            // The stop for the committed level sits at the finger; the whole scale stays inside the bounds.
            var position = localOrigin - new Vector2(0f, LevelY(committed));
            float top = LevelY(0) + platePadding, bottom = LevelY(HudDensityModel.LevelCount - 1) - platePadding;
            if (position.y + top > bounds.yMax) position.y = bounds.yMax - top;
            if (position.y + bottom < bounds.yMin) position.y = bounds.yMin - bottom;
            position.x = Mathf.Clamp(position.x, bounds.xMin + plateWidth * 0.5f, bounds.xMax - plateWidth * 0.5f);
            root.anchoredPosition = position;
            if (plate != null)
            {
                plate.rectTransform.anchoredPosition = new Vector2(0f, (top + bottom) * 0.5f);
                plate.rectTransform.sizeDelta = new Vector2(plateWidth, top - bottom);
            }
            if (rail != null)
            {
                rail.rectTransform.anchoredPosition = new Vector2(0f, (LevelY(0) + LevelY(HudDensityModel.LevelCount - 1)) * 0.5f);
                rail.rectTransform.sizeDelta = new Vector2(4f, LevelY(0) - LevelY(HudDensityModel.LevelCount - 1));
                rail.color = railColor;
            }
            for (int i = 0; i < HudDensityModel.LevelCount; i++)
            {
                if (stops[i] != null) stops[i].rectTransform.anchoredPosition = new Vector2(0f, LevelY(i));
                if (labels[i] != null) labels[i].rectTransform.anchoredPosition = new Vector2(labelOffset, LevelY(i));
            }
            if (committedRing != null) committedRing.rectTransform.anchoredPosition = new Vector2(0f, LevelY(committed));
            if (note != null) note.rectTransform.anchoredPosition = new Vector2(labelOffset, LevelY(HudDensityModel.LevelCount - 1) - 40f);
            _markerY = LevelY(committed);
            _fade?.SetTarget(1f, motion.FadeSeconds(_reducedMotion), 0f);
            Present(model);
        }

        public void Hide() => _fade?.SetTarget(0f, motion.FadeSeconds(_reducedMotion), 0f);

        public void Present(HudDensityModel model)
        {
            if (model == null || !model.IsActive) return;
            float targetY = -model.ScalePosition * settings.stepDistance;
            _markerY = _reducedMotion ? targetY : HudEasing.Approach(_markerY, targetY, 0.04f, Time.unscaledDeltaTime);
            if (marker != null)
            {
                marker.rectTransform.anchoredPosition = new Vector2(0f, _markerY);
                marker.color = model.Changed ? previewColor : committedColor;
            }
            int preview = model.PreviewLevel, committed = HudDensityModel.LevelOf(model.Committed);
            for (int i = 0; i < HudDensityModel.LevelCount; i++)
            {
                Color c = i == preview ? previewColor : (i == committed ? committedColor : stopColor);
                if (stops[i] != null) stops[i].color = c;
                if (labels[i] != null) labels[i].color = c;
            }
            if (note != null)
            {
                string text = model.Changed ? ReleaseToApply : NoChange;
                if (!ReferenceEquals(note.text, text)) note.text = text;
            }
        }

        void LateUpdate()
        {
            if (_initialized) _fade?.Tick(Time.unscaledDeltaTime);
        }
    }
}
