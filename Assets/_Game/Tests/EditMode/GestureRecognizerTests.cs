using System.Collections.Generic;
using NUnit.Framework;
using OpenSkies.Gestures;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>
    /// The gesture state machine driven by synthetic pointer samples with explicit
    /// times (screen height 1000 px, so 0.02 = 20 px). Proves timing, thresholds,
    /// arming, completion and cancellation; not how a gesture feels on a device.
    /// </summary>
    public class GestureRecognizerTests
    {
        const float H = 1000f;
        static readonly Vector2 O = new Vector2(500f, 300f);

        static GestureRecognizer New(GestureMask mask, GestureSettings settings = null)
        {
            var r = new GestureRecognizer(settings ?? new GestureSettings(), mask);
            r.SetScreenHeight(H);
            return r;
        }

        static List<GestureEvent> Drain(GestureRecognizer r)
        {
            var list = new List<GestureEvent>();
            while (r.TryDequeue(out var e)) list.Add(e);
            return list;
        }

        static bool Has(List<GestureEvent> events, GestureKind kind, GestureEventType type)
        {
            foreach (var e in events)
                if (e.Kind == kind && e.Type == type) return true;
            return false;
        }

        static GestureEvent Find(List<GestureEvent> events, GestureKind kind, GestureEventType type)
        {
            foreach (var e in events)
                if (e.Kind == kind && e.Type == type) return e;
            Assert.Fail($"no {kind} {type} event");
            return default;
        }

        // ---- Taps ---------------------------------------------------------------------------------

        [Test]
        public void Tap_QuickReleaseWithoutMovement()
        {
            var r = New(GestureMask.Tap);
            r.Press(1, O, 0f);
            Assert.AreEqual(GestureState.Candidate, r.State);
            r.Move(1, O + new Vector2(10f, 5f), 0.05f); // within the 20 px slop
            r.Release(1, 0.2f);
            var events = Drain(r);
            Assert.IsTrue(Has(events, GestureKind.Tap, GestureEventType.Completed));
            Assert.AreEqual(GestureState.Idle, r.State, "ready for the next finger");
            Assert.IsFalse(r.IsTracking);
        }

        [Test]
        public void Tap_TooSlowIsIncomplete_TooFarIsMovedOut()
        {
            var r = New(GestureMask.Tap);
            r.Press(1, O, 0f);
            r.Release(1, 0.5f);
            var slow = Drain(r);
            Assert.IsFalse(Has(slow, GestureKind.Tap, GestureEventType.Completed));
            Assert.AreEqual(CancelReason.Incomplete, Find(slow, GestureKind.None, GestureEventType.Cancelled).Reason);

            r.Press(2, O, 1f);
            r.Move(2, O + new Vector2(40f, 0f), 1.05f);
            var moved = Drain(r);
            Assert.AreEqual(CancelReason.MovedOut, Find(moved, GestureKind.None, GestureEventType.Cancelled).Reason);
            r.Release(2, 1.1f);
            Assert.IsFalse(Has(Drain(r), GestureKind.Tap, GestureEventType.Completed), "a cancelled press never taps on release");
        }

        [Test]
        public void DoubleTap_WithinTimeAndDistance_OtherwiseTwoTaps()
        {
            var r = New(GestureMask.Tap | GestureMask.DoubleTap);
            r.Press(1, O, 0f); r.Release(1, 0.1f);
            r.Press(2, O + new Vector2(20f, 0f), 0.25f); r.Release(2, 0.3f);
            var events = Drain(r);
            Assert.IsTrue(Has(events, GestureKind.Tap, GestureEventType.Completed), "the first tap is reported at once");
            Assert.IsTrue(Has(events, GestureKind.DoubleTap, GestureEventType.Completed));

            r.Press(3, O, 2f); r.Release(3, 2.1f);
            r.Press(4, O, 2.6f); r.Release(4, 2.7f); // 0.5 s after the previous tap: too late
            events = Drain(r);
            Assert.IsFalse(Has(events, GestureKind.DoubleTap, GestureEventType.Completed));
            int taps = 0;
            foreach (var e in events) if (e.Kind == GestureKind.Tap && e.Type == GestureEventType.Completed) taps++;
            Assert.AreEqual(2, taps);

            r.Press(5, O, 4f); r.Release(5, 4.1f);
            r.Press(6, O + new Vector2(200f, 0f), 4.2f); r.Release(6, 4.3f); // far away
            Assert.IsFalse(Has(Drain(r), GestureKind.DoubleTap, GestureEventType.Completed));
        }

        [Test]
        public void RapidTaps_NeverLeaveStateBehind()
        {
            var r = New(GestureMask.Tap | GestureMask.DoubleTap | GestureMask.LongPress);
            float t = 0f;
            for (int i = 0; i < 20; i++)
            {
                r.Press(i, O + new Vector2(i * 3f, 0f), t);
                r.Tick(t + 0.03f);
                r.Release(i, t + 0.06f);
                t += 0.08f;
                Assert.AreEqual(GestureState.Idle, r.State, "tap " + i);
            }
            var events = Drain(r);
            Assert.IsTrue(Has(events, GestureKind.Tap, GestureEventType.Completed));
            Assert.IsFalse(Has(events, GestureKind.LongPress, GestureEventType.Began));
        }

        // ---- Long press ---------------------------------------------------------------------------

        [Test]
        public void LongPress_ArmsAfterTheThreshold_CompletesOnRelease()
        {
            var r = New(GestureMask.Tap | GestureMask.LongPress);
            r.Press(1, O, 0f);
            r.Tick(0.3f);
            Assert.AreEqual(GestureState.Candidate, r.State);
            Assert.That(r.Progress, Is.InRange(0.6f, 0.7f), "hold progress for a visual ring");
            r.Tick(0.46f);
            Assert.AreEqual(GestureState.Armed, r.State);
            Assert.AreEqual(GestureKind.LongPress, r.Kind);
            var events = Drain(r);
            Assert.IsTrue(Has(events, GestureKind.LongPress, GestureEventType.Began));
            Assert.IsTrue(Has(events, GestureKind.LongPress, GestureEventType.Armed));

            r.Release(1, 1.2f);
            Assert.IsTrue(Has(Drain(r), GestureKind.LongPress, GestureEventType.Completed));
        }

        [Test]
        public void LongPress_IsCancelledByMovement_BeforeAndAfterArming()
        {
            var r = New(GestureMask.LongPress);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, 30f), 0.2f); // past the slop before the threshold
            r.Tick(0.6f);
            Assert.AreNotEqual(GestureKind.LongPress, r.Kind);
            r.Release(1, 0.7f);
            Assert.IsFalse(Has(Drain(r), GestureKind.LongPress, GestureEventType.Completed));

            r.Press(2, O, 1f);
            r.Tick(1.5f);
            Assert.AreEqual(GestureState.Armed, r.State);
            r.Move(2, O + new Vector2(0f, 30f), 1.6f);
            Assert.AreEqual(CancelReason.MovedOut, Find(Drain(r), GestureKind.LongPress, GestureEventType.Cancelled).Reason);
            r.Release(2, 1.7f);
            Assert.IsFalse(Has(Drain(r), GestureKind.LongPress, GestureEventType.Completed));
        }

        [Test]
        public void LongPress_ThatStartsMoving_BecomesThePull()
        {
            var r = New(GestureMask.Pull | GestureMask.LongPress | GestureMask.Tap);
            r.Press(1, O, 0f);
            r.Tick(0.6f);
            Assert.AreEqual(GestureKind.LongPress, r.Kind);
            Drain(r);
            r.Move(1, O + new Vector2(0f, 60f), 0.7f);
            var events = Drain(r);
            Assert.AreEqual(CancelReason.MovedOut, Find(events, GestureKind.LongPress, GestureEventType.Cancelled).Reason);
            Assert.IsTrue(Has(events, GestureKind.Pull, GestureEventType.Began), "a held handle that is then pulled unfolds the arc");
            Assert.AreEqual(GestureState.Peek, r.State);
            r.Move(1, O + new Vector2(0f, 120f), 0.8f);
            r.Release(1, 0.9f);
            Assert.IsTrue(Has(Drain(r), GestureKind.Pull, GestureEventType.Completed));
        }

        // ---- Pull ---------------------------------------------------------------------------------

        [Test]
        public void Pull_PeeksThenRevealsThenArms_ByDistance()
        {
            var r = New(GestureMask.Pull);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, 15f), 0.05f); // below the 20 px peek distance
            Assert.AreEqual(GestureState.Candidate, r.State);
            r.Move(1, O + new Vector2(0f, 30f), 0.1f);
            Assert.AreEqual(GestureState.Peek, r.State);
            Assert.AreEqual(GestureKind.Pull, r.Kind);
            Assert.That(r.Progress, Is.InRange(0.1f, 0.2f));
            r.Move(1, O + new Vector2(0f, 95f), 0.2f); // past the 90 px reveal distance
            Assert.AreEqual(GestureState.Armed, r.State, "no target arming: fully revealed means armed");
            Assert.AreEqual(1f, r.Progress, 1e-5f);
            var events = Drain(r);
            Assert.IsTrue(Has(events, GestureKind.Pull, GestureEventType.Began));
            Assert.IsTrue(Has(events, GestureKind.Pull, GestureEventType.Armed));

            r.Release(1, 0.3f);
            var completed = Find(Drain(r), GestureKind.Pull, GestureEventType.Completed);
            Assert.AreEqual(new Vector2(0f, 95f), completed.Delta);
        }

        [Test]
        public void Pull_ReleasedBeforeArming_Cancels()
        {
            var r = New(GestureMask.Pull);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, 50f), 0.1f);
            Assert.AreEqual(GestureState.Peek, r.State);
            r.Release(1, 0.2f);
            var events = Drain(r);
            Assert.IsFalse(Has(events, GestureKind.Pull, GestureEventType.Completed));
            Assert.AreEqual(CancelReason.ReleasedUnarmed, Find(events, GestureKind.Pull, GestureEventType.Cancelled).Reason);
        }

        [Test]
        public void Pull_HysteresisAtTheRevealEdge()
        {
            var r = New(GestureMask.Pull);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, 91f), 0.1f);
            Assert.AreEqual(GestureState.Armed, r.State);
            r.Move(1, O + new Vector2(0f, 85f), 0.2f); // back 6 px: inside the 12 px hysteresis band
            Assert.AreEqual(GestureState.Armed, r.State, "a finger resting on the edge does not flicker");
            r.Move(1, O + new Vector2(0f, 70f), 0.3f);
            Assert.AreEqual(GestureState.Peek, r.State);
            Assert.IsTrue(Has(Drain(r), GestureKind.Pull, GestureEventType.Disarmed));
        }

        [Test]
        public void Pull_TargetArming_DecidesCompletion_NotDistance()
        {
            var r = New(GestureMask.Pull);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, 200f), 0.1f);
            r.SetTargetArmed(false); // revealed, but the finger is between segments
            Assert.AreEqual(GestureState.ActiveManipulation, r.State);
            Drain(r);
            r.SetTargetArmed(true);
            Assert.AreEqual(GestureState.Armed, r.State);
            Assert.IsTrue(Has(Drain(r), GestureKind.Pull, GestureEventType.Armed));
            r.SetTargetArmed(false);
            Assert.IsTrue(Has(Drain(r), GestureKind.Pull, GestureEventType.Disarmed));
            r.Release(1, 0.3f);
            Assert.AreEqual(CancelReason.ReleasedUnarmed, Find(Drain(r), GestureKind.Pull, GestureEventType.Cancelled).Reason);

            r.Press(2, O, 1f);
            r.Move(2, O + new Vector2(0f, 40f), 1.1f);
            r.SetTargetArmed(true); // a target under the finger arms even before the full reveal
            Assert.AreEqual(GestureState.Armed, r.State);
            r.Release(2, 1.2f);
            Assert.IsTrue(Has(Drain(r), GestureKind.Pull, GestureEventType.Completed));
        }

        [Test]
        public void Pull_ConsumerCancel_LeavesThePointerInert()
        {
            var r = New(GestureMask.Pull | GestureMask.Tap);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, 100f), 0.1f);
            r.Cancel(CancelReason.Interrupted);
            Assert.AreEqual(CancelReason.Interrupted, Find(Drain(r), GestureKind.Pull, GestureEventType.Cancelled).Reason);
            r.Move(1, O + new Vector2(0f, 150f), 0.2f);
            r.Release(1, 0.25f);
            var events = Drain(r);
            Assert.AreEqual(0, events.Count, "nothing more from that finger");
            Assert.AreEqual(GestureState.Idle, r.State);
        }

        // ---- Dwell --------------------------------------------------------------------------------

        [Test]
        public void Dwell_WhenTheFingerRestsDuringAPull_EndsWhenItMoves()
        {
            var r = New(GestureMask.Pull | GestureMask.Dwell);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, 100f), 0.1f);
            r.Tick(0.4f);
            Assert.IsFalse(r.IsDwelling);
            r.Move(1, O + new Vector2(5f, 105f), 0.45f); // within the dwell slop: still resting
            r.Tick(0.65f);
            Assert.IsTrue(r.IsDwelling, "0.5 s at rest, counted from the last real movement");
            Assert.AreEqual(1, r.DwellCount);
            Drain(r);
            r.Move(1, O + new Vector2(0f, 140f), 0.7f);
            Assert.IsFalse(r.IsDwelling);
            Assert.IsTrue(Has(Drain(r), GestureKind.Dwell, GestureEventType.DwellEnded));
            r.Release(1, 0.8f);
            Assert.IsTrue(Has(Drain(r), GestureKind.Pull, GestureEventType.Completed), "a dwell never stops the pull completing");
        }

        [Test]
        public void Dwell_NotRecognised_WithoutMovementFirst()
        {
            var r = New(GestureMask.Pull | GestureMask.Dwell);
            r.Press(1, O, 0f);
            r.Tick(2f);
            Assert.IsFalse(r.IsDwelling, "a still finger on the handle is a candidate, not a dwell");
        }

        // ---- Drag and swipe -----------------------------------------------------------------------

        [Test]
        public void Drag_ReportsStepsAndCompletes()
        {
            var r = New(GestureMask.Drag);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(10f, 0f), 0.02f); // below the threshold: nothing yet
            Assert.AreEqual(0, Drain(r).Count);
            r.Move(1, O + new Vector2(30f, 0f), 0.04f);
            var events = Drain(r);
            Assert.IsTrue(Has(events, GestureKind.Drag, GestureEventType.Began));
            Assert.AreEqual(new Vector2(30f, 0f), Find(events, GestureKind.Drag, GestureEventType.Changed).Step, "the whole movement so far: no dead jump");
            r.Move(1, O + new Vector2(45f, 0f), 0.06f);
            Assert.AreEqual(new Vector2(15f, 0f), Find(Drain(r), GestureKind.Drag, GestureEventType.Changed).Step);
            r.Release(1, 0.08f);
            Assert.IsTrue(Has(Drain(r), GestureKind.Drag, GestureEventType.Completed));
        }

        [Test]
        public void Swipe_NeedsDistanceSpeedAndDirection()
        {
            var r = New(GestureMask.Drag | GestureMask.Swipe);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, -60f), 0.05f);
            r.Move(1, O + new Vector2(0f, -120f), 0.1f);
            r.Release(1, 0.12f);
            var swipe = Find(Drain(r), GestureKind.Swipe, GestureEventType.Completed);
            Assert.AreEqual(SwipeDirection.Down, swipe.Direction);

            r.Press(2, O, 1f);
            r.Move(2, O + new Vector2(0f, -120f), 2f); // slow
            r.Release(2, 2.1f);
            Assert.IsFalse(Has(Drain(r), GestureKind.Swipe, GestureEventType.Completed));

            r.Press(3, O, 3f);
            r.Move(3, O + new Vector2(90f, 90f), 3.08f); // diagonal: no direction
            r.Release(3, 3.1f);
            Assert.IsFalse(Has(Drain(r), GestureKind.Swipe, GestureEventType.Completed));
        }

        [Test]
        public void Direction_Classification()
        {
            Assert.AreEqual(SwipeDirection.Up, GestureMath.Direction(new Vector2(10f, 100f), 35f));
            Assert.AreEqual(SwipeDirection.Down, GestureMath.Direction(new Vector2(-20f, -100f), 35f));
            Assert.AreEqual(SwipeDirection.Left, GestureMath.Direction(new Vector2(-100f, 30f), 35f));
            Assert.AreEqual(SwipeDirection.Right, GestureMath.Direction(new Vector2(100f, -30f), 35f));
            Assert.AreEqual(SwipeDirection.None, GestureMath.Direction(new Vector2(100f, 100f), 35f));
            Assert.AreEqual(SwipeDirection.None, GestureMath.Direction(Vector2.zero, 35f));
        }

        // ---- Two fingers --------------------------------------------------------------------------

        [Test]
        public void Pinch_ReportsDistanceChange_AndEndsWhenAFingerLifts()
        {
            var r = New(GestureMask.Drag | GestureMask.Pinch | GestureMask.Tap);
            r.Press(1, new Vector2(400f, 300f), 0f);
            r.Press(2, new Vector2(600f, 300f), 0.05f);
            Assert.AreEqual(2, r.PointerCount);
            r.Move(2, new Vector2(620f, 300f), 0.1f); // +20 px = 0.02 of the height: past the 0.012 minimum
            var events = Drain(r);
            Assert.IsTrue(Has(events, GestureKind.Pinch, GestureEventType.Began));
            Assert.AreEqual(0.02f, Find(events, GestureKind.Pinch, GestureEventType.Changed).PinchDelta, 1e-5f);
            r.Move(1, new Vector2(390f, 300f), 0.15f);
            Assert.AreEqual(0.01f, Find(Drain(r), GestureKind.Pinch, GestureEventType.Changed).PinchDelta, 1e-5f);

            r.Release(2, 0.3f);
            Assert.IsTrue(Has(Drain(r), GestureKind.Pinch, GestureEventType.Completed));
            r.Move(1, new Vector2(300f, 300f), 0.35f);
            r.Release(1, 0.4f);
            Assert.AreEqual(0, Drain(r).Count, "the finger left over from a pinch neither drags nor taps");
        }

        [Test]
        public void TwoFingerTap_BothQuick_NoMovement()
        {
            var r = New(GestureMask.Tap | GestureMask.TwoFingerTap | GestureMask.Pinch);
            r.Press(1, new Vector2(400f, 300f), 0f);
            r.Press(2, new Vector2(600f, 300f), 0.08f);
            r.Release(1, 0.2f);
            var events = Drain(r);
            Assert.IsTrue(Has(events, GestureKind.TwoFingerTap, GestureEventType.Completed));
            Assert.IsFalse(Has(events, GestureKind.Tap, GestureEventType.Completed));
            r.Release(2, 0.25f);
            Assert.AreEqual(0, Drain(r).Count);

            // The second finger lands too late to be a two-finger tap.
            r.Press(3, new Vector2(400f, 300f), 1f);
            r.Press(4, new Vector2(600f, 300f), 1.3f);
            r.Release(3, 1.35f);
            r.Release(4, 1.4f);
            Assert.IsFalse(Has(Drain(r), GestureKind.TwoFingerTap, GestureEventType.Completed));
        }

        [Test]
        public void ThirdFinger_IsRefused()
        {
            var r = New(GestureMask.Drag | GestureMask.Pinch);
            Assert.IsTrue(r.Press(1, O, 0f));
            Assert.IsTrue(r.Press(2, O + new Vector2(100f, 0f), 0.05f));
            Assert.IsFalse(r.Press(3, O + new Vector2(0f, 100f), 0.1f));
            Assert.AreEqual(2, r.PointerCount);
        }

        [Test]
        public void SecondFinger_NotAccepted_WithoutTwoFingerGestures()
        {
            var r = New(GestureMask.Pull);
            Assert.IsTrue(r.Press(1, O, 0f));
            Assert.IsFalse(r.Press(2, O + new Vector2(100f, 0f), 0.05f));
        }

        // ---- Cancellation -------------------------------------------------------------------------

        [Test]
        public void Revoke_CancelsWhateverWasHappening()
        {
            var r = New(GestureMask.Pull | GestureMask.Dwell);
            r.Press(1, O, 0f);
            r.Move(1, O + new Vector2(0f, 100f), 0.1f);
            Assert.AreEqual(GestureState.Armed, r.State);
            r.Revoke(1);
            var events = Drain(r);
            Assert.AreEqual(CancelReason.Revoked, Find(events, GestureKind.Pull, GestureEventType.Cancelled).Reason);
            Assert.IsFalse(Has(events, GestureKind.Pull, GestureEventType.Completed));
            Assert.AreEqual(GestureState.Idle, r.State);
            Assert.IsFalse(r.IsTracking);

            r.Press(2, O, 1f); // the next finger starts clean
            Assert.AreEqual(GestureState.Candidate, r.State);
        }

        [Test]
        public void Revoke_DuringAPinch_CancelsIt()
        {
            var r = New(GestureMask.Pinch);
            r.Press(1, O, 0f);
            r.Press(2, O + new Vector2(100f, 0f), 0.05f);
            r.Move(2, O + new Vector2(150f, 0f), 0.1f);
            Drain(r);
            r.Revoke(1);
            Assert.AreEqual(CancelReason.Revoked, Find(Drain(r), GestureKind.Pinch, GestureEventType.Cancelled).Reason);
            r.Move(2, O + new Vector2(300f, 0f), 0.2f);
            r.Release(2, 0.3f);
            Assert.AreEqual(0, Drain(r).Count);
        }

        [Test]
        public void EventQueue_OverflowDropsTheOldest_NeverThrows()
        {
            var r = New(GestureMask.Drag);
            r.Press(1, O, 0f);
            for (int i = 1; i <= 100; i++) r.Move(1, O + new Vector2(30f + i, 0f), i * 0.01f);
            int count = 0;
            while (r.TryDequeue(out _)) count++;
            Assert.That(count, Is.LessThanOrEqualTo(32));
            Assert.That(count, Is.GreaterThan(0));
        }

        // ---- Settings -----------------------------------------------------------------------------

        [Test]
        public void Settings_AreSanitizedIntoAUsableOrder()
        {
            var s = new GestureSettings { pullPeekDistance = 0.1f, pullRevealDistance = 0.05f, tapSlop = -1f, longPressSeconds = 0.01f, pullHysteresis = 1f };
            var clean = s.Sanitized();
            Assert.That(clean.pullRevealDistance, Is.GreaterThan(clean.pullPeekDistance));
            Assert.That(clean.tapSlop, Is.GreaterThan(0f));
            Assert.That(clean.longPressSeconds, Is.GreaterThanOrEqualTo(clean.tapMaxSeconds));
            Assert.That(clean.pullHysteresis, Is.LessThan(clean.pullRevealDistance - clean.pullPeekDistance));
        }

        [Test]
        public void SteppedLevel_HasADeadBandAtEachBoundary()
        {
            // Step 100, band 15: level 1 is reached at 65 and left again below 35.
            Assert.AreEqual(0, GestureMath.SteppedLevel(60f, 100f, 15f, 0, 0, 2));
            Assert.AreEqual(1, GestureMath.SteppedLevel(66f, 100f, 15f, 0, 0, 2));
            Assert.AreEqual(1, GestureMath.SteppedLevel(40f, 100f, 15f, 1, 0, 2), "inside the band: keeps the level");
            Assert.AreEqual(0, GestureMath.SteppedLevel(34f, 100f, 15f, 1, 0, 2));
            Assert.AreEqual(2, GestureMath.SteppedLevel(500f, 100f, 15f, 0, 0, 2), "clamped to the range");
            Assert.AreEqual(0, GestureMath.SteppedLevel(-500f, 100f, 15f, 2, 0, 2));
            Assert.AreEqual(1, GestureMath.SteppedLevel(float.NaN, 100f, 15f, 1, 0, 2));
        }

        [Test]
        public void Motion_CurvesStayRestrained()
        {
            Assert.AreEqual(0f, GestureMath.EaseOut(0f), 1e-6f);
            Assert.AreEqual(1f, GestureMath.EaseOut(1f), 1e-6f);
            Assert.AreEqual(1f, GestureMath.SoftSpring(1f), 1e-6f);
            float peak = 0f;
            for (int i = 0; i <= 100; i++) peak = Mathf.Max(peak, GestureMath.SoftSpring(i / 100f));
            Assert.That(peak, Is.LessThan(1.06f), "a single soft overshoot of a few percent at most");
            Assert.AreEqual(50f, GestureMath.RubberBand(50f, 100f), 1e-6f);
            Assert.That(GestureMath.RubberBand(300f, 100f), Is.LessThan(200f).And.GreaterThan(100f));
            Assert.That(GestureMath.RubberBand(-300f, 100f), Is.GreaterThan(-200f).And.LessThan(-100f));
        }
    }
}
