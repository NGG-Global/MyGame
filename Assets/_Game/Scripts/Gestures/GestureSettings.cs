using System;
using UnityEngine;

namespace OpenSkies.Gestures
{
    /// <summary>
    /// Thresholds for gesture recognition. Distances are fractions of the screen
    /// height, so the same values feel alike on phones and tablets (a 20:9 phone
    /// held in landscape is about 68 mm tall: 0.02 is about 1.4 mm). Times are
    /// seconds of unscaled time. Prototype values, to be tuned on a device.
    /// </summary>
    [Serializable]
    public sealed class GestureSettings
    {
        [Header("Taps")]
        [Tooltip("A press released within this time, without moving, is a tap, s.")]
        public float tapMaxSeconds = 0.3f;
        [Tooltip("Movement that still counts as a tap or a long press, fraction of the screen height.")]
        public float tapSlop = 0.02f;
        [Tooltip("A second tap within this time of the first is a double tap, s.")]
        public float doubleTapSeconds = 0.3f;
        [Tooltip("The second tap must land within this distance of the first.")]
        public float doubleTapSlop = 0.06f;

        [Header("Long press and dwell")]
        [Tooltip("Hold this long without moving for a long press, s.")]
        public float longPressSeconds = 0.45f;
        [Tooltip("During a pull or drag, the finger resting this long reveals the next level, s.")]
        public float dwellSeconds = 0.5f;
        [Tooltip("Movement that still counts as resting.")]
        public float dwellSlop = 0.015f;

        [Header("Pull (handle, HUD band)")]
        [Tooltip("Movement beyond this starts the preview (peek).")]
        public float pullPeekDistance = 0.02f;
        [Tooltip("Movement to reveal the control fully (progress 1).")]
        public float pullRevealDistance = 0.09f;
        [Tooltip("Progress must fall this far below a threshold before the state steps back (no flicker at the edge).")]
        public float pullHysteresis = 0.012f;

        [Header("Drag and swipe")]
        [Tooltip("Movement beyond this turns a press into a drag.")]
        public float dragThreshold = 0.02f;
        [Tooltip("A drag released after at least this distance, quickly enough, is also a swipe.")]
        public float swipeMinDistance = 0.08f;
        [Tooltip("The whole swipe must take at most this long, s.")]
        public float swipeMaxSeconds = 0.5f;
        [Tooltip("Minimum release velocity for a swipe, screen heights per second.")]
        public float swipeMinVelocity = 0.5f;
        [Tooltip("A swipe is accepted this far from a straight up, down, left or right, degrees.")]
        public float directionToleranceDegrees = 35f;
        [Tooltip("Velocity smoothing time constant, s.")]
        public float velocitySmoothingSeconds = 0.05f;

        [Header("Two fingers")]
        [Tooltip("A second finger landing within this time of the first, before either moved, may make a two-finger tap, s.")]
        public float secondFingerWindowSeconds = 0.15f;
        [Tooltip("Both fingers released within this time of landing, without moving, make a two-finger tap, s.")]
        public float twoFingerTapMaxSeconds = 0.3f;
        [Tooltip("A change of finger distance beyond this starts a pinch.")]
        public float pinchMinChange = 0.012f;

        /// <summary>Distance thresholds are kept positive and ordered so the state machine cannot wedge.</summary>
        public GestureSettings Sanitized()
        {
            var s = (GestureSettings)MemberwiseClone();
            s.tapSlop = Mathf.Max(0.001f, s.tapSlop);
            s.doubleTapSlop = Mathf.Max(s.tapSlop, s.doubleTapSlop);
            s.pullPeekDistance = Mathf.Max(0.001f, s.pullPeekDistance);
            s.pullRevealDistance = Mathf.Max(s.pullPeekDistance + 0.001f, s.pullRevealDistance);
            s.pullHysteresis = Mathf.Clamp(s.pullHysteresis, 0f, (s.pullRevealDistance - s.pullPeekDistance) * 0.5f);
            s.dragThreshold = Mathf.Max(0.001f, s.dragThreshold);
            s.dwellSlop = Mathf.Max(0.001f, s.dwellSlop);
            s.pinchMinChange = Mathf.Max(0.001f, s.pinchMinChange);
            s.tapMaxSeconds = Mathf.Max(0.01f, s.tapMaxSeconds);
            s.longPressSeconds = Mathf.Max(s.tapMaxSeconds, s.longPressSeconds);
            s.dwellSeconds = Mathf.Max(0.05f, s.dwellSeconds);
            s.velocitySmoothingSeconds = Mathf.Max(0f, s.velocitySmoothingSeconds);
            return s;
        }
    }
}
