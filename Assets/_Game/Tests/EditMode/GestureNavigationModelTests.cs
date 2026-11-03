using NUnit.Framework;
using OpenSkies.UI;

namespace OpenSkies.Tests
{
    /// <summary>The pause sheet and the entry stations as data: stops, velocity-aware settling, dismissal, overscroll.</summary>
    public class GestureNavigationModelTests
    {
        static SheetModel Sheet() => new SheetModel(new SheetSettings { firstStop = 300f, secondStop = 860f, settleSeconds = 0.2f, velocityProjectionSeconds = 0.18f });

        static void Settle(SheetModel sheet) { for (int i = 0; i < 40; i++) sheet.Tick(0.05f); }

        [Test]
        public void Sheet_OpensToTheFirstStop_DragsUp_ReleasesToTheNearestStop()
        {
            var sheet = Sheet();
            sheet.Open(SheetModel.FirstStop, instant: true);
            Assert.AreEqual(300f, sheet.Height, 1e-3f);
            Assert.AreEqual(SheetModel.FirstStop, sheet.Stop);

            sheet.BeginDrag(100f);
            sheet.Drag(100f + 200f); // half way up
            Assert.AreEqual(500f, sheet.Height, 1e-3f, "the sheet follows the finger");
            Assert.That(sheet.Depth, Is.InRange(0.3f, 0.4f));
            sheet.Release(0f);
            Assert.AreEqual(SheetModel.FirstStop, sheet.Stop, "nearer the first stop");
            Settle(sheet);
            Assert.AreEqual(300f, sheet.Height, 1e-3f);

            sheet.BeginDrag(100f);
            sheet.Drag(100f + 200f);
            sheet.Release(2000f); // a flick up
            Assert.AreEqual(SheetModel.SecondStop, sheet.Stop, "the velocity carries it to the second stop");
            Settle(sheet);
            Assert.AreEqual(860f, sheet.Height, 1e-3f);
            Assert.IsFalse(sheet.IsSettling);
        }

        [Test]
        public void Sheet_DraggedBelowTheDismissShare_Closes()
        {
            var sheet = Sheet();
            sheet.Open(SheetModel.FirstStop, instant: true);
            sheet.BeginDrag(500f);
            sheet.Drag(500f - 200f); // down to 100: below 55 % of 300
            Assert.AreEqual(SheetModel.Closed, sheet.Release(0f));
            Settle(sheet);
            Assert.AreEqual(0f, sheet.Height, 1e-3f);
            Assert.IsFalse(sheet.IsOpen);

            sheet.Open(SheetModel.FirstStop, instant: true);
            sheet.BeginDrag(500f);
            sheet.Drag(500f - 60f); // to 240: above the share
            Assert.AreEqual(SheetModel.FirstStop, sheet.Release(0f));
            sheet.BeginDrag(500f);
            sheet.Drag(500f - 60f);
            Assert.AreEqual(SheetModel.Closed, sheet.Release(-2500f), "a flick down dismisses from anywhere near the first stop");
        }

        [Test]
        public void Sheet_RubberBandsAboveTheTop_AndIsLimitedToTheScreen()
        {
            var sheet = Sheet();
            sheet.SetLimits(700f);
            Assert.AreEqual(652f, sheet.StopHeight(SheetModel.SecondStop), 1e-3f, "the second stop keeps 48 units of screen");
            sheet.Open(SheetModel.SecondStop, instant: true);
            sheet.BeginDrag(0f);
            sheet.Drag(400f);
            Assert.That(sheet.Height, Is.GreaterThan(652f).And.LessThan(652f + 60f), "held by its stop");
            sheet.Release(0f);
            Assert.AreEqual(SheetModel.SecondStop, sheet.Stop);
        }

        [Test]
        public void Sheet_SettlesWithASingleSoftOvershoot()
        {
            var sheet = Sheet();
            sheet.Open(SheetModel.FirstStop, instant: true);
            sheet.GoTo(SheetModel.SecondStop);
            float peak = 0f;
            for (int i = 0; i < 40; i++) { sheet.Tick(0.01f); peak = System.Math.Max(peak, sheet.Height); }
            Assert.That(peak, Is.LessThan(860f + 860f * 0.05f), "at most a few percent over");
            Assert.AreEqual(860f, sheet.Height, 1e-3f);
            sheet.GoTo(SheetModel.Closed);
            float min = 1000f;
            for (int i = 0; i < 40; i++) { sheet.Tick(0.01f); min = System.Math.Min(min, sheet.Height); }
            Assert.That(min, Is.GreaterThanOrEqualTo(0f), "closing never goes below zero");
        }

        // ---- Entry stations -----------------------------------------------------------------------

        static EntryStationModel Stations() => new EntryStationModel(new EntryStationSettings { stationWidth = 900f, settleSeconds = 0.2f, velocityProjectionSeconds = 0.2f });

        static void Settle(EntryStationModel m) { for (int i = 0; i < 40; i++) m.Tick(0.05f); }

        [Test]
        public void Stations_DragLeftShowsTheNext_ReleaseSnapsToTheNearest()
        {
            var m = Stations();
            m.Set(0, instant: true);
            m.BeginDrag(1000f);
            m.Drag(1000f - 300f); // a third of a station
            Assert.AreEqual(1f / 3f, m.Position, 1e-3f, "the presentation follows the finger");
            Assert.AreEqual(0, m.Release(0f), "nearer the first station");
            Settle(m);
            Assert.AreEqual(0f, m.Position, 1e-3f);

            m.BeginDrag(1000f);
            m.Drag(1000f - 300f);
            Assert.AreEqual(1, m.Release(-2000f), "a flick left carries it to the next station");
            Settle(m);
            Assert.AreEqual(1f, m.Position, 1e-3f);
            Assert.AreEqual(0f, m.Displacement, 1e-3f);
        }

        [Test]
        public void Stations_OverscrollAtTheEnds_IsCompressed_AndStepsAreClamped()
        {
            var m = Stations();
            m.Set(2, instant: true);
            m.BeginDrag(0f);
            m.Drag(-900f); // a whole station past the end
            Assert.That(m.Position, Is.GreaterThan(2f).And.LessThan(2.25f));
            m.Release(0f);
            Assert.AreEqual(2, m.Station);
            m.Step(1);
            Assert.AreEqual(2, m.Station);
            m.Step(-1);
            Assert.AreEqual(1, m.Station);
            Settle(m);
            Assert.AreEqual(1f, m.Position, 1e-3f);
            m.Set(0, instant: true);
            m.BeginDrag(0f);
            m.Drag(500f);
            Assert.That(m.Position, Is.LessThan(0f).And.GreaterThan(-0.25f));
        }

        [Test]
        public void Stations_SettleIsInterruptible()
        {
            var m = Stations();
            m.Set(0, instant: true);
            m.Step(1);
            m.Tick(0.05f);
            Assert.IsTrue(m.IsSettling);
            float mid = m.Position;
            m.BeginDrag(500f); // a new drag takes over where the presentation is
            Assert.IsFalse(m.IsSettling);
            m.Drag(500f);
            Assert.AreEqual(mid, m.Position, 1e-3f);
        }
    }
}
