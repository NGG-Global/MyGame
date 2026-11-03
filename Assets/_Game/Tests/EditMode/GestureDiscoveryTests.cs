using NUnit.Framework;
using OpenSkies.Gameplay;
using OpenSkies.Gestures;
using OpenSkies.Missions;
using OpenSkies.Options;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Tests
{
    /// <summary>Gesture hints as a policy, learned gestures in the progress, the gesture options, and the interaction diary.</summary>
    public class GestureDiscoveryTests
    {
        sealed class FakeHintView : IGestureHintView
        {
            public int Shows, Hides;
            public string LastText;
            public bool IsShown { get; private set; }
            public Rect Bounds => new Rect(0f, 0f, 1920f, 1080f);
            public Vector2 ToLocal(Vector2 screenPoint) => screenPoint;
            public void Show(Vector2 anchor, Vector2 direction, float distance, string text, Rect bounds) { Shows++; LastText = text; IsShown = true; }
            public void Hide() { Hides++; IsShown = false; }
        }

        static readonly Rect B = new Rect(0f, 0f, 1920f, 1080f);

        [Test]
        public void Hints_ShowOnce_StopWhenLearned_AndNeverReturn()
        {
            var store = new MemoryProgressStore();
            var progress = new PlayerProgress(store);
            var view = new FakeHintView();
            var hints = new GestureHints(progress, view);

            Assert.IsTrue(hints.Offer(GestureId.PullSystems, Vector2.zero, Vector2.up, 100f, "Pull", B, 0f));
            Assert.AreEqual(1, view.Shows);
            Assert.IsTrue(hints.Offer(GestureId.PullSystems, Vector2.zero, Vector2.up, 100f, "Pull", B, 1f), "the same cue, already showing");
            Assert.AreEqual(1, view.Shows);
            Assert.IsFalse(hints.Offer(GestureId.PullSheet, Vector2.zero, Vector2.up, 100f, "Sheet", B, 1f), "one cue at a time");

            hints.Learned(GestureId.PullSystems, 2f);
            Assert.IsFalse(view.IsShown, "performing the gesture hides its cue at once");
            Assert.IsTrue(progress.HasLearned(GestureId.PullSystems));
            Assert.IsFalse(hints.Offer(GestureId.PullSystems, Vector2.zero, Vector2.up, 100f, "Pull", B, 100f), "learnt: never again");

            var reloaded = new PlayerProgress(store);
            Assert.IsTrue(reloaded.HasLearned(GestureId.PullSystems), "remembered between sessions");
            Assert.IsFalse(reloaded.HasLearned(GestureId.PullSheet));
            Assert.AreEqual(1, reloaded.GesturesLearnedCount);
        }

        [Test]
        public void Hints_TimeOut_Cooldown_SessionLimit_AndCanBeDisabled()
        {
            var hints = new GestureHints(new PlayerProgress(new MemoryProgressStore()), new FakeHintView()) { ShowSeconds = 5f, CooldownSeconds = 10f, PerSessionLimit = 2 };
            Assert.IsTrue(hints.Offer(GestureId.PullSheet, Vector2.zero, Vector2.up, 100f, "Sheet", B, 0f));
            hints.Tick(4f);
            Assert.IsTrue(hints.IsShowing);
            hints.Tick(5.1f);
            Assert.IsFalse(hints.IsShowing, "timed out");
            Assert.IsFalse(hints.Offer(GestureId.PullSheet, Vector2.zero, Vector2.up, 100f, "Sheet", B, 10f), "inside the cooldown");
            Assert.IsTrue(hints.Offer(GestureId.PullSheet, Vector2.zero, Vector2.up, 100f, "Sheet", B, 16f), "second time this session");
            hints.Tick(30f);
            Assert.IsFalse(hints.Offer(GestureId.PullSheet, Vector2.zero, Vector2.up, 100f, "Sheet", B, 60f), "the session limit");

            hints.SetEnabled(false);
            Assert.IsFalse(hints.Offer(GestureId.SwipeStations, Vector2.zero, Vector2.left, 100f, "Swipe", B, 100f));
            hints.SetEnabled(true);
            Assert.IsTrue(hints.Offer(GestureId.SwipeStations, Vector2.zero, Vector2.left, 100f, "Swipe", B, 100f));
        }

        [Test]
        public void Hints_WithoutAView_NeverShow_ButStillLearn()
        {
            var progress = new PlayerProgress(new MemoryProgressStore());
            var hints = new GestureHints(progress, null);
            Assert.IsFalse(hints.HasView);
            Assert.IsFalse(hints.Offer(GestureId.PullSystems, Vector2.zero, Vector2.up, 100f, "Pull", B, 0f));
            hints.Learned(GestureId.PullSystems, 1f);
            Assert.IsTrue(progress.HasLearned(GestureId.PullSystems));
            progress.ForgetGestures();
            Assert.IsFalse(progress.HasLearned(GestureId.PullSystems));
        }

        [Test]
        public void GestureOptions_HaveDefaults_AreSanitized_AndScaleThresholds()
        {
            var d = GameOptionsRules.Defaults;
            Assert.AreEqual(GestureSpeed.Standard, d.GestureSpeed);
            Assert.AreEqual(CameraGestureSensitivity.Medium, d.CameraGesture);
            Assert.IsTrue(d.GestureHints);
            var bad = d;
            bad.GestureSpeed = (GestureSpeed)9;
            bad.CameraGesture = (CameraGestureSensitivity)(-1);
            var clean = GameOptionsRules.Sanitize(bad);
            Assert.AreEqual(GestureSpeed.Standard, clean.GestureSpeed);
            Assert.AreEqual(CameraGestureSensitivity.Medium, clean.CameraGesture);
            Assert.IsTrue(GameOptionsRules.Same(d, clean));
            var changed = d;
            changed.GestureHints = false;
            Assert.IsFalse(GameOptionsRules.Same(d, changed));

            Assert.That(GameOptionsRules.GestureTimeScale(GestureSpeed.Relaxed), Is.GreaterThan(1f));
            Assert.That(GameOptionsRules.GestureTimeScale(GestureSpeed.Quick), Is.LessThan(1f));
            Assert.AreEqual(1f, GameOptionsRules.GestureTimeScale(GestureSpeed.Standard));
            Assert.That(GameOptionsRules.GestureDistanceScale(GestureSpeed.Relaxed), Is.LessThan(1f), "relaxed reveals sooner");
            Assert.That(GameOptionsRules.CameraGestureScale(CameraGestureSensitivity.High), Is.GreaterThan(GameOptionsRules.CameraGestureScale(CameraGestureSensitivity.Low)));

            var config = new InteractionConfiguration();
            var tuned = config.TunedGestures(GameOptionsRules.GestureTimeScale(GestureSpeed.Relaxed), GameOptionsRules.GestureDistanceScale(GestureSpeed.Relaxed));
            Assert.That(tuned.longPressSeconds, Is.GreaterThan(config.gestures.longPressSeconds));
            Assert.That(tuned.pullRevealDistance, Is.LessThan(config.gestures.pullRevealDistance));
            Assert.That(tuned.pullRevealDistance, Is.GreaterThan(tuned.pullPeekDistance), "still sane after scaling");
        }

        [Test]
        public void Diary_CountsOutcomes_AndQuickCancels()
        {
            var diary = new InteractionDiary();
            diary.Record(InteractionRoute.SystemsHandle, new GestureEvent { Kind = GestureKind.Pull, Type = GestureEventType.Began, Time = 10f });
            diary.Record(InteractionRoute.SystemsHandle, new GestureEvent { Kind = GestureKind.Pull, Type = GestureEventType.Completed, Time = 11f });
            diary.Record(InteractionRoute.SystemsHandle, new GestureEvent { Kind = GestureKind.Pull, Type = GestureEventType.Began, Time = 20f });
            diary.Record(InteractionRoute.SystemsHandle, new GestureEvent { Kind = GestureKind.Pull, Type = GestureEventType.Cancelled, Reason = CancelReason.ReleasedUnarmed, Time = 20.1f });
            diary.Record(InteractionRoute.World, new GestureEvent { Kind = GestureKind.Drag, Type = GestureEventType.Began, Time = 30f });
            diary.Record(InteractionRoute.World, new GestureEvent { Kind = GestureKind.Drag, Type = GestureEventType.Completed, Time = 32f });
            Assert.AreEqual(1, diary.Completed(InteractionRoute.SystemsHandle));
            Assert.AreEqual(1, diary.Cancelled(InteractionRoute.SystemsHandle));
            Assert.AreEqual(1, diary.CancelledFor(CancelReason.ReleasedUnarmed));
            Assert.AreEqual(1, diary.QuickCancels, "a pull cancelled within 0.3 s is the likeliest accident");
            Assert.AreEqual(2, diary.TotalCompleted);
            Assert.AreEqual(1f, diary.AveragePullSeconds, 1e-4f);
            StringAssert.Contains("done 2", diary.Summary());
        }
    }
}
