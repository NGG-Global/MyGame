using UnityEngine;

namespace OpenSkies.Cockpit
{
    /// <summary>An oriented pick volume: centre, unit axes and half extents, world space.</summary>
    public struct PickBox
    {
        public Vector3 Centre;
        public Vector3 Right;
        public Vector3 Up;
        public Vector3 Forward;
        public Vector3 HalfExtents;
    }

    /// <summary>
    /// Pure picking math: a touch ray against oriented boxes, and a screen-space
    /// fallback so a tap close to a control still selects it. No physics colliders
    /// are involved, so the cockpit cannot disturb the aircraft's collisions or
    /// the landing-gear raycasts.
    /// </summary>
    public static class CockpitPicking
    {
        /// <summary>Slab test of a ray against an oriented box. Distance along the ray (≥ 0) to the entry point.</summary>
        public static bool Raycast(Vector3 origin, Vector3 direction, in PickBox box, out float distance)
        {
            distance = 0f;
            Vector3 offset = box.Centre - origin;
            float near = float.NegativeInfinity, far = float.PositiveInfinity;
            if (!Slab(offset, direction, box.Right, box.HalfExtents.x, ref near, ref far)) return false;
            if (!Slab(offset, direction, box.Up, box.HalfExtents.y, ref near, ref far)) return false;
            if (!Slab(offset, direction, box.Forward, box.HalfExtents.z, ref near, ref far)) return false;
            if (far < 0f) return false;            // behind the origin
            distance = near >= 0f ? near : 0f;     // origin inside the box
            return true;
        }

        static bool Slab(Vector3 offset, Vector3 direction, Vector3 axis, float halfExtent, ref float near, ref float far)
        {
            float e = Vector3.Dot(axis, offset);
            float f = Vector3.Dot(axis, direction);
            if (Mathf.Abs(f) > 1e-6f)
            {
                float t1 = (e + halfExtent) / f, t2 = (e - halfExtent) / f;
                if (t1 > t2) { float swap = t1; t1 = t2; t2 = swap; }
                if (t1 > near) near = t1;
                if (t2 < far) far = t2;
                return near <= far;
            }
            return -e - halfExtent <= 0f && -e + halfExtent >= 0f; // parallel: inside the slab?
        }

        /// <summary>Index of the nearest box hit by the ray, or -1.</summary>
        public static int Nearest(Vector3 origin, Vector3 direction, PickBox[] boxes, int count, out float distance)
        {
            int best = -1;
            distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!Raycast(origin, direction, boxes[i], out float d) || d >= distance) continue;
                best = i;
                distance = d;
            }
            return best;
        }

        /// <summary>
        /// Index of the screen point closest to <paramref name="tap"/> within
        /// <paramref name="tolerance"/> pixels, or -1. Points with a negative
        /// depth (behind the camera) are skipped.
        /// </summary>
        public static int NearestOnScreen(Vector2 tap, Vector3[] screenPoints, int count, float tolerance)
        {
            int best = -1;
            float bestSquared = tolerance * tolerance;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = screenPoints[i];
                if (p.z <= 0f) continue;
                float dx = p.x - tap.x, dy = p.y - tap.y;
                float squared = dx * dx + dy * dy;
                if (squared > bestSquared) continue;
                best = i;
                bestSquared = squared;
            }
            return best;
        }
    }
}
