using NUnit.Framework;
using OpenSkies.Cockpit;
using OpenSkies.Editor;
using OpenSkies.FlightState;
using OpenSkies.Options;
using OpenSkies.Platform;
using OpenSkies.World;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The modelled aircraft's design data, the terrain's shape and land use, the
    /// scenery's generated patterns and the airframe view's pure rules.
    /// </summary>
    public class VisualWorldTests
    {
        // ---- Aircraft ---------------------------------------------------------------------------------------

        [Test]
        public void Aircraft_WindowsAreWhereTheCabinIs()
        {
            Assert.IsTrue(AircraftMeshBuilder.IsWindow(AircraftMeshBuilder.UOf(1.9f), 0.5f), "windscreen, top centre");
            Assert.IsTrue(AircraftMeshBuilder.IsWindow(AircraftMeshBuilder.UOf(0.9f), 0.33f), "right door window");
            Assert.IsTrue(AircraftMeshBuilder.IsWindow(AircraftMeshBuilder.UOf(0.9f), 0.67f), "left door window");
            Assert.IsFalse(AircraftMeshBuilder.IsWindow(AircraftMeshBuilder.UOf(0.9f), 0.05f), "not the belly");
            Assert.IsFalse(AircraftMeshBuilder.IsWindow(AircraftMeshBuilder.UOf(2.8f), 0.5f), "not the cowling");
            Assert.IsFalse(AircraftMeshBuilder.IsWindow(AircraftMeshBuilder.UOf(-3f), 0.5f), "not the tail boom");
        }

        [Test]
        public void Aircraft_StationsRunNoseToTail_AndTheCabinIsTallEnoughForTheEyePoint()
        {
            var stations = AircraftMeshBuilder.Stations;
            for (int i = 1; i < stations.Length; i++) Assert.Less(stations[i].Z, stations[i - 1].Z, "stations in order");
            Assert.That(stations[0].Z, Is.EqualTo(AircraftMeshBuilder.NoseZ).Within(1e-4f));
            Assert.That(stations[stations.Length - 1].Z, Is.EqualTo(AircraftMeshBuilder.TailZ).Within(1e-4f));
            // The cockpit eye point (0.9 m aft of the origin, 1.9 m up) must be inside the cabin with room above it.
            float roof = 0f, floor = 0f;
            foreach (var s in stations)
                if (Mathf.Abs(s.Z - 0.9f) < 0.6f)
                {
                    roof = Mathf.Max(roof, s.CentreY + s.HalfHeight);
                    floor = s.CentreY - s.HalfHeight;
                }
            Assert.Greater(roof, 1.95f, "roof above the eye");
            Assert.Less(floor, 0.9f, "floor below the seats");
        }

        [Test]
        public void Aircraft_WingTapersOutboardOnly_AndTheWingSitsAboveTheRoof()
        {
            Assert.AreEqual(AircraftMeshBuilder.WingRootChord, AircraftMeshBuilder.WingChord(0f));
            Assert.AreEqual(AircraftMeshBuilder.WingRootChord, AircraftMeshBuilder.WingChord(AircraftMeshBuilder.WingTaperStart));
            Assert.That(AircraftMeshBuilder.WingChord(1f), Is.EqualTo(AircraftMeshBuilder.WingTipChord).Within(1e-5f));
            Assert.Less(AircraftMeshBuilder.WingChord(0.9f), AircraftMeshBuilder.WingChord(0.7f));
            float roof = 0f;
            foreach (var s in AircraftMeshBuilder.Stations) roof = Mathf.Max(roof, s.CentreY + s.HalfHeight);
            Assert.Greater(AircraftMeshBuilder.WingY, roof, "a high wing on the cabin roof");
            Assert.Less(AircraftMeshBuilder.FlapSpan.y, AircraftMeshBuilder.AileronSpan.x, "flaps inboard of the ailerons");
        }

        [Test]
        public void Aircraft_LiveryHasTheDesign()
        {
            // Fuselage region: blue nose ring, dark anti-glare panel on top of the cowling, white sides, a blue cheat line.
            float fuselage(float v) => 0.5f + 0.5f * v;
            Assert.AreEqual(AircraftMeshBuilder.LiveryColour(0.02f, fuselage(0.5f)), AircraftMeshBuilder.LiveryColour(0.02f, fuselage(0.1f)), "the nose ring goes all the way round");
            var antiGlare = AircraftMeshBuilder.LiveryColour(0.1f, fuselage(0.5f));
            Assert.Less(antiGlare.r + antiGlare.g + antiGlare.b, 0.8f, "anti-glare panel is dark");
            var side = AircraftMeshBuilder.LiveryColour(0.5f, fuselage(0.1f));
            Assert.Greater(side.r + side.g + side.b, 2.7f, "the belly side is white");
            bool blueFound = false;
            for (float v = 0.2f; v < 0.35f; v += 0.002f)
            {
                var c = AircraftMeshBuilder.LiveryColour(0.5f, fuselage(v));
                if (c.b > c.r + 0.3f) blueFound = true;
            }
            Assert.IsTrue(blueFound, "a blue cheat line along the side");
            // Wing region: blue tips.
            var tip = AircraftMeshBuilder.LiveryColour(0.97f, 0.25f + 0.125f);
            Assert.Greater(tip.b, tip.r + 0.3f);
            // Propeller disc: clear at the hub, translucent across the disc.
            var disc = AircraftMeshBuilder.PropellerDiscPixels(64);
            Assert.AreEqual(0, disc[32 * 64 + 32].a, "clear at the centre");
            Assert.Greater(disc[32 * 64 + 50].a, 40, "translucent over the blades' sweep");
            Assert.AreEqual(0, disc[0].a, "clear outside the disc");
        }

        [Test]
        public void AirframeView_PropellerFollowsTheEngine()
        {
            Assert.AreEqual(0f, AirframeView.TargetRpm(EngineState.Off, 1f, 600f, 2500f, 200f));
            Assert.AreEqual(200f, AirframeView.TargetRpm(EngineState.Starting, 1f, 600f, 2500f, 200f));
            Assert.AreEqual(600f, AirframeView.TargetRpm(EngineState.Running, 0f, 600f, 2500f, 200f));
            Assert.AreEqual(2500f, AirframeView.TargetRpm(EngineState.Running, 1f, 600f, 2500f, 200f));
            Assert.That(AirframeView.TargetRpm(EngineState.Running, 0.5f, 600f, 2500f, 200f), Is.EqualTo(1550f).Within(1e-3f));
            Assert.AreEqual(0f, AirframeView.Approach(10f, 0f), "no time, no movement");
            Assert.That(AirframeView.Approach(10f, 10f), Is.EqualTo(1f).Within(1e-3f), "settles");
            Assert.Less(AirframeView.Approach(5f, 0.016f), AirframeView.Approach(5f, 0.033f), "frame-rate independent: a longer frame moves further");
        }

        [Test]
        public void AirframeView_MovesSurfacesWithTheCommands_AndShowsTheDiscAtSpeed()
        {
            var go = new GameObject("view");
            try
            {
                var view = go.AddComponent<AirframeView>();
                view.aileronRight = new GameObject("ar").transform;
                view.aileronLeft = new GameObject("al").transform;
                view.elevator = new GameObject("e").transform;
                view.rudder = new GameObject("r").transform;
                view.flapRight = new GameObject("fr").transform;
                view.propeller = new GameObject("p").transform;
                var discGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var bladesGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                view.propellerDisc = discGo.GetComponent<Renderer>();
                view.propellerBlades = bladesGo.GetComponent<Renderer>();
                view.surfaceSharpness = 1000f; // settle at once

                var telemetry = new FlightTelemetry { EngineState = EngineState.Running, EnginePower = 1f, FlapDegrees = 20f };
                var commands = new FlightControls(1f, 1f, 1f, 1f);
                for (int i = 0; i < 60; i++) view.Present(telemetry, commands, 0.1f);

                Assert.That(view.AileronDegrees, Is.EqualTo(view.aileronDegrees).Within(0.01f));
                Assert.That(view.aileronRight.localRotation.eulerAngles.x, Is.EqualTo(view.aileronDegrees).Within(0.1f), "stick right: right aileron trailing edge up");
                Assert.That(view.aileronLeft.localRotation.eulerAngles.x, Is.EqualTo(360f - view.aileronDegrees).Within(0.1f), "left aileron down");
                Assert.That(view.elevator.localRotation.eulerAngles.x, Is.EqualTo(view.elevatorDegrees).Within(0.1f), "stick back: elevator up");
                Assert.That(view.flapRight.localRotation.eulerAngles.x, Is.EqualTo(360f - 20f).Within(0.1f), "flaps follow their travel, trailing edge down");
                Assert.That(view.PropellerRpm, Is.EqualTo(view.maxRpm).Within(1f));
                Assert.IsTrue(view.DiscShown, "at full power the disc replaces the blades");
                Assert.IsTrue(view.propellerDisc.enabled);
                Assert.IsFalse(view.propellerBlades.enabled);

                telemetry.EngineState = EngineState.Off;
                for (int i = 0; i < 100; i++) view.Present(telemetry, FlightControls.Neutral, 0.1f);
                Assert.Less(view.PropellerRpm, 1f);
                Assert.IsFalse(view.DiscShown);
                Assert.IsTrue(view.propellerBlades.enabled, "the blades come back when the engine stops");
                Assert.That(view.AileronDegrees, Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                foreach (var t in go.GetComponentsInChildren<Transform>()) if (t != null && t != go.transform) Object.DestroyImmediate(t.gameObject);
                Object.DestroyImmediate(go);
                foreach (var name in new[] { "ar", "al", "e", "r", "fr", "p", "Quad" })
                {
                    GameObject stray;
                    while ((stray = GameObject.Find(name)) != null) Object.DestroyImmediate(stray);
                }
            }
        }

        // ---- Terrain ----------------------------------------------------------------------------------------------

        [Test]
        public void Terrain_IsFlatAtTheAirfield_AndRisesInTheWest()
        {
            foreach (var p in new[] { Vector2.zero, new Vector2(0f, 740f), new Vector2(0f, -740f), new Vector2(-150f, 0f), new Vector2(300f, 1200f), new Vector2(-600f, -1400f) })
                Assert.That(TerrainShape.Height(p.x, p.y), Is.EqualTo(PrototypeWorld.LandHeight).Within(0.01f), $"flat at {p}");
            Assert.AreEqual(1f, TerrainShape.AirfieldFlatness(0f, 0f));
            Assert.AreEqual(0f, TerrainShape.AirfieldFlatness(-5000f, 0f));

            float highest = 0f;
            for (float x = -6300f; x < -4000f; x += 100f)
            for (float z = -7800f; z < 7800f; z += 200f)
                highest = Mathf.Max(highest, TerrainShape.Height(x, z));
            Assert.Greater(highest, 150f, "hills in the west");
            float plainMax = 0f;
            for (float x = -2500f; x < 1200f; x += 150f)
            for (float z = -7000f; z < 7000f; z += 250f)
                plainMax = Mathf.Max(plainMax, TerrainShape.Height(x, z));
            Assert.Less(plainMax, 25f, "the plain stays low: mission routes keep their clearance");
            for (float x = -6300f; x < 1500f; x += 300f)
            for (float z = -7900f; z < 7900f; z += 300f)
                Assert.GreaterOrEqual(TerrainShape.Height(x, z), PrototypeWorld.LandHeight - 1.5f, "never below the beach");
        }

        [Test]
        public void Terrain_LandUseKeepsTheAirfieldTownAndRoadsClear()
        {
            Assert.AreEqual(0f, TerrainShape.Woodland(0f, 0f), "no woods on the runway");
            Assert.AreEqual(0f, TerrainShape.Woodland(TerrainShape.Town.center.x, TerrainShape.Town.center.y), "no woods in the town");
            var road = TerrainShape.Roads[0][1];
            Assert.That(TerrainShape.RoadDistance(road.x, road.y), Is.EqualTo(0f).Within(1e-3f));
            Assert.AreEqual(0f, TerrainShape.Woodland(road.x, road.y), "no woods on a road");
            Assert.IsTrue(TerrainShape.OnTownStreet(TerrainShape.Town.xMin + 3f, TerrainShape.Town.yMin + 30f), "a street along the town's edge");
            Assert.IsFalse(TerrainShape.OnTownStreet(TerrainShape.Town.xMin + 50f, TerrainShape.Town.yMin + 40f), "inside a block");
            Assert.IsFalse(TerrainShape.OnTownStreet(0f, 0f));
            int wooded = 0, samples = 0;
            for (float x = -6000f; x < 1200f; x += 200f)
            for (float z = -7500f; z < 7500f; z += 300f)
            {
                samples++;
                if (TerrainShape.Woodland(x, z) > 0.5f) wooded++;
            }
            Assert.That((float)wooded / samples, Is.InRange(0.05f, 0.5f), "some woods, not a forest everywhere");
        }

        [Test]
        public void Terrain_ColourIsOpaqueAndPlausible()
        {
            foreach (var p in new[] { Vector2.zero, new Vector2(-3000f, 2000f), new Vector2(-5500f, -3000f), new Vector2(1500f, 0f), new Vector2(1200f, -1900f) })
            {
                var c = TerrainShape.Colour(p.x, p.y);
                Assert.AreEqual(1f, c.a);
                Assert.That(c.r, Is.InRange(0f, 1f));
                Assert.That(c.g, Is.InRange(0f, 1f));
                Assert.That(c.b, Is.InRange(0f, 1f));
            }
            var sand = TerrainShape.Colour(1510f, 3000f);
            Assert.Greater(sand.r, sand.b + 0.1f, "sand on the beach");
            var grass = TerrainShape.Colour(0f, 0f);
            Assert.Greater(grass.g, grass.r, "grass on the airfield");
            Assert.AreEqual(TerrainShape.Colour(100f, 100f), TerrainShape.Colour(100f, 100f), "deterministic");
        }

        [Test]
        public void Terrain_TileCoversItsPartOfTheLand()
        {
            var tile = TerrainBuilder.BuildTile(0, 0);
            Assert.AreEqual((TerrainBuilder.CellsX + 1) * (TerrainBuilder.CellsZ + 1), tile.VertexCount);
            Assert.AreEqual(TerrainBuilder.CellsX * TerrainBuilder.CellsZ * 6, tile.Triangles(0).Count);
            var bounds = tile.Bounds();
            Assert.That(bounds.min.x, Is.EqualTo(TerrainShape.WestX).Within(0.01f));
            Assert.That(bounds.min.z, Is.EqualTo(TerrainShape.SouthZ).Within(0.01f));
            Assert.That(bounds.max.x, Is.EqualTo(TerrainShape.WestX + TerrainShape.Width / TerrainBuilder.TilesX).Within(0.01f));
            foreach (var n in tile.Normals) Assert.Greater(n.y, 0.3f, "no cliffs");
        }

        [Test]
        public void Trees_StayOutOfTheRunwayCorridor_AndTheCluster_IsOneColouredMesh()
        {
            Assert.IsFalse(TerrainBuilder.ClearOfAirfield(0f, 3000f), "approach corridor");
            Assert.IsFalse(TerrainBuilder.ClearOfAirfield(-150f, 50f), "apron");
            Assert.IsFalse(TerrainBuilder.ClearOfAirfield(TerrainShape.Town.center.x, TerrainShape.Town.center.y), "town");
            Assert.IsTrue(TerrainBuilder.ClearOfAirfield(-2500f, 2500f));
            var cluster = TerrainBuilder.BuildCluster(0);
            Assert.AreEqual(1, cluster.SubmeshCount, "one palette material");
            Assert.Greater(cluster.VertexCount, 200);
            foreach (var uv in cluster.Uv)
            {
                Assert.That(uv.x, Is.InRange(0f, 1f));
                Assert.That(uv.y, Is.InRange(0f, 1f));
            }
        }

        // ---- Scenery ----------------------------------------------------------------------------------------------

        [Test]
        public void Palette_HasAColourForEveryCell_AndCellCentresInsideTheTexture()
        {
            Assert.AreEqual(ScenePalette.Columns * ScenePalette.Rows, ScenePalette.Colours.Length);
            foreach (ScenePalette.Cell cell in System.Enum.GetValues(typeof(ScenePalette.Cell)))
            {
                var uv = ScenePalette.Uv(cell);
                Assert.That(uv.x, Is.InRange(0f, 1f));
                Assert.That(uv.y, Is.InRange(0f, 1f));
                Assert.AreEqual(0f, uv.width);
            }
            var pixels = ScenePalette.Pixels();
            Assert.AreEqual(ScenePalette.Columns * ScenePalette.CellPixels * ScenePalette.Rows * ScenePalette.CellPixels, pixels.Length);
            var red = ScenePalette.Uv(ScenePalette.Cell.Red);
            int px = (int)(red.x * ScenePalette.Columns * ScenePalette.CellPixels), py = (int)(red.y * ScenePalette.Rows * ScenePalette.CellPixels);
            var sample = pixels[py * ScenePalette.Columns * ScenePalette.CellPixels + px];
            Assert.Greater(sample.r, sample.g + 100, "the red cell is red where its uv points");
        }

        [Test]
        public void RunwayNumbers_AreFiveBySevenGlyphs_PaintedAsBlocks()
        {
            Assert.AreEqual(10, SceneryBuilder.Digits.Length);
            foreach (var glyph in SceneryBuilder.Digits)
            {
                Assert.AreEqual(7, glyph.Length);
                foreach (var row in glyph) Assert.AreEqual(5, row.Length);
            }
            var mesh = new MeshData();
            SceneryBuilder.Number(mesh, "36", Vector3.zero, facingNorth: true);
            int lit = 0;
            foreach (var row in SceneryBuilder.Digits[3]) foreach (var c in row) if (c == '1') lit++;
            foreach (var row in SceneryBuilder.Digits[6]) foreach (var c in row) if (c == '1') lit++;
            Assert.AreEqual(lit * 24, mesh.VertexCount, "one box per painted cell");
            var bounds = mesh.Bounds();
            Assert.That(bounds.size.z, Is.EqualTo(7 * 2.15f + 0.05f).Within(0.01f), "15 m tall digits");
        }

        [Test]
        public void Scenery_GeneratedShapesAreWellFormed()
        {
            var lighthouse = SceneryBuilder.Lighthouse(22f);
            Assert.Greater(lighthouse.Bounds().max.y, 26f, "lantern and cap above the tower");
            Assert.That(lighthouse.Bounds().min.y, Is.EqualTo(0f).Within(1e-3f));
            var boat = SceneryBuilder.Boat(12f, ScenePalette.Cell.HullBlue);
            Assert.Greater(boat.Bounds().size.z, 11f);
            Assert.Less(boat.Bounds().min.y, -0.3f, "a hull below the waterline");
            var cloud = SceneryBuilder.CloudPixels(64);
            int clear = 0, cloudy = 0;
            foreach (var p in cloud)
            {
                if (p.a == 0) clear++;
                else if (p.a > 200) cloudy++;
            }
            Assert.Greater(clear, 64 * 8, "sky between the clouds");
            Assert.Greater(cloudy, 64 * 4, "solid cloud cores");
            var water = SceneryBuilder.WaterNormalPixels(32);
            foreach (var p in water) Assert.Greater(p.b, 100, "normals point mostly up");
        }

        // ---- Presets ----------------------------------------------------------------------------------------------

        [Test]
        public void GraphicsPresets_OnlyBatterySaverDropsTheLookFeatures()
        {
            var saver = GraphicsPresets.For(GraphicsPreset.BatterySaver);
            var balanced = GraphicsPresets.For(GraphicsPreset.Balanced);
            var high = GraphicsPresets.For(GraphicsPreset.HighQuality);
            Assert.IsFalse(saver.PostProcessing || saver.Hdr || saver.SoftShadows);
            Assert.IsTrue(balanced.PostProcessing && balanced.Hdr && balanced.SoftShadows);
            Assert.IsTrue(high.PostProcessing && high.Hdr && high.SoftShadows);
            Assert.LessOrEqual(high.ShadowDistance, RenderPipelineSetup.MaxShadowDistance, "never beyond the asset's shadow distance");
        }

        [Test]
        public void ScrollingSurface_WrapsItsOffsets()
        {
            var go = new GameObject("scroll");
            try
            {
                var scroll = go.AddComponent<ScrollingSurface>();
                scroll.baseSpeed = new Vector2(0.3f, -0.3f);
                scroll.Advance(2f);
                Assert.That(scroll.BaseOffset.x, Is.EqualTo(0.6f).Within(1e-4f));
                Assert.That(scroll.BaseOffset.y, Is.EqualTo(0.4f).Within(1e-4f), "wrapped into 0..1");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
