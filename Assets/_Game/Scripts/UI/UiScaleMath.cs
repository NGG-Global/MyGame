using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// How far the interface can grow on a given screen. The canvas scales by
    /// height (1080 reference units), so a scale s leaves 1080/s units of height and
    /// aspect × 1080/s of width; elements keep their sizes in units, so a larger
    /// scale means less room. The limits come from the layout's tightest places:
    /// the action rail under the top bar, the SKIP/MENU column over the throttle,
    /// and the warning column between the joystick region and the rail.
    /// Plain C#, EditMode-tested; a PlayMode test checks the layout at the limit.
    /// </summary>
    public static class UiScaleMath
    {
        public const float ReferenceHeight = 1080f;
        public const float ReferenceWidth = 1920f;
        /// <summary>Clearance kept between neighbours at the limit, canvas units (edges never touch).</summary>
        public const float Clearance = 2f;

        /// <summary>Layout facts the limit depends on, in canvas units.</summary>
        public struct Layout
        {
            /// <summary>Right edge of the joystick region, fraction of the width.</summary>
            public float JoystickRegionRight;
            /// <summary>Top of the throttle's touch area above the bottom edge.</summary>
            public float ThrottleTop;
            /// <summary>Lowest point of the top-right session buttons (MENU, SKIP) below the top edge.</summary>
            public float SessionColumnBottom;
            /// <summary>Top of the highest action rail slot above the bottom edge.</summary>
            public float RailTop;
            /// <summary>Lowest point of the top bar (HUD profile, RESET, CAM) below the top edge.</summary>
            public float TopBarBottom;
            /// <summary>From the right edge to the left edge of the rail.</summary>
            public float RailLeftInset;
            public float WarningWidth;
            /// <summary>Warning column centre, fraction of the width.</summary>
            public float WarningColumn;
            /// <summary>The widest panel that must fit (the settings screen).</summary>
            public float WidestPanel;
        }

        /// <summary>The layout of this game's HUD, with the joystick region and throttle the scene uses.</summary>
        public static Layout HudLayout(float joystickRegionRight, float throttleTop) => new Layout
        {
            JoystickRegionRight = joystickRegionRight,
            ThrottleTop = throttleTop,
            SessionColumnBottom = -MissionUiFactory.SkipPosition.y + MissionUiFactory.SessionButtonSize.y,
            RailTop = ContextualHudFactory.RailBase.y + ContextualHud.RailSlotCount * ContextualHudFactory.RailStep + ContextualHudFactory.RailButtonSize.y,
            TopBarBottom = ContextualHudFactory.TopMargin + HudStyle.MinTouchTarget,
            RailLeftInset = ContextualHudFactory.RailLeftInset,
            WarningWidth = ContextualHudFactory.WarningSize.x,
            WarningColumn = ContextualHudFactory.WarningColumn,
            WidestPanel = SettingsScreenFactory.PanelWidth + 2f * HudStyle.Gap,
        };

        /// <summary>
        /// The largest scale at which nothing overlaps, for a safe area of this aspect ratio
        /// (width / height) that covers <paramref name="heightShare"/> of the screen height.
        /// </summary>
        public static float MaxScale(float aspect, in Layout layout, float heightShare = 1f)
        {
            if (float.IsNaN(aspect) || aspect <= 0f) aspect = ReferenceWidth / ReferenceHeight;
            if (float.IsNaN(heightShare) || heightShare <= 0f || heightShare > 1f) heightShare = 1f;

            // Vertical: the stacks that share a column must fit one above the other.
            float railStack = layout.RailTop + layout.TopBarBottom;
            float sessionStack = layout.ThrottleTop + layout.SessionColumnBottom;
            float neededHeight = Mathf.Max(railStack, sessionStack) + Clearance;
            float vertical = ReferenceHeight * heightShare / Mathf.Max(1f, neededHeight);

            // Horizontal: a warning banner fits between the joystick region and the rail,
            // and the widest panel fits.
            float half = layout.WarningWidth * 0.5f;
            float rightLimited = (layout.RailLeftInset + half + Clearance) / Mathf.Max(0.01f, 1f - layout.WarningColumn);
            float leftLimited = (half + Clearance) / Mathf.Max(0.01f, layout.WarningColumn - layout.JoystickRegionRight);
            float neededWidth = Mathf.Max(rightLimited, Mathf.Max(leftLimited, layout.WidestPanel));
            float horizontal = ReferenceHeight * heightShare * aspect / Mathf.Max(1f, neededWidth);

            return Mathf.Min(vertical, horizontal);
        }

        /// <summary>The scale to use: the player's choice, limited by the screen (never below the smallest option).</summary>
        public static float Effective(float requested, float aspect, in Layout layout, float heightShare = 1f, float minimum = 0.9f)
        {
            float max = MaxScale(aspect, layout, heightShare);
            float scale = Mathf.Min(requested, max);
            return Mathf.Max(Mathf.Min(minimum, max), scale);
        }

        /// <summary>CanvasScaler reference resolution for a scale (height matching).</summary>
        public static Vector2 ReferenceResolution(float scale)
        {
            scale = Mathf.Max(0.1f, scale);
            return new Vector2(ReferenceWidth / scale, ReferenceHeight / scale);
        }
    }
}
