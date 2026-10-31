using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Flight phase estimation from scripted telemetry sequences (see
    /// <see cref="FlightScript"/>). The prototype runway is 36/18 with the
    /// runway 36 threshold at z = −750 m.
    /// </summary>
    public class FlightPhaseEstimatorTests
    {
        [Test]
        public void TakeoffToClimb()
        {
            var f = new FlightScript();
            f.Run(3f);
            Assert.AreEqual(FlightPhase.Parked, f.Phase);

            f.Throttle = 1f;
            f.RunUntil(s => s.Groundspeed >= 60f, 40f, s => s.ChangeSpeed(3.3f, 60f));
            Assert.AreEqual(FlightPhase.TakeoffRoll, f.Phase);
            Assert.AreEqual(PhaseTransitionReason.TakeoffPower, f.Reason);

            // Lift-off.
            f.WeightOnWheels = false;
            f.VerticalSpeed = 700f;
            f.Run(3f, s => s.ChangeSpeed(1f, 75f));
            Assert.AreEqual(FlightPhase.InitialClimb, f.Phase);
            Assert.AreEqual(PhaseTransitionReason.LiftOff, f.Reason);

            f.RunUntil(s => s.AltitudeFeet >= 1500f, 200f, s => s.ChangeSpeed(1f, 80f));
            Assert.AreEqual(FlightPhase.Climb, f.Phase);
            Assert.AreEqual(PhaseTransitionReason.Climbing, f.Reason);
            Assert.That(f.Estimator.Confidence, Is.GreaterThan(0.8f));

            CollectionAssert.AreEqual(new[] { FlightPhase.Parked, FlightPhase.TakeoffRoll, FlightPhase.InitialClimb, FlightPhase.Climb }, f.Phases);
        }

        [Test]
        public void CruiseToApproach_LevelArrival()
        {
            var f = FlightScript.Airborne(nmBeforeThreshold: 7f, altitudeFeet: 1200f, knots: 100f, verticalSpeed: 0f, throttle: 0.6f);
            f.Run(10f);
            Assert.AreEqual(FlightPhase.Cruise, f.Phase);

            f.RunUntil(s => s.Phase != FlightPhase.Cruise, 300f);
            Assert.AreEqual(FlightPhase.Approach, f.Phase);
            Assert.AreEqual(PhaseTransitionReason.ApproachGeometry, f.Reason);
            float distanceNm = f.LastRunway.DistanceToThresholdMetres / FlightScript.MetresPerNm;
            Assert.That(distanceNm, Is.InRange(4.5f, 5.3f), "approach starts near the 5 NM limit");
            CollectionAssert.AreEqual(new[] { FlightPhase.Cruise, FlightPhase.Approach }, f.Phases);
        }

        [Test]
        public void CruiseDescentApproach_DescendingArrival()
        {
            var f = FlightScript.Airborne(nmBeforeThreshold: 9f, altitudeFeet: 3000f, knots: 100f, verticalSpeed: 0f, throttle: 0.6f);
            f.Run(10f);
            f.Run(240f, s => s.ApproachVerticalSpeed(s.AltitudeFeet > 1200f ? -500f : 0f, 300f));
            CollectionAssert.AreEqual(new[] { FlightPhase.Cruise, FlightPhase.Descent, FlightPhase.Approach }, f.Phases);
        }

        [TestCase(45f, FlightPhase.Rollout)]
        [TestCase(12f, FlightPhase.Taxi)]
        public void AbortedTakeoff(float abortKnots, FlightPhase expectedAfterAbort)
        {
            var f = new FlightScript();
            f.Run(3f);
            f.Throttle = 1f;
            f.RunUntil(s => s.Groundspeed >= abortKnots, 30f, s => s.ChangeSpeed(3.3f, 80f));
            Assert.AreEqual(FlightPhase.TakeoffRoll, f.Phase);

            // Reject: idle and brakes.
            f.Throttle = 0f;
            f.Brake = 1f;
            f.RunUntil(s => s.Phase != FlightPhase.TakeoffRoll, 5f, s => s.ChangeSpeed(-5f, 0f));
            Assert.AreEqual(expectedAfterAbort, f.Phase);
            Assert.AreEqual(PhaseTransitionReason.TakeoffRejected, f.Reason);

            f.RunUntil(s => s.Groundspeed <= 0f, 30f, s => s.ChangeSpeed(-5f, 0f));
            f.Run(5f);
            Assert.AreEqual(FlightPhase.Parked, f.Phase);
            Assert.IsFalse(f.Phases.Exists(FlightPhases.IsAirbornePhase), "never airborne");
            Assert.AreEqual(FlightPhase.Parked, f.Phases[f.Phases.Count - 1]);
        }

        [Test]
        public void TouchAndGo()
        {
            var f = ShortFinal();
            FlyToTouchdown(f);

            // A second on the wheels, then full power without stopping.
            f.Run(1f, s => s.ChangeSpeed(-2f, 0f));
            f.Throttle = 1f;
            f.RunUntil(s => s.Groundspeed >= 65f, 15f, s => s.ChangeSpeed(3f, 70f));
            f.WeightOnWheels = false;
            f.VerticalSpeed = 600f;
            f.Run(3f);

            Assert.AreEqual(FlightPhase.InitialClimb, f.Phase);
            CollectionAssert.Contains(f.Phases, FlightPhase.Landing);
            CollectionAssert.DoesNotContain(f.Phases, FlightPhase.Parked);
            CollectionAssert.DoesNotContain(f.Phases, FlightPhase.Taxi);
            CollectionAssert.DoesNotContain(f.Phases, FlightPhase.Rollout);
            int takeoffRoll = f.Phases.IndexOf(FlightPhase.TakeoffRoll);
            Assert.That(takeoffRoll, Is.GreaterThan(0), "a take-off roll follows the touchdown");
            Assert.AreEqual(PhaseTransitionReason.TouchAndGo, f.Reasons[takeoffRoll]);
            CollectionAssert.AreEqual(new[] { FlightPhase.Approach, FlightPhase.Landing, FlightPhase.TakeoffRoll, FlightPhase.InitialClimb }, f.Phases);
        }

        [Test]
        public void FullStopLanding_LandingRolloutTaxiParked()
        {
            var f = ShortFinal();
            FlyToTouchdown(f);
            Assert.AreEqual(FlightPhase.Landing, f.Phase);
            f.Brake = 1f;
            f.RunUntil(s => s.Groundspeed <= 12f, 30f, s => s.ChangeSpeed(-4f, 12f));
            f.Brake = 0f;
            f.Run(6f);
            f.RunUntil(s => s.Groundspeed <= 0f, 10f, s => s.ChangeSpeed(-4f, 0f));
            f.Run(4f);
            CollectionAssert.AreEqual(new[]
            {
                FlightPhase.Approach, FlightPhase.Landing, FlightPhase.Rollout, FlightPhase.Taxi, FlightPhase.Parked,
            }, f.Phases);
            Assert.That(f.Reasons[1], Is.EqualTo(PhaseTransitionReason.ShortFinal).Or.EqualTo(PhaseTransitionReason.Touchdown));
        }

        [Test]
        public void BounceAtIdle_StaysInLanding()
        {
            var f = ShortFinal();
            FlyToTouchdown(f);

            // The aircraft bounces back into the air for about two seconds at idle.
            f.WeightOnWheels = false;
            f.VerticalSpeed = 300f;
            f.Run(1f);
            f.RunUntil(s => s.AltitudeFeet <= 0.01f, 10f, s => s.VerticalSpeed = -300f);
            f.WeightOnWheels = true;
            f.Run(1f);

            Assert.AreEqual(FlightPhase.Landing, f.Phase);
            CollectionAssert.AreEqual(new[] { FlightPhase.Approach, FlightPhase.Landing }, f.Phases, "no climb or approach during the bounce");
        }

        [TestCase(250f, FlightPhase.Approach)]
        [TestCase(60f, FlightPhase.Landing)]
        public void GoAround(float goAroundHeightFeet, FlightPhase phaseBeforeGoAround)
        {
            var f = FlightScript.Airborne(nmBeforeThreshold: 2f, altitudeFeet: 640f, knots: 70f, verticalSpeed: -370f, throttle: 0.3f);
            f.RunUntil(s => s.AltitudeFeet <= goAroundHeightFeet, 120f, s => s.VerticalSpeed = s.GlidepathVerticalSpeed(3f));
            f.Run(1f, s => s.VerticalSpeed = s.GlidepathVerticalSpeed(3f));
            Assert.AreEqual(phaseBeforeGoAround, f.Phase);

            f.Throttle = 1f;
            f.VerticalSpeed = 700f;
            f.RunUntil(s => s.Phase != phaseBeforeGoAround, 10f);
            Assert.AreEqual(FlightPhase.InitialClimb, f.Phase);
            Assert.AreEqual(PhaseTransitionReason.GoAround, f.Reason);
            Assert.IsFalse(f.Estimator.OnGround);

            f.RunUntil(s => s.AltitudeFeet >= 1200f, 120f);
            Assert.AreEqual(FlightPhase.Climb, f.Phase);
            foreach (var phase in f.Phases)
                Assert.IsFalse(FlightPhases.IsGroundPhase(phase), "no ground phase during a go-around");
        }

        [Test]
        public void BriefFluctuations_DoNotChangeThePhase_InCruise()
        {
            // East of the airfield, well away from any approach sector.
            var f = new FlightScript(onGround: false) { Heading = 90f, Groundspeed = 100f, Throttle = 0.6f };
            f.Position = new Vector3(20000f, 0f, 0f);
            f.AltitudeFeet = 3000f;
            f.Run(15f);
            Assert.AreEqual(FlightPhase.Cruise, f.Phase);
            int transitions = f.Estimator.TransitionTotal;

            f.Run(1f, s => s.VerticalSpeed = -900f);                 // one second of turbulence
            f.VerticalSpeed = 0f;
            f.Run(8f);
            f.CorruptNextSample(t => { t.WeightOnWheels = true; return t; });   // one false weight-on-wheels sample
            f.Run(2f);
            f.CorruptNextSample(t =>                                  // a sample of garbage
            {
                t.VerticalSpeedFeetPerMinute = float.NaN;
                t.GroundspeedKnots = float.PositiveInfinity;
                t.HeightAboveGroundFeet = float.NaN;
                return t;
            });
            f.Run(2f);
            f.Run(0.3f, s => s.StallWarning = true);                 // short airspeed/attitude upset
            f.StallWarning = false;
            f.Run(5f);

            Assert.AreEqual(FlightPhase.Cruise, f.Phase);
            Assert.AreEqual(transitions, f.Estimator.TransitionTotal, "no phase change at all");
            Assert.That(f.Estimator.Confidence, Is.GreaterThan(0.8f));
            Assert.IsFalse(float.IsNaN(f.Estimator.SmoothedVerticalSpeed));
        }

        [Test]
        public void BriefFluctuations_DoNotChangeThePhase_OnTheGround()
        {
            var f = new FlightScript { Throttle = 0.3f, Groundspeed = 12f };
            f.Run(5f);
            Assert.AreEqual(FlightPhase.Taxi, f.Phase);
            int transitions = f.Estimator.TransitionTotal;

            var random = new System.Random(1234);
            f.Run(10f, s => s.Groundspeed = 12f + (float)(random.NextDouble() - 0.5) * 0.6f); // speed noise
            f.CorruptNextSample(t => { t.WeightOnWheels = false; return t; });                 // one-sample gear glitch
            f.Run(1f);
            f.Run(0.3f, s => s.Throttle = 0.8f);                                                // short throttle blip
            f.Throttle = 0.3f;
            f.Run(5f);

            Assert.AreEqual(FlightPhase.Taxi, f.Phase);
            Assert.AreEqual(transitions, f.Estimator.TransitionTotal);
        }

        [Test]
        public void MinimumDuration_HoldsAPhaseEvenWhenTheChallengerIsConfirmed()
        {
            var settings = new FlightPhaseSettings { minimumPhaseSeconds = 5f, descentConfirmSeconds = 0.5f };
            var f = new FlightScript(onGround: false, phaseSettings: settings) { Heading = 90f, Groundspeed = 100f, Throttle = 0.6f };
            f.Position = new Vector3(20000f, 0f, 0f);
            f.AltitudeFeet = 3000f;
            f.Run(0.1f);
            Assert.AreEqual(FlightPhase.Cruise, f.Phase);

            f.VerticalSpeed = -800f;
            f.Run(3f);
            Assert.AreEqual(FlightPhase.Cruise, f.Phase, "held for the minimum time");
            f.Run(3f);
            Assert.AreEqual(FlightPhase.Descent, f.Phase);
        }

        [Test]
        public void PhaseAndConfidence_AreExposedSeparatelyFromTelemetry()
        {
            var f = new FlightScript();
            f.Run(3f);
            var estimate = f.Estimator.Current;
            Assert.AreEqual(FlightPhase.Parked, estimate.Phase);
            Assert.That(estimate.Confidence, Is.InRange(0.9f, 1f));
            Assert.IsTrue(estimate.OnGround);
            Assert.IsFalse(estimate.HasCandidate);
            Assert.AreEqual(1, f.Estimator.TransitionCount);
            Assert.AreEqual(PhaseTransitionReason.InitialEstimate, f.Estimator.GetTransition(0).Reason);
        }

        [Test]
        public void Reset_ForgetsHistoryAndStartsParked()
        {
            var f = FlightScript.Airborne(nmBeforeThreshold: 9f, altitudeFeet: 3000f, knots: 100f, verticalSpeed: 0f, throttle: 0.6f);
            f.Run(10f);
            f.Estimator.Reset(assumeOnGround: true);
            Assert.AreEqual(FlightPhase.Parked, f.Estimator.Phase);
            Assert.AreEqual(0, f.Estimator.TransitionCount);
            Assert.AreEqual(0f, f.Estimator.Clock);
        }

        [Test]
        public void TransitionLog_IsNewestFirstAndBounded()
        {
            var f = new FlightScript();
            for (int i = 0; i < 6; i++)
            {
                f.Throttle = 0.3f;
                f.Run(4f, s => s.ChangeSpeed(4f, 10f));   // taxi
                f.Throttle = 0f;
                f.Run(6f, s => s.ChangeSpeed(-4f, 0f));   // stop
            }
            Assert.AreEqual(FlightPhaseEstimator.HistoryCapacity, f.Estimator.TransitionCount);
            var newest = f.Estimator.GetTransition(0);
            Assert.AreEqual(FlightPhase.Parked, newest.To);
            Assert.That(newest.Time, Is.GreaterThan(f.Estimator.GetTransition(1).Time));
        }

        // ---- Helpers ------------------------------------------------------------------

        /// <summary>200 ft on a 3° glide path to runway 36 (aim point 150 m beyond the threshold), 65 kt.</summary>
        static FlightScript ShortFinal()
        {
            float heightMetres = 200f / FlightUnits.FeetPerMetre;
            float toAimPoint = heightMetres / Mathf.Tan(3f * Mathf.Deg2Rad);
            float nmBeforeThreshold = (toAimPoint - 150f) / FlightScript.MetresPerNm;
            var f = FlightScript.Airborne(nmBeforeThreshold, 200f, 65f, -350f, 0.2f);
            f.Run(0.1f);
            Assert.AreEqual(FlightPhase.Approach, f.Phase, "starts on approach");
            return f;
        }

        /// <summary>Follows the glide path, flares at 20 ft and touches down.</summary>
        static void FlyToTouchdown(FlightScript f)
        {
            f.RunUntil(s => s.AltitudeFeet <= 0.01f, 60f, s =>
            {
                s.Throttle = s.AltitudeFeet < 30f ? 0f : 0.2f;
                s.VerticalSpeed = s.AltitudeFeet > 20f ? s.GlidepathVerticalSpeed(3f) : -150f;
            });
            f.WeightOnWheels = true;
            f.Groundspeed = 60f;
            f.Throttle = 0f;
            f.Run(0.5f);
            Assert.AreEqual(FlightPhase.Landing, f.Phase);
        }
    }
}
