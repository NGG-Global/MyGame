using NUnit.Framework;
using OpenSkies.FlightInput.TouchControls;

namespace OpenSkies.Tests
{
    public class PointerOwnershipTests
    {
        sealed class Owner : IPointerOwner
        {
            public int Revoked;
            public int LastRevoked = -1;
            public Owner(string name) => OwnerName = name;
            public string OwnerName { get; }
            public void OnPointerRevoked(int pointerId) { Revoked++; LastRevoked = pointerId; }
        }

        [Test]
        public void PointerKeepsFirstOwnerUntilReleased()
        {
            var table = new PointerOwnership();
            var joystick = new Owner("Joystick");
            var throttle = new Owner("Throttle");

            Assert.IsTrue(table.TryClaim(1, joystick));
            Assert.IsFalse(table.TryClaim(1, throttle), "a second control cannot take a pointer mid-gesture");
            Assert.IsTrue(table.TryClaim(1, joystick), "re-claim by the owner is harmless");
            Assert.AreSame(joystick, table.OwnerOf(1));

            Assert.IsFalse(table.Release(1, throttle), "only the owner can release");
            Assert.IsTrue(table.IsOwned(1));
            Assert.IsTrue(table.Release(1, joystick));
            Assert.IsFalse(table.IsOwned(1));
            Assert.IsTrue(table.TryClaim(1, throttle), "after release the id is free again");
        }

        [Test]
        public void SimultaneousPointersHaveIndependentOwners()
        {
            var table = new PointerOwnership();
            var joystick = new Owner("Joystick");
            var throttle = new Owner("Throttle");
            Assert.IsTrue(table.TryClaim(10, joystick, PointerKind.Touch, 0));
            Assert.IsTrue(table.TryClaim(11, throttle, PointerKind.Touch, 1));
            Assert.AreEqual(2, table.Count);
            Assert.IsTrue(table.IsOwnedBy(10, joystick));
            Assert.IsTrue(table.IsOwnedBy(11, throttle));

            table.Release(10, joystick);
            Assert.IsTrue(table.IsOwnedBy(11, throttle), "releasing one finger leaves the other untouched");
        }

        [Test]
        public void RevokeAll_NotifiesEveryOwnerAndClears()
        {
            var table = new PointerOwnership();
            var joystick = new Owner("Joystick");
            var brake = new Owner("Brake");
            table.TryClaim(1, joystick);
            table.TryClaim(2, brake);
            table.TryClaim(3, brake);

            table.RevokeAll();

            Assert.AreEqual(0, table.Count);
            Assert.AreEqual(1, joystick.Revoked);
            Assert.AreEqual(2, brake.Revoked);
        }

        [Test]
        public void RevokeAllFrom_OnlyAffectsThatOwner()
        {
            var table = new PointerOwnership();
            var joystick = new Owner("Joystick");
            var brake = new Owner("Brake");
            table.TryClaim(1, brake);
            table.TryClaim(2, joystick);
            table.TryClaim(3, brake);

            table.RevokeAllFrom(brake);

            Assert.AreEqual(1, table.Count);
            Assert.IsTrue(table.IsOwnedBy(2, joystick));
            Assert.AreEqual(0, joystick.Revoked);
            Assert.AreEqual(2, brake.Revoked);
        }

        [Test]
        public void TableIsBoundedAndRejectsNullOwners()
        {
            var table = new PointerOwnership();
            var owner = new Owner("X");
            Assert.IsFalse(table.TryClaim(1, null));
            for (int i = 0; i < PointerOwnership.Capacity; i++) Assert.IsTrue(table.TryClaim(100 + i, owner));
            Assert.IsFalse(table.TryClaim(999, owner));
        }
    }
}
