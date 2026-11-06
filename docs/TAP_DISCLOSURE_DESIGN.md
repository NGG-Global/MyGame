# Open Skies: tap-first disclosure (UI redesign, milestone 1)

The transient interfaces of the HUD, the cockpit, the coaching, the settings and
the pause no longer need a hold or a drag to open, to go deeper or to close. A
visible trigger is tapped, an option is tapped, and a tap anywhere else puts the
HUD back in its quiet state. One coordinator knows what the player has expanded
and decides what an outside tap collapses; the interaction surface consumes that
tap so nothing underneath can react to it.

Status: implemented with EditMode and PlayMode tests; **not yet compiled by CI,
not yet run in the Editor, not yet touched on a device.** Not in this milestone:
the final HUD layout, an icon set, the tutorial redesign.

## The contract

Every transient interface follows these rules:

1. **Tap a visible trigger to expand.** SYSTEMS opens the arc; a cockpit instrument
   or switch opens its focus panel; ASSIST (on the arc or the rail) opens the flight
   settings; the coaching message opens its explanation; MENU opens the pause sheet;
   SETTINGS opens the settings.
2. **Tap an option to select, or to open deeper functionality.** An arc segment acts
   (as the rail button would) or fans its options out on the outer ring; a detent, a
   profile, a destination is chosen by a tap.
3. **Tap outside to collapse.** A tap clearly outside the active transient branch
   returns to the quiet HUD state.
4. **Tap the trigger again to collapse it.** SYSTEMS again folds the arc; ASSIST again
   closes the settings; the open action again folds its options; a tap on the
   enlarged instrument closes it.
5. **A different cluster replaces the previous one.** Tapping another trigger closes
   what was open and opens the new one (the arc gives way to the flap selector, the
   flight settings give way to the arc).
6. **No X-shaped close controls anywhere.** The close buttons of the cockpit focus
   cards and the flight settings card are gone; the coaching message shows "–"
   instead of "×" while its explanation is open. DONE on the settings screen,
   RESUME on the pause sheet, RETRY and FREE FLIGHT on a result are navigation and
   confirmation actions, not closes, and stay.
7. **No invisible required close gesture.** Every close has a tap: outside, or the
   trigger. The drags that remain (the sheet pulled down, the arc pulled) are
   alternatives, never the only way.
8. **No accidental activation underneath.** The dismissing tap is consumed by the
   interaction surface before anything behind it can see it: not a cockpit switch,
   not the free look, not a double tap that would switch the view.
9. **Warnings are not transient overlays.** Banners are never registered, never
   dismissed by a tap and never dimmed; a tap on a banner is a tap outside the
   open interface.
10. **Flight controls stay active and independently owned.** A finger the joystick,
    the throttle or the brake owns is never an outside tap. Nothing here changes
    `PointerOwnership`.

## Outside taps

| The tap lands on | What happens |
|---|---|
| Empty space (the sky, the cockpit panel behind a card) | The whole branch collapses, deepest surface first, down to the first surface that stays through outside taps (a paused interface) |
| A parent while a child is open (the arc while an action's options are out; the pause sheet while its settings are up) | The children collapse; the parent stays |
| The deepest open surface (a segment, a detent button, a settings row) | Not an outside tap: the surface handles it |
| A warning banner, a flight value, the HUD band | Outside: the branch collapses; the banner and the value are untouched |
| Another trigger (the handle, MENU, a rail button, a cockpit item in the legacy comparison) | Not an outside tap: the new cluster replaces the old one |
| A finger the flight controls own | Nothing: the pointer is never routed |

The press itself decides: when the surface receives a press on the open world (nothing
drawn on top took it) and the coordinator says the point is outside the active branch,
the press is claimed on the `Dismiss` route, which listens for a tap only. The tap
collapses; a drag, a hold or a second finger on that pointer does nothing at all. Once
the branch has collapsed the next press is the world's again (free look, cockpit).

**Paused interfaces.** The pause sheet and the legacy pause card are registered as
surfaces that outside taps may not dismiss. A tap on the dimmed world at the sheet's
first stop is consumed and does nothing; at the second stop it folds the settings
back to the first stop. A tap on the dimmed world around the full settings screen
closes the settings back to the pause card or the entry screen, which stay. Resuming
is RESUME, MENU, Esc or the deliberate pull down of the sheet.

**Safety.** Previews never change aircraft state (unchanged). A hazardous change
waiting for its confirmation (engine off in flight) is dropped when the selector
collapses on an outside tap: the panel closes through `CockpitSession.Close`, which
clears the gate. `SystemChangeGate` remains the only way a system changes. No reset,
engine shutdown or mode change can come from a dismissal, and flight input ownership
is never altered.

## Architecture

```
Press on the open surface ──▶ InteractionSurface.Begin
    │  owned pointer? ignored.  hotspot route? kept (another trigger).
    │  DisclosureCoordinator.WantsOutsideTap(point)? ──▶ route Dismiss (claimed, tap only)
    │                                                        │ tap Completed
    │                                                        ▼
    │                                        DisclosureCoordinator.OutsideTap(point)
    │                                             collapses the branch, deepest first,
    │                                             through each owner's collapse callback
    └─▶ otherwise the routes of docs/GESTURE_INTERACTION.md (World, Cockpit, Target...)

Owners register their surfaces once, open and close them as they always did, and tell the coordinator:
  GestureSession ─ Systems arc (root), Systems sub-arc (child of the arc)
  FlightSession ─ Flight settings card
  CockpitSession ─ Cockpit panel (instrument card or selector)
  GameModeSession ─ Pause sheet (stays), Pause sheet settings (child), Pause card (stays), Coaching detail
  PresentationSession ─ Settings screen (child of the pause card when it opened from it)
```

### `DisclosureCoordinator` (`OpenSkies.Gestures`, plain C#)

The one place that knows which transient interface the player has expanded. It does
not replace `DisclosureController`: the controller (awareness layer) decides what the
flight context makes *eligible*; the coordinator manages what the player *expanded*.

| Responsibility | How |
|---|---|
| Register transient surfaces | `Register(name, contains, collapse, dismissOnOutsideTap, parent)`: a screen hit test, a collapse callback, whether outside taps may dismiss it, an optional parent. Fixed capacity (16), ids are small integers; `Unregister`, `SetParent`, `Clear`. |
| Maintain the active branch | One branch from the root to the deepest surface (`Depth`, `ActiveRoot`, `Deepest`, `LayerAt`, `IsActive`). |
| Parent and child | `Open(child)` under an open parent closes the parent's other children; `Open(root)` replaces every other cluster (`Replaced`); a child whose parent is closed opens as a root. |
| Opening, replacement, dismissal | `Open`, `Toggle` (the trigger again), `Close(id, reason)` (with its descendants), `NotifyClosed(id)` (the owner closed itself: no callback), `CollapseAll(reason)`, `CollapseDismissable(reason)`. |
| States | `Closed`, `Opening`, `Open`, `Closing`; `Opening` and `Closing` settle after `TransitionSeconds` (the fade time, 0 with reduced motion) in `Tick`; `NotifyOpened` settles early. Opening counts as active; closing does not. `StateChanged` for visual components. |
| Focus loss and transitions | `OnFocusLost`: a branch whose root an outside tap could dismiss folds; a paused interface is left as it is. `Reset` (a new flight) collapses everything. The flight's `Restart` calls it; the game modes collapse everything when the flight freezes (`Blocked`). |
| Outside taps | `WantsOutsideTap(point)` (the press-time question) and `OutsideTap(point)` (the tap). The branch is cut before any owner is told, so an owner that opens something else from its collapse callback keeps it. |

Collapse reasons: `OutsideTap`, `ParentTap`, `Trigger`, `Replaced`, `FocusLost`,
`Reset`, `Blocked`, `Owner`.

### Routing (`InteractionRouter`, `InteractionSurface`)

A new route, `Dismiss` (tap only), is chosen for a press on the open surface when the
coordinator wants it, in every view and in both interfaces, after the `Blocked` check
and before the Target, Cockpit and World routes. Hotspot routes (the handle, the band,
the instrument face, the sheet, the entry) are never redirected: a press on another
trigger replaces the cluster. The surface owns the coordinator (`InteractionSurface.Disclosure`),
ticks its states, hands it focus loss and performs the outside tap itself in `Drain`, so
no session wiring can forget it. No full-screen raycast blocker was added: the surface
that already receives every untaken press does the consuming.

### The arc, tap-first (`GestureSession`, `SystemsArcView`, `GestureHudFactory`)

- **Tap SYSTEMS**: the arc unfolds and stays open, with every reachable system on it:
  the actions on the band and the lights on the outer ring (no resting needed).
- **Tap a segment**: an action without options acts (PARK BRAKE, APPROACH, FLAPS
  opens the detent selector as the rail does) and the arc folds. An action with
  options (NAV, ASSIST) fans them out on the outer ring as a child surface; by
  convention the first option is the action itself (GUIDE, SETTINGS), so the tap path
  loses nothing the rail offers. Tap an option: chosen, the arc folds. Tap the open
  action again: back to the arc. A segment that is not available now (the parking
  brake in the air) answers with a tick and stays.
- **Tap the handle again, or outside**: folds.
- **The pull remains** as the continuous way through the same arc, and a slide that
  starts on the open arc and releases on a segment chooses it.
- The open arc takes presses through a `HotspotShape` (a raycast target shaped by a
  filter): only the band and, when shown, the outer ring are targets, so the hit area
  never covers the throttle, the brake or a banner, and a folded arc intercepts nothing
  (its `CanvasGroup` blocks raycasts only while open).

### What else changed

| Interface | Before | Now |
|---|---|---|
| Cockpit focus cards | × close button, tap the instrument, tap nothing in the cockpit | tap outside (consumed), tap the instrument, the trigger again, a camera change; no close button |
| Flight settings card | × close button | tap outside (consumed), ASSIST again; no close button |
| Coaching explanation | "×" glyph; tap the message to fold | "–" glyph; tap the message or outside it to fold |
| Pause sheet scrim | a tap resumed | a tap folds the settings to the first stop, or does nothing; RESUME resumes |
| Settings screen scrim | took touches, did nothing | a tap closes the settings (as DONE) back to the pause card or the entry screen |
| HUD band | pull only | unchanged as a continuous preview; a tap on it counts as a tap outside; the profile button remains the tap twin |
| Explanation plate (hold) | shown while held | unchanged: it is tied to the held finger, never a surface of its own |

Presses on a panel that has no handler of its own bubble to the nearest ancestor with
one in uGUI; the settings panel and the pause sheet's surface therefore carry a
catch-all `HudButton` (no feedback), so a tap between rows never reaches the scrim's
handler. The legacy comparison mode (SYSTEMS and the rail) keeps its behaviour; its
panels follow the same dismissal contract.

## Tests

EditMode `DisclosureCoordinatorTests`: opening and closing with states and reasons;
an owner closing itself; toggle; nested surfaces closing with their parent, a child
without its parent as a root, reopening a parent; cluster replacement with children,
siblings under one parent; outside taps outside everything, inside the deepest
surface, on a parent; surfaces without a hit test; a paused interface through outside
taps with its settings folding back to it; `CollapseAll`; focus loss folding flight
transients and leaving a paused interface alone; reset; opening and closing states
settling after the transition and bad time steps; rapid repeated opening and closing;
capacity, unregistering, reparenting, clearing; a collapse callback that opens
something else. `InteractionRouterTests`: the Dismiss route in every view and
interface, hotspots and overlays keeping their routes, tap-only mask.

PlayMode `TapDisclosureTests` (real `FlightSession`, Rigidbody aircraft, touch
controls, both HUDs, the flight settings card, the focus panel, and the game modes with
the pause sheet where needed): the handle opens and keeps the arc, again closes;
a segment acts like the rail and the arc folds; FLAPS opens the detent selector, a
detent applies through the gate, an outside tap closes it; a tap outside the arc folds
it, is consumed and never becomes a look; a drag outside does nothing; the band is
inside, the gap to the ring is outside; ASSIST fans out, again returns, outside folds
everything; SETTINGS on the sub-arc switches to the flight settings cluster, which an
outside tap closes; a profile on the sub-arc; opening the arc closes the flight
settings; a joystick finger is never an outside tap and keeps flying; the hit area
covers no flight control and takes nothing when folded; a pending engine-off
confirmation is cancelled by the outside tap with nothing applied; warnings persist
through outside taps and a tap on a banner; the paused sheet never resumes on an
outside tap and folds its settings back; rapid repeated taps; focus loss folds the arc
with nothing applied; reset folds the flight settings and the arc; the pull still works
and a slide on the open arc chooses. `CockpitTests` and `FlightAssistPlayModeTests`
were updated for the removed close buttons.

What the tests cannot show: whether a tap on the arc band is comfortable for a thumb,
whether an outside tap is ever an accident, how the folded "–" reads. Device plan.

## Limitations and open points

- The arc hit area is shaped by the session's filter at runtime; a press on the arc
  while it is folding (the 0.16 s fold) goes through to the world, as intended.
- A segment with options opens them on a tap, but a pull released on the same
  segment still performs the action (the gesture grammar of M13: release commits).
  The two grammars are each consistent; a device test should say whether one should win.
- The lights ring shows on every tap-open of the arc. Progressive disclosure of that
  ring (hidden until needed) is a layout decision for the next milestone.
- Focus loss folds the flight-time transients; a paused interface keeps its state.
  Coming back from another app while the sheet was at the settings finds it there.
- The HUD band has no tap-first ladder; the profile button is its tap twin. A tappable
  density ladder is possible under the same contract if wanted.
- The explanation plate (hold to explain) is not a registered surface by design.
- Nothing here is compiled or run until CI does it (`tests.yml`).
