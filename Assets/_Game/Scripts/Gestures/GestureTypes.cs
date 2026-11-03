using System;
using UnityEngine;

namespace OpenSkies.Gestures
{
    /// <summary>
    /// Where a gesture is in its life. The interface discloses in proportion to
    /// the movement: a candidate shows nothing, a peek shows a preview, active
    /// manipulation shows the full control, armed means releasing now completes the
    /// action. Nothing is applied to the aircraft until the gesture is Completed.
    /// </summary>
    public enum GestureState
    {
        Idle = 0,
        /// <summary>A finger is down; nothing decided yet (could become a tap, a long press, a pull).</summary>
        Candidate = 1,
        /// <summary>A small movement: a preview is shown, releasing now cancels.</summary>
        Peek = 2,
        /// <summary>The finger is manipulating the control; releasing now cancels unless armed.</summary>
        ActiveManipulation = 3,
        /// <summary>The completion condition holds: releasing now commits.</summary>
        Armed = 4,
        Cancelled = 5,
        Completed = 6,
    }

    public enum GestureKind
    {
        None = 0,
        Tap = 1,
        DoubleTap = 2,
        LongPress = 3,
        /// <summary>A quick directional flick, reported on release of a drag.</summary>
        Swipe = 4,
        /// <summary>Continuous movement away from the origin that reveals a control (handle, HUD band).</summary>
        Pull = 5,
        /// <summary>Continuous movement with per-move deltas (camera look).</summary>
        Drag = 6,
        /// <summary>The finger stopped moving during a pull or drag: deeper disclosure.</summary>
        Dwell = 7,
        Pinch = 8,
        TwoFingerTap = 9,
    }

    /// <summary>Which gestures a recognizer listens for (one bit per <see cref="GestureKind"/>).</summary>
    [Flags]
    public enum GestureMask
    {
        None = 0,
        Tap = 1 << GestureKind.Tap,
        DoubleTap = 1 << GestureKind.DoubleTap,
        LongPress = 1 << GestureKind.LongPress,
        Swipe = 1 << GestureKind.Swipe,
        Pull = 1 << GestureKind.Pull,
        Drag = 1 << GestureKind.Drag,
        Dwell = 1 << GestureKind.Dwell,
        Pinch = 1 << GestureKind.Pinch,
        TwoFingerTap = 1 << GestureKind.TwoFingerTap,
    }

    public enum SwipeDirection
    {
        None = 0,
        Up = 1,
        Down = 2,
        Left = 3,
        Right = 4,
    }

    public enum CancelReason
    {
        None = 0,
        /// <summary>Focus loss, a lost release, a pause: the pointer was taken away.</summary>
        Revoked = 1,
        /// <summary>The finger moved too far for the gesture it had started (a tap or long press that slid).</summary>
        MovedOut = 2,
        /// <summary>Released before the gesture was armed (a pull let go on nothing).</summary>
        ReleasedUnarmed = 3,
        /// <summary>Held longer than a tap but not long enough for a long press.</summary>
        Incomplete = 4,
        /// <summary>The consumer cancelled it (a lever moved out of its track).</summary>
        Interrupted = 5,
    }

    public enum GestureEventType
    {
        /// <summary>The kind of gesture has been decided and it is under way.</summary>
        Began = 0,
        /// <summary>Position, progress or pinch changed.</summary>
        Changed = 1,
        /// <summary>The finger stopped moving long enough (see <see cref="GestureKind.Dwell"/>).</summary>
        Dwell = 2,
        DwellEnded = 3,
        Armed = 4,
        Disarmed = 5,
        Completed = 6,
        Cancelled = 7,
    }

    /// <summary>One thing that happened in a recognizer, queued without allocation.</summary>
    public struct GestureEvent
    {
        public GestureKind Kind;
        public GestureEventType Type;
        /// <summary>Where the gesture started, screen pixels.</summary>
        public Vector2 Origin;
        /// <summary>Where the finger is now (the primary finger), screen pixels.</summary>
        public Vector2 Position;
        /// <summary>Position minus origin, screen pixels.</summary>
        public Vector2 Delta;
        /// <summary>Movement since the previous Changed event, screen pixels (drags).</summary>
        public Vector2 Step;
        /// <summary>0-1 for pulls (peek to full reveal) and long presses (hold time).</summary>
        public float Progress;
        public SwipeDirection Direction;
        public CancelReason Reason;
        /// <summary>Pinch: change of finger distance since the previous event, as a fraction of the screen height.</summary>
        public float PinchDelta;
        public float Time;
    }
}
