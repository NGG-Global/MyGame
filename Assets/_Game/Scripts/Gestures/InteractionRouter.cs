namespace OpenSkies.Gestures
{
    /// <summary>Who handles a touch that no flight control or HUD button took.</summary>
    public enum InteractionRoute
    {
        /// <summary>Nobody: the touch is ignored.</summary>
        None = 0,
        /// <summary>Forwarded to the cockpit interaction (tap to select, drag to look, pinch, double tap).</summary>
        Cockpit = 1,
        /// <summary>Empty world in the external view: free look, pinch zoom, double tap to switch view, long press.</summary>
        World = 2,
        /// <summary>The systems handle: pull to unfold the arc, dwell for more, tap to peek.</summary>
        SystemsHandle = 3,
        /// <summary>The HUD band at the top: pull down or up for more or less HUD, long press to explain a value.</summary>
        HudBand = 4,
        /// <summary>An explainable element (a warning banner, a guide): long press to explain.</summary>
        Target = 5,
        /// <summary>The enlarged instrument in the cockpit focus panel: drag to adjust, pinch to zoom, double tap to recentre.</summary>
        Instrument = 6,
        /// <summary>The pause sheet's grip: drag between its stops.</summary>
        Sheet = 7,
        /// <summary>The entry screen: swipe between stations, tap to start.</summary>
        Entry = 8,
    }

    /// <summary>What the router needs to know about a press.</summary>
    public struct RouteContext
    {
        /// <summary>The route a hotspot asked for (None for a press on the open surface).</summary>
        public InteractionRoute Requested;
        /// <summary>Gesture-first interface on (legacy: only the cockpit route exists).</summary>
        public bool GestureFirst;
        public bool CockpitView;
        /// <summary>A modal card, the entry screen or the settings are open: nothing below them reacts.</summary>
        public bool Blocked;
        /// <summary>The press landed on an explainable element.</summary>
        public bool OnTarget;
    }

    /// <summary>
    /// The one place that decides which consumer gets a touch, from where it
    /// landed and the state of the interface. Pure, so the policy is testable and
    /// has no hidden dependence on draw order.
    /// </summary>
    public static class InteractionRouter
    {
        /// <summary>Number of routes (array sizes in the surface).</summary>
        public const int RouteCount = 9;

        public static InteractionRoute Decide(in RouteContext c)
        {
            // Hotspots that belong to overlays (the pause sheet, the entry screen) work while the flight is blocked.
            if (c.Requested == InteractionRoute.Sheet || c.Requested == InteractionRoute.Entry) return c.Requested;
            if (c.Blocked) return InteractionRoute.None;
            if (c.Requested == InteractionRoute.SystemsHandle || c.Requested == InteractionRoute.HudBand)
                return c.GestureFirst ? c.Requested : InteractionRoute.None;
            if (c.Requested == InteractionRoute.Instrument) return c.Requested; // the focus panel's instrument, in both interfaces
            if (c.GestureFirst && c.OnTarget) return InteractionRoute.Target;
            if (c.CockpitView) return InteractionRoute.Cockpit;
            return c.GestureFirst ? InteractionRoute.World : InteractionRoute.None;
        }

        /// <summary>The gestures each route listens for.</summary>
        public static GestureMask MaskFor(InteractionRoute route)
        {
            switch (route)
            {
                case InteractionRoute.SystemsHandle:
                    return GestureMask.Pull | GestureMask.Dwell | GestureMask.Tap | GestureMask.LongPress;
                case InteractionRoute.HudBand:
                    return GestureMask.Pull | GestureMask.LongPress | GestureMask.Tap;
                case InteractionRoute.World:
                    return GestureMask.Drag | GestureMask.Pinch | GestureMask.DoubleTap | GestureMask.Tap | GestureMask.LongPress | GestureMask.TwoFingerTap;
                case InteractionRoute.Target:
                    return GestureMask.LongPress | GestureMask.Tap;
                case InteractionRoute.Instrument:
                    return GestureMask.Drag | GestureMask.Pinch | GestureMask.DoubleTap | GestureMask.Tap | GestureMask.LongPress | GestureMask.Dwell;
                case InteractionRoute.Sheet:
                    return GestureMask.Drag | GestureMask.Swipe | GestureMask.Tap;
                case InteractionRoute.Entry:
                    return GestureMask.Drag | GestureMask.Swipe | GestureMask.Tap | GestureMask.DoubleTap;
                default:
                    return GestureMask.None;
            }
        }

        /// <summary>Routes the surface recognises itself (the cockpit has its own recogniser).</summary>
        public static bool IsRecognized(InteractionRoute route) =>
            route != InteractionRoute.None && route != InteractionRoute.Cockpit;
    }
}
