using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Editor;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>The procedural geometry the scene builders model with: sections, lofts, revolutions, solids.</summary>
    public class MeshKitTests
    {
        [Test]
        public void Airfoil_StartsAtTheTrailingEdge_LeadingEdgeHalfWayRound_AndIsClosed()
        {
            var ring = MeshKit.Airfoil(12, 0.12f, 0.02f, 0.4f);
            Assert.AreEqual(24, ring.Length);
            Assert.That(ring[0].x, Is.EqualTo(1f).Within(1e-4f), "trailing edge first");
            Assert.That(ring[0].y, Is.EqualTo(0f).Within(1e-3f), "closed trailing edge");
            Assert.That(ring[12].x, Is.EqualTo(0f).Within(1e-4f), "leading edge half way round");
            float maxUpper = 0f, minLower = 0f;
            for (int i = 1; i < 12; i++) maxUpper = Mathf.Max(maxUpper, ring[i].y);
            for (int i = 13; i < 24; i++) minLower = Mathf.Min(minLower, ring[i].y);
            Assert.Greater(maxUpper, 0.05f, "upper surface above the chord");
            Assert.Less(minLower, -0.02f, "lower surface below the chord");
            Assert.Greater(maxUpper, -minLower, "camber lifts the upper surface more than the lower");
        }

        [Test]
        public void AirfoilFront_KeepsThePointCount_AndNothingBehindTheHinge()
        {
            var whole = MeshKit.Airfoil(12, 0.12f, 0.02f, 0.4f);
            var front = MeshKit.AirfoilFront(12, 0.12f, 0.02f, 0.4f, 0.75f);
            Assert.AreEqual(whole.Length, front.Length, "a loft can mix whole and cut sections");
            foreach (var p in front) Assert.LessOrEqual(p.x, 0.75f + 1e-5f);
            Assert.That(front[0].x, Is.EqualTo(0.75f).Within(1e-5f), "the trailing edge point sits on the cut");
        }

        [Test]
        public void AirfoilSurface_SitsBehindTheHinge_WithARoundedNose()
        {
            var surface = MeshKit.AirfoilSurface(12, 0.12f, 0.02f, 0.4f, 0.75f);
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in surface)
            {
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
            }
            Assert.That(maxX, Is.EqualTo(0.25f).Within(1e-4f), "the trailing edge is a quarter chord behind the hinge");
            Assert.Less(minX, 0f, "the nose turns inside the cut, in front of the hinge line");
            Assert.Greater(minX, -0.05f, "but not far");
        }

        [Test]
        public void Loft_OfACylinder_HasOutwardNormals_AndTheExpectedTopology()
        {
            var rings = new List<MeshKit.Ring>();
            for (int i = 0; i < 4; i++) rings.Add(MeshKit.Place(MeshKit.Circle(16, 1f), i * 0.5f, i / 3f));
            var mesh = new MeshData();
            MeshKit.Loft(mesh, rings, 0, null, capStart: true, capEnd: true);
            Assert.AreEqual(4 * 17 + 2 * 17, mesh.VertexCount, "rings with a seam vertex, plus two caps");
            Assert.AreEqual(3 * 16 * 2 * 3 + 2 * 16 * 3, mesh.Triangles(0).Count, "two triangles per quad, a fan per cap");
            for (int i = 0; i < 4 * 17; i++)
            {
                var radial = new Vector3(mesh.Vertices[i].x, mesh.Vertices[i].y, 0f).normalized;
                Assert.Greater(Vector3.Dot(mesh.Normals[i], radial), 0.99f, $"vertex {i} normal points out");
            }
            AssertWindingMatchesNormals(mesh);
        }

        [Test]
        public void Loft_Opening_LeavesQuadsOut()
        {
            var rings = new List<MeshKit.Ring>();
            for (int i = 0; i < 3; i++) rings.Add(MeshKit.Place(MeshKit.Circle(8, 1f), i, i / 2f));
            var mesh = new MeshData();
            MeshKit.Loft(mesh, rings, 0, (segment, point, u, v) => segment == 0 && v < 0.5f);
            Assert.AreEqual((16 - 4) * 2 * 3, mesh.Triangles(0).Count, "four of the first segment's eight quads are open");
        }

        [Test]
        public void Revolve_Cone_IsFinite_AndFacesOut()
        {
            var mesh = new MeshData();
            MeshKit.Revolve(mesh, new[] { new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 2f) }, 12, 0);
            Assert.Greater(mesh.VertexCount, 0);
            foreach (var n in mesh.Normals) Assert.IsFalse(float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z), "no NaN normals at the tip");
            AssertWindingMatchesNormals(mesh);
        }

        [Test]
        public void Box_IsFlatShaded_AndFacesOut()
        {
            var mesh = new MeshData();
            MeshKit.Box(mesh, new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f), 0);
            Assert.AreEqual(24, mesh.VertexCount);
            Assert.AreEqual(36, mesh.Triangles(0).Count);
            var centre = new Vector3(1f, 2f, 3f);
            for (int i = 0; i < mesh.VertexCount; i++)
                Assert.Greater(Vector3.Dot(mesh.Normals[i], mesh.Vertices[i] - centre), 0f, "every face normal points away from the centre");
            AssertWindingMatchesNormals(mesh);
        }

        [Test]
        public void House_HasWallsAndRoofInSeparateSubmeshes_FacingOut()
        {
            var mesh = new MeshData();
            MeshKit.House(mesh, 10f, 8f, 4f, 2f, 0, 1);
            Assert.AreEqual(2, mesh.SubmeshCount);
            Assert.AreEqual(36, mesh.Triangles(0).Count, "the walls are a box");
            Assert.AreEqual(2 * 3 + 2 * 6, mesh.Triangles(1).Count, "two gables and two slopes");
            AssertWindingMatchesNormals(mesh);
            var bounds = mesh.Bounds();
            Assert.That(bounds.max.y, Is.EqualTo(6.02f).Within(0.01f), "ridge at wall plus roof height");
        }

        [Test]
        public void Append_Mirrored_KeepsFacesPointingOut()
        {
            var box = new MeshData();
            MeshKit.Box(box, new Vector3(2f, 0f, 0f), Vector3.one, 0);
            var mesh = new MeshData();
            mesh.Append(box, Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)));
            var centre = new Vector3(-2f, 0f, 0f);
            for (int i = 0; i < mesh.VertexCount; i++)
                Assert.Greater(Vector3.Dot(mesh.Normals[i], mesh.Vertices[i] - centre), 0f, "normals mirrored with the positions");
            AssertWindingMatchesNormals(mesh);
        }

        [Test]
        public void MapUv_MovesCoordinatesIntoTheRegion()
        {
            var mesh = new MeshData();
            mesh.AddVertex(Vector3.zero, Vector3.up, new Vector2(0f, 0f));
            mesh.AddVertex(Vector3.zero, Vector3.up, new Vector2(1f, 1f));
            mesh.MapUv(new Rect(0.5f, 0.25f, 0.5f, 0.25f));
            Assert.AreEqual(new Vector2(0.5f, 0.25f), mesh.Uv[0]);
            Assert.AreEqual(new Vector2(1f, 0.5f), mesh.Uv[1]);
        }

        /// <summary>Every triangle's geometric normal agrees with its vertices' normals (front faces are where the normals say).</summary>
        static void AssertWindingMatchesNormals(MeshData mesh)
        {
            for (int s = 0; s < mesh.SubmeshCount; s++)
            {
                var t = mesh.Triangles(s);
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 a = mesh.Vertices[t[i]], b = mesh.Vertices[t[i + 1]], c = mesh.Vertices[t[i + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    if (face.sqrMagnitude < 1e-10f) continue; // degenerate (collapsed) quads are allowed
                    Vector3 average = mesh.Normals[t[i]] + mesh.Normals[t[i + 1]] + mesh.Normals[t[i + 2]];
                    Assert.Greater(Vector3.Dot(face.normalized, average), 0f, $"submesh {s} triangle {i / 3} winds against its normals");
                }
            }
        }
    }
}
