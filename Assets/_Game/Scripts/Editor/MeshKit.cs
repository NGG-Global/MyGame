using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Procedural geometry for the scene builders: an accumulating mesh with
    /// submeshes, lofts through rings of points (with openings and analytic smooth
    /// normals), surfaces of revolution, NACA four-digit aerofoil sections (whole
    /// or cut at a hinge line) and simple flat-shaded solids. Pure functions on
    /// plain lists; <see cref="MeshData.ToMesh"/> is the only engine call. Every
    /// builder in the project models with these so the parts share one look.
    /// </summary>
    public static class MeshKit
    {
        // ---- Sections (2D rings, counter-clockwise seen from +Z) -------------------------------------

        /// <summary>
        /// A rounded rectangle (superellipse) ring: <paramref name="squareness"/> 2 is an
        /// ellipse, 3-4 a rounded box. Point 0 is at the bottom, the ring runs through the
        /// right side (+X), the top and the left side, so the ring parameter v = i / count maps
        /// 0.25 to the right, 0.5 to the top and 0.75 to the left.
        /// </summary>
        public static Vector2[] RoundedSection(int count, float halfWidth, float halfHeight, float centreY, float squareness)
        {
            var ring = new Vector2[count];
            float exponent = 2f / Mathf.Max(1.5f, squareness);
            for (int i = 0; i < count; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * 2f * i / count;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float x = halfWidth * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), exponent);
                float y = halfHeight * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), exponent);
                ring[i] = new Vector2(x, centreY + y);
            }
            return ring;
        }

        /// <summary>A circle of the given radius, same orientation as <see cref="RoundedSection"/>.</summary>
        public static Vector2[] Circle(int count, float radius, float centreY = 0f) => RoundedSection(count, radius, radius, centreY, 2f);

        /// <summary>
        /// NACA four-digit aerofoil outline, chord 1 along +X (leading edge at the origin),
        /// thickness up (+Y). The ring starts at the trailing edge, runs over the upper
        /// surface to the leading edge and back under, so v = 0 and 1 are the trailing edge
        /// and v = 0.5 the leading edge. <paramref name="samples"/> points per surface.
        /// </summary>
        public static Vector2[] Airfoil(int samples, float thickness, float camber, float camberPosition)
        {
            var ring = new List<Vector2>(samples * 2);
            // Upper surface from the trailing edge to the leading edge (cosine spacing: dense at the leading edge).
            for (int i = samples; i >= 0; i--)
                ring.Add(AirfoilPoint(0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / samples), thickness, camber, camberPosition, upper: true));
            // Lower surface back towards the trailing edge, without the two shared end points.
            for (int i = 1; i < samples; i++)
                ring.Add(AirfoilPoint(0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / samples), thickness, camber, camberPosition, upper: false));
            return ring.ToArray();
        }

        /// <summary>
        /// The front part of an aerofoil, cut square at <paramref name="hingeFraction"/> of
        /// the chord (where a flap or aileron starts). Same point count and order as
        /// <see cref="Airfoil"/>: the points behind the hinge are projected onto the cut
        /// edge (upper ones onto its upper half, lower ones onto its lower half), so a loft
        /// can mix whole and cut sections and the bay ends close themselves.
        /// </summary>
        public static Vector2[] AirfoilFront(int samples, float thickness, float camber, float camberPosition, float hingeFraction)
        {
            var ring = Airfoil(samples, thickness, camber, camberPosition);
            var top = AirfoilPoint(hingeFraction, thickness, camber, camberPosition, true);
            var bottom = AirfoilPoint(hingeFraction, thickness, camber, camberPosition, false);
            float middle = 0.5f * (top.y + bottom.y);
            for (int i = 0; i < ring.Length; i++)
            {
                if (ring[i].x <= hingeFraction) continue;
                float f = Mathf.Clamp01((ring[i].x - hingeFraction) / Mathf.Max(1e-4f, 1f - hingeFraction)); // 0 at the hinge, 1 at the trailing edge
                bool upper = i <= samples; // the ring runs trailing edge (0), upper surface, leading edge (samples), lower surface
                ring[i] = new Vector2(hingeFraction, Mathf.Lerp(upper ? top.y : bottom.y, middle, f));
            }
            return ring;
        }

        /// <summary>
        /// The rear part of an aerofoil behind <paramref name="hingeFraction"/>, with a rounded
        /// nose that turns inside the front part's cut: a flap, aileron, elevator or rudder. The
        /// origin is on the hinge line (chord line at the hinge), chord 1 is the whole wing's
        /// chord, so this ring is (1 - hinge) long.
        /// </summary>
        public static Vector2[] AirfoilSurface(int samples, float thickness, float camber, float camberPosition, float hingeFraction)
        {
            var full = Airfoil(samples, thickness, camber, camberPosition);
            float noseRadius = 0.5f * (AirfoilPoint(hingeFraction, thickness, camber, camberPosition, true).y
                                       - AirfoilPoint(hingeFraction, thickness, camber, camberPosition, false).y) * 0.92f;
            var ring = new List<Vector2>(full.Length);
            float hingeY = Camber(hingeFraction, camber, camberPosition);
            // Trailing edge over the top to the hinge (the ring starts at the trailing edge).
            foreach (var p in full)
            {
                if (p.x < hingeFraction + 0.01f) break;
                ring.Add(p);
            }
            // Round nose turning down inside the cut, centred slightly behind the hinge line.
            const int noseSteps = 6;
            var centre = new Vector2(hingeFraction + 0.012f, hingeY);
            for (int i = 0; i <= noseSteps; i++)
            {
                float a = Mathf.PI * 0.5f + Mathf.PI * i / noseSteps; // from the top (90°) round the front to the bottom (270°)
                ring.Add(centre + new Vector2(Mathf.Cos(a) * noseRadius, Mathf.Sin(a) * noseRadius));
            }
            // Lower surface from the hinge back to the trailing edge (the ring's leading edge is at index samples).
            for (int i = samples; i < full.Length; i++)
                if (full[i].x >= hingeFraction + 0.01f) ring.Add(full[i]);
            for (int i = 0; i < ring.Count; i++) ring[i] -= new Vector2(hingeFraction, hingeY);
            return ring.ToArray();
        }

        static Vector2 AirfoilPoint(float x, float thickness, float camber, float camberPosition, bool upper)
        {
            x = Mathf.Clamp01(x);
            float t = 5f * thickness * (0.2969f * Mathf.Sqrt(x) - 0.1260f * x - 0.3516f * x * x + 0.2843f * x * x * x - 0.1036f * x * x * x * x);
            float yc = Camber(x, camber, camberPosition);
            return new Vector2(x, upper ? yc + t : yc - t);
        }

        static float Camber(float x, float m, float p)
        {
            if (m <= 0f || p <= 0f || p >= 1f) return 0f;
            return x < p ? m / (p * p) * (2f * p * x - x * x) : m / ((1f - p) * (1f - p)) * ((1f - 2f * p) + 2f * p * x - x * x);
        }

        // ---- Lofts -----------------------------------------------------------------------------------

        /// <summary>One ring of a loft: 3D points (the same count in every ring) and its texture coordinate along the loft.</summary>
        public struct Ring
        {
            public Vector3[] Points;
            public float U;

            public Ring(Vector3[] points, float u)
            {
                Points = points;
                U = u;
            }
        }

        /// <summary>A 2D section placed in 3D: its points go to (x, y) and the station along +Z, with an optional transform.</summary>
        public static Ring Place(Vector2[] section, float z, float u, Matrix4x4 transform)
        {
            var points = new Vector3[section.Length];
            for (int i = 0; i < section.Length; i++) points[i] = transform.MultiplyPoint3x4(new Vector3(section[i].x, section[i].y, z));
            return new Ring(points, u);
        }

        public static Ring Place(Vector2[] section, float z, float u) => Place(section, z, u, Matrix4x4.identity);

        /// <summary>Decides whether the quad between ring segment i (0..rings-2) and ring point j (0..points-1) is left open.</summary>
        public delegate bool Opening(int segment, int point, float u, float v);

        /// <summary>
        /// Skins the rings with quads, smooth analytic normals (closed around each ring and
        /// continuous along the loft), u from the rings and v around the ring (0..1, the first
        /// point duplicated at v = 1). Quads inside <paramref name="opening"/> are skipped;
        /// <paramref name="capStart"/> and <paramref name="capEnd"/> close the ends with a fan.
        /// <paramref name="inside"/> flips the faces (an interior shell).
        /// </summary>
        public static void Loft(MeshData mesh, IList<Ring> rings, int submesh, Opening opening = null, bool capStart = false, bool capEnd = false,
            bool inside = false)
        {
            if (rings.Count < 2) throw new ArgumentException("A loft needs at least two rings.");
            int n = rings[0].Points.Length;
            foreach (var r in rings)
                if (r.Points.Length != n) throw new ArgumentException("Every ring of a loft must have the same number of points.");

            int first = mesh.VertexCount;
            for (int i = 0; i < rings.Count; i++)
            {
                var ring = rings[i].Points;
                var previous = rings[Mathf.Max(0, i - 1)].Points;
                var next = rings[Mathf.Min(rings.Count - 1, i + 1)].Points;
                for (int j = 0; j <= n; j++)
                {
                    int jj = j % n;
                    Vector3 around = ring[(jj + 1) % n] - ring[(jj + n - 1) % n];
                    Vector3 along = next[jj] - previous[jj];
                    Vector3 normal = Vector3.Cross(around, along);
                    if (normal.sqrMagnitude < 1e-12f)
                    {
                        // Degenerate (a ring collapsed to a point): use the loft direction.
                        normal = i == rings.Count - 1 ? along : -along;
                        if (normal.sqrMagnitude < 1e-12f) normal = Vector3.up;
                    }
                    normal.Normalize();
                    if (inside) normal = -normal;
                    mesh.AddVertex(ring[jj], normal, new Vector2(rings[i].U, (float)j / n));
                }
            }
            int stride = n + 1;
            for (int i = 0; i < rings.Count - 1; i++)
            for (int j = 0; j < n; j++)
            {
                float u = 0.5f * (rings[i].U + rings[i + 1].U);
                float v = (j + 0.5f) / n;
                if (opening != null && opening(i, j, u, v)) continue;
                int a = first + i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                // Rings run counter-clockwise seen from +Z and the loft advances along +Z: this winding faces out.
                if (inside) mesh.AddQuad(submesh, a, c, d, b);
                else mesh.AddQuad(submesh, a, b, d, c);
            }
            if (capStart) Cap(mesh, rings[0], submesh, !inside);
            if (capEnd) Cap(mesh, rings[rings.Count - 1], submesh, inside);
        }

        /// <summary>A flat fan closing a ring; <paramref name="facingBack"/> faces -Z of the loft (a start cap).</summary>
        static void Cap(MeshData mesh, Ring ring, int submesh, bool facingBack)
        {
            int n = ring.Points.Length;
            var centre = Vector3.zero;
            foreach (var p in ring.Points) centre += p;
            centre /= n;
            Vector3 normal = Vector3.zero;
            for (int j = 0; j < n; j++) normal += Vector3.Cross(ring.Points[j] - centre, ring.Points[(j + 1) % n] - centre);
            if (normal.sqrMagnitude < 1e-12f) return; // a degenerate ring needs no cap
            normal.Normalize();
            if (facingBack) normal = -normal;
            int c = mesh.AddVertex(centre, normal, new Vector2(ring.U, 0.5f));
            int first = mesh.VertexCount;
            for (int j = 0; j < n; j++) mesh.AddVertex(ring.Points[j], normal, new Vector2(ring.U, (float)j / n));
            for (int j = 0; j < n; j++)
            {
                int a = first + j, b = first + (j + 1) % n;
                if (facingBack) mesh.AddTriangle(submesh, c, b, a);
                else mesh.AddTriangle(submesh, c, a, b);
            }
        }

        /// <summary>
        /// A surface of revolution about +Y from a profile of (radius, height) points, bottom
        /// first. A zero radius makes a tip; the ends are capped when open.
        /// </summary>
        public static void Revolve(MeshData mesh, IList<Vector2> profile, int segments, int submesh, bool capBottom = true, bool capTop = true)
        {
            var rings = new List<Ring>(profile.Count);
            float length = 0f;
            for (int i = 1; i < profile.Count; i++) length += Vector2.Distance(profile[i], profile[i - 1]);
            float run = 0f;
            for (int i = 0; i < profile.Count; i++)
            {
                if (i > 0) run += Vector2.Distance(profile[i], profile[i - 1]);
                var points = new Vector3[segments];
                for (int s = 0; s < segments; s++)
                {
                    float a = Mathf.PI * 2f * s / segments;
                    // Counter-clockwise seen from +Y (the loft direction): x = cos, z = -sin.
                    points[s] = new Vector3(Mathf.Cos(a) * profile[i].x, profile[i].y, -Mathf.Sin(a) * profile[i].x);
                }
                rings.Add(new Ring(points, length > 0f ? run / length : 0f));
            }
            bool bottomOpen = profile[0].x > 1e-4f, topOpen = profile[profile.Count - 1].x > 1e-4f;
            Loft(mesh, rings, submesh, null, capStart: capBottom && bottomOpen, capEnd: capTop && topOpen);
        }

        // ---- Flat-shaded solids ------------------------------------------------------------------------

        /// <summary>An axis-aligned box centred at <paramref name="centre"/>, flat shaded, one unit of texture per face.</summary>
        public static void Box(MeshData mesh, Vector3 centre, Vector3 size, int submesh)
        {
            Vector3 h = size * 0.5f;
            Vector3 c = centre;
            // Each face: four corners counter-clockwise seen from outside.
            Face(mesh, submesh, c + new Vector3(-h.x, -h.y, -h.z), c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(h.x, h.y, -h.z), c + new Vector3(h.x, -h.y, -h.z)); // back (-Z)
            Face(mesh, submesh, c + new Vector3(h.x, -h.y, h.z), c + new Vector3(h.x, h.y, h.z), c + new Vector3(-h.x, h.y, h.z), c + new Vector3(-h.x, -h.y, h.z));     // front (+Z)
            Face(mesh, submesh, c + new Vector3(-h.x, -h.y, h.z), c + new Vector3(-h.x, h.y, h.z), c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(-h.x, -h.y, -h.z)); // left (-X)
            Face(mesh, submesh, c + new Vector3(h.x, -h.y, -h.z), c + new Vector3(h.x, h.y, -h.z), c + new Vector3(h.x, h.y, h.z), c + new Vector3(h.x, -h.y, h.z));     // right (+X)
            Face(mesh, submesh, c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(-h.x, h.y, h.z), c + new Vector3(h.x, h.y, h.z), c + new Vector3(h.x, h.y, -h.z));     // top (+Y)
            Face(mesh, submesh, c + new Vector3(-h.x, -h.y, h.z), c + new Vector3(-h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, h.z)); // bottom (-Y)
        }

        /// <summary>
        /// A house: a box of walls and a gabled roof (ridge along +Z) with a small overhang;
        /// walls in <paramref name="wallSubmesh"/>, roof and gables in <paramref name="roofSubmesh"/>.
        /// The footprint is centred on the origin and the floor is at y = 0.
        /// </summary>
        public static void House(MeshData mesh, float width, float depth, float wallHeight, float roofHeight, int wallSubmesh, int roofSubmesh)
        {
            float hw = width * 0.5f, hd = depth * 0.5f, over = 0.25f;
            Box(mesh, new Vector3(0f, wallHeight * 0.5f, 0f), new Vector3(width, wallHeight, depth), wallSubmesh);
            // Gable triangles above the end walls (in the wall colour would be usual, but the roof colour reads better at distance).
            var ridgeFront = new Vector3(0f, wallHeight + roofHeight, hd);
            var ridgeBack = new Vector3(0f, wallHeight + roofHeight, -hd);
            Tri(mesh, roofSubmesh, new Vector3(hw, wallHeight, hd), ridgeFront, new Vector3(-hw, wallHeight, hd));
            Tri(mesh, roofSubmesh, new Vector3(-hw, wallHeight, -hd), ridgeBack, new Vector3(hw, wallHeight, -hd));
            // Two roof slopes with an overhang.
            var rf = ridgeFront + new Vector3(0f, 0.02f, over);
            var rb = ridgeBack + new Vector3(0f, 0.02f, -over);
            float eave = wallHeight - roofHeight * over / hw;
            Face(mesh, roofSubmesh, new Vector3(-hw - over, eave, hd + over), rf, rb, new Vector3(-hw - over, eave, -hd - over));
            Face(mesh, roofSubmesh, new Vector3(hw + over, eave, -hd - over), rb, rf, new Vector3(hw + over, eave, hd + over));
        }

        /// <summary>A flat quad from four counter-clockwise corners (seen from the front), with its own normal.</summary>
        public static void Face(MeshData mesh, int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            int i = mesh.AddVertex(a, normal, new Vector2(0f, 0f));
            mesh.AddVertex(b, normal, new Vector2(0f, 1f));
            mesh.AddVertex(c, normal, new Vector2(1f, 1f));
            mesh.AddVertex(d, normal, new Vector2(1f, 0f));
            mesh.AddQuad(submesh, i, i + 1, i + 2, i + 3);
        }

        public static void Tri(MeshData mesh, int submesh, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            int i = mesh.AddVertex(a, normal, new Vector2(0f, 0f));
            mesh.AddVertex(b, normal, new Vector2(0.5f, 1f));
            mesh.AddVertex(c, normal, new Vector2(1f, 0f));
            mesh.AddTriangle(submesh, i, i + 1, i + 2);
        }

        /// <summary>A flat disc in the XY plane facing +Z, radial texture coordinates (centre 0.5, 0.5).</summary>
        public static void Disc(MeshData mesh, float radius, int segments, int submesh)
        {
            int c = mesh.AddVertex(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f));
            int first = mesh.VertexCount;
            for (int s = 0; s < segments; s++)
            {
                float a = Mathf.PI * 2f * s / segments;
                mesh.AddVertex(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f), Vector3.forward,
                    new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
            }
            for (int s = 0; s < segments; s++) mesh.AddTriangle(submesh, c, first + s, first + (s + 1) % segments);
        }

        /// <summary>A regular grid in the XZ plane (y from <paramref name="height"/>), facing up, with texture coordinates 0..1.</summary>
        public static void Grid(MeshData mesh, int columns, int rows, float width, float depth, Func<int, int, float> height, int submesh)
        {
            int first = mesh.VertexCount;
            for (int r = 0; r <= rows; r++)
            for (int c = 0; c <= columns; c++)
            {
                float u = (float)c / columns, v = (float)r / rows;
                mesh.AddVertex(new Vector3((u - 0.5f) * width, height(c, r), (v - 0.5f) * depth), Vector3.up, new Vector2(u, v));
            }
            int stride = columns + 1;
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                int a = first + r * stride + c, b = a + 1, d = a + stride, e = d + 1;
                // Alternate the diagonal so slopes read evenly.
                if ((r + c) % 2 == 0) { mesh.AddTriangle(submesh, a, d, e); mesh.AddTriangle(submesh, a, e, b); }
                else { mesh.AddTriangle(submesh, a, d, b); mesh.AddTriangle(submesh, b, d, e); }
            }
        }
    }

    /// <summary>Vertices, normals, texture coordinates and per-submesh triangles under construction.</summary>
    public sealed class MeshData
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<List<int>> _submeshes = new List<List<int>>();

        public int VertexCount => _vertices.Count;
        public int SubmeshCount => _submeshes.Count;
        public IReadOnlyList<Vector3> Vertices => _vertices;
        public IReadOnlyList<Vector3> Normals => _normals;
        public IReadOnlyList<Vector2> Uv => _uv;
        public IReadOnlyList<int> Triangles(int submesh) => _submeshes[submesh];

        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _uv.Add(uv);
            return _vertices.Count - 1;
        }

        public void AddTriangle(int submesh, int a, int b, int c)
        {
            while (_submeshes.Count <= submesh) _submeshes.Add(new List<int>());
            var list = _submeshes[submesh];
            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        /// <summary>Two triangles from four counter-clockwise corners.</summary>
        public void AddQuad(int submesh, int a, int b, int c, int d)
        {
            AddTriangle(submesh, a, b, c);
            AddTriangle(submesh, a, c, d);
        }

        /// <summary>
        /// Appends another mesh, transformed; its submesh s lands in submesh s + <paramref name="submeshOffset"/>.
        /// A mirrored transform (negative determinant) flips the winding so the faces still point out.
        /// </summary>
        public void Append(MeshData other, Matrix4x4 transform, int submeshOffset = 0)
        {
            int first = _vertices.Count;
            bool mirrored = transform.determinant < 0f;
            var normalMatrix = transform.inverse.transpose;
            for (int i = 0; i < other._vertices.Count; i++)
            {
                var n = normalMatrix.MultiplyVector(other._normals[i]);
                if (n.sqrMagnitude > 1e-12f) n.Normalize();
                AddVertex(transform.MultiplyPoint3x4(other._vertices[i]), n, other._uv[i]);
            }
            for (int s = 0; s < other._submeshes.Count; s++)
            {
                var source = other._submeshes[s];
                for (int t = 0; t < source.Count; t += 3)
                {
                    if (mirrored) AddTriangle(s + submeshOffset, first + source[t], first + source[t + 2], first + source[t + 1]);
                    else AddTriangle(s + submeshOffset, first + source[t], first + source[t + 1], first + source[t + 2]);
                }
            }
        }

        /// <summary>Moves every texture coordinate into a rectangle of the atlas (0..1 becomes the rectangle).</summary>
        public void MapUv(Rect region)
        {
            for (int i = 0; i < _uv.Count; i++)
                _uv[i] = new Vector2(region.xMin + _uv[i].x * region.width, region.yMin + _uv[i].y * region.height);
        }

        /// <summary>Moves the texture coordinates of the vertices added since <paramref name="fromVertex"/>.</summary>
        public void MapUv(Rect region, int fromVertex)
        {
            for (int i = fromVertex; i < _uv.Count; i++)
                _uv[i] = new Vector2(region.xMin + _uv[i].x * region.width, region.yMin + _uv[i].y * region.height);
        }

        public void Transform(Matrix4x4 transform, int fromVertex = 0)
        {
            var normalMatrix = transform.inverse.transpose;
            for (int i = fromVertex; i < _vertices.Count; i++)
            {
                _vertices[i] = transform.MultiplyPoint3x4(_vertices[i]);
                var n = normalMatrix.MultiplyVector(_normals[i]);
                _normals[i] = n.sqrMagnitude > 1e-12f ? n.normalized : _normals[i];
            }
            if (transform.determinant < 0f)
                foreach (var list in _submeshes)
                    for (int t = 0; t < list.Count; t += 3)
                    {
                        int swap = list[t + 1];
                        list[t + 1] = list[t + 2];
                        list[t + 2] = swap;
                    }
        }

        /// <summary>Takes over another mesh's contents (used when re-mapping submeshes).</summary>
        public void Replace(MeshData other)
        {
            _vertices.Clear();
            _normals.Clear();
            _uv.Clear();
            _submeshes.Clear();
            _vertices.AddRange(other._vertices);
            _normals.AddRange(other._normals);
            _uv.AddRange(other._uv);
            foreach (var list in other._submeshes) _submeshes.Add(new List<int>(list));
        }

        public Bounds Bounds()
        {
            if (_vertices.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var b = new Bounds(_vertices[0], Vector3.zero);
            foreach (var v in _vertices) b.Encapsulate(v);
            return b;
        }

        /// <summary>The engine mesh: 32-bit indices when needed, tangents for normal maps, bounds.</summary>
        public Mesh ToMesh(string name, bool tangents = true)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uv);
            mesh.subMeshCount = Math.Max(1, _submeshes.Count);
            for (int s = 0; s < _submeshes.Count; s++) mesh.SetTriangles(_submeshes[s], s, true);
            if (tangents) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
