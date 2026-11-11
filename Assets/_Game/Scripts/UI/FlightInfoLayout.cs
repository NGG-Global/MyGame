using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// Geometry of the top-left flight information cluster, in canvas units (1080 reference
    /// height) measured from the safe area's top-left corner: x grows to the right, so every x
    /// is at least 0, and y grows upwards, so every y is at most 0. Pure, so the placement on
    /// 4:3, 16:10, 16:9, 19.5:9 and 20:9 screens at every interface scale is tested in EditMode;
    /// the factory builds exactly these rectangles.
    ///
    /// One corner, three states (docs/TAP_DISCLOSURE_DESIGN.md, milestone 4):
    /// - The capsule: a 72-unit pill inside a 96-unit touch target at the top-left corner. Its
    ///   left end is the trigger (the flight-data symbol); the readings follow in fixed-width
    ///   cells, then at most one guide chip. It grows and shrinks from its right end only.
    /// - The panel opens from the same corner: its first row starts with the trigger in the
    ///   capsule's place, then a tile per reading in the same order as the capsule's cells; its
    ///   second row holds the navigation details (left, region A) and the approach details
    ///   (right, region B).
    /// - A focused reading takes region A (enlarged) and its explanation region B; a focused
    ///   guide keeps its own tile and the explanation takes the other region.
    ///
    /// The whole cluster stays in the top band: above the joystick region (whose top is 22 % of
    /// the safe height below the top edge; the interface-scale limit keeps
    /// <see cref="Bottom"/> above it) and left of the command cluster's row, so it never meets
    /// the flight controls, the warnings (250 units down) or the right-hand controls.
    /// </summary>
    public static class FlightInfoLayout
    {
        public const float LeftMargin = 24f;   // ContextualHudFactory.Margin
        public const float TopMargin = 16f;    // ContextualHudFactory.TopMargin
        public const float HitHeight = HudStyle.MinTouchTarget;

        // ---- Capsule ------------------------------------------------------------------------------
        public const float CapsuleVisualHeight = 72f;
        public const float CapsulePadding = 4f;
        /// <summary>The trigger's cell at the capsule's left end (the flight-data symbol).</summary>
        public const float TriggerCellWidth = 64f;
        public const float CellWidth = 112f;
        public const float ChipWidth = 192f;

        // ---- Panel --------------------------------------------------------------------------------
        public const float TileWidth = 140f;
        public const float TileGap = 4f;
        public const float RowGap = 2f;
        /// <summary>The plate beyond the touch targets at the right and the bottom (kept small: the band above the joystick region is tight).</summary>
        public const float PanelPadding = 2f;
        /// <summary>Region A of the lower row: the navigation details, or an enlarged reading, or an explanation.</summary>
        public const float RegionAWidth = 376f;
        /// <summary>Visual height of a tile's plate inside its 96-unit touch target.</summary>
        public const float TileVisualHeight = 88f;

        /// <summary>The trigger's touch target: the capsule's left end, and the panel's first cell (the same place in every state).</summary>
        public static Rect TriggerHit => Rect.MinMaxRect(LeftMargin, -(TopMargin + HitHeight), LeftMargin + HitHeight, -TopMargin);

        static float CapsuleTop => -TopMargin - (HitHeight - CapsuleVisualHeight) * 0.5f;

        /// <summary>The capsule's visual width for a number of readings, with or without a guide chip.</summary>
        public static float CapsuleVisualWidth(int readings, bool chip) =>
            2f * CapsulePadding + TriggerCellWidth + Mathf.Clamp(readings, 0, FlightInfoModel.MaxCapsuleReadings) * CellWidth + (chip ? ChipWidth : 0f);

        /// <summary>The capsule's pill.</summary>
        public static Rect CapsuleVisual(int readings, bool chip)
        {
            float top = CapsuleTop;
            return Rect.MinMaxRect(LeftMargin, top - CapsuleVisualHeight, LeftMargin + CapsuleVisualWidth(readings, chip), top);
        }

        /// <summary>The capsule's touch target: the pill and the space above and below it, never narrower than a touch target.</summary>
        public static Rect CapsuleHit(int readings, bool chip) =>
            Rect.MinMaxRect(LeftMargin, -(TopMargin + HitHeight), LeftMargin + Mathf.Max(HitHeight, CapsuleVisualWidth(readings, chip)), -TopMargin);

        /// <summary>The trigger's cell inside the pill.</summary>
        public static Rect CapsuleTrigger
        {
            get
            {
                float top = CapsuleTop, left = LeftMargin + CapsulePadding;
                return Rect.MinMaxRect(left, top - CapsuleVisualHeight, left + TriggerCellWidth, top);
            }
        }

        /// <summary>A reading's cell in the pill (fixed width; cell 0 next to the trigger).</summary>
        public static Rect CapsuleCell(int cell)
        {
            cell = Mathf.Clamp(cell, 0, FlightInfoModel.MaxCapsuleReadings - 1);
            float top = CapsuleTop, left = LeftMargin + CapsulePadding + TriggerCellWidth + cell * CellWidth;
            return Rect.MinMaxRect(left, top - CapsuleVisualHeight, left + CellWidth, top);
        }

        /// <summary>The guide chip after the readings.</summary>
        public static Rect CapsuleChip(int readings)
        {
            float top = CapsuleTop;
            float left = LeftMargin + CapsulePadding + TriggerCellWidth + Mathf.Clamp(readings, 0, FlightInfoModel.MaxCapsuleReadings) * CellWidth;
            return Rect.MinMaxRect(left, top - CapsuleVisualHeight, left + ChipWidth, top);
        }

        /// <summary>The widest the capsule can be (every reading cell and the chip).</summary>
        public static float MaxCapsuleWidth => CapsuleVisualWidth(FlightInfoModel.MaxCapsuleReadings, true);

        // ---- Panel --------------------------------------------------------------------------------

        static float Row2Top => -(TopMargin + HitHeight + RowGap);

        /// <summary>A reading's tile in the panel's first row, after the trigger (index = the reading's <c>HudElement</c> value).</summary>
        public static Rect Tile(int reading)
        {
            reading = Mathf.Clamp(reading, 0, FlightReadouts.ReadingCount - 1);
            float left = TriggerHit.xMax + TileGap + reading * (TileWidth + TileGap);
            return Rect.MinMaxRect(left, -(TopMargin + HitHeight), left + TileWidth, -TopMargin);
        }

        /// <summary>The right edge of the panel's content (the last tile).</summary>
        static float ContentRight => Tile(FlightReadouts.ReadingCount - 1).xMax;

        /// <summary>The lower row, both regions.</summary>
        public static Rect LowerRow => Rect.MinMaxRect(LeftMargin, Row2Top - HitHeight, ContentRight, Row2Top);

        /// <summary>Region A (left): navigation details, an enlarged reading, or the approach's explanation.</summary>
        public static Rect RegionA => Rect.MinMaxRect(LeftMargin, Row2Top - HitHeight, LeftMargin + RegionAWidth, Row2Top);

        /// <summary>Region B (right): approach details, or an explanation.</summary>
        public static Rect RegionB => Rect.MinMaxRect(RegionA.xMax + TileGap, Row2Top - HitHeight, ContentRight, Row2Top);

        /// <summary>The panel's plate, around both rows.</summary>
        public static Rect Panel => Rect.MinMaxRect(LeftMargin, LowerRow.yMin - PanelPadding, ContentRight + PanelPadding, -TopMargin);

        /// <summary>The lowest point of the cluster below the top edge (the open panel): what must stay above the joystick region.</summary>
        public static float Bottom => -Panel.yMin;
        /// <summary>The rightmost point of the cluster from the left edge (the open panel): what must stay left of the command cluster's row.</summary>
        public static float Right => Mathf.Max(Panel.xMax, LeftMargin + MaxCapsuleWidth);

        // ---- Helpers ------------------------------------------------------------------------------

        /// <summary>A visual of the given size centred in a touch target.</summary>
        public static Rect Centred(Rect hit, Vector2 visual) =>
            new Rect(hit.center.x - visual.x * 0.5f, hit.center.y - visual.y * 0.5f, visual.x, visual.y);

        /// <summary>A top-left rectangle placed in a safe area given in canvas coordinates (origin bottom left).</summary>
        public static Rect InSafeArea(Rect topLeft, Rect safeArea) =>
            new Rect(safeArea.xMin + topLeft.xMin, safeArea.yMax + topLeft.yMin, topLeft.width, topLeft.height);

        /// <summary>The anchored position (pivot and anchors at the top-left corner) of a top-left rectangle.</summary>
        public static Vector2 AnchoredPosition(Rect topLeft) => new Vector2(topLeft.xMin, topLeft.yMax);

        /// <summary>A rectangle relative to another one's top-left corner (children of a placed parent).</summary>
        public static Rect Relative(Rect r, Rect parent) => new Rect(r.xMin - parent.xMin, r.yMin - parent.yMax, r.width, r.height);
    }
}
