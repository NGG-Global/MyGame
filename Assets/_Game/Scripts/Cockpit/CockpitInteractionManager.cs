using System;
using OpenSkies.CameraSystems;
using OpenSkies.FlightInput.TouchControls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.Cockpit
{
    /// <summary>
    /// Turns touches that no flight control or HUD element took into cockpit
    /// interaction: a tap selects an instrument or control, a drag looks around,
    /// two fingers adjust the field of view, a double tap on nothing looks forward
    /// again.
    ///
    /// It lives on a full-screen transparent graphic drawn behind every other
    /// control, so uGUI already gives the joystick, throttle, brake and HUD
    /// buttons first claim on a touch. On top of that it shares the
    /// <see cref="PointerOwnership"/> table with the touch controls: it never
    /// handles a pointer someone else owns, and it claims the pointers it uses so
    /// nothing else reacts to them. Revocation (focus loss, lost release events)
    /// cancels its gestures without a tap. Taps take priority over looking: a
    /// press that starts on a control may move further before it becomes a look.
    /// </summary>
    public sealed class CockpitInteractionManager : MonoBehaviour, IPointerOwner,
        IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public const int MaxPointers = 2;

        public CameraRig cameraRig;
        [Tooltip("Full-screen transparent graphic behind all controls; receives the touches nothing else took.")]
        public Graphic surface;
        public CockpitInteractable[] interactables = new CockpitInteractable[0];

        [Header("Gestures (distances are fractions of the screen height)")]
        public float tapMaxSeconds = 0.35f;
        [Tooltip("Movement that still counts as a tap.")]
        public float tapSlop = 0.02f;
        [Tooltip("Movement that still counts as a tap when the press started on a control (selection wins over looking).")]
        public float tapSlopOnControl = 0.045f;
        [Tooltip("A tap this close to a control selects it even if it misses the pick volume.")]
        public float pickTolerance = 0.06f;
        [Tooltip("Look rotation for a drag across the full screen height, degrees.")]
        public float lookDegreesPerScreenHeight = 100f;
        [Tooltip("On: dragging right turns the view right. Off: the view follows the scene under the finger.")]
        public bool lookFollowsFinger = true;
        [Tooltip("Field of view change for a pinch across the full screen height, degrees.")]
        public float pinchDegreesPerScreenHeight = 40f;
        public float doubleTapSeconds = 0.3f;

        /// <summary>A tap on an instrument or control.</summary>
        public event Action<CockpitInteractable> InteractableTapped;
        /// <summary>A tap on nothing (used to dismiss a focus panel).</summary>
        public event Action EmptyTapped;

        struct Gesture
        {
            public int PointerId;
            public Vector2 Start;
            public Vector2 Last;
            public float StartTime;
            public bool Looking;      // past the tap slop, or part of a pinch: never a tap
            public int PressedOn;     // interactable index under the press, or -1
        }

        readonly Gesture[] _gestures = new Gesture[MaxPointers];
        int _count;
        PointerOwnership _ownership;
        bool _active = true;
        float _pinchDistance;
        float _lastEmptyTapTime = float.NegativeInfinity;
        PickBox[] _boxes = new PickBox[0];
        Vector3[] _screenPoints = new Vector3[0];

        public string OwnerName => "Cockpit";
        public bool IsActive => _active;
        /// <summary>Pointers this manager currently owns.</summary>
        public int ActivePointers => _count;
        public PointerOwnership Ownership => _ownership;

        /// <summary>Share the touch controls' ownership table (one table for every touch consumer).</summary>
        public void Initialize(PointerOwnership ownership) => _ownership = ownership;

        public void SetInteractables(CockpitInteractable[] items)
        {
            interactables = items ?? new CockpitInteractable[0];
            EnsureBuffers();
        }

        /// <summary>Cockpit view on: takes unclaimed touches. Off: ignores touches and drops any gesture.</summary>
        public void SetActive(bool active)
        {
            _active = active;
            if (surface != null) surface.raycastTarget = active;
            if (!active) CancelAll();
        }

        void Awake()
        {
            if (_ownership == null) _ownership = new PointerOwnership(); // standalone (no touch controls)
            EnsureBuffers();
        }

        void OnDisable() => CancelAll();

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) CancelAll();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) CancelAll();
        }

        // ---- Pointer events (from uGUI, only for touches nothing in front took) ---------

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_active || _ownership == null || _count >= MaxPointers) return; // extra fingers do nothing
            if (_ownership.IsOwned(eventData.pointerId)) return;                  // a flight control or HUD element has it
            TouchControlBase.DescribePointer(eventData, out var kind, out int touchId);
            if (!_ownership.TryClaim(eventData.pointerId, this, kind, touchId)) return;

            _gestures[_count++] = new Gesture
            {
                PointerId = eventData.pointerId,
                Start = eventData.position,
                Last = eventData.position,
                StartTime = Time.unscaledTime,
                PressedOn = Pick(eventData.position),
            };
            if (_count == 2)
            {
                // Two fingers: a pinch, never a tap or a look.
                _gestures[0].Looking = true;
                _gestures[1].Looking = true;
                _pinchDistance = Vector2.Distance(_gestures[0].Last, _gestures[1].Last);
            }
        }

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnDrag(PointerEventData eventData)
        {
            int i = IndexOf(eventData.pointerId);
            if (i < 0) return;
            Vector2 previous = _gestures[i].Last;
            _gestures[i].Last = eventData.position;
            float height = ScreenHeight;

            if (_count == 2)
            {
                float distance = Vector2.Distance(_gestures[0].Last, _gestures[1].Last);
                float spread = distance - _pinchDistance;
                _pinchDistance = distance;
                if (cameraRig != null)
                    cameraRig.SetCockpitFieldOfView(cameraRig.cockpitFieldOfView - spread / height * pinchDegreesPerScreenHeight);
                return;
            }

            Vector2 delta = eventData.position - previous;
            if (!_gestures[i].Looking)
            {
                float slop = (_gestures[i].PressedOn >= 0 ? tapSlopOnControl : tapSlop) * height;
                Vector2 moved = eventData.position - _gestures[i].Start;
                if (moved.sqrMagnitude <= slop * slop) return;
                _gestures[i].Looking = true;
                delta = moved; // the whole movement so far turns the view: no dead jump
            }
            if (cameraRig == null) return;
            Vector2 look = CockpitLookMath.DragToLook(delta, height, lookDegreesPerScreenHeight, lookFollowsFinger);
            cameraRig.AddCockpitLook(look.x, look.y);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            int i = IndexOf(eventData.pointerId);
            if (i < 0) return;
            var gesture = _gestures[i];
            RemoveAt(i);
            _ownership.Release(eventData.pointerId, this);
            if (_count == 1) _gestures[0].Looking = true; // the finger left from a pinch does not tap
            if (gesture.Looking || Time.unscaledTime - gesture.StartTime > tapMaxSeconds) return;

            int target = Pick(eventData.position);
            if (target < 0) target = gesture.PressedOn; // released just off the control it was pressed on
            if (target >= 0)
            {
                _lastEmptyTapTime = float.NegativeInfinity;
                InteractableTapped?.Invoke(interactables[target]);
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastEmptyTapTime <= doubleTapSeconds)
            {
                _lastEmptyTapTime = float.NegativeInfinity;
                if (cameraRig != null) cameraRig.RecentreCockpitLook();
            }
            else
            {
                _lastEmptyTapTime = now;
            }
            EmptyTapped?.Invoke();
        }

        /// <summary>Focus loss, lost release, or the cockpit was switched off: the gesture ends without a tap.</summary>
        public void OnPointerRevoked(int pointerId)
        {
            int i = IndexOf(pointerId);
            if (i >= 0) RemoveAt(i);
            if (_count == 1) _gestures[0].Looking = true;
        }

        // ---- Picking --------------------------------------------------------------------------

        /// <summary>Index of the interactable under a screen point (direct hit first, then the nearest within tolerance), or -1.</summary>
        public int Pick(Vector2 screenPoint)
        {
            var viewCamera = cameraRig != null ? cameraRig.ViewCamera : null;
            if (viewCamera == null || interactables == null) return -1;
            EnsureBuffers();

            int count = 0;
            for (int i = 0; i < interactables.Length; i++)
            {
                var item = interactables[i];
                bool usable = item != null && item.isActiveAndEnabled;
                _boxes[i] = usable ? item.WorldPickBox : Unreachable;
                _screenPoints[i] = usable ? viewCamera.WorldToScreenPoint(item.transform.position) : new Vector3(0f, 0f, -1f);
                count = i + 1;
            }

            Ray ray = viewCamera.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0f));
            int hit = CockpitPicking.Nearest(ray.origin, ray.direction, _boxes, count, out _);
            if (hit >= 0) return hit;
            return CockpitPicking.NearestOnScreen(screenPoint, _screenPoints, count, pickTolerance * ScreenHeight);
        }

        public CockpitInteractable PickInteractable(Vector2 screenPoint)
        {
            int i = Pick(screenPoint);
            return i >= 0 ? interactables[i] : null;
        }

        // ---- Internals ------------------------------------------------------------------------

        static float ScreenHeight => Mathf.Max(1f, Screen.height);

        static readonly PickBox Unreachable = new PickBox
        {
            Centre = new Vector3(0f, -1e9f, 0f), Right = Vector3.right, Up = Vector3.up, Forward = Vector3.forward, HalfExtents = Vector3.zero,
        };

        void EnsureBuffers()
        {
            int n = interactables != null ? interactables.Length : 0;
            if (_boxes.Length != n) _boxes = new PickBox[n];
            if (_screenPoints.Length != n) _screenPoints = new Vector3[n];
        }

        int IndexOf(int pointerId)
        {
            for (int i = 0; i < _count; i++)
                if (_gestures[i].PointerId == pointerId) return i;
            return -1;
        }

        void RemoveAt(int index)
        {
            for (int i = index; i < _count - 1; i++) _gestures[i] = _gestures[i + 1];
            _count--;
        }

        void CancelAll()
        {
            if (_ownership != null) _ownership.RevokeAllFrom(this); // calls OnPointerRevoked for each
            _count = 0;
        }
    }
}
