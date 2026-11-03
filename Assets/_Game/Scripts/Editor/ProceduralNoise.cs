using UnityEngine;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Deterministic noise for the generated textures and terrain: integer hashing
    /// (no engine random state), periodic value noise that tiles across a texture,
    /// fractal sums and a cellular (Voronoi) field for field patterns. Pure functions.
    /// </summary>
    public static class ProceduralNoise
    {
        /// <summary>A hash of an integer lattice point in 0..1, periodic in <paramref name="period"/> cells (0 = not periodic).</summary>
        public static float Hash(int x, int y, int period, int seed)
        {
            if (period > 0)
            {
                x = ((x % period) + period) % period;
                y = ((y % period) + period) % period;
            }
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Smooth value noise, 0..1, with <paramref name="cells"/> cells across the unit square; tiles when sampled in 0..1.</summary>
        public static float Value(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, cells, seed), b = Hash(x0 + 1, y0, cells, seed);
            float c = Hash(x0, y0 + 1, cells, seed), d = Hash(x0 + 1, y0 + 1, cells, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fractal (octave) value noise, 0..1, centred near 0.5.</summary>
        public static float Fractal(float u, float v, int baseCells, int octaves, int seed, float persistence = 0.5f)
        {
            float n = 0f, amplitude = 1f, total = 0f;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                n += amplitude * Value(u, v, cells, seed + o * 101);
                total += amplitude;
                amplitude *= persistence;
                cells *= 2;
            }
            return n / total;
        }

        /// <summary>
        /// Cellular field: the id of the nearest random point (one per cell, jittered) and
        /// the distance to it and to the second nearest, in cell units. Periodic in
        /// <paramref name="cells"/>. Field boundaries are where the two distances are equal.
        /// </summary>
        public static void Cellular(float u, float v, int cells, int seed, out int id, out float nearest, out float second)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            nearest = float.MaxValue;
            second = float.MaxValue;
            id = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = x0 + dx, cy = y0 + dy;
                float px = cx + Hash(cx, cy, cells, seed), py = cy + Hash(cx, cy, cells, seed + 7);
                float d = (px - x) * (px - x) + (py - y) * (py - y);
                if (d < nearest)
                {
                    second = nearest;
                    nearest = d;
                    int wx = ((cx % cells) + cells) % cells, wy = ((cy % cells) + cells) % cells;
                    id = wy * cells + wx;
                }
                else if (d < second) second = d;
            }
            nearest = Mathf.Sqrt(nearest);
            second = Mathf.Sqrt(second);
        }

        /// <summary>A pseudo-random 0..1 value for an integer id.</summary>
        public static float Random01(int id, int seed) => Hash(id, seed, 0, seed * 31 + 17);

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
