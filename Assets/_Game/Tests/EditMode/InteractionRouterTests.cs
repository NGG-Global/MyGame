using NUnit.Framework;
using OpenSkies.Gestures;

namespace OpenSkies.Tests
{
    public class InteractionRouterTests
    {
        static RouteContext Context(InteractionRoute requested = InteractionRoute.None, bool gestureFirst = true, bool cockpit = false,
            bool blocked = false, bool onTarget = false) => new RouteContext
        {
            Requested = requested, GestureFirst = gestureFirst, CockpitView = cockpit, Blocked = blocked, OnTarget = onTarget,
        };

        [Test]
        public void OpenSurface_GoesToTheWorldInChase_AndToTheCockpitInCockpitView()
        {
            Assert.AreEqual(InteractionRoute.World, InteractionRouter.Decide(Context()));
            Assert.AreEqual(InteractionRoute.Cockpit, InteractionRouter.Decide(Context(cockpit: true)));
        }

        [Test]
        public void Hotspots_KeepTheirRoute_OnlyInTheGestureFirstInterface()
        {
            Assert.AreEqual(InteractionRoute.SystemsHandle, InteractionRouter.Decide(Context(InteractionRoute.SystemsHandle)));
            Assert.AreEqual(InteractionRoute.HudBand, InteractionRouter.Decide(Context(InteractionRoute.HudBand, cockpit: true)));
            Assert.AreEqual(InteractionRoute.None, InteractionRouter.Decide(Context(InteractionRoute.SystemsHandle, gestureFirst: false)));
            Assert.AreEqual(InteractionRoute.None, InteractionRouter.Decide(Context(InteractionRoute.HudBand, gestureFirst: false)));
        }

        [Test]
        public void Legacy_OnlyTheCockpitRouteExists()
        {
            Assert.AreEqual(InteractionRoute.None, InteractionRouter.Decide(Context(gestureFirst: false)));
            Assert.AreEqual(InteractionRoute.Cockpit, InteractionRouter.Decide(Context(gestureFirst: false, cockpit: true)));
            Assert.AreEqual(InteractionRoute.Cockpit, InteractionRouter.Decide(Context(gestureFirst: false, cockpit: true, onTarget: true)));
        }

        [Test]
        public void Targets_WinOverTheWorldAndTheCockpit()
        {
            Assert.AreEqual(InteractionRoute.Target, InteractionRouter.Decide(Context(onTarget: true)));
            Assert.AreEqual(InteractionRoute.Target, InteractionRouter.Decide(Context(cockpit: true, onTarget: true)));
        }

        [Test]
        public void Blocked_RoutesNothing()
        {
            Assert.AreEqual(InteractionRoute.None, InteractionRouter.Decide(Context(blocked: true)));
            Assert.AreEqual(InteractionRoute.None, InteractionRouter.Decide(Context(InteractionRoute.SystemsHandle, blocked: true)));
            Assert.AreEqual(InteractionRoute.None, InteractionRouter.Decide(Context(cockpit: true, blocked: true)));
        }

        [Test]
        public void Masks_MatchTheVocabulary()
        {
            Assert.IsTrue((InteractionRouter.MaskFor(InteractionRoute.SystemsHandle) & GestureMask.Pull) != 0);
            Assert.IsTrue((InteractionRouter.MaskFor(InteractionRoute.SystemsHandle) & GestureMask.Dwell) != 0);
            Assert.IsTrue((InteractionRouter.MaskFor(InteractionRoute.HudBand) & GestureMask.LongPress) != 0);
            Assert.IsTrue((InteractionRouter.MaskFor(InteractionRoute.World) & GestureMask.Pinch) != 0);
            Assert.IsTrue((InteractionRouter.MaskFor(InteractionRoute.World) & GestureMask.DoubleTap) != 0);
            Assert.AreEqual(GestureMask.None, InteractionRouter.MaskFor(InteractionRoute.Cockpit), "the cockpit recognises its own gestures");
            Assert.IsFalse(InteractionRouter.IsRecognized(InteractionRoute.Cockpit));
            Assert.IsTrue(InteractionRouter.IsRecognized(InteractionRoute.Target));
        }
    }
}
