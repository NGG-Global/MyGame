using NUnit.Framework;
using OpenSkies.FlightState;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>The rotary adjustment, the map view and the instrument settings as data.</summary>
    public class InstrumentManipulationTests
    {
        static Vector2 OnDial(float clockwiseDegrees, float radius)
        {
            float a = clockwiseDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius);
        }

        // ---- Rotary -------------------------------------------------------------------------------

        [Test]
        public void Rotary_QuarterTurnClockwise_AddsAQuarterOfTheRange()
        {
            var rotary = new RotaryModel(new RotarySettings { unitsPerTurn = 360f, step = 1f, fineRadius = 1000f });
            rotary.Begin(360f, 1f, 360f, wrap: true, OnDial(0f, 100f));
            Assert.IsFalse(rotary.Changed);
            for (int i = 1; i <= 9; i++) rotary.Update(OnDial(i * 10f, 100f));
            Assert.AreEqual(90f, rotary.Preview, 1e-3f, "a quarter turn clockwise: heading 090");
            Assert.IsTrue(rotary.Changed);
            Assert.AreEqual(90f, rotary.AccumulatedDegrees, 1e-3f);
        }

        [Test]
        public void Rotary_WrapsThroughNorth_AndUnwrapsManyTurns()
        {
            var rotary = new RotaryModel(new RotarySettings { unitsPerTurn = 360f, step = 1f, fineRadius = 1000f });
            rotary.Begin(350f, 1f, 360f, wrap: true, OnDial(0f, 100f));
            for (int i = 1; i <= 3; i++) rotary.Update(OnDial(i * 10f, 100f));
            Assert.AreEqual(20f, rotary.Preview, 1e-3f, "350 plus 30 wraps to 020");
            for (int i = 4; i <= 72; i++) rotary.Update(OnDial(i * 10f, 100f)); // two full turns from the start
            Assert.AreEqual(350f, rotary.Preview, 1e-3f);
            Assert.AreEqual(720f, rotary.AccumulatedDegrees, 1e-2f, "turns accumulate; the finger never jumps half a turn");
            for (int i = 71; i >= 0; i--) rotary.Update(OnDial(i * 10f, 100f)); // and back
            Assert.AreEqual(350f, rotary.Preview, 1e-3f);
            Assert.IsFalse(rotary.Changed);
        }

        [Test]
        public void Rotary_FineBeyondTheFineRadius_SnapsToTheStep()
        {
            var rotary = new RotaryModel(new RotarySettings { unitsPerTurn = 144f, step = 1f, fineRadius = 150f, fineGain = 0.5f });
            rotary.Begin(1013f, 950f, 1050f, wrap: false, OnDial(0f, 100f));
            for (int i = 1; i <= 9; i++) rotary.Update(OnDial(i * 10f, 100f)); // coarse: 90° of 360° at 144 per turn = 36
            Assert.AreEqual(1049f, rotary.Preview, 1e-3f);
            rotary.Begin(1013f, 950f, 1050f, wrap: false, OnDial(0f, 200f));
            for (int i = 1; i <= 9; i++) rotary.Update(OnDial(i * 10f, 200f)); // fine: half
            Assert.AreEqual(1031f, rotary.Preview, 1e-3f, "further out, the same angle moves the value half as much");
            for (int i = 10; i <= 72; i++) rotary.Update(OnDial(i * 10f, 200f));
            Assert.AreEqual(1050f, rotary.Preview, 1e-3f, "clamped at the top of the subscale");
        }

        [Test]
        public void Rotary_CancelZone_AtTheCentre_WithHysteresis()
        {
            var rotary = new RotaryModel(new RotarySettings { cancelRadius = 46f, cancelHysteresis = 10f, fineRadius = 1000f });
            rotary.Begin(360f, 1f, 360f, true, OnDial(0f, 100f));
            rotary.Update(OnDial(45f, 100f));
            Assert.AreEqual(45f, rotary.Preview, 1e-3f);
            rotary.Update(OnDial(45f, 30f));
            Assert.IsTrue(rotary.Cancelled, "moved into the centre");
            Assert.IsFalse(rotary.Changed, "releasing now changes nothing");
            rotary.Update(OnDial(45f, 50f));
            Assert.IsTrue(rotary.Cancelled, "inside the hysteresis band: still cancelled");
            rotary.Update(OnDial(45f, 60f));
            Assert.IsFalse(rotary.Cancelled);
            Assert.AreEqual(45f, rotary.Preview, 1e-3f, "coming back out continues from the same angle: no jump");
        }

        [Test]
        public void Rotary_TicksEveryFiveUnits()
        {
            var rotary = new RotaryModel(new RotarySettings { unitsPerTurn = 360f, tickEvery = 5f, fineRadius = 1000f });
            rotary.Begin(360f, 1f, 360f, true, OnDial(0f, 100f));
            rotary.Update(OnDial(12f, 100f));
            Assert.AreEqual(2, rotary.TakeTicks());
            Assert.AreEqual(0, rotary.TakeTicks(), "reading clears");
            rotary.Update(OnDial(3f, 100f)); // back 9°: crosses 10 and 5
            Assert.AreEqual(2, rotary.TakeTicks());
        }

        [Test]
        public void Rotary_BeginInsideTheCentre_StartsCancelled()
        {
            var rotary = new RotaryModel();
            rotary.Begin(100f, 1f, 360f, true, new Vector2(5f, 5f));
            Assert.IsTrue(rotary.Cancelled);
        }

        // ---- Map ----------------------------------------------------------------------------------

        [Test]
        public void Map_PanIsClamped_AndRecentres()
        {
            var map = new MapViewModel(new MapViewSettings { maxPanRadii = 1.5f });
            map.SetRadius(100f);
            map.Drag(new Vector2(50f, 0f));
            Assert.AreEqual(new Vector2(50f, 0f), map.Pan);
            Assert.IsTrue(map.IsCustom);
            map.Drag(new Vector2(500f, 0f));
            Assert.AreEqual(150f, map.Pan.magnitude, 1e-3f, "no further than 1.5 radii");
            map.Recenter();
            Assert.IsFalse(map.IsCustom);
            Assert.AreEqual(0f, map.RangeNm, "automatic range again");
        }

        [Test]
        public void Map_PinchZoomsInSteps_WithHysteresis_FromTheAutomaticRange()
        {
            var map = new MapViewModel(new MapViewSettings { zoomStepsPerScreenHeight = 6f, zoomHysteresis = 0.3f });
            map.Pinch(0.05f, 10f); // 0.3 of a step: nothing yet
            Assert.AreEqual(10f, map.RangeNm, "the automatic range is taken as the start");
            map.Pinch(0.2f, 10f); // now 1.5 steps
            Assert.AreEqual(5f, map.RangeNm, "spreading zooms in");
            map.Pinch(-0.6f, 10f);
            Assert.AreEqual(20f, map.RangeNm, "pinching in zooms out");
            map.Pinch(-5f, 10f);
            Assert.AreEqual(50f, map.RangeNm, "clamped at the widest range");
            Assert.AreEqual(1, MapViewModel.IndexOf(5f));
            Assert.AreEqual(1, MapViewModel.IndexOf(7f), "unknown ranges fall back to 5 NM");
        }

        // ---- Instrument settings ------------------------------------------------------------------

        [Test]
        public void InstrumentSettings_WrapHeading_ClampBaro_RaiseChanged()
        {
            var settings = new InstrumentSettings();
            int changes = 0;
            settings.Changed += _ => changes++;
            Assert.IsFalse(settings.Set(InstrumentSetting.HeadingBug, 0), "a fresh bug sits at 360; 0 is the same heading");
            Assert.IsTrue(settings.Set(InstrumentSetting.HeadingBug, 90));
            Assert.IsTrue(settings.Set(InstrumentSetting.HeadingBug, 720));
            Assert.AreEqual(360, settings.HeadingBugDegrees, "720 wraps to 360");
            Assert.IsTrue(settings.Set(InstrumentSetting.HeadingBug, -90));
            Assert.AreEqual(270, settings.HeadingBugDegrees);
            Assert.IsTrue(settings.Set(InstrumentSetting.BaroHectopascals, 2000));
            Assert.AreEqual(InstrumentSettings.MaxBaroHectopascals, settings.BaroHectopascals);
            Assert.AreEqual(4, changes);
            Assert.AreEqual(4, settings.Version);
            settings.Reset();
            Assert.AreEqual(1013, settings.BaroHectopascals);
            Assert.AreEqual(360, settings.HeadingBugDegrees);
        }

        [Test]
        public void BaroCorrection_StandardIsZero_HigherSettingReadsHigher()
        {
            Assert.AreEqual(0f, InstrumentSettings.BaroCorrectionFeet(1013), 10f);
            Assert.That(InstrumentSettings.BaroCorrectionFeet(1023), Is.GreaterThan(250f).And.LessThan(290f));
            Assert.That(InstrumentSettings.BaroCorrectionFeet(1003), Is.LessThan(-250f));
            var t = new FlightTelemetry { AltitudeFeet = 1000f };
            var readings = InstrumentReadings.From(t, default, InstrumentSettings.BaroCorrectionFeet(1023));
            Assert.That(readings.AltitudeFeet, Is.GreaterThan(1250f), "the altimeter reads higher with a higher subscale; the aircraft is where it was");
        }
    }
}
