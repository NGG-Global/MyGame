using UnityEngine;

namespace OpenSkies.Editor
{
    /// <summary>
    /// The scenery's colours in one tiny texture: every generated prop (trees,
    /// houses, boats, the lighthouse, the tower) maps its texture coordinates to the
    /// centre of a palette cell, so one material draws many colours in one batch.
    /// No mipmaps and nearest filtering keep the cells from bleeding at distance.
    /// </summary>
    public static class ScenePalette
    {
        public const string Path = "Assets/_Game/Art/Textures/Prototype/ScenePalette.png";
        public const int Columns = 8, Rows = 4, CellPixels = 8;

        public enum Cell
        {
            White = 0, Red, HouseCream, HouseOchre, HouseWhite, HouseGrey, HouseRose, RoofTerracotta,
            RoofDark, RoofSlate, FoliageLight, FoliageMid, FoliageDark, Conifer, Trunk, HullWhite,
            HullBlue, HullRed, Windsock, Concrete, DarkMetal, HangarDoor, CabGlass, FoliageAutumn,
            Wood, FuelRed, Stone, Asphalt, Sand, Rust, Steel, Navy,
        }

        public static readonly Color[] Colours =
        {
            new Color(0.92f, 0.92f, 0.90f), new Color(0.72f, 0.12f, 0.10f), new Color(0.90f, 0.85f, 0.74f), new Color(0.85f, 0.70f, 0.45f),
            new Color(0.93f, 0.92f, 0.90f), new Color(0.70f, 0.70f, 0.68f), new Color(0.86f, 0.68f, 0.60f), new Color(0.62f, 0.30f, 0.20f),
            new Color(0.30f, 0.30f, 0.32f), new Color(0.42f, 0.45f, 0.50f), new Color(0.33f, 0.48f, 0.20f), new Color(0.24f, 0.40f, 0.17f),
            new Color(0.17f, 0.30f, 0.13f), new Color(0.15f, 0.28f, 0.17f), new Color(0.33f, 0.25f, 0.17f), new Color(0.92f, 0.92f, 0.90f),
            new Color(0.15f, 0.30f, 0.55f), new Color(0.70f, 0.15f, 0.12f), new Color(0.95f, 0.45f, 0.10f), new Color(0.62f, 0.60f, 0.57f),
            new Color(0.25f, 0.26f, 0.28f), new Color(0.40f, 0.42f, 0.45f), new Color(0.15f, 0.20f, 0.25f), new Color(0.50f, 0.42f, 0.18f),
            new Color(0.45f, 0.35f, 0.22f), new Color(0.75f, 0.15f, 0.12f), new Color(0.55f, 0.52f, 0.48f), new Color(0.22f, 0.22f, 0.23f),
            new Color(0.79f, 0.73f, 0.57f), new Color(0.55f, 0.30f, 0.18f), new Color(0.60f, 0.62f, 0.65f), new Color(0.10f, 0.16f, 0.30f),
        };

        /// <summary>A zero-size rectangle at the centre of the cell: mapping texture coordinates to it gives the cell's colour everywhere.</summary>
        public static Rect Uv(Cell cell)
        {
            int i = (int)cell;
            float u = (i % Columns + 0.5f) / Columns, v = (i / Columns + 0.5f) / Rows;
            return new Rect(u, v, 0f, 0f);
        }

        public static Color32[] Pixels()
        {
            int width = Columns * CellPixels, height = Rows * CellPixels;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int cell = (y / CellPixels) * Columns + x / CellPixels;
                pixels[y * width + x] = cell < Colours.Length ? (Color32)Colours[cell] : new Color32(255, 0, 255, 255);
            }
            return pixels;
        }

        /// <summary>The palette texture asset (created once).</summary>
        public static Texture2D LoadOrCreate() =>
            GeneratedAssets.LoadOrCreateTexture(Path, Columns * CellPixels, Rows * CellPixels, Pixels, GeneratedAssets.TextureKind.Palette, 64);
    }
}
