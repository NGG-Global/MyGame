using NUnit.Framework;
using OpenSkies.FlightInput;
using OpenSkies.FlightState;

namespace OpenSkies.Tests
{
    public class AircraftSystemsTests
    {
        [Test]
        public void StartState_IsReadyOnTheRunway()
        {
            var s = new AircraftSystems();
            Assert.AreEqual(0, s.FlapDetent);
            Assert.IsFalse(s.ParkingBrakeSet);
            Assert.IsFalse(s.LandingLightOn);
            Assert.IsTrue(s.NavigationLightsOn);
            Assert.IsTrue(s.IgnitionOn, "the engine starts running");
            Assert.AreEqual(4, s.FlapDetentCount);
        }

        [Test]
        public void Apply_ClampsCountsVersionsAndNotifies()
        {
            var s = new AircraftSystems(new[] { 0f, 10f, 20f, 30f });
            int changes = 0;
            AircraftSystem last = default;
            s.Changed += system => { changes++; last = system; };
            int version = s.Version;

            Assert.IsTrue(s.Set(AircraftSystem.Flaps, 2));
            Assert.AreEqual(2f / 3f, s.FlapSelection, 1e-5f, "20 of 30 degrees");
            Assert.AreEqual(AircraftSystem.Flaps, last);
            Assert.IsFalse(s.Set(AircraftSystem.Flaps, 2), "same position: no change");
            Assert.IsTrue(s.Set(AircraftSystem.Flaps, 9));
            Assert.AreEqual(3, s.FlapDetent, "clamped to full flap");
            Assert.IsTrue(s.Set(AircraftSystem.LandingLight, true));
            Assert.AreEqual(1, s.Get(AircraftSystem.LandingLight));
            Assert.AreEqual(3, changes);
            Assert.AreEqual(version + 3, s.Version);
        }

        [Test]
        public void Policy_ParkingBrakeOnlySetOnTheGround_EngineOffInFlightNeedsConfirmation()
        {
            var s = new AircraftSystems();
            var setBrake = new SystemChange(AircraftSystem.ParkingBrake, 1);
            Assert.AreEqual(AircraftSystemsPolicy.Verdict.Refused, AircraftSystemsPolicy.Evaluate(setBrake, s, onGround: false));
            Assert.AreEqual(AircraftSystemsPolicy.Verdict.Allowed, AircraftSystemsPolicy.Evaluate(setBrake, s, onGround: true));
            s.Set(AircraftSystem.ParkingBrake, true);
            Assert.AreEqual(AircraftSystemsPolicy.Verdict.Allowed,
                AircraftSystemsPolicy.Evaluate(new SystemChange(AircraftSystem.ParkingBrake, 0), s, onGround: false), "release anywhere");

            var engineOff = new SystemChange(AircraftSystem.Ignition, 0);
            Assert.AreEqual(AircraftSystemsPolicy.Verdict.NeedsConfirmation, AircraftSystemsPolicy.Evaluate(engineOff, s, onGround: false));
            Assert.AreEqual(AircraftSystemsPolicy.Verdict.Allowed, AircraftSystemsPolicy.Evaluate(engineOff, s, onGround: true));
            Assert.AreEqual(AircraftSystemsPolicy.Verdict.Allowed,
                AircraftSystemsPolicy.Evaluate(new SystemChange(AircraftSystem.Flaps, 3), s, onGround: false));
        }

        [Test]
        public void Gate_HoldsAHazardousChangeUntilConfirmed()
        {
            var s = new AircraftSystems();
            var gate = new SystemChangeGate { ConfirmationWindowSeconds = 4f };
            var engineOff = new SystemChange(AircraftSystem.Ignition, 0);

            Assert.AreEqual(SystemChangeGate.Outcome.AwaitingConfirmation, gate.Request(engineOff, s, false, 10f));
            Assert.IsTrue(s.IgnitionOn, "not yet");
            Assert.IsTrue(gate.HasPending(11f, out var pending));
            Assert.AreEqual(AircraftSystem.Ignition, pending.System);
            Assert.AreEqual(SystemChangeGate.Outcome.Applied, gate.Request(engineOff, s, false, 12f));
            Assert.IsFalse(s.IgnitionOn);
            Assert.IsFalse(gate.HasPending(12f, out _));
        }

        [Test]
        public void Gate_ConfirmationExpiresAndOtherChangesCancelIt()
        {
            var s = new AircraftSystems();
            var gate = new SystemChangeGate { ConfirmationWindowSeconds = 4f };
            var engineOff = new SystemChange(AircraftSystem.Ignition, 0);

            gate.Request(engineOff, s, false, 0f);
            Assert.AreEqual(SystemChangeGate.Outcome.AwaitingConfirmation, gate.Request(engineOff, s, false, 5f), "too late: asks again");
            Assert.IsTrue(s.IgnitionOn);

            gate.Request(new SystemChange(AircraftSystem.LandingLight, 1), s, false, 5.5f);
            Assert.AreEqual(SystemChangeGate.Outcome.AwaitingConfirmation, gate.Request(engineOff, s, false, 6f), "another change in between cancels");
            Assert.IsTrue(s.IgnitionOn);

            Assert.AreEqual(SystemChangeGate.Outcome.Refused, gate.Request(new SystemChange(AircraftSystem.ParkingBrake, 1), s, false, 7f));
            Assert.IsFalse(s.ParkingBrakeSet);
            Assert.AreEqual(SystemChangeGate.Outcome.Unchanged, gate.Request(new SystemChange(AircraftSystem.Flaps, 0), s, true, 8f));
        }

        [Test]
        public void Systems_ApplyToAnyPilotCommands()
        {
            var s = new AircraftSystems();
            var pilot = new FlightControls(0.4f, -0.2f, 0.1f, 0.7f, 0.3f, flaps: 0.9f, ignition: false);

            var c = s.ApplyTo(pilot);
            Assert.AreEqual(0.4f, c.Pitch);
            Assert.AreEqual(0.7f, c.Throttle);
            Assert.AreEqual(0.3f, c.Brake, "the pilot's own braking passes through");
            Assert.AreEqual(0f, c.Flaps, "the flap selector, not the source, decides");
            Assert.IsTrue(c.Ignition, "the ignition switch, not the source, decides");

            s.Set(AircraftSystem.Flaps, 3);
            s.Set(AircraftSystem.Ignition, false);
            s.Set(AircraftSystem.ParkingBrake, true);
            c = s.ApplyTo(new FlightInputRouter().Read(0.02f));
            Assert.AreEqual(1f, c.Flaps);
            Assert.IsFalse(c.Ignition);
            Assert.AreEqual(1f, c.Brake);
        }

        [Test]
        public void RouterWithoutSystems_KeepsTheEngineRunningAndFlapsUp()
        {
            var c = new FlightInputRouter().Read(0.02f);
            Assert.IsTrue(c.Ignition);
            Assert.AreEqual(0f, c.Flaps);
            Assert.IsTrue(FlightControls.Neutral.Ignition);
            Assert.IsTrue(new FlightControls(0f, 0f, 0f, 1f).Ignition, "existing callers keep a running engine");
        }
    }
}
