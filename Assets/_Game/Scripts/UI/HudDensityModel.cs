using System;
using OpenSkies.Awareness;
using OpenSkies.Gestures;
using UnityEngine;

namespace OpenSkies.UI
{
    [Serializable]
    public sealed class HudDensitySettings
    {
        [Tooltip("Finger travel between two HUD levels, canvas units.")]
        public float stepDistance = 110f;
        [Tooltip("The level changes only this far past the midpoint (no flicker).")]
        public float hysteresis = 18f;
    }

    /// <summary>
    /// The HUD density pull as data: the three profiles as a vertical scale
    /// (Cinematic at the top, Smart, Detailed at the bottom). Pulling down from the
    /// HUD band previews the next level, pulling up the previous; the player can
    /// reverse before releasing. The committed profile changes only when the
    /// session commits the preview. Pure.
    /// </summary>
    public sealed class HudDensityModel
    {
        public const int LevelCount = 3;

        readonly HudDensitySettings _s;
        HudProfile _committed = HudProfile.Smart;
        int _previewLevel;
        float _startY, _travel;
        bool _active;

        public HudDensityModel(HudDensitySettings settings = null)
        {
            _s = settings ?? new HudDensitySettings();
        }

        public HudDensitySettings Settings => _s;
        public bool IsActive => _active;
        public HudProfile Committed => _committed;
        public HudProfile Preview => ProfileAt(_previewLevel);
        public int PreviewLevel => _previewLevel;
        public bool Changed => _active && Preview != _committed;
        /// <summary>Finger travel from the start, positive = down (more HUD), canvas units.</summary>
        public float Travel => _travel;
        /// <summary>Continuous position on the scale in levels (0 = Cinematic, 2 = Detailed), for the ladder.</summary>
        public float ScalePosition => Mathf.Clamp(LevelOf(_committed) + (_s.stepDistance > 0f ? _travel / _s.stepDistance : 0f), 0f, LevelCount - 1);

        /// <summary>Cinematic (least) to Detailed (most).</summary>
        public static int LevelOf(HudProfile profile)
        {
            switch (profile)
            {
                case HudProfile.Cinematic: return 0;
                case HudProfile.Detailed: return 2;
                default: return 1;
            }
        }

        public static HudProfile ProfileAt(int level)
        {
            switch (Mathf.Clamp(level, 0, LevelCount - 1))
            {
                case 0: return HudProfile.Cinematic;
                case 2: return HudProfile.Detailed;
                default: return HudProfile.Smart;
            }
        }

        public void Begin(HudProfile committed, float fingerY)
        {
            _committed = committed;
            _previewLevel = LevelOf(committed);
            _startY = fingerY;
            _travel = 0f;
            _active = true;
        }

        public void End() => _active = false;

        public void Update(float fingerY)
        {
            if (!_active) return;
            _travel = _startY - fingerY;
            int from = LevelOf(_committed);
            // Travel is relative to the committed level; SteppedLevel works from level 0, so offset it.
            _previewLevel = GestureMath.SteppedLevel(_travel, _s.stepDistance, _s.hysteresis, _previewLevel - from, -from, LevelCount - 1 - from) + from;
        }
    }
}
