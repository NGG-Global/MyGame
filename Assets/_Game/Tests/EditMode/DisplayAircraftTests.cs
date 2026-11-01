using NUnit.Framework;
using OpenSkies.Editor;
using UnityEditor;
using UnityEngine;
using Role = OpenSkies.Editor.DisplayAircraft.Role;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The static display aircraft: colour roles from the models' own material names,
    /// which alternative part is kept, orientation and size from the real aircraft, and
    /// (in Unity, with the models imported) the full bake of each model.
    /// </summary>
    public class DisplayAircraftTests
    {
        [TestCase("Paint1", Role.Paint)]
        [TestCase("Plane_SHD", Role.Paint)]
        [TestCase("gövde", Role.Paint)]
        [TestCase("gövdeparca", Role.Secondary)]
        [TestCase("lambert1", Role.Secondary)]
        [TestCase("gray", Role.Secondary)]
        [TestCase("Glass", Role.Glass)]
        [TestCase("Window_SHD", Role.Glass)]
        [TestCase("cam", Role.Glass)]
        [TestCase("cam2", Role.Glass)]
        [TestCase("camouflage", Role.Paint)]
        [TestCase("Tire 04", Role.Tyre)]
        [TestCase("Tire_SHD", Role.Tyre)]
        [TestCase("lastikön", Role.Tyre)]
        [TestCase("arkateker", Role.Tyre)]
        [TestCase("arkatekertucu", Role.Metal)]
        [TestCase("jant", Role.Metal)]
        [TestCase("Chrom", Role.Metal)]
        [TestCase("Brushed Aluminium Procedural", Role.Metal)]
        [TestCase("black", Role.Dark)]
        [TestCase("pervane", Role.Dark)]
        [TestCase("Interior_SHD", Role.Dark)]
        [TestCase("koltuk", Role.Dark)]
        [TestCase("red", Role.RedLight)]
        [TestCase("green", Role.GreenLight)]
        [TestCase(null, Role.Paint)]
        public void MaterialNames_MapToColourRoles(string name, Role expected)
        {
            Assert.AreEqual(expected, DisplayAircraft.RoleOf(name));
        }

        [Test]
        public void Alternatives_KeepTheAuthorsChoice_ThenTheParkedPart_UnlessItIsDetached()
        {
            Assert.AreEqual(1, DisplayAircraft.ChooseAlternative(false, true, true, true), "the author hid the parked part");
            Assert.AreEqual(0, DisplayAircraft.ChooseAlternative(true, false, false, true), "the author hid the in-flight part");
            Assert.AreEqual(0, DisplayAircraft.ChooseAlternative(true, true, true, true), "both shown: parked");
            Assert.AreEqual(1, DisplayAircraft.ChooseAlternative(true, true, false, true), "the parked part is stored away from the aircraft");
            Assert.AreEqual(0, DisplayAircraft.ChooseAlternative(true, true, true, false));
        }

        [Test]
        public void Orientation_FindsTheNoseAxis_AndScalesToTheRealSpan()
        {
            // A model 3 units long along -Z (nose at -Z) and 4 units wide.
            var bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 3f));
            DisplayAircraft.Orientation(bounds, new Vector3(0.1f, 1f, -1.5f), 10f, out var forward, out float span, out float length, out float scale);
            Assert.AreEqual(new Vector3(0f, 0f, -1f), forward);
            Assert.AreEqual(4f, span, 1e-5f);
            Assert.AreEqual(3f, length, 1e-5f);
            Assert.AreEqual(2.5f, scale, 1e-5f);

            DisplayAircraft.Orientation(bounds, new Vector3(2f, 1f, 0.2f), 10f, out forward, out span, out length, out _);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), forward, "nose along +X");
            Assert.AreEqual(3f, span, 1e-5f);
            Assert.AreEqual(4f, length, 1e-5f);
        }

        [Test]
        public void Specs_UseRealDimensions_AndParkClearOfTheTaxiLaneAndTheHangars()
        {
            foreach (var spec in DisplayAircraft.Specs)
            {
                Assert.That(spec.SpanMetres, Is.InRange(8f, 13f), spec.Name);
                Assert.That(spec.LengthMetres, Is.InRange(7f, 16f), spec.Name);
                float half = Mathf.Max(spec.SpanMetres, spec.LengthMetres) * 0.5f;
                Assert.Greater(Mathf.Abs(spec.Position.z) - half, 9f, spec.Name + " clear of the taxiway lane (z within ±9 m)");
                Assert.Less(spec.Position.x - half, -45f, spec.Name + " on the apron, west of the taxiway");
                Assert.Greater(spec.Position.x - half, -136f, spec.Name + " east of the hangars");
            }
        }

        // ---- The real models (needs Unity and the imported FBX files) ---------------------------

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Models_BakeToOneMesh_WithTheRealAircraftsProportions(int index)
        {
            var spec = DisplayAircraft.Specs[index];
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(DisplayAircraft.ModelsFolder + "/" + spec.File);
            if (model == null) Assert.Ignore(spec.File + " is not imported (Git LFS object missing?)");

            var mesh = DisplayAircraft.Bake(model, spec, out var used, out var bounds, out var nose, out string problem);
            try
            {
                Assert.IsNotNull(mesh, spec.Name + ": " + problem);
                Assert.Greater(mesh.vertexCount, 1000);
                Assert.IsTrue(used[(int)Role.Paint], "body paint found");
                int roles = 0;
                foreach (bool u in used) if (u) roles++;
                Assert.AreEqual(roles, mesh.subMeshCount, "one submesh (one draw call) per colour role");

                DisplayAircraft.Orientation(bounds, nose, spec.SpanMetres, out _, out _, out float length, out float scale);
                Assert.That(length * scale / spec.LengthMetres, Is.InRange(0.75f, 1.25f), spec.Name + ": length matches the real aircraft");
                Assert.That(bounds.size.y * scale, Is.InRange(1.5f, 7f), spec.Name + ": plausible height");
            }
            finally
            {
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }
    }
}
