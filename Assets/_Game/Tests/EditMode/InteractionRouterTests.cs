using NUnit.Framework;
using OpenSkies.Gestures;

namespace OpenSkies.Tests
{
    public class InteractionRouterTests
    {
        static RouteContext Context(InteractionRoute requested = InteractionRoute.None, bool gestureFirst = true, bool cockpit = false,
            bool blocked = false, bool onTarget = false, bool outside = false) => new RouteContext
        {
            Requested = requested, GestureFirst = gestureFirst, CockpitView = cockpit, Blocked = blocked, OnTarget = onTarget,
            OutsideActiveDisclosure = outside,
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
        public void OutsideAnExpandedInterface_TheOpenSurfaceIsConsumed_InEveryViewAndInterface()
        {
            Assert.AreEqual(InteractionRoute.Dismiss, InteractionRouter.Decide(Context(outside: true)), "the sky: no free look under an open panel");
            Assert.AreEqual(InteractionRoute.Dismiss, InteractionRouter.Decide(Context(cockpit: true, outside: true)), "the cockpit: no switch under an open panel");
            Assert.AreEqual(InteractionRoute.Dismiss, InteractionRouter.Decide(Context(onTarget: true, outside: true)), "an explainable value: the dismissal wins");
            Assert.AreEqual(InteractionRoute.Dismiss, InteractionRouter.Decide(Context(gestureFirst: false, cockpit: true, outside: true)), "the legacy interface too");
        }

        [Test]
        public void OutsideAnExpandedInterface_HotspotsAndOverlaysKeepTheirRoutes()
        {
            Assert.AreEqual(InteractionRoute.SystemsHandle, InteractionRouter.Decide(Context(InteractionRoute.SystemsHandle, outside: true)), "another trigger replaces the cluster");
            Assert.AreEqual(InteractionRoute.HudBand, InteractionRouter.Decide(Context(InteractionRoute.HudBand, outside: true)));
            Assert.AreEqual(InteractionRoute.Instrument, InteractionRouter.Decide(Context(InteractionRoute.Instrument, outside: true)));
            Assert.AreEqual(InteractionRoute.Sheet, InteractionRouter.Decide(Context(InteractionRoute.Sheet, blocked: true, outside: true)));
            Assert.AreEqual(InteractionRoute.None, InteractionRouter.Decide(Context(blocked: true, outside: true)), "a frozen flight routes nothing; the scrims take the touches");
        }

        [Test]
        public void DismissRoute_ListensForTapsOnly()
        {
            Assert.AreEqual(GestureMask.Tap, InteractionRouter.MaskFor(InteractionRoute.Dismiss), "a drag or a hold outside an open panel does nothing at all");
            Assert.IsTrue(InteractionRouter.IsRecognized(InteractionRoute.Dismiss));
            Assert.AreEqual((int)InteractionRoute.Dismiss + 1, InteractionRouter.RouteCount);
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
