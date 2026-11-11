using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Gestures;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The disclosure coordinator as a pure state machine: registration, the active
    /// branch, replacement between clusters, nested surfaces, outside and parent taps,
    /// sticky paused interfaces, focus loss, reset, the opening and closing states and
    /// rapid repeated opening and closing. No scene, no pointers: screen points are
    /// matched by rectangles the test defines.
    /// </summary>
    public class DisclosureCoordinatorTests
    {
        sealed class Surface
        {
            public readonly string Name;
            public Rect Area;
            public bool Open;
            public int Id = DisclosureCoordinator.None;
            public readonly List<DisclosureCollapseReason> Collapses = new List<DisclosureCollapseReason>();
            readonly DisclosureCoordinator _coordinator;

            public Surface(DisclosureCoordinator coordinator, string name, Rect area, bool dismissOnOutsideTap = true, Surface parent = null)
            {
                _coordinator = coordinator;
                Name = name;
                Area = area;
                Id = coordinator.Register(name, Contains, Collapse, dismissOnOutsideTap, parent != null ? parent.Id : DisclosureCoordinator.None);
            }

            bool Contains(Vector2 p) => Open && Area.Contains(p);

            void Collapse(DisclosureCollapseReason reason)
            {
                Collapses.Add(reason);
                Open = false;
            }

            public void Show()
            {
                Open = true;
                _coordinator.Open(Id);
            }

            public void HideMyself()
            {
                Open = false;
                _coordinator.NotifyClosed(Id);
            }
        }

        static readonly Rect ArcArea = new Rect(1400f, 200f, 300f, 300f);
        static readonly Rect SubArcArea = new Rect(1300f, 500f, 400f, 150f);
        static readonly Rect PanelArea = new Rect(600f, 300f, 500f, 400f);
        static readonly Rect SheetArea = new Rect(0f, 0f, 1920f, 300f);
        static readonly Rect SheetSettingsArea = new Rect(0f, 300f, 1920f, 560f);
        static readonly Vector2 Sky = new Vector2(960f, 1000f);

        DisclosureCoordinator _c;

        [SetUp]
        public void SetUp()
        {
            _c = new DisclosureCoordinator { TransitionSeconds = 0f };
        }

        [Test]
        public void OpeningAndClosing_TrackTheBranch_AndNotifyStates()
        {
            var states = new List<(int, DisclosureState)>();
            _c.StateChanged += (id, state) => states.Add((id, state));
            var arc = new Surface(_c, "arc", ArcArea);
            Assert.IsFalse(_c.HasActive);
            Assert.IsFalse(_c.WantsOutsideTap(Sky), "nothing expanded: every tap belongs to the world");

            arc.Show();
            Assert.IsTrue(_c.IsActive(arc.Id));
            Assert.AreEqual(1, _c.Depth);
            Assert.AreEqual(arc.Id, _c.ActiveRoot);
            Assert.AreEqual(DisclosureState.Open, _c.StateOf(arc.Id), "no transition time: open at once");
            Assert.AreEqual((arc.Id, DisclosureState.Open), states[states.Count - 1]);

            _c.Close(arc.Id, DisclosureCollapseReason.Trigger);
            Assert.IsFalse(_c.IsActive(arc.Id));
            Assert.AreEqual(0, _c.Depth);
            Assert.AreEqual(DisclosureState.Closed, _c.StateOf(arc.Id));
            Assert.AreEqual(1, arc.Collapses.Count, "the owner was told to fold, once");
            Assert.AreEqual(DisclosureCollapseReason.Trigger, arc.Collapses[0], "with the reason");
            Assert.AreEqual(DisclosureCollapseReason.Trigger, _c.LastReasonOf(arc.Id));
        }

        [Test]
        public void OwnerClosingItself_LeavesTheBranch_WithoutACallback()
        {
            var panel = new Surface(_c, "panel", PanelArea);
            panel.Show();
            panel.HideMyself();
            Assert.IsFalse(_c.HasActive);
            Assert.IsEmpty(panel.Collapses, "an owner that closed itself is not told to close again");
            Assert.AreEqual(DisclosureCollapseReason.Owner, _c.LastReasonOf(panel.Id));
        }

        [Test]
        public void Toggle_OpensThenCloses_AsTheTriggerTappedAgain()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            Assert.IsTrue(_c.Toggle(arc.Id));
            Assert.IsTrue(_c.IsActive(arc.Id));
            Assert.IsFalse(_c.Toggle(arc.Id));
            Assert.IsFalse(_c.IsActive(arc.Id));
            Assert.AreEqual(DisclosureCollapseReason.Trigger, arc.Collapses[0]);
        }

        // ---- Nesting --------------------------------------------------------------------------------

        [Test]
        public void Nested_ChildUnderItsParent_ClosesWithTheParent()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var sub = new Surface(_c, "sub-arc", SubArcArea, parent: arc);
            arc.Show();
            sub.Show();
            Assert.AreEqual(2, _c.Depth);
            Assert.AreEqual(arc.Id, _c.LayerAt(0));
            Assert.AreEqual(sub.Id, _c.LayerAt(1));
            Assert.AreEqual(sub.Id, _c.Deepest);

            _c.Close(arc.Id, DisclosureCollapseReason.Trigger);
            Assert.AreEqual(0, _c.Depth);
            Assert.AreEqual(1, sub.Collapses.Count, "the child folds with its parent, deepest first");
            Assert.AreEqual(1, arc.Collapses.Count);
        }

        [Test]
        public void Nested_ChildWithoutItsParentOpen_StandsAsARoot()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var sub = new Surface(_c, "sub-arc", SubArcArea, parent: arc);
            sub.Show();
            Assert.AreEqual(sub.Id, _c.ActiveRoot, "a child whose parent is closed opens on its own");
            Assert.AreEqual(1, _c.Depth);
        }

        [Test]
        public void ReopeningAParent_FoldsItsChildren_BackToIt()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var sub = new Surface(_c, "sub-arc", SubArcArea, parent: arc);
            arc.Show();
            sub.Show();
            arc.Show();
            Assert.AreEqual(1, _c.Depth);
            Assert.IsTrue(_c.IsActive(arc.Id));
            Assert.IsFalse(_c.IsActive(sub.Id));
            Assert.AreEqual(DisclosureCollapseReason.Replaced, sub.Collapses[0]);
        }

        // ---- Switching clusters ---------------------------------------------------------------------

        [Test]
        public void OpeningAnotherCluster_ClosesThePreviousOne_WithItsChildren()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var sub = new Surface(_c, "sub-arc", SubArcArea, parent: arc);
            var panel = new Surface(_c, "panel", PanelArea);
            arc.Show();
            sub.Show();
            panel.Show();
            Assert.AreEqual(1, _c.Depth);
            Assert.AreEqual(panel.Id, _c.ActiveRoot);
            Assert.AreEqual(DisclosureCollapseReason.Replaced, arc.Collapses[0]);
            Assert.AreEqual(DisclosureCollapseReason.Replaced, sub.Collapses[0]);
            Assert.IsFalse(arc.Open);
        }

        [Test]
        public void SiblingsUnderOneParent_ReplaceEachOther_AndKeepTheParent()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var navOptions = new Surface(_c, "nav", SubArcArea, parent: arc);
            var assistOptions = new Surface(_c, "assist", SubArcArea, parent: arc);
            arc.Show();
            navOptions.Show();
            assistOptions.Show();
            Assert.AreEqual(2, _c.Depth);
            Assert.IsTrue(_c.IsActive(arc.Id));
            Assert.IsTrue(_c.IsActive(assistOptions.Id));
            Assert.IsFalse(_c.IsActive(navOptions.Id));
            Assert.IsEmpty(arc.Collapses, "the parent stays");
        }

        // ---- Outside taps ---------------------------------------------------------------------------

        [Test]
        public void TapOutsideEverything_CollapsesTheWholeBranch_AndTheTapIsWanted()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var sub = new Surface(_c, "sub-arc", SubArcArea, parent: arc);
            arc.Show();
            sub.Show();
            Assert.IsTrue(_c.WantsOutsideTap(Sky), "the press is consumed before anything underneath sees it");
            Assert.AreEqual(2, _c.OutsideTap(Sky));
            Assert.AreEqual(0, _c.Depth);
            Assert.AreEqual(DisclosureCollapseReason.OutsideTap, arc.Collapses[0]);
            Assert.AreEqual(DisclosureCollapseReason.OutsideTap, sub.Collapses[0]);
            Assert.IsFalse(_c.WantsOutsideTap(Sky), "quiet again: the next tap goes to the world");
        }

        [Test]
        public void TapInsideTheDeepestSurface_IsNotOutside()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var sub = new Surface(_c, "sub-arc", SubArcArea, parent: arc);
            arc.Show();
            sub.Show();
            Assert.IsFalse(_c.WantsOutsideTap(SubArcArea.center), "a tap on the options belongs to the options");
            Assert.AreEqual(0, _c.OutsideTap(SubArcArea.center));
            Assert.AreEqual(2, _c.Depth);
        }

        [Test]
        public void TapOnAParentWhileAChildIsOpen_ReturnsToTheParent()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var sub = new Surface(_c, "sub-arc", SubArcArea, parent: arc);
            arc.Show();
            sub.Show();
            Assert.IsTrue(_c.WantsOutsideTap(ArcArea.center));
            Assert.AreEqual(1, _c.OutsideTap(ArcArea.center));
            Assert.IsTrue(_c.IsActive(arc.Id), "the parent stays open");
            Assert.IsFalse(_c.IsActive(sub.Id));
            Assert.AreEqual(DisclosureCollapseReason.ParentTap, sub.Collapses[0]);
            Assert.IsFalse(_c.WantsOutsideTap(ArcArea.center), "now the tap is inside the deepest surface");
        }

        [Test]
        public void ASurfaceWithoutAHitTest_TreatsEveryTapAsOutside()
        {
            int id = _c.Register("explanation", null, null);
            _c.Open(id);
            Assert.IsTrue(_c.WantsOutsideTap(Sky));
            Assert.AreEqual(1, _c.OutsideTap(Sky));
            Assert.IsFalse(_c.HasActive);
        }

        // ---- Paused interfaces ----------------------------------------------------------------------

        [Test]
        public void PausedInterface_StaysThroughOutsideTaps_ItsExpandedSettingsCollapseBackToIt()
        {
            var sheet = new Surface(_c, "pause sheet", SheetArea, dismissOnOutsideTap: false);
            var settings = new Surface(_c, "sheet settings", SheetSettingsArea, parent: sheet);
            sheet.Show();
            Assert.IsFalse(_c.WantsOutsideTap(Sky), "nothing to dismiss: resuming is a deliberate action");
            Assert.AreEqual(0, _c.OutsideTap(Sky));
            Assert.IsTrue(sheet.Open);

            settings.Show();
            Assert.IsTrue(_c.WantsOutsideTap(Sky));
            Assert.AreEqual(1, _c.OutsideTap(Sky), "only the settings fold");
            Assert.IsTrue(_c.IsActive(sheet.Id));
            Assert.IsFalse(_c.IsActive(settings.Id));
            Assert.IsEmpty(sheet.Collapses, "the paused interface was never told to go");

            settings.Show();
            Assert.AreEqual(1, _c.OutsideTap(SheetArea.center), "a tap on the sheet itself: back to the sheet");
            Assert.IsTrue(_c.IsActive(sheet.Id));
            Assert.AreEqual(0, _c.CollapseDismissable(DisclosureCollapseReason.Blocked), "nothing dismissable is left above the sheet");
            Assert.IsTrue(_c.IsActive(sheet.Id));
        }

        [Test]
        public void CollapseAll_ClosesEvenAPausedInterface_AndStopsAtNothing()
        {
            var sheet = new Surface(_c, "pause sheet", SheetArea, dismissOnOutsideTap: false);
            var settings = new Surface(_c, "sheet settings", SheetSettingsArea, parent: sheet);
            sheet.Show();
            settings.Show();
            Assert.AreEqual(2, _c.CollapseAll(DisclosureCollapseReason.Reset));
            Assert.AreEqual(0, _c.Depth);
            Assert.AreEqual(DisclosureCollapseReason.Reset, sheet.Collapses[0]);
            Assert.AreEqual(DisclosureCollapseReason.Reset, settings.Collapses[0]);
        }

        // ---- Focus loss and reset -------------------------------------------------------------------

        [Test]
        public void FocusLoss_FoldsFlightTransients_ButLeavesAPausedInterfaceAlone()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var sub = new Surface(_c, "sub-arc", SubArcArea, parent: arc);
            arc.Show();
            sub.Show();
            _c.OnFocusLost();
            Assert.AreEqual(0, _c.Depth);
            Assert.AreEqual(DisclosureCollapseReason.FocusLost, arc.Collapses[0]);
            Assert.AreEqual(DisclosureCollapseReason.FocusLost, sub.Collapses[0]);

            var sheet = new Surface(_c, "pause sheet", SheetArea, dismissOnOutsideTap: false);
            var settings = new Surface(_c, "sheet settings", SheetSettingsArea, parent: sheet);
            sheet.Show();
            settings.Show();
            _c.OnFocusLost();
            Assert.AreEqual(2, _c.Depth, "coming back from another app finds the paused interface as it was");
            Assert.IsEmpty(sheet.Collapses);
            Assert.IsEmpty(settings.Collapses);
        }

        [Test]
        public void Reset_CollapsesEverything()
        {
            var arc = new Surface(_c, "arc", ArcArea);
            var panel = new Surface(_c, "panel", PanelArea);
            arc.Show();
            panel.Show();
            _c.Reset();
            Assert.AreEqual(0, _c.Depth);
            Assert.AreEqual(DisclosureCollapseReason.Replaced, arc.Collapses[0], "the arc had already gone when the panel opened");
            Assert.AreEqual(DisclosureCollapseReason.Reset, panel.Collapses[0]);
        }

        // ---- States and time ------------------------------------------------------------------------

        [Test]
        public void States_SettleAfterTheTransitionTime()
        {
            _c.TransitionSeconds = 0.2f;
            var arc = new Surface(_c, "arc", ArcArea);
            arc.Show();
            Assert.AreEqual(DisclosureState.Opening, _c.StateOf(arc.Id));
            Assert.IsTrue(_c.IsActive(arc.Id), "opening counts as active: a tap outside already collapses it");
            _c.Tick(0.1f);
            Assert.AreEqual(DisclosureState.Opening, _c.StateOf(arc.Id));
            _c.Tick(0.11f);
            Assert.AreEqual(DisclosureState.Open, _c.StateOf(arc.Id));

            _c.Close(arc.Id, DisclosureCollapseReason.Trigger);
            Assert.AreEqual(DisclosureState.Closing, _c.StateOf(arc.Id));
            Assert.IsFalse(_c.IsActive(arc.Id), "closing no longer takes taps");
            _c.Tick(0.25f);
            Assert.AreEqual(DisclosureState.Closed, _c.StateOf(arc.Id));

            arc.Show();
            _c.NotifyOpened(arc.Id);
            Assert.AreEqual(DisclosureState.Open, _c.StateOf(arc.Id), "an owner can settle the state early (reduced motion)");
            _c.Tick(float.NaN);
            _c.Tick(-1f);
            Assert.AreEqual(DisclosureState.Open, _c.StateOf(arc.Id), "bad time steps do nothing");
        }

        [Test]
        public void RapidRepeatedOpeningAndClosing_NeverLeavesAStaleBranch()
        {
            _c.TransitionSeconds = 0.2f;
            var arc = new Surface(_c, "arc", ArcArea);
            for (int i = 0; i < 20; i++)
            {
                arc.Show();
                Assert.AreEqual(1, _c.Depth);
                Assert.AreEqual(1, _c.OutsideTap(Sky));
                Assert.AreEqual(0, _c.Depth);
                Assert.AreEqual(0, _c.OutsideTap(Sky), "a second tap on nothing finds nothing to close");
            }
            Assert.AreEqual(20, arc.Collapses.Count);
            arc.Show();
            arc.Show();
            Assert.AreEqual(1, _c.Depth, "opening twice is once");
            _c.Tick(1f);
            Assert.AreEqual(DisclosureState.Open, _c.StateOf(arc.Id));
        }

        // ---- Registration ---------------------------------------------------------------------------

        [Test]
        public void Registration_HasFixedCapacity_AndUnregisteringFreesTheSlot()
        {
            var ids = new List<int>();
            for (int i = 0; i < DisclosureCoordinator.Capacity; i++) ids.Add(_c.Register("s" + i, null, null));
            Assert.AreEqual(DisclosureCoordinator.None, _c.Register("one too many", null, null));
            _c.Open(ids[3]);
            _c.Unregister(ids[3]);
            Assert.IsFalse(_c.IsRegistered(ids[3]));
            Assert.IsFalse(_c.HasActive, "an unregistered surface leaves the branch");
            Assert.AreNotEqual(DisclosureCoordinator.None, _c.Register("fits again", null, null));
            Assert.IsFalse(_c.Open(DisclosureCoordinator.None));
            Assert.IsFalse(_c.Open(99));
            Assert.AreEqual(DisclosureState.Closed, _c.StateOf(99));
        }

        [Test]
        public void SetParent_RehomesASurface_AndUnregisteringAParentOrphansItsChildren()
        {
            var card = new Surface(_c, "pause card", PanelArea, dismissOnOutsideTap: false);
            var screen = new Surface(_c, "settings", PanelArea);
            _c.SetParent(screen.Id, card.Id);
            Assert.AreEqual(card.Id, _c.ParentOf(screen.Id));
            card.Show();
            screen.Show();
            Assert.AreEqual(2, _c.Depth);
            Assert.AreEqual(1, _c.OutsideTap(Sky), "outside the settings: back to the card, which stays");
            Assert.IsTrue(_c.IsActive(card.Id));

            _c.SetParent(screen.Id, DisclosureCoordinator.None);
            screen.Show();
            Assert.AreEqual(1, _c.Depth, "as a root it replaced the card");
            Assert.AreEqual(screen.Id, _c.ActiveRoot);

            _c.SetParent(screen.Id, card.Id);
            _c.Unregister(card.Id);
            Assert.AreEqual(DisclosureCoordinator.None, _c.ParentOf(screen.Id));
            _c.Clear();
            Assert.IsFalse(_c.IsRegistered(screen.Id));
            Assert.AreEqual(0, _c.Depth);
        }

        [Test]
        public void ACollapseCallbackMayOpenSomethingElse_WithoutCorruptingTheBranch()
        {
            var panel = new Surface(_c, "panel", PanelArea);
            int arc = _c.Register("arc", p => false, _ => panel.Show());
            _c.Open(arc);
            Assert.AreEqual(1, _c.OutsideTap(Sky));
            Assert.AreEqual(1, _c.Depth);
            Assert.AreEqual(panel.Id, _c.ActiveRoot, "the surface opened from the callback is the branch now");
            Assert.IsFalse(_c.IsActive(arc));
        }
    }
}
