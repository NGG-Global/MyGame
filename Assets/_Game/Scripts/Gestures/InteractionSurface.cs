using System;
using OpenSkies.FlightInput.TouchControls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.Gestures
{
    /// <summary>
    /// The single entry point for every touch that no flight control or HUD
    /// button took. It draws nothing and sits behind every control (first child of
    /// the safe area), and the gesture hotspots (systems handle, HUD band) hand it
    /// their presses with an explicit route. For each press it asks
    /// <see cref="InteractionRouter"/> who gets it:
    ///
    /// - Cockpit: forwarded, event by event, to the cockpit interaction (which claims
    ///   the pointer itself). No full-screen overlay behind the HUD is needed any more.
    /// - World, SystemsHandle, HudBand, Target: recognised here, one
    ///   <see cref="GestureRecognizer"/> channel per route, and the surface claims the
    ///   pointer in the shared <see cref="PointerOwnership"/> table (owner "Gestures"),
    ///   so a finger on the joystick or throttle can never reach a gesture, and
    ///   focus loss, pauses and lost releases cancel gestures like any flight control.
    ///
    /// - Dismiss: a transient interface is expanded (the <see cref="DisclosureCoordinator"/>
    ///   knows which) and the press is outside it: the pointer is consumed here, a tap
    ///   collapses the branch, and nothing underneath reacts.
    ///
    /// It raises <see cref="GestureRaised"/>; it never produces flight input and
    /// never changes anything itself.
    /// </summary>
    public sealed class InteractionSurface : MonoBehaviour, IPointerOwner,
        IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public const int MaxPointers = 6;
        const int RouteCount = InteractionRouter.RouteCount;
        const int NoTarget = -1;

        [Tooltip("Full-screen raycast target that draws nothing.")]
        public Graphic surface;
        public GestureSettings settings = new GestureSettings();
        [Tooltip("Use this screen height for gesture thresholds instead of Screen.height (tests with a fixed canvas). 0 = Screen.height.")]
        public float screenHeightOverride;

        /// <summary>Gesture-first interface on. Off: only the cockpit route exists (legacy interface).</summary>
        public bool GestureFirst { get; set; } = true;
        /// <summary>Set by the session each frame.</summary>
        public bool CockpitView { get; set; }
        /// <summary>A modal card, the entry screen or the settings are open.</summary>
        public bool Blocked { get; set; }
        /// <summary>Finds the explainable element under a screen point (an id of the caller's choosing), or -1.</summary>
        public Func<Vector2, int> TargetResolver { get; set; }

        /// <summary>A gesture event on a route, with the target id for Target and HudBand routes (-1 otherwise).</summary>
        public event Action<InteractionRoute, GestureEvent, int> GestureRaised;

        readonly int[] _pointerIds = new int[MaxPointers];
        readonly InteractionRoute[] _pointerRoutes = new InteractionRoute[MaxPointers];
        int _pointerCount;
        readonly GestureRecognizer[] _channels = new GestureRecognizer[RouteCount];
        readonly int[] _channelTargets = new int[RouteCount];
        PointerOwnership _ownership;
        DisclosureCoordinator _disclosure;
        IPointerDownHandler _cockpitDown;
        IDragHandler _cockpitDrag;
        IPointerUpHandler _cockpitUp;
        bool _initialized;

        public string OwnerName => "Gestures";
        public int ActivePointers => _pointerCount;
        public PointerOwnership Ownership => _ownership;

        /// <summary>
        /// The one coordinator of expanded transient interfaces: owners register with it;
        /// the surface asks it whether a press is outside the active branch and hands it the
        /// collapsing tap. Created on first use, so it exists before Awake for the sessions.
        /// </summary>
        public DisclosureCoordinator Disclosure => _disclosure ?? (_disclosure = new DisclosureCoordinator());

        /// <summary>Share the touch controls' ownership table (one table for every touch consumer).</summary>
        public void Initialize(PointerOwnership ownership) => _ownership = ownership;

        /// <summary>The cockpit interaction that receives the Cockpit route (it implements the uGUI pointer handlers).</summary>
        public void SetCockpitTarget(Component target)
        {
            _cockpitDown = target as IPointerDownHandler;
            _cockpitDrag = target as IDragHandler;
            _cockpitUp = target as IPointerUpHandler;
        }

        public GestureRecognizer Channel(InteractionRoute route) => _channels[(int)route];
        public int TargetOf(InteractionRoute route) => _channelTargets[(int)route];
        public bool HasCockpitTarget => _cockpitDown != null;

        /// <summary>The route a pointer is on, or None.</summary>
        public InteractionRoute RouteOf(int pointerId)
        {
            int i = IndexOf(pointerId);
            return i >= 0 ? _pointerRoutes[i] : InteractionRoute.None;
        }

        void Awake() => EnsureInitialized();

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            if (_ownership == null) _ownership = new PointerOwnership(); // standalone (no touch controls)
            for (int r = 0; r < RouteCount; r++)
            {
                var route = (InteractionRoute)r;
                _channelTargets[r] = NoTarget;
                if (InteractionRouter.IsRecognized(route)) _channels[r] = new GestureRecognizer(settings, InteractionRouter.MaskFor(route));
            }
        }

        void OnDisable() => CancelAll();

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) return;
            CancelAll();
            _disclosure?.OnFocusLost();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) CancelAll();
        }

        GestureSettings _pendingSettings;

        /// <summary>
        /// New thresholds (the player's gesture speed): channels are rebuilt as soon as no
        /// gesture is in progress, so a gesture under way keeps the rules it started with.
        /// </summary>
        public void ApplySettings(GestureSettings next)
        {
            if (next == null) return;
            settings = next;
            _pendingSettings = next;
            TryRebuildChannels();
        }

        void TryRebuildChannels()
        {
            if (_pendingSettings == null || !_initialized) return;
            for (int r = 0; r < RouteCount; r++)
                if (_channels[r] != null && _channels[r].IsTracking) return;
            for (int r = 0; r < RouteCount; r++)
            {
                var route = (InteractionRoute)r;
                if (InteractionRouter.IsRecognized(route)) _channels[r] = new GestureRecognizer(_pendingSettings, InteractionRouter.MaskFor(route));
            }
            _pendingSettings = null;
        }

        void Update()
        {
            if (!_initialized) return;
            TryRebuildChannels();
            _disclosure?.Tick(Time.unscaledDeltaTime);
            float now = Time.unscaledTime;
            for (int r = 0; r < RouteCount; r++)
            {
                var channel = _channels[r];
                if (channel == null || !channel.IsTracking) continue;
                channel.Tick(now);
                Drain((InteractionRoute)r);
            }
        }

        // ---- Pointer events ----------------------------------------------------------------------

        /// <summary>A press on the open surface (nothing else took it).</summary>
        public void OnPointerDown(PointerEventData eventData) => Begin(InteractionRoute.None, eventData);

        /// <summary>A press on a hotspot, with the route it asks for.</summary>
        public void BeginFromHotspot(InteractionRoute requested, PointerEventData eventData) => Begin(requested, eventData);

        void Begin(InteractionRoute requested, PointerEventData eventData)
        {
            EnsureInitialized();
            int id = eventData.pointerId;
            if (_ownership.IsOwned(id) || IndexOf(id) >= 0) return; // a flight control or HUD element has it
            if (_pointerCount >= MaxPointers) return;

            // A press on the open surface while something is expanded and it lands outside: the dismissing tap.
            bool outside = requested == InteractionRoute.None && !Blocked && _disclosure != null && _disclosure.WantsOutsideTap(eventData.position);
            int target = NoTarget;
            if (!outside && GestureFirst && TargetResolver != null && (requested == InteractionRoute.None || requested == InteractionRoute.HudBand))
                target = TargetResolver(eventData.position);
            var route = InteractionRouter.Decide(new RouteContext
            {
                Requested = requested,
                GestureFirst = GestureFirst,
                CockpitView = CockpitView,
                Blocked = Blocked,
                OnTarget = target != NoTarget && requested == InteractionRoute.None,
                OutsideActiveDisclosure = outside,
            });
            if (route == InteractionRoute.None) return;

            if (route == InteractionRoute.Cockpit)
            {
                if (_cockpitDown == null) return;
                _cockpitDown.OnPointerDown(eventData); // it claims the pointer itself, or ignores it
                if (_ownership.IsOwned(id)) Track(id, route);
                return;
            }

            var channel = _channels[(int)route];
            if (channel == null) return;
            TouchControlBase.DescribePointer(eventData, out var kind, out int touchId);
            if (!_ownership.TryClaim(id, this, kind, touchId)) return;
            channel.SetScreenHeight(ScreenHeight);
            if (!channel.Press(id, eventData.position, Time.unscaledTime))
            {
                _ownership.Release(id, this); // a third finger: nobody wants it
                return;
            }
            if (!channel.HasSecondPointer) _channelTargets[(int)route] = route == InteractionRoute.HudBand || route == InteractionRoute.Target ? target : NoTarget;
            Track(id, route);
            Drain(route);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnDrag(PointerEventData eventData)
        {
            int i = IndexOf(eventData.pointerId);
            if (i < 0) return;
            var route = _pointerRoutes[i];
            if (route == InteractionRoute.Cockpit)
            {
                _cockpitDrag?.OnDrag(eventData);
                return;
            }
            var channel = _channels[(int)route];
            if (channel == null) return;
            channel.Move(eventData.pointerId, eventData.position, Time.unscaledTime);
            Drain(route);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            int i = IndexOf(eventData.pointerId);
            if (i < 0) return;
            var route = _pointerRoutes[i];
            RemoveAt(i);
            if (route == InteractionRoute.Cockpit)
            {
                _cockpitUp?.OnPointerUp(eventData);
                return;
            }
            var channel = _channels[(int)route];
            _ownership.Release(eventData.pointerId, this);
            if (channel == null) return;
            channel.Release(eventData.pointerId, Time.unscaledTime);
            Drain(route);
        }

        /// <summary>Focus loss, lost release, pause: the gesture is cancelled without completing.</summary>
        public void OnPointerRevoked(int pointerId)
        {
            int i = IndexOf(pointerId);
            if (i < 0) return;
            var route = _pointerRoutes[i];
            RemoveAt(i);
            var channel = route != InteractionRoute.Cockpit ? _channels[(int)route] : null;
            if (channel == null) return;
            channel.Revoke(pointerId);
            Drain(route);
        }

        /// <summary>Ends every gesture (view change, mode change, pause). Cockpit pointers belong to the cockpit.</summary>
        public void CancelAll()
        {
            if (_ownership != null) _ownership.RevokeAllFrom(this); // OnPointerRevoked for each of ours
            for (int i = _pointerCount - 1; i >= 0; i--)
                if (_pointerRoutes[i] == InteractionRoute.Cockpit) RemoveAt(i);
        }

        // ---- Internals ---------------------------------------------------------------------------

        float ScreenHeight => screenHeightOverride > 1f ? screenHeightOverride : Mathf.Max(1f, Screen.height);

        void Drain(InteractionRoute route)
        {
            var channel = _channels[(int)route];
            if (channel == null) return;
            int target = _channelTargets[(int)route];
            while (channel.TryDequeue(out var e))
            {
                // The dismissing tap acts here, so no session wiring can forget it; anything else on that pointer does nothing.
                if (route == InteractionRoute.Dismiss && e.Kind == GestureKind.Tap && e.Type == GestureEventType.Completed)
                    _disclosure?.OutsideTap(e.Position);
                GestureRaised?.Invoke(route, e, target);
                if (e.Type == GestureEventType.Completed || e.Type == GestureEventType.Cancelled)
                    _channelTargets[(int)route] = NoTarget;
            }
        }

        void Track(int pointerId, InteractionRoute route)
        {
            _pointerIds[_pointerCount] = pointerId;
            _pointerRoutes[_pointerCount] = route;
            _pointerCount++;
        }

        int IndexOf(int pointerId)
        {
            for (int i = 0; i < _pointerCount; i++)
                if (_pointerIds[i] == pointerId) return i;
            return -1;
        }

        void RemoveAt(int index)
        {
            for (int i = index; i < _pointerCount - 1; i++)
            {
                _pointerIds[i] = _pointerIds[i + 1];
                _pointerRoutes[i] = _pointerRoutes[i + 1];
            }
            _pointerCount--;
        }
    }
}
