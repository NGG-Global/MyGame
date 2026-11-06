using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// Fits the systems wheel to the free space around its handle: the largest band (up to
    /// the preferred outer radius) whose segments are all full touch targets and stay clear
    /// of everything the wheel must never cover (the joystick region, the throttle, the
    /// brake, the handle itself, the warning banners, the safe-area edges). The angles come
    /// from the longest free run around the handle; the slots are centred in it. Pure and
    /// deterministic, so the layout at 4:3, 16:9 and 20:9 is tested in EditMode. The slots keep
    /// the preferred first angle when the run allows, so positions stay the same from screen to
    /// screen wherever there is room.
    /// </summary>
    public static class SystemsWheelLayout
    {
        /// <summary>Most keep-out rectangles a fit looks at.</summary>
        public const int MaxKeepOut = 16;
        /// <summary>The angular window searched for a free run, degrees (0 = right, 90 = up).</summary>
        public const int SearchFrom = 30, SearchTo = 300;
        const int RadialSamples = 8;
        const float RadiusStep = 4f;

        /// <summary>The space the wheel opens into, in the HUD's local units (origin at the safe area's bottom left).</summary>
        public struct Space
        {
            public Rect Bounds;
            /// <summary>The wheel's centre: the handle's centre.</summary>
            public Vector2 Anchor;
            public Rect[] KeepOut;
            public int KeepOutCount;
        }

        public struct Result
        {
            /// <summary>A copy of the preferred settings with the fitted radii and angles (never the asset itself).</summary>
            public SystemsArcSettings Geometry;
            /// <summary>Every segment is a full touch target and clear of every keep-out.</summary>
            public bool Fits;
            /// <summary>Segment length along the middle of the band, canvas units.</summary>
            public float SegmentLength;
            /// <summary>How many segments fit at the minimum segment length (the most options a focus can show at full size).</summary>
            public int Capacity;
            /// <summary>The free angular run the slots were placed in, degrees.</summary>
            public float RunStart, RunEnd;
        }

        /// <summary>
        /// The geometry for <paramref name="itemCount"/> primary segments. Tries the preferred
        /// outer radius first and shrinks it until the segments fit; if none does, returns the
        /// candidate with the longest segments and <see cref="Result.Fits"/> false.
        /// </summary>
        public static Result Fit(SystemsArcSettings preferred, in Space space, int itemCount)
        {
            var p = preferred ?? new SystemsArcSettings();
            int n = Mathf.Clamp(itemCount, 1, SystemsArcModel.MaxItems);
            float minThickness = Mathf.Max(1f, p.minBandThickness);
            float preferredThickness = Mathf.Max(minThickness, p.outerRadius - p.innerRadius);
            float minOuter = p.selectionInnerRadius + minThickness;
            var best = new Result { Geometry = p.Clone(), Fits = false, SegmentLength = -1f };
            if (!(space.Bounds.width > 0f) || !(space.Bounds.height > 0f)) return best;
            var firstFit = best;

            for (float outer = Mathf.Max(p.outerRadius, minOuter); outer >= minOuter - 0.01f; outer -= RadiusStep)
            {
                for (int t = 0; t < 2; t++)
                {
                    float thickness = t == 0 ? preferredThickness : minThickness;
                    if (t == 1 && thickness >= preferredThickness - 0.01f) break;
                    float inner = outer - thickness;
                    if (inner < p.selectionInnerRadius) continue;
                    if (!LongestRun(space, inner, outer, p.fitClearance, p.fitMargin, out float runStart, out float runEnd)) continue;

                    var candidate = Place(p, n, inner, outer, runStart, runEnd);
                    if (candidate.Fits)
                    {
                        // Consistent positions first: the largest band that keeps the preferred first slot wins;
                        // otherwise the largest band that fits at all, with its slots moved as little as possible.
                        if (Mathf.Abs(candidate.Geometry.firstSlotAngle - p.firstSlotAngle) < 0.5f) return candidate;
                        if (!firstFit.Fits) firstFit = candidate;
                    }
                    else if (candidate.SegmentLength > best.SegmentLength) best = candidate;
                }
            }
            return firstFit.Fits ? firstFit : best;
        }

        /// <summary>Slots of a fitted band, centred in a free run.</summary>
        static Result Place(SystemsArcSettings p, int n, float inner, float outer, float runStart, float runEnd)
        {
            float usable = runEnd - runStart - 2f * Mathf.Max(0f, p.endTolerance);
            float step = Mathf.Min(p.maxSlotStep, usable / n);
            var g = p.Clone();
            g.innerRadius = inner;
            g.outerRadius = outer;
            float mid = (inner + outer) * 0.5f;
            float length = step > 0f ? mid * step * Mathf.Deg2Rad : 0f;
            if (step > 0f)
            {
                // The first slot stays where the design puts it (left of straight up, away from the
                // throttle) whenever the free run allows; otherwise it moves just enough.
                g.slotStep = step;
                float lowest = runStart + p.endTolerance + step * 0.5f;
                float highest = runEnd - p.endTolerance - step * 0.5f - (n - 1) * step;
                g.firstSlotAngle = Mathf.Clamp(p.firstSlotAngle, lowest, Mathf.Max(lowest, highest));
                g.slotCentreMax = runEnd - p.endTolerance - step * 0.5f;
            }
            float minStep = p.minSegmentLength / Mathf.Max(1f, mid) * Mathf.Rad2Deg;
            return new Result
            {
                Geometry = g,
                Fits = step > 0f && length >= p.minSegmentLength - 0.01f && outer - inner >= p.minBandThickness - 0.01f,
                SegmentLength = length,
                Capacity = minStep > 0f ? Mathf.Clamp(Mathf.FloorToInt(usable / minStep + 1e-3f), 0, SystemsArcModel.MaxItems) : 0,
                RunStart = runStart,
                RunEnd = runEnd,
            };
        }

        /// <summary>The longest run of whole degrees where a radial line through the band touches nothing it must avoid.</summary>
        public static bool LongestRun(in Space space, float inner, float outer, float clearance, float margin, out float start, out float end)
        {
            start = end = 0f;
            int bestStart = -1, bestLength = -1, runStart = -1;
            for (int a = SearchFrom; a <= SearchTo + 1; a++)
            {
                bool free = a <= SearchTo && RadialLineFree(space, a, inner, outer, clearance, margin);
                if (free && runStart < 0) runStart = a;
                if (!free && runStart >= 0)
                {
                    int length = a - 1 - runStart;
                    if (length > bestLength) { bestLength = length; bestStart = runStart; }
                    runStart = -1;
                }
            }
            if (bestLength <= 0) return false;
            start = bestStart;
            end = bestStart + bestLength;
            return true;
        }

        /// <summary>Whether the band's radial line at an angle stays inside the bounds and outside every keep-out.</summary>
        public static bool RadialLineFree(in Space space, float angleDegrees, float inner, float outer, float clearance, float margin)
        {
            var direction = new Vector2(Mathf.Cos(angleDegrees * Mathf.Deg2Rad), Mathf.Sin(angleDegrees * Mathf.Deg2Rad));
            var b = space.Bounds;
            for (int i = 0; i <= RadialSamples; i++)
            {
                float r = inner + (outer - inner) * i / RadialSamples;
                var point = space.Anchor + direction * r;
                if (point.x < b.xMin + margin || point.x > b.xMax - margin || point.y < b.yMin + margin || point.y > b.yMax - margin) return false;
                if (InKeepOut(space, point, clearance)) return false;
            }
            return true;
        }

        /// <summary>Whether a point is within <paramref name="clearance"/> of a keep-out rectangle.</summary>
        public static bool InKeepOut(in Space space, Vector2 point, float clearance)
        {
            if (space.KeepOut == null) return false;
            int count = Mathf.Min(space.KeepOutCount, space.KeepOut.Length);
            for (int k = 0; k < count; k++)
            {
                var r = space.KeepOut[k];
                if (r.width <= 0f || r.height <= 0f) continue;
                if (point.x >= r.xMin - clearance && point.x <= r.xMax + clearance && point.y >= r.yMin - clearance && point.y <= r.yMax + clearance)
                    return true;
            }
            return false;
        }
    }
}
