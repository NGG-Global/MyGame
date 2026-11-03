using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.Gestures;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>The arc, the flap lever and the HUD density scale as data: slots, hit wedges, arming, detents, cancellation.</summary>
    public class GestureModelTests
    {
        static SystemsArcModel Arc(int inner = 5, int outer = 2)
        {
            var arc = new SystemsArcModel();
            arc.SetInnerCount(inner);
            for (int i = 0; i < inner; i++)
                arc.InnerItem(i) = new ArcItem { Label = "I" + i, Emphasis = ArcEmphasis.Full, Available = true };
            arc.SetOuterCount(outer);
            for (int i = 0; i < outer; i++)
                arc.OuterItem(i) = new ArcItem { Label = "O" + i, Emphasis = ArcEmphasis.Quiet, Available = true };
            arc.Begin();
            return arc;
        }

        static Vector2 At(SystemsArcModel arc, float angle, float radius) => GestureMath.FromAngle(angle) * radius;

        // ---- Arc ----------------------------------------------------------------------------------

        [Test]
        public void Arc_SlotsAreFixedAndAwayFromTheThrottle()
        {
            var arc = Arc();
            var s = arc.Settings;
            for (int i = 0; i < 5; i++)
            {
                float angle = arc.SlotAngle(i);
                Assert.That(angle, Is.GreaterThanOrEqualTo(90f), "every slot is left of straight up (the throttle is to the right)");
                Assert.That(angle, Is.LessThanOrEqualTo(200f), "and not below the handle");
                Assert.AreEqual(s.firstSlotAngle + i * s.slotStep, angle, 1e-4f);
            }
            float arcLength = (s.innerRadius + s.outerRadius) * 0.5f * s.slotStep * Mathf.Deg2Rad;
            Assert.That(arcLength, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "each segment is at least a touch target along the arc");
            Assert.That(s.outerRadius - s.innerRadius, Is.GreaterThanOrEqualTo(HudStyle.MinTouchTarget), "and across it");
        }

        [Test]
        public void Arc_NothingSelectable_BeforeTheArcHasUnfoldedEnough()
        {
            var arc = Arc();
            arc.Update(At(arc, arc.SlotAngle(1), 230f), 0.3f);
            Assert.IsFalse(arc.HasHovered);
            Assert.IsFalse(arc.IsArmed);
            arc.Update(At(arc, arc.SlotAngle(1), 230f), 0.6f);
            Assert.AreEqual(1, arc.HoveredIndex);
            Assert.IsTrue(arc.IsArmed);
        }

        [Test]
        public void Arc_WedgesSelectByAngle_FromTheInnerRadiusOutwards()
        {
            var arc = Arc();
            arc.Update(At(arc, arc.SlotAngle(2) + 9f, 400f), 1f); // inside the wedge, far beyond the band
            Assert.AreEqual(2, arc.HoveredIndex, "the selection area extends outward: a large effective target");
            arc.Update(At(arc, arc.SlotAngle(2) + 13f, 230f), 1f); // past the half step: the neighbour
            Assert.AreEqual(3, arc.HoveredIndex);
            arc.Update(At(arc, arc.SlotAngle(2), 60f), 1f); // too close to the handle: the cancel zone
            Assert.IsFalse(arc.HasHovered);
            arc.Update(At(arc, arc.SlotAngle(0) - 8f, 230f), 1f); // the end tolerance widens the first wedge
            Assert.AreEqual(0, arc.HoveredIndex);
            arc.Update(At(arc, arc.SlotAngle(4) + 8f, 230f), 1f);
            Assert.AreEqual(4, arc.HoveredIndex);
            arc.Update(At(arc, arc.SlotAngle(4) + 30f, 230f), 1f);
            Assert.IsFalse(arc.HasHovered, "beyond the last wedge is nothing");
            arc.Update(At(arc, 10f, 230f), 1f);
            Assert.IsFalse(arc.HasHovered, "towards the throttle is nothing");
        }

        [Test]
        public void Arc_DisabledAndUnavailableItems_NeverArm()
        {
            var arc = Arc();
            arc.InnerItem(1).Emphasis = ArcEmphasis.Disabled;
            arc.Update(At(arc, arc.SlotAngle(1), 230f), 1f);
            Assert.AreEqual(1, arc.HoveredIndex, "the finger is over it (it can show why it is disabled)");
            Assert.IsFalse(arc.IsArmed);

            arc.InnerItem(3).Available = false;
            arc.Update(At(arc, arc.SlotAngle(3), 230f), 1f);
            Assert.IsFalse(arc.HasHovered, "a slot the aircraft lacks is empty");
        }

        [Test]
        public void Arc_OuterRing_OnlyAfterItIsRevealed()
        {
            var arc = Arc();
            float outerRadius = (arc.Settings.OuterRingInner + arc.Settings.OuterRingOuter) * 0.5f;
            arc.Update(At(arc, arc.SlotAngle(0), outerRadius), 1f);
            Assert.AreEqual(0, arc.HoveredIndex);
            Assert.IsFalse(arc.HoveredIsOuter, "beyond the band still selects the inner item while the outer ring is folded");

            arc.RevealOuter(true);
            arc.Update(At(arc, arc.SlotAngle(0), outerRadius), 1f);
            Assert.IsTrue(arc.HoveredIsOuter);
            Assert.AreEqual(0, arc.HoveredIndex);
            arc.Update(At(arc, arc.SlotAngle(0), 230f), 1f);
            Assert.IsFalse(arc.HoveredIsOuter, "back inside the band: the inner item again");

            arc.Begin();
            Assert.IsFalse(arc.OuterRevealed, "a new pull starts folded");
        }

        [Test]
        public void Arc_SubArc_FansOutFromItsParent_AndStaysInsideTheSpan()
        {
            var arc = Arc(5, 0);
            for (int i = 0; i < 3; i++) arc.OuterItem(i) = new ArcItem { Label = "S" + i, Emphasis = ArcEmphasis.Full, Available = true };
            arc.RevealSubArc(2, 3);
            Assert.IsTrue(arc.OuterRevealed);
            Assert.AreEqual(2, arc.OuterParent);
            Assert.AreEqual(arc.SlotAngle(2), arc.OuterAngle(1), 1e-3f, "three options centre on the parent");
            Assert.AreEqual(arc.SlotAngle(2) - arc.Settings.slotStep, arc.OuterAngle(0), 1e-3f);
            float outerRadius = (arc.Settings.OuterRingInner + arc.Settings.OuterRingOuter) * 0.5f;
            arc.Update(At(arc, arc.OuterAngle(0), outerRadius), 1f);
            Assert.IsTrue(arc.HoveredIsOuter);
            Assert.AreEqual(0, arc.HoveredIndex);
            arc.Update(At(arc, arc.SlotAngle(2), 230f), 1f);
            Assert.IsFalse(arc.HoveredIsOuter, "back on the parent segment: the primary action");
            Assert.AreEqual(arc.SlotAngle(2), arc.UnfoldedOuterAngle(0, 0f), 1e-3f, "options unfold from the parent's angle");

            // At the end of the arc the fan is shifted inward, never beyond the span.
            arc.RevealSubArc(4, 4);
            float max = arc.SlotAngle(4) + arc.Settings.subArcMargin;
            Assert.That(arc.OuterAngle(3), Is.LessThanOrEqualTo(max + 1e-3f));
            arc.RevealSubArc(0, 4);
            Assert.That(arc.OuterAngle(0), Is.GreaterThanOrEqualTo(arc.Settings.firstSlotAngle - arc.Settings.subArcMargin - 1e-3f));

            arc.RevealOuter(true); // the free ring again: at the inner slots' angles, with the free items
            Assert.AreEqual(SystemsArcModel.NoItem, arc.OuterParent);
        }

        [Test]
        public void Arc_UnfoldsFromTheHandleTowardsTheSlots()
        {
            var arc = Arc();
            Assert.AreEqual(arc.Settings.foldedAngle, arc.UnfoldedAngle(3, 0f), 1e-4f);
            Assert.AreEqual(arc.SlotAngle(3), arc.UnfoldedAngle(3, 1f), 1e-4f);
            var centre = arc.SlotCentre(false, 0, 1f);
            Assert.AreEqual((arc.Settings.innerRadius + arc.Settings.outerRadius) * 0.5f, centre.magnitude, 1e-3f);
            Assert.That(centre.x, Is.LessThan(0f), "slot 0 leans left of the handle");
            Assert.That(centre.y, Is.GreaterThan(0f));
        }

        // ---- Flap lever ---------------------------------------------------------------------------

        [Test]
        public void Lever_CurrentDetentUnderTheThumb_DownExtends_UpRetracts()
        {
            var lever = new FlapsLeverModel();
            var start = new Vector2(1500f, 500f);
            lever.Begin(4, 1, start);
            Assert.AreEqual(1, lever.Preview);
            Assert.IsFalse(lever.Changed);

            lever.Update(start + new Vector2(0f, -80f)); // one spacing down
            Assert.AreEqual(2, lever.Preview);
            Assert.IsTrue(lever.Changed);
            lever.Update(start + new Vector2(0f, -400f)); // far past the end
            Assert.AreEqual(3, lever.Preview, "clamped to full flap");
            Assert.That(lever.KnobOffset, Is.LessThan(400f).And.GreaterThan(lever.DetentOffset(3)), "the knob is held by its stop");

            lever.Update(start + new Vector2(0f, 80f)); // one spacing up from the start
            Assert.AreEqual(0, lever.Preview);
            lever.Update(start);
            Assert.AreEqual(1, lever.Preview);
            Assert.IsFalse(lever.Changed, "back where it started: releasing changes nothing");
        }

        [Test]
        public void Lever_DetentsHaveHysteresis()
        {
            var lever = new FlapsLeverModel();
            var start = Vector2.zero;
            lever.Begin(4, 0, start);
            float half = lever.Settings.detentSpacing * 0.5f;
            lever.Update(new Vector2(0f, -(half + 5f))); // just past the midpoint: inside the band
            Assert.AreEqual(0, lever.Preview);
            lever.Update(new Vector2(0f, -(half + lever.Settings.detentHysteresis + 1f)));
            Assert.AreEqual(1, lever.Preview);
            lever.Update(new Vector2(0f, -(half - 5f))); // back just before the midpoint: still 1
            Assert.AreEqual(1, lever.Preview);
            lever.Update(new Vector2(0f, -(half - lever.Settings.detentHysteresis - 1f)));
            Assert.AreEqual(0, lever.Preview);
        }

        [Test]
        public void Lever_SidewaysCancels_AndComesBack()
        {
            var lever = new FlapsLeverModel();
            lever.Begin(4, 0, Vector2.zero);
            lever.Update(new Vector2(0f, -160f));
            Assert.AreEqual(2, lever.Preview);
            lever.Update(new Vector2(250f, -160f));
            Assert.IsTrue(lever.Cancelled);
            Assert.IsFalse(lever.Changed, "releasing while cancelled changes nothing");
            lever.Update(new Vector2(170f, -160f)); // between the two distances: still cancelled
            Assert.IsTrue(lever.Cancelled);
            lever.Update(new Vector2(100f, -160f));
            Assert.IsFalse(lever.Cancelled);
            Assert.AreEqual(2, lever.Preview, "the preview is where the finger is again");
        }

        [Test]
        public void Lever_FollowsTheCommittedDetent_WhenItChangesUnderneath()
        {
            var lever = new FlapsLeverModel();
            lever.Begin(3, 0, Vector2.zero);
            lever.SetCurrent(1); // the keyboard moved the flaps meanwhile
            lever.Update(new Vector2(0f, -80f));
            Assert.AreEqual(1, lever.Preview);
            Assert.IsFalse(lever.Changed);
        }

        // ---- HUD density --------------------------------------------------------------------------

        [Test]
        public void Density_DownIsMore_UpIsLess_WithHysteresis()
        {
            var model = new HudDensityModel();
            model.Begin(HudProfile.Smart, 1000f);
            Assert.AreEqual(HudProfile.Smart, model.Preview);
            model.Update(1000f - 40f);
            Assert.AreEqual(HudProfile.Smart, model.Preview, "a small pull only peeks");
            model.Update(1000f - 80f);
            Assert.AreEqual(HudProfile.Detailed, model.Preview);
            Assert.IsTrue(model.Changed);
            model.Update(1000f - 60f); // inside the band
            Assert.AreEqual(HudProfile.Detailed, model.Preview);
            model.Update(1000f + 80f); // reversed, past the midpoint above
            Assert.AreEqual(HudProfile.Cinematic, model.Preview);
            model.Update(1000f + 400f);
            Assert.AreEqual(HudProfile.Cinematic, model.Preview, "clamped at the least");
            Assert.AreEqual(0f, model.ScalePosition, 1e-4f);
            model.Update(1000f);
            Assert.AreEqual(HudProfile.Smart, model.Preview);
            Assert.IsFalse(model.Changed);
        }

        [Test]
        public void Density_FromTheEnds()
        {
            var model = new HudDensityModel();
            model.Begin(HudProfile.Detailed, 500f);
            model.Update(500f - 300f);
            Assert.AreEqual(HudProfile.Detailed, model.Preview, "nothing beyond Detailed");
            model.Update(500f + 80f);
            Assert.AreEqual(HudProfile.Smart, model.Preview);
            model.Update(500f + 190f);
            Assert.AreEqual(HudProfile.Cinematic, model.Preview);
            Assert.AreEqual(2, HudDensityModel.LevelOf(HudProfile.Detailed));
            Assert.AreEqual(HudProfile.Cinematic, HudDensityModel.ProfileAt(-5));
        }
    }
}
