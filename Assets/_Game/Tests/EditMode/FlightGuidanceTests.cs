using NUnit.Framework;
using OpenSkies.Awareness;
using OpenSkies.FlightState;

namespace OpenSkies.Tests
{
    /// <summary>The Assisted profile's flying tips and the ASSIST rail action.</summary>
    public class FlightGuidanceTests
    {
        const float Dt = 0.02f;

        static FlightGuidanceAdvisor.Input In(FlightPhase phase, int warnings = 0, float ias = 94f, float bank = 0f, float throttle = 0.6f,
            bool onGround = false, bool onRunway = false, bool parkingBrake = false, float height = 1000f, float flaps = 0f) =>
            new FlightGuidanceAdvisor.Input
            {
                Telemetry = new FlightTelemetry
                {
                    IndicatedAirspeedKnots = ias, BankDegrees = bank, Throttle = throttle, WeightOnWheels = onGround,
                    HeightAboveGroundFeet = height, FlapPosition = flaps,
                },
                Phase = phase, WarningMask = warnings, OnRunway = onRunway, ParkingBrakeSet = parkingBrake,
            };

        static GuidanceHint Run(FlightGuidanceAdvisor advisor, FlightGuidanceAdvisor.Input input, float seconds)
        {
            var hint = advisor.Current;
            for (int i = 0; i < (int)(seconds / Dt + 0.5f); i++) hint = advisor.Update(input, Dt);
            return hint;
        }

        [Test]
        public void SafetyTipsComeFirst_AndAppearAtOnce()
        {
            var advisor = new FlightGuidanceAdvisor(AircraftEnvelope.PrototypeTrainer);
            Run(advisor, In(FlightPhase.InitialClimb), 1f);
            Assert.AreEqual(GuidanceHint.ClimbSpeed, advisor.Current);
            Assert.AreEqual(GuidanceHint.StallRecovery, advisor.Update(In(FlightPhase.InitialClimb, FlightWarnings.Bit(FlightWarning.Stall)), Dt),
                "a stall replaces any advice immediately");
            StringAssert.Contains("lower the nose", advisor.CurrentText);
            int both = FlightWarnings.Bit(FlightWarning.Overspeed) | FlightWarnings.Bit(FlightWarning.SinkRate);
            Assert.AreEqual(GuidanceHint.SinkRate, Run(advisor, In(FlightPhase.Descent, both), 3f), "most urgent first");
        }

        [Test]
        public void PhaseAdvice_ShowsOncePerPhase_ThenStaysQuiet()
        {
            var advisor = new FlightGuidanceAdvisor(AircraftEnvelope.PrototypeTrainer);
            Assert.AreEqual(GuidanceHint.ApproachSetup, Run(advisor, In(FlightPhase.Approach), 1f));
            Assert.AreEqual(GuidanceHint.None, Run(advisor, In(FlightPhase.Approach), advisor.Settings.adviceSeconds + 3f), "never nags");
            Assert.AreEqual(GuidanceHint.ClimbSpeed, Run(advisor, In(FlightPhase.InitialClimb), 1f), "a new phase brings its advice");
            Assert.AreEqual(GuidanceHint.None, Run(advisor, In(FlightPhase.Cruise), 5f), "nothing to say in cruise");
        }

        [Test]
        public void CautionsDoNotExpire_AndTipsNeverFlicker()
        {
            var advisor = new FlightGuidanceAdvisor(AircraftEnvelope.PrototypeTrainer);
            Assert.AreEqual(GuidanceHint.SteepBank, Run(advisor, In(FlightPhase.Cruise, bank: 60f), 20f), "a steep bank is mentioned while it lasts");
            // The reason goes away for a moment: the tip stays (minimum time and linger), no flicker.
            Assert.AreEqual(GuidanceHint.SteepBank, Run(advisor, In(FlightPhase.Cruise, bank: 40f), 0.5f));
            Assert.AreEqual(GuidanceHint.SteepBank, Run(advisor, In(FlightPhase.Cruise, bank: 60f), 0.1f));
            Assert.AreEqual(GuidanceHint.None, Run(advisor, In(FlightPhase.Cruise, bank: 10f), advisor.Settings.minimumSeconds + advisor.Settings.lingerSeconds + 0.1f));

            Assert.AreEqual(GuidanceHint.LowSpeed, Run(advisor, In(FlightPhase.Cruise, ias: 55f), 20f), "slow with flaps up");
            Assert.AreEqual(GuidanceHint.None, Run(advisor, In(FlightPhase.Approach, ias: 58f, flaps: 1f), 12f), "fine with full flap (after the approach advice)");
        }

        [Test]
        public void GroundAdvice_AndSpeedsFromTheAircraft()
        {
            var advisor = new FlightGuidanceAdvisor(AircraftEnvelope.PrototypeTrainer);
            Assert.AreEqual(GuidanceHint.TakeOffPower, Run(advisor, In(FlightPhase.Parked, onGround: true, onRunway: true, throttle: 0f), 1f));
            advisor.Reset();
            Assert.AreEqual(GuidanceHint.ReleaseParkingBrake,
                Run(advisor, In(FlightPhase.Parked, onGround: true, onRunway: true, throttle: 0.8f, parkingBrake: true), 1f));
            advisor.Reset();
            Assert.AreEqual(GuidanceHint.Rotate, Run(advisor, In(FlightPhase.TakeoffRoll, onGround: true, ias: 55f, throttle: 1f), 1f));
            StringAssert.Contains("55 KT", advisor.CurrentText, "1.1 × the 51 kt clean stall speed, rounded to 5 kt");
            StringAssert.Contains("65 KT", advisor.Text(GuidanceHint.ApproachSetup), "1.4 × the 46 kt full-flap stall speed");
            advisor.Reset();
            Assert.AreEqual(GuidanceHint.Flare, Run(advisor, In(FlightPhase.Landing, height: 20f, ias: 62f, flaps: 1f), 1f));
            Assert.AreEqual(GuidanceHint.None, new FlightGuidanceAdvisor(default).Current, "an undefined envelope falls back to the trainer");
        }

        // ---- ASSIST on the action rail -----------------------------------------------------------------

        static DisclosureInput Disclosure(FlightPhase phase, HudProfile profile, bool hasSettings) => new DisclosureInput
        {
            Profile = profile,
            Phase = new FlightPhaseEstimate { Phase = phase, Candidate = phase, Confidence = 0.95f },
            OnGround = FlightPhases.IsGroundPhase(phase),
            HasAssistanceSettings = hasSettings,
        };

        [Test]
        public void AssistAction_OfferedBeforeTakeOff_ReachableAnyTimeThroughSystems()
        {
            var d = new DisclosureController();
            d.Update(Disclosure(FlightPhase.Parked, HudProfile.Smart, true), Dt);
            Assert.IsTrue(d.IsActionVisible(ContextAction.Assistance), "choose the assistance before take-off");
            Assert.IsFalse(d.IsActionEngaged(ContextAction.Assistance), "a settings action is never 'engaged'");

            d = new DisclosureController();
            d.Update(Disclosure(FlightPhase.Cruise, HudProfile.Smart, true), Dt);
            Assert.IsFalse(d.IsActionVisible(ContextAction.Assistance), "not cluttering the flight");
            d.SetRailExpanded(true);
            d.Update(Disclosure(FlightPhase.Cruise, HudProfile.Smart, true), Dt);
            Assert.IsTrue(d.IsActionVisible(ContextAction.Assistance), "SYSTEMS reaches it any time");

            d = new DisclosureController();
            d.Update(Disclosure(FlightPhase.Parked, HudProfile.Detailed, false), Dt);
            Assert.IsFalse(d.IsActionVisible(ContextAction.Assistance), "not offered without a settings panel");
        }
    }
}
