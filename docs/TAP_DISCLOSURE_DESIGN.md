# Open Skies: tap-first disclosure (UI redesign, milestones 1 and 2)

The transient interfaces of the HUD, the cockpit, the coaching, the settings and
the pause no longer need a hold or a drag to open, to go deeper or to close. A
visible trigger is tapped, an option is tapped, and a tap anywhere else puts the
HUD back in its quiet state. One coordinator knows what the player has expanded
and decides what an outside tap collapses; the interaction surface consumes that
tap so nothing underneath can react to it.

Milestone 1 made every transient interface tap-first under one coordinator. Milestone 2
turns the systems arc into a polished tap-first **systems wheel** (the section of that
name below); the wheel-picker concept stays, by the owner's decision.

Status: implemented with EditMode and PlayMode tests. Milestone 1 passed CI (the only
red runs were the CI editor being killed at start-up and one intermittent tip test that
predates it). Milestone 2's status is in its pull request; **neither has been run in the
Editor by hand or touched on a device.** Not in these milestones: the top-left flight
data and the top-right controls (unchanged), the tutorial redesign, an APK.

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

## The systems wheel (milestone 2)

### Interaction

1. **Tap SYSTEMS.** The wheel unfolds from the handle and stays (no finger needed).
2. **Tap an action.** PARK and APPROACH act at once (through the gate, as the rail does)
   and the wheel folds. FLAPS, SWITCHES, NAV (when the mode gives it destinations) and
   ASSIST have options: the wheel **focuses** on them.
3. **Tap an option.** It applies through the authoritative request
   (`FlightSession.RequestSystemChange` for the aircraft, the owner's callback for a
   destination or a profile) and the wheel folds: the selection is complete.
4. **The handle goes back one level**: from the options to the wheel, from the wheel to
   folded. In the focused state the handle carries the action's name and a chevron
   pointing back, so the player always sees where they are.
5. **A tap anywhere else** folds everything and applies nothing. The tap is consumed
   (milestone 1): nothing underneath reacts.

No press-and-hold, no pull, no X. A finger that slides on the open wheel and is released
on a segment or an option chooses it, exactly as a tap there would (a sloppy tap is not
lost); a press that moves on the folded handle and is released on it opens the wheel; a
slightly slow tap (longer than a tap, shorter than a long press) on the handle or the
wheel is still a tap. A long press on the handle still explains it.

### The wheel

| Slot | Segment | Symbol | State line | Options (focused) |
|---|---|---|---|---|
| 0 | PARK | P in a ring | SET when set | none: toggles the parking brake (ground only to set; dimmed in the air) |
| 1 | FLAPS | wing, flap lowered | the detent (UP, 10°...) | one per detent of the aircraft (`AircraftConfiguration.flapDetentDegrees`) |
| 2 | SWITCHES | toggle switch | ENGINE OFF, or LDG ON | LANDING, NAV LTS, ENGINE (each shows ON or OFF; a tap flips it) |
| 3 | APPROACH | runway | ON when shown | none: toggles the approach guide |
| 4 | NAV | compass | ON when shown | the destinations in Free Flight (AIRFIELD, landmarks); otherwise none: toggles the guide |
| 5 | ASSIST | sliders | the profile | SETTINGS (the card), ASSISTED, STANDARD, MANUAL |

- **Fixed positions.** Slots keep their order and angle for the session; the screen fit
  keeps the preferred first angle wherever there is room, so 16:9 and 20:9 phones put
  every action in the same place. A slot the aircraft or the airfield lacks is not shown.
- **Emphasis by phase** comes from the same rules as the rail (`DisclosureController`):
  full where relevant, quiet otherwise, engaged (cyan, with a mark) while on, dimmed when
  not possible now. Nothing is hidden: every system stays reachable by hand. The switch
  panel's relevance is `DisclosureController.SwitchPanelRelevant` (parked; take-off roll,
  initial climb, approach, landing: the landing light).
- **Readable.** Each segment has a symbol (generated in code, `HudSprites.Icon`, no
  imported art), a short label and a state line; the symbols always sit next to words.
  PARK, not BRAKE: the hold-to-brake button is right below the handle.
- **The switch panel.** The landing light, the navigation lights and the engine are one
  segment, as on the aircraft's switch panel. Seven primary segments do not fit on a 4:3
  tablet at full touch size (see the geometry below); grouping these three costs no tap
  (each of them was going to be two taps anyway) and leaves the lights and the engine
  manually reachable from the HUD for the first time.

### The focused state (secondary selection)

The tapped action's options take the band around the action's own angle: the primary
segments fold into it and fade, the options unfold out of it (instant with reduced
motion). One level only; nothing is nested deeper. The options keep the slot step where
they fit and are held between the first slot and the last angle the screen allows
(`SystemsArcModel.Focus`). The hub (the handle) shows the action's name and a back
chevron; the caption beside the wheel says what a tap does (FLAPS: tap a setting · the
handle goes back). The set option is engaged (cyan with a mark, SET or ON).

For the coordinator the focus is a child of the wheel (`Systems wheel options`): the
handle closes it alone; a tap outside the wheel closes both; another cluster (the cockpit
card, the flight settings, the pause) replaces both.

### FLAPS, the reference interaction

Tap SYSTEMS → tap FLAPS → tap a detent. The detents are the aircraft's configured ones,
labelled from its data (UP, 10°, 20°, 30° on the trainer; a configuration with 15° and
40° shows 15° and 40°), in order down the wheel like a lever: further round is more flap.
The current detent is marked; a detent changed by the keys or the cockpit while the
options are out moves the mark at once. The tap sets the detent with one request through
the gate. The cockpit's flap lever keeps its direct manipulation (drag, flick, the
selector) through the same `AircraftSystems` and the same gate: the HUD and the cockpit
can never disagree. The rail's FLAPS (legacy) still opens the cockpit selector.

A focused wheel was chosen over a lever-shaped selector because it is the same picker the
player already holds in mind (no second widget, no drag), the detents keep the wheel's
touch size, and the order down the wheel reads like a lever anyway.

### Hazardous changes

`SystemChangeGate` stays the only way a system changes. ENGINE off in flight is the one
hazardous change: the first tap returns `AwaitingConfirmation`, nothing is applied, the
wheel stays focused and the option turns red with the word CONFIRM and the state ENGINE
OFF (never colour alone); the caption says "Hazardous: tap CONFIRM again, or tap outside
to cancel". The second tap within the gate's window applies it. An outside tap, the
handle (back), another cluster, focus loss or a reset drop the waiting change
(`FlightSession.ClearPendingSystemChange`), so nothing is left armed. If the window runs
out the option simply returns to ENGINE ON.

### Geometry, fitted to the screen

`SystemsWheelLayout.Fit` (pure, `OpenSkies.UI`) chooses the band from the preferred
settings (`SystemsArcSettings`: band 196 to 300 units, first slot at 98°, at most 26°
between slots) and the space around the handle. It searches the largest outer radius,
the preferred band thickness first and then the minimum (96), for which the longest free
run of angles (every radial line through the band clear of the joystick region, the
throttle, the brake, the handle, the four warning slots, the top bar and the session
buttons, 8 units of clearance and 16 from the safe-area edges) holds every slot with
segments of at least 96 units along the middle of the band. The largest band that keeps
the preferred first slot wins; otherwise the largest that fits. It works on a copy of the
settings (the tuning asset is never written at runtime), runs when the screen or the
entries change and never while the wheel is open.

| Screen (1080 units high) | Band | Slots | Segment along the band |
|---|---|---|---|
| 4:3 (1440 wide) | 172 to 276 | 85.8° + 25.7° × i | 100 |
| 16:9 (1920) | 196 to 300 | 87.3° + 24.5° × i | 106 |
| 20:9 (2400) | 196 to 300 | 87.3° + 24.5° × i | 106 |

(Six segments; computed with the fit's own algorithm before CI, to be confirmed by the
EditMode tests.) On 4:3 the band shrinks because the warning column comes close above the
handle; nothing else moves. At the smallest interface scale (0.9) every aspect fits at
the preferred size; the largest scale is already limited on 4:3 by `UiScaleMath`. The
caption sits left of the wheel, in the free space before the joystick region, and hides
itself if fewer than 140 units are left.

The open wheel takes presses only on its segments (`HotspotShape` filtered by the
segments of the current level); the hole between the handle and the band and everything
beyond the band are outside.

### Input

Unchanged architecture: the handle and the wheel are `GestureHotspot`s on the
`SystemsHandle` route of the one `InteractionSurface`; the surface ignores every pointer
the joystick, the throttle or the brake owns (a flight-control finger is never a
dismissal), so flying with the wheel open and focused works as before; the coordinator
decides outside taps. Keyboard and gamepad keep their keys (`[` `]` flaps, `L` `N` lights,
`P`, F4); they change the same systems and the open wheel follows them.

### The pull comparison (development only)

The M13/M14 gesture model (pull the handle to unfold, rest for an action's options on the
outer ring, slide from FLAPS into the lever) remains behind
`FlightSession.pullGestureComparison` for comparison on a device. F4 (or a tap on the
gesture inspector) cycles: tap wheel → pull comparison → legacy rail. It is never
available in a release build (`GestureSession.SetPullComparisonAllowed(Debug.isDebugBuild)`),
whatever a saved preference says. The lights ring of milestone 1 is gone in both: the
lights are on the switch panel.

### Code

| Where | What |
|---|---|
| `SystemsArcModel` (UI, pure) | items gain a state line and a symbol, `ArcEmphasis.Confirm`; the focused state (`Focus`, `Unfocus`, `OptionItem`, `OptionAngle`, `OptionAt`, `HoveredIsOption`, `HoveredItem`, `SlotCentreMax`); `SetSettings` for the fitted geometry |
| `SystemsArcSettings` | new preferred band (196 to 300), end tolerance 4°, the fitting fields, `Clone` |
| `SystemsWheelLayout` (UI, pure) | the fit to the screen |
| `SystemsArcView`, `GestureHudFactory` | symbols, state lines, option segments on the band, the focus transition, the confirmation colour, the caption beside the wheel |
| `GestureHud` | the hub (`SetHub`: the action's name and a back chevron) |
| `HudSprites` | `HudIcon` and `Icon`: the six symbols, 64 × 64, generated once |
| `DisclosureController` (Awareness) | `SwitchPanelRelevant`, `SwitchPanelProminence` |
| `GestureSession` (Gameplay) | the wheel's entries (the switch panel after FLAPS), the tap flow, the options and their requests, the confirmation, the fit, the pull comparison |
| `FlightSession` | `TryGetPendingSystemChange`, `ClearPendingSystemChange`, `pullGestureComparison` |
| `FlightPrototypeSceneBuilder` | an `InteractionConfiguration` asset still holding the untouched M13 arc defaults is moved to the new preferred band once; tuned values are left alone |

### Tests

EditMode `SystemsWheelTests`: six segments fitted at 4:3, 16:9 and 20:9 are full touch
targets and every wedge (with its end tolerance), and every option slot a focus may use,
stays on screen and off the joystick region, the throttle, the brake, the handle, the four
warning slots and the top bar; the preferred first slot is kept wherever there is room
and 16:9 and 20:9 match; the smallest interface scale fits; the preferred settings are
never changed; too little space returns the best geometry and says so; the focus lays the
options out from their parent, held inside the allowed span, narrowing the step only when
needed; the finger finds options, not primary segments, until unfocused; a confirmation
is selectable, a disabled item is not; the switch panel's relevance (never hidden); the
symbols are generated once. The arc model tests of M13 still pass on the new defaults.

PlayMode `SystemsWheelPlayModeTests` (real `FlightSession`, Rigidbody aircraft, touch
controls, both HUDs, the flight settings card, the cockpit focus panel): a tap opens the
wheel with six segments, symbols and labels, no finger held, and the same trigger closes
it; a sloppy tap on the handle still opens it and a pull unfolds nothing; PARK and
APPROACH act through the gate and fold the wheel, changing nothing else; FLAPS shows the
aircraft's own detents (a 0/15/25/40 configuration), the hub says FLAPS, a detent applies
once and the wheel folds, the segment then shows 40° and the option SET; the cockpit
selector and the wheel share the flap state, the open options follow a key change; each
switch option flips its own system only; an owner's options (destinations) and the
assistance profiles are chosen on the wheel; an outside tap on the focused wheel is
consumed, folds everything and applies nothing; active states (SET, 20°, LDG ON, the
switches ON) are drawn; engine off in flight needs the second tap (red, CONFIRM, the
caption), and an outside tap or the handle drops the confirmation; the joystick and the
throttle keep working with the wheel open and focused; the pull comparison never turns on
in a release build; no segment or option overlaps a flight control, the handle or a
warning slot at 4:3, 16:9 and 20:9, the segments are at least 96 units and the caption
stays off the joystick region. `TapDisclosureTests` and `GestureInteractionTests` were
updated: FLAPS now focuses the wheel instead of opening the cockpit card, the options take
the band instead of the outer ring, and the pull tests switch the comparison on.

### Decisions and open points

- **Six primary segments at most** (owner may revisit): at 4:3 the warning column above
  the handle limits the band to an outer radius of about 278 units, and seven segments
  would be about 80 units long. The switch panel groups the landing light, the navigation
  lights and the engine.
- The handle goes back one level instead of folding everything when options are out
  (milestone 1 folded everything); outside taps fold everything in one tap.
- An action's options replace the primary segments on the band rather than opening an
  outer ring, so the wheel never grows towards the warnings.
- Device questions: whether the band at 172 to 276 on a tablet and 196 to 300 on a phone
  is comfortable for a thumb; whether the hub's back chevron is understood; whether the
  slide tolerance ever chooses by accident; how the symbols read at arm's length.
- An `InteractionConfiguration.asset` made before milestone 2 with tuned arc values keeps
  them: the fit starts from them (it never writes them).

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

### The arc, tap-first (milestone 1; superseded by the systems wheel above)

As milestone 1 delivered it, kept for the record: the pull comparison still behaves this
way, except that the lights ring is gone and the handle goes back one level.

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
