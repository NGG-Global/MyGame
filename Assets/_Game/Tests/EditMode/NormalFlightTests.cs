using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Awareness;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Acceptance: one complete scripted flight (take-off, climb, cruise, a turn,
    /// descent, level-off, approach, landing, rollout, taxi, stop) through the
    /// phase estimator and the Smart HUD. Checks the phase sequence, what the
    /// HUD shows at each stage, and that nothing flickers.
    /// </summary>
    public class NormalFlightTests
    {
        /// <summary>Records every visibility change of every element and action.</summary>
        sealed class VisibilityTracker
        {
            public readonly Dictionary<string, List<(float time, bool visible)>> Changes = new Dictionary<string, List<(float, bool)>>();
            readonly Dictionary<string, bool> _last = new Dictionary<string, bool>();

            public void Record(FlightScript f)
            {
                for (int i = 0; i < HudElements.Count; i++)
                    Note("element " + (HudElement)i, f.Time, f.Disclosure.IsVisible((HudElement)i));
                for (int i = 0; i < ContextActions.Count; i++)
                    Note("action " + (ContextAction)i, f.Time, f.Disclosure.IsActionVisible((ContextAction)i));
            }

            void Note(string key, float time, bool visible)
            {
                if (_last.TryGetValue(key, out bool last) && last == visible) return;
                _last[key] = visible;
                if (!Changes.TryGetValue(key, out var list)) Changes[key] = list = new List<(float, bool)>();
                list.Add((time, visible));
            }
        }

        [Test]
        public void SmartHud_AdaptsThroughoutANormalFlight_WithoutFlicker()
        {
            var f = new FlightScript();
            var tracker = new VisibilityTracker();
            f.AfterStep = tracker.Record;
            var d = f.Disclosure;

            // Parked.
            f.Run(5f);
            Assert.AreEqual(FlightPhase.Parked, f.Phase);
            Assert.IsFalse(d.IsVisible(HudElement.Airspeed), "nothing to read while parked");
            Assert.IsTrue(d.IsActionVisible(ContextAction.ParkingBrake));

            // Take-off roll.
            f.Throttle = 1f;
            f.RunUntil(s => s.Groundspeed >= 60f, 40f, s => s.ChangeSpeed(3.3f, 60f));
            Assert.AreEqual(FlightPhase.TakeoffRoll, f.Phase);
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.Airspeed));
            Assert.IsFalse(d.IsActionVisible(ContextAction.ParkingBrake));

            // Lift-off and initial climb.
            f.WeightOnWheels = false;
            f.VerticalSpeed = 700f;
            f.Run(5f, s => s.ChangeSpeed(1f, 80f));
            Assert.AreEqual(FlightPhase.InitialClimb, f.Phase);
            Assert.IsTrue(d.IsVisible(HudElement.Altitude));
            Assert.IsTrue(d.IsVisible(HudElement.VerticalSpeed));

            // Climb to 3000 ft, level off, cruise.
            f.RunUntil(s => s.AltitudeFeet >= 3000f, 400f, s => s.ChangeSpeed(0.5f, 85f));
            Assert.AreEqual(FlightPhase.Climb, f.Phase);
            f.Throttle = 0.6f;
            f.Run(60f, s => { s.ApproachVerticalSpeed(0f, 200f); s.ChangeSpeed(0.5f, 100f); });
            Assert.AreEqual(FlightPhase.Cruise, f.Phase);
            Assert.IsTrue(d.IsStableFlight);
            Assert.AreEqual(Prominence.Quiet, d.GetProminence(HudElement.Airspeed), "stable flight recedes");
            Assert.AreEqual(Prominence.Quiet, d.GetProminence(HudElement.Altitude));
            Assert.IsFalse(d.IsVisible(HudElement.VerticalSpeed));
            Assert.IsTrue(d.IsActionVisible(ContextAction.Navigation), "navigation offered in cruise");

            // Turn back towards the airfield: information returns during the turn.
            f.Run(2f, s => { s.Heading = RunwayGeometry.Normalize360(s.Heading - 3f * FlightScript.Dt); s.Bank = -25f; });
            Assert.AreEqual(Prominence.Full, d.GetProminence(HudElement.Heading));
            f.Turn(-174f, 3f);
            Assert.AreEqual(FlightPhase.Cruise, f.Phase, "a level turn is still cruise");

            // South past the airfield at 3000 ft, then descend to 1500 ft.
            f.RunUntil(s => s.Position.z <= FlightScript.Threshold36Z - 2f * FlightScript.MetresPerNm, 900f);
            Assert.AreEqual(FlightPhase.Cruise, f.Phase, "overflying the airfield high is not an approach");
            f.RunUntil(s => s.AltitudeFeet <= 1500f, 400f, s => s.ApproachVerticalSpeed(-500f, 300f));
            Assert.AreEqual(FlightPhase.Descent, f.Phase);
            f.Run(25f, s => s.ApproachVerticalSpeed(0f, 300f));
            Assert.AreEqual(FlightPhase.Cruise, f.Phase);

            // Turn onto a long final for runway 36 and fly to the glide path.
            f.Turn(-180f, 3f);
            f.RunUntil(s => s.LastRunway.GlidepathValid && s.LastRunway.GlidepathAngleDegrees >= 3f, 600f);
            Assert.AreEqual(FlightPhase.Approach, f.Phase);
            Assert.IsTrue(d.IsVisible(HudElement.ApproachGuide));
            Assert.IsTrue(d.IsActionVisible(ContextAction.ApproachGuide));
            Assert.IsTrue(d.IsVisible(HudElement.VerticalSpeed));

            // Glide path, flare, touchdown.
            f.Throttle = 0.3f;
            f.RunUntil(s => s.AltitudeFeet <= 0.01f, 400f, s =>
            {
                s.ChangeSpeed(-1f, 65f);
                s.Throttle = s.AltitudeFeet < 30f ? 0f : 0.3f;
                s.VerticalSpeed = s.AltitudeFeet > 20f ? s.GlidepathVerticalSpeed(3f) : -150f;
            });
            Assert.AreEqual(FlightPhase.Landing, f.Phase);
            Assert.IsTrue(d.IsVisible(HudElement.Airspeed));
            f.WeightOnWheels = true;
            f.Throttle = 0f;

            // Rollout with brakes, taxi, stop.
            f.Brake = 1f;
            f.RunUntil(s => s.Groundspeed <= 12f, 60f, s => s.ChangeSpeed(-4f, 12f));
            f.Brake = 0f;
            f.Run(10f);
            Assert.AreEqual(FlightPhase.Taxi, f.Phase);
            Assert.IsTrue(d.IsVisible(HudElement.Groundspeed));
            Assert.IsTrue(d.IsActionVisible(ContextAction.ParkingBrake));
            f.RunUntil(s => s.Groundspeed <= 0f, 10f, s => s.ChangeSpeed(-4f, 0f));
            f.Run(6f);
            Assert.AreEqual(FlightPhase.Parked, f.Phase);

            CollectionAssert.AreEqual(new[]
            {
                FlightPhase.Parked, FlightPhase.TakeoffRoll, FlightPhase.InitialClimb, FlightPhase.Climb, FlightPhase.Cruise,
                FlightPhase.Descent, FlightPhase.Cruise, FlightPhase.Approach, FlightPhase.Landing, FlightPhase.Rollout,
                FlightPhase.Taxi, FlightPhase.Parked,
            }, f.Phases, "phases: " + string.Join(", ", f.Phases));

            // No phase is held for less than the minimum time.
            for (int i = 1; i < f.Times.Count; i++)
                Assert.That(f.Times[i] - f.Times[i - 1], Is.GreaterThanOrEqualTo(2f), $"{f.Phases[i - 1]} lasted too briefly");

            // Nothing flickers: every visible stretch lasts at least the minimum visible time,
            // and nothing disappears and comes back within a second.
            foreach (var entry in tracker.Changes)
            {
                var changes = entry.Value;
                for (int i = 1; i < changes.Count; i++)
                {
                    float stretch = changes[i].time - changes[i - 1].time;
                    if (changes[i - 1].visible)
                        Assert.That(stretch, Is.GreaterThanOrEqualTo(f.DisclosureSettings.minimumVisibleSeconds - 0.03f), entry.Key + " shown too briefly");
                    else
                        Assert.That(stretch, Is.GreaterThanOrEqualTo(1f), entry.Key + " hidden and shown again too quickly");
                }
            }
        }
    }
}
