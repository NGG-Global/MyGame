using NUnit.Framework;
using OpenSkies.CameraSystems;
using OpenSkies.Cockpit;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>Cockpit layout, picking and look math (pure functions).</summary>
    public class CockpitMathTests
    {
        const float DefaultCockpitFieldOfView = 62f; // CameraRig.cockpitFieldOfView default
        const float ReferenceHeight = 1080f;

        /// <summary>
        /// Flight controls and HUD buttons in reference canvas units (1080 high), from
        /// TouchControlsFactory and ContextualHudFactory. If those layouts change, update here.
        /// </summary>
        static Rect[] OccupiedAreas(float width) => new[]
        {
            Rect.MinMaxRect(16f, 16f, 0.42f * width, 0.78f * ReferenceHeight),   // joystick region
            Rect.MinMaxRect(width - 244f, 24f, width - 24f, 644f),              // throttle touch area
            Rect.MinMaxRect(width - 436f, 48f, width - 268f, 160f),             // BRAKE
            Rect.MinMaxRect(width - 436f, 184f, width - 268f, 728f),            // SYSTEMS and action rail
            Rect.MinMaxRect(0f, ReferenceHeight - 112f, width, ReferenceHeight), // top bar (CAM, RESET, HUD, flight data)
        };

        [TestCase(16f / 9f)]
        [TestCase(18f / 9f)]
        [TestCase(20f / 9f)]
        public void EveryCockpitItem_IsTappableInTheDefaultView(float aspect)
        {
            float width = ReferenceHeight * aspect;
            var occupied = OccupiedAreas(width);
            foreach (var placement in CockpitLayout.Placements)
            {
                Vector3 point = CockpitLayout.PointOnPanel(placement);
                Assert.IsTrue(CockpitLayout.ProjectToViewport(point, DefaultCockpitFieldOfView, aspect, out var viewport));
                var centre = new Vector2(viewport.x * width, viewport.y * ReferenceHeight);
                Assert.That(centre.x, Is.InRange(0f, width), placement.Item + " on screen");
                Assert.That(centre.y, Is.InRange(0f, ReferenceHeight), placement.Item + " on screen");

                // Projected size of the pick volume.
                Vector3 right = CockpitLayout.PointOnPanel(placement) + new Vector3(0.5f * placement.PickSize.x, 0f, 0f);
                CockpitLayout.ProjectToViewport(right, DefaultCockpitFieldOfView, aspect, out var rightViewport);
                float halfWidth = Mathf.Abs(rightViewport.x - viewport.x) * width;
                Assert.That(2f * halfWidth, Is.GreaterThanOrEqualTo(96f), placement.Item + " target at least 96 units wide");

                foreach (var area in occupied)
                    Assert.IsFalse(area.Contains(centre), $"{placement.Item} at {centre} lies under a flight control or HUD button ({area})");
            }
        }

        [Test]
        public void Items_SitOnThePanelAndDoNotOverlap()
        {
            var placements = CockpitLayout.Placements;
            Vector3 normal = CockpitLayout.PanelNormal;
            for (int i = 0; i < placements.Length; i++)
            {
                Vector3 p = CockpitLayout.PointOnPanel(placements[i]);
                Assert.AreEqual(0f, Vector3.Dot(p - CockpitLayout.PanelCentre, normal), 1e-4f, "on the panel plane");
                for (int j = i + 1; j < placements.Length; j++)
                {
                    Vector3 q = CockpitLayout.PointOnPanel(placements[j]);
                    float minimum = 0.5f * Mathf.Min(placements[i].Size.x, placements[j].Size.x);
                    Assert.That(Vector3.Distance(p, q), Is.GreaterThan(minimum), $"{placements[i].Item} and {placements[j].Item} overlap");
                }
            }
            Assert.AreEqual(CockpitItems.Count, placements.Length, "every item has a place");
        }

        [Test]
        public void Items_HaveAccessibleNamesAndHints_AndControlsMapToSystems()
        {
            for (int i = 0; i < CockpitItems.Count; i++)
            {
                var item = (CockpitItem)i;
                Assert.IsNotEmpty(CockpitItems.DisplayName(item));
                Assert.IsNotEmpty(CockpitItems.AccessibilityHint(item));
                bool control = CockpitItems.TryGetSystem(item, out var system);
                Assert.AreEqual(!CockpitItems.IsInstrument(item), control, item.ToString());
                if (control) Assert.AreEqual(item, CockpitItems.ControlFor(system));
            }
        }

        [Test]
        public void InstrumentItems_MatchInstrumentKinds()
        {
            // The UI's InstrumentKind and the cockpit's instrument items share their order.
            Assert.AreEqual((int)OpenSkies.UI.InstrumentKind.Airspeed, (int)CockpitItem.AirspeedIndicator);
            Assert.AreEqual((int)OpenSkies.UI.InstrumentKind.Attitude, (int)CockpitItem.AttitudeIndicator);
            Assert.AreEqual((int)OpenSkies.UI.InstrumentKind.Altimeter, (int)CockpitItem.Altimeter);
            Assert.AreEqual((int)OpenSkies.UI.InstrumentKind.Heading, (int)CockpitItem.HeadingIndicator);
            Assert.AreEqual((int)OpenSkies.UI.InstrumentKind.VerticalSpeed, (int)CockpitItem.VerticalSpeedIndicator);
            Assert.AreEqual((int)OpenSkies.UI.InstrumentKind.Navigation, (int)CockpitItem.NavigationDisplay);
        }

        static PickBox Box(Vector3 centre, Vector3 half) => new PickBox
        {
            Centre = centre, Right = Vector3.right, Up = Vector3.up, Forward = Vector3.forward, HalfExtents = half,
        };

        [Test]
        public void Picking_RayAgainstBoxes()
        {
            var box = Box(new Vector3(0f, 0f, 5f), new Vector3(0.5f, 0.5f, 0.5f));
            Assert.IsTrue(CockpitPicking.Raycast(Vector3.zero, Vector3.forward, box, out float d));
            Assert.AreEqual(4.5f, d, 1e-4f);
            Assert.IsFalse(CockpitPicking.Raycast(new Vector3(0.6f, 0f, 0f), Vector3.forward, box, out _), "beside it");
            Assert.IsFalse(CockpitPicking.Raycast(Vector3.zero, Vector3.back, box, out _), "behind the origin");
            Assert.IsTrue(CockpitPicking.Raycast(new Vector3(0f, 0f, 5f), Vector3.forward, box, out d));
            Assert.AreEqual(0f, d, "origin inside");

            // Rotated box: 45° about Y, so a ray 0.6 to the side still hits its corner region.
            float s = Mathf.Sqrt(0.5f);
            var turned = new PickBox
            {
                Centre = new Vector3(0f, 0f, 5f), Right = new Vector3(s, 0f, -s), Up = Vector3.up, Forward = new Vector3(s, 0f, s),
                HalfExtents = new Vector3(0.5f, 0.5f, 0.5f),
            };
            Assert.IsTrue(CockpitPicking.Raycast(new Vector3(0.6f, 0f, 0f), Vector3.forward, turned, out _));

            var boxes = new[] { Box(new Vector3(0f, 0f, 8f), Vector3.one * 0.5f), box };
            Assert.AreEqual(1, CockpitPicking.Nearest(Vector3.zero, Vector3.forward, boxes, 2, out d), "the nearer one");
            Assert.AreEqual(-1, CockpitPicking.Nearest(Vector3.up * 3f, Vector3.forward, boxes, 2, out _));
        }

        [Test]
        public void Picking_ScreenToleranceFallback()
        {
            var points = new[] { new Vector3(100f, 100f, 1f), new Vector3(180f, 100f, 1f), new Vector3(120f, 100f, -1f) };
            Assert.AreEqual(0, CockpitPicking.NearestOnScreen(new Vector2(130f, 110f), points, 3, 60f));
            Assert.AreEqual(1, CockpitPicking.NearestOnScreen(new Vector2(150f, 100f), points, 3, 60f));
            Assert.AreEqual(-1, CockpitPicking.NearestOnScreen(new Vector2(400f, 400f), points, 3, 60f), "too far");
            Assert.AreEqual(0, CockpitPicking.NearestOnScreen(new Vector2(118f, 100f), points, 3, 60f), "points behind the camera are ignored");
        }

        [Test]
        public void Look_ClampsRecentresAndScalesDrags()
        {
            var yaw = new Vector2(-135f, 135f);
            var pitch = new Vector2(-40f, 45f);
            Assert.AreEqual(new Vector2(135f, -40f), CockpitLookMath.Clamp(new Vector2(500f, -90f), yaw, pitch));
            Assert.AreEqual(Vector2.zero, CockpitLookMath.Clamp(new Vector2(float.NaN, float.PositiveInfinity), yaw, pitch));

            var from = new Vector2(60f, -20f);
            Assert.AreEqual(from, CockpitLookMath.Recentre(from, 0f));
            Assert.AreEqual(Vector2.zero, CockpitLookMath.Recentre(from, 1f));
            float previous = 61f;
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float x = CockpitLookMath.Recentre(from, t).x;
                Assert.That(x, Is.LessThanOrEqualTo(previous), "moves steadily back");
                previous = x;
            }

            Assert.AreEqual(50f, CockpitLookMath.ClampFieldOfView(10f, 50f, 80f));
            Assert.AreEqual(65f, CockpitLookMath.ClampFieldOfView(float.NaN, 50f, 80f), "bad input: the middle");

            Vector2 look = CockpitLookMath.DragToLook(new Vector2(540f, -108f), 1080f, 100f, followFinger: true);
            Assert.AreEqual(50f, look.x, 1e-4f, "half a screen height of drag turns half of 100°");
            Assert.AreEqual(-10f, look.y, 1e-4f);
            Assert.AreEqual(-50f, CockpitLookMath.DragToLook(new Vector2(540f, 0f), 1080f, 100f, followFinger: false).x, 1e-4f);
        }

        [Test]
        public void LookingAway_OnlyBeyondTheThresholdInYawOrPitch()
        {
            Assert.IsFalse(CockpitLookMath.IsLookingAway(Vector2.zero, 10f), "resting view");
            Assert.IsFalse(CockpitLookMath.IsLookingAway(new Vector2(9.9f, -9.9f), 10f), "small offsets do not count");
            Assert.IsFalse(CockpitLookMath.IsLookingAway(new Vector2(10f, 0f), 10f), "the threshold itself does not count");
            Assert.IsTrue(CockpitLookMath.IsLookingAway(new Vector2(-10.1f, 0f), 10f), "left");
            Assert.IsTrue(CockpitLookMath.IsLookingAway(new Vector2(0f, 25f), 10f), "up");
            Assert.IsTrue(CockpitLookMath.IsLookingAway(new Vector2(135f, -40f), 10f), "at the limits");
            Assert.IsFalse(CockpitLookMath.IsLookingAway(new Vector2(float.NaN, float.PositiveInfinity), 10f), "bad input reads as forward");
            Assert.IsTrue(CockpitLookMath.IsLookingAway(new Vector2(0.5f, 0f), -5f), "a negative threshold acts as zero");
        }
    }
}
