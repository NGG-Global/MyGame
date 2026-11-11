using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// Geometry of the top-right command cluster, in canvas units (1080 reference height)
    /// measured from the safe area's top-right corner: x grows to the right, so every x is at
    /// most 0, and y grows upwards, so every y is at most 0. Pure, so the placement on 4:3, 16:9
    /// and 20:9 screens and at every interface scale is tested in EditMode; the factory builds
    /// exactly these rectangles.
    ///
    /// Hierarchy by size and place (docs/TAP_DISCLOSURE_DESIGN.md, milestone 3):
    /// - Primary row, right to left: the camera (the most used, nearest the corner and the
    ///   largest visual, with the view's name), the options trigger (a smaller disc), and pause
    ///   (the smallest and quietest). Each touch target is 96 units tall whatever its visual.
    /// - LOOK FWD under the camera, only while it is useful (cockpit view turned away).
    /// - The options panel under the row, against the right edge: a secondary row (HUD) and a
    ///   tertiary row of three slots (RESET, SETTINGS, SKIP TUTORIAL), or the HUD selector in
    ///   the same place. It stays inside the rail's inset from the right edge, so it can never
    ///   reach the warning column at any interface scale, and above the throttle.
    /// </summary>
    public static class CommandClusterLayout
    {
        public const float RightMargin = 24f;   // ContextualHudFactory.Margin
        public const float TopMargin = 16f;     // ContextualHudFactory.TopMargin
        public const float HitHeight = HudStyle.MinTouchTarget;
        public const float Gap = HudStyle.Space;

        public const float CameraHitWidth = 112f;
        public const float OptionsHitWidth = HudStyle.MinTouchTarget;
        public const float PauseHitWidth = HudStyle.MinTouchTarget;
        public static readonly Vector2 CameraVisualSize = new Vector2(92f, 72f);
        public static readonly Vector2 OptionsVisualSize = new Vector2(64f, 64f);
        public static readonly Vector2 PauseVisualSize = new Vector2(52f, 52f);
        public static readonly Vector2 LookForwardVisualSize = new Vector2(104f, 52f);

        public const float PanelWidth = 380f;
        public const float PanelPadding = 4f;
        public const float RowHeight = HudStyle.MinTouchTarget;
        public const float SlotGap = 4f;
        public const int SlotCount = 3;
        /// <summary>Visual height of a panel row's pill inside its 96-unit touch target.</summary>
        public const float RowVisualHeight = 80f;
        /// <summary>Visual height of a tertiary action's pill (quieter, smaller than the secondary row).</summary>
        public const float SlotVisualHeight = 64f;

        /// <summary>Tertiary slots: fixed places, so an action never moves under a finger.</summary>
        public const int ResetSlot = 0, SettingsSlot = 1, SkipSlot = 2;

        static float RowTop => -(TopMargin + HitHeight + Gap);

        // ---- Primary row ------------------------------------------------------------------------------

        public static Rect CameraHit => Rect.MinMaxRect(-(RightMargin + CameraHitWidth), -(TopMargin + HitHeight), -RightMargin, -TopMargin);

        public static Rect OptionsHit
        {
            get
            {
                float right = CameraHit.xMin - Gap;
                return Rect.MinMaxRect(right - OptionsHitWidth, -(TopMargin + HitHeight), right, -TopMargin);
            }
        }

        public static Rect PauseHit
        {
            get
            {
                float right = OptionsHit.xMin - Gap;
                return Rect.MinMaxRect(right - PauseHitWidth, -(TopMargin + HitHeight), right, -TopMargin);
            }
        }

        /// <summary>Distance from the right edge to the left edge of the primary row.</summary>
        public static float RowWidth => -PauseHit.xMin;
        /// <summary>The row's bottom below the top edge (the top bar's height).</summary>
        public static float RowBottom => TopMargin + HitHeight;

        // ---- Contextual camera option -----------------------------------------------------------------

        /// <summary>LOOK FWD: under the camera, the same width, a full touch target.</summary>
        public static Rect LookForwardHit => Rect.MinMaxRect(CameraHit.xMin, RowTop - HitHeight, CameraHit.xMax, RowTop);

        // ---- Options panel ----------------------------------------------------------------------------

        public static Rect Panel => Rect.MinMaxRect(-(RightMargin + PanelWidth), RowTop - (2f * RowHeight + 3f * PanelPadding), -RightMargin, RowTop);

        /// <summary>The secondary row (HUD, or the selector's title): the panel's full inner width.</summary>
        public static Rect SecondaryRow
        {
            get
            {
                var p = Panel;
                return Rect.MinMaxRect(p.xMin + PanelPadding, p.yMax - PanelPadding - RowHeight, p.xMax - PanelPadding, p.yMax - PanelPadding);
            }
        }

        /// <summary>A slot of the lower row (the tertiary actions, or the three profiles of the selector).</summary>
        public static Rect Slot(int index)
        {
            index = Mathf.Clamp(index, 0, SlotCount - 1);
            var p = Panel;
            float inner = p.width - 2f * PanelPadding;
            float width = (inner - (SlotCount - 1) * SlotGap) / SlotCount;
            float left = p.xMin + PanelPadding + index * (width + SlotGap);
            float bottom = p.yMin + PanelPadding;
            return Rect.MinMaxRect(left, bottom, left + width, bottom + RowHeight);
        }

        /// <summary>The lowest point of the cluster below the top edge (the open panel): what must stay above the throttle.</summary>
        public static float ColumnBottom => -Panel.yMin;
        /// <summary>From the right edge to the panel's left edge.</summary>
        public static float PanelInset => -Panel.xMin;

        // ---- Helpers ----------------------------------------------------------------------------------

        /// <summary>A visual of the given size centred in a touch target.</summary>
        public static Rect Centred(Rect hit, Vector2 visual) =>
            new Rect(hit.center.x - visual.x * 0.5f, hit.center.y - visual.y * 0.5f, visual.x, visual.y);

        /// <summary>A top-right rectangle placed in a safe area given in canvas coordinates (origin bottom left).</summary>
        public static Rect InSafeArea(Rect topRight, Rect safeArea) =>
            new Rect(safeArea.xMax + topRight.xMin, safeArea.yMax + topRight.yMin, topRight.width, topRight.height);

        /// <summary>The anchored position (pivot and anchors at the top-right corner) of a top-right rectangle.</summary>
        public static Vector2 AnchoredPosition(Rect topRight) => new Vector2(topRight.xMax, topRight.yMax);
    }
}
