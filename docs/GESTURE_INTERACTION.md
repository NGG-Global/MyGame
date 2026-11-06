# Open Skies: gesture-first interaction

The experimental interaction model approved on 3 November 2026
(`docs/INTERACTION_DESIGN_PROPOSAL.md`): the first vertical slice (M13) and the
"Gesture-First 2.0" milestone (M14) that extends it to the cockpit
(`docs/COCKPIT_DIRECT_MANIPULATION.md`), to pausing and entering
(`docs/GESTURE_NAVIGATION.md`), and to discovery (below). The principle:

**Disclosure is proportional to the gesture.** A small movement shows a preview,
a larger one reveals the control, continuing arms an action, releasing commits it,
moving away cancels it, and resting reveals the next level. The interface answers
the finger continuously.

**Since the UI redesign (milestone 1, `docs/TAP_DISCLOSURE_DESIGN.md`) no hold or
drag is required** to open, navigate or dismiss a HUD system: a tap on SYSTEMS opens
the arc and keeps it, a tap on a segment acts or fans its options out, and a tap
outside collapses whatever is open (the tap is consumed). The continuous gestures of
this document remain for flight controls, camera look, the map, the cockpit
instruments, the band and the sheet. One `DisclosureCoordinator` keeps the branch of
expanded interfaces.

**Since milestone 2 the systems arc is the tap-first systems wheel**
(`docs/TAP_DISCLOSURE_DESIGN.md`, "The systems wheel"): tap SYSTEMS, tap an action, tap
its option (FLAPS shows the aircraft's detents). The rows below marked *comparison* (the
handle pull, the rest for a sub-arc, the slide into the flap lever) are now a
development-only comparison (`FlightSession.pullGestureComparison`, F4), never in a
release build.

Status: implemented in code with EditMode and PlayMode tests; compiled offline
against stand-ins for the Unity API; **not yet compiled by CI, not yet run in the
Editor and not yet touched on a device**. Every geometry, threshold and timing
below is a first proposal to be tuned on a phone (`docs/GESTURE_TEST_PLAN.md`).

## Vocabulary

| Gesture | Where | Peek | Manipulate | Armed | Release | Fallback |
|---|---|---|---|---|---|---|
| **Pull the systems handle** (*comparison*) | The 168 × 96 handle left of the throttle, above BRAKE (where SYSTEMS was) | Past 2 % of the screen height the arc starts unfolding | By 9 % it is fully unfolded; the thumb slides onto a segment, which lifts | A selectable segment under the thumb (cyan, dark label) | Does what the segment says; the arc folds | HUD profile button, keyboard keys, cockpit switches |
| **Continue into the flap lever** (*comparison*) | From the FLAPS segment, the same finger | Resting 0.18 s on FLAPS (or pulling 30 units beyond the band) folds the arc and puts a vertical lever under the thumb, the current detent at the finger | Down extends, up retracts, with detent snapping and hysteresis; the knob is held by its stops | The previewed detent differs from the aircraft's | Sets that detent through the gate; nothing if unchanged | Release on FLAPS without the lever: one detent down (wrapping to UP); `[` `]` keys; cockpit lever |
| **Rest while the arc is open** (*comparison*) | On a segment, 0.5 s without moving | | A sub-arc unfolds centred on the rested segment: LIGHTS opens LDG LIGHT and NAV LIGHTS; NAV opens the destinations (airfield, then each landmark); ASSIST opens the assistance profiles. The current choice is marked | A sub-arc item under the thumb | Chooses it (the same request the selector or MENU › GUIDE makes) | Cockpit switches, L and N keys, MENU › GUIDE, the assistance selector |
| **Tap the handle** (primary) | | | | | The wheel opens and stays, every system on it; tap a segment to act or focus its options, tap an option to apply it; the handle goes back one level, outside folds (`docs/TAP_DISCLOSURE_DESIGN.md`) | |
| **Long press** (0.45 s, still) | The handle, a flight value, a guide, a warning banner | A thin ring fills under the finger from 20 % of the hold | | | An explanation plate appears next to the element and stays while held; lifting hides it | Coaching explanations, this document |
| **Pull the HUD band** | The band over the flight data strip (top left) | Past 2 % the density ladder appears with the committed stop at the finger | Down is more HUD, up is less: Cinematic, Smart, Detailed, 110 units apart with hysteresis; the HUD itself shows the previewed level | The previewed level differs from the committed one | Commits the profile; the ladder fades | HUD profile button, H key |
| **Drag the sky** (chase view) | Open world outside every control and target | | The camera orbits the aircraft with the finger (120° per screen height), within limits | | Springs back to the chase position in 0.45 s (at once with reduced motion) | None needed: a look is never a state |
| **Pinch the sky** (chase view) | Two fingers the surface owns | | Chase distance 8 to 30 m, 30 m per screen height of spread | | | Fixed distance |
| **Double tap the sky** (chase view) | | | | | Switches to the cockpit view | CAM button, C key |
| **Two-finger tap** | The sky (chase view) or the cockpit | | | | Pauses: the pause sheet rises (`docs/GESTURE_NAVIGATION.md`) | MENU, Esc |
| **Pull the pause sheet** | The sheet's grip | | Two stops: actions, then the settings; the depth fades the settings in | | Settles on the nearest stop; pulled below 55 % of the first stop it dismisses and resumes | MENU, RESUME, the settings screen |
| **Swipe the entry stations** | The entry screen | | The three modes slide under the finger; the showcase camera moves with them | | Settles on the nearest station (a flick carries over) | Chips, START, arrow keys and Enter |
| **Drag around a dial** (cockpit focus panel) | The enlarged heading indicator or altimeter | | The heading bug or the subscale follows the sweep; cyan preview; the dial's centre cancels | The previewed value differs | Sets it through `RequestInstrumentSetting` | None required: the defaults fly |
| **Flick a switch** (cockpit) | A switch on the panel | The switch tilts with the finger | | Past 5 % of the screen height | Requests the change through the gate (hazardous: the selector with its confirmation) | Tap: the selector |
| **Drag the flap lever** (cockpit) | The lever on the panel | | The same lever interaction as the arc's | | Sets the detent through the gate | Tap: the detent selector |

Cockpit view keeps its own gestures (`docs/COCKPIT.md`): tap selects, drag on
nothing looks, pinch sets the field of view, double tap looks forward; the
instrument, switch and lever gestures are in `docs/COCKPIT_DIRECT_MANIPULATION.md`.

### Discovery and settings (M14)

- **Contextual cues** (`GestureHints`, `GestureHintView`): a travelling dot and a
  caption at the control, for one gesture at a time, at most 7 s, at most twice a
  session and 12 s apart, never while the player is mid-gesture. Offered: the
  systems pull the first time the phase offers an action; holding an instrument
  the first time the cockpit opens; pulling the sheet when it opens; swiping the
  stations when the entry opens. A cue goes the moment its gesture is performed
  and never returns: the learned gestures are part of `PlayerProgress` (local
  PlayerPrefs, no service). The lever drag, the rotary set and the two-finger
  pause are learned without a cue (the first two follow a cued gesture; pause has
  MENU). Hints can be switched off; the arc's self-showing hint of M13 remains the
  fallback when no hint view is built.
- **Gesture reference**: the GESTURES page of the settings lists every gesture
  and its tappable twin in one screen of text (`SettingsScreen.GestureReferenceText`).
- **Gesture settings** (`GameOptions`, PlayerPrefs through the options store):
  gesture speed (Relaxed, Standard, Quick: scales the long-press and dwell times
  and, inversely, the pull distances through `InteractionConfiguration.TunedGestures`),
  camera gesture sensitivity (Low, Medium, High: scales the look-around gain) and
  hints on or off. Applied by `FlightSession.ApplyGestureOptions`; the surface
  rebuilds its channels only while idle, so a change never breaks a gesture in
  progress.
- **Interaction diary** (`InteractionDiary`, development only): counts of
  completed and cancelled gestures per route and cancel reason, quick cancels
  (under 0.3 s, the likeliest accidents) and the mean pull time, summarised on the
  gesture inspector's last line and, when `FlightSession.recordInteractionDiary`
  is on in a development build, appended to `interaction-diary.csv` in the app's
  data folder. Nothing leaves the device; no analytics service; release builds
  record nothing.

### Rules that never bend

1. **A gesture never produces flight input.** The gesture layer has no path to
   `FlightControls`; it raises the same requests the buttons raise
   (`FlightSession.RequestSystemChange`, `PerformContextAction`, `SetHudProfile`,
   `ToggleCamera`, `CameraRig.AddChaseLook`).
2. **A gesture never steals a flight-control touch.** The joystick, throttle and
   brake claim their pointers first; the surface ignores any owned pointer, and a
   finger that started on a flight control is never re-routed while it moves.
3. **Armed is not applied.** The flap lever's preview and the HUD density preview
   change nothing committed; only a Completed event does, and only through the gate.
4. **Cancelling changes nothing.** Releasing on nothing, moving the lever sideways,
   revocation (focus loss, pause, a lost release) and a view change all end a
   gesture without a request.
5. **Every gesture has a visible, tappable twin**, and the keyboard keys stay.
6. **Warnings stay.** The banners are untouched by the gesture layer; a long press
   on one explains it with the same safety text the coach uses.
7. **Reduced motion** replaces every unfold, settle and spring-back with an instant
   state. The gestures themselves are unchanged.

## Ownership and arbitration

```
Touch (uGUI)
 ├─▶ joystick / throttle / brake / HUD buttons / cards: drawn on top, claim their pointer first (unchanged)
 ├─▶ GestureHotspot (systems handle, HUD band): forwards its press to the surface with an explicit route
 └─▶ InteractionSurface (first child of the safe area, draws nothing): every press nothing else took
        │  ignores pointers already owned; asks InteractionRouter (pure) for the route:
        │  hotspot route → that route (gesture-first only); explainable element → Target;
        │  cockpit view → Cockpit; gesture-first → World; otherwise nothing
        ├─▶ Cockpit: the press, drags and release are forwarded to CockpitInteractionManager, which claims
        │   the pointer itself (its own gestures, unchanged); it no longer has a full-screen graphic
        └─▶ World / SystemsHandle / HudBand / Target: the surface claims the pointer (owner "Gestures")
            and feeds one GestureRecognizer channel per route; events go to GestureSession
```

- One `PointerOwnership` table for everything that touches the screen. The surface
  claims the pointers it recognises, so the touch controls' liveness check (a finger
  no longer in progress on the device) and the focus-loss, pause and disable
  revocations cancel gestures exactly as they cancel a joystick touch.
- Up to six pointers are tracked; a channel takes at most two (pinch, two-finger
  tap). A third finger on a channel is refused and left unowned.
- The cockpit route is explicit forwarding, not a graphic behind the HUD, so the
  routing policy is in one testable function (`InteractionRouterTests`).
- A modal card, the entry screen or the settings mark the surface as blocked (the
  session passes `Time.timeScale <= 0`); their scrims take the touches anyway.

## Gesture states

```
Idle ──press──▶ Candidate ──move past slop──▶ Peek ──reveal distance──▶ ActiveManipulation ──target or distance──▶ Armed
                   │  hold 0.45 s                                                     ▲                     │
                   └──────────▶ LongPress (Armed) ──release──▶ Completed              └──── Disarmed ◀──────┘
                   quick release ▶ Tap / DoubleTap (Completed)                        release ▶ Completed
Any ──revoke, move out, consumer cancel, released unarmed──▶ Cancelled
```

`GestureRecognizer` (pure C#, `OpenSkies.Gestures`) runs this for one channel from
raw samples with explicit times. A consumer refines arming with `SetTargetArmed`
(the arc says whether a selectable segment is under the thumb; the lever says
whether it is cancelled) and can `Cancel`. Dwell (rest during a pull or drag) is
reported as an event on top of the state. A long press that starts moving becomes
the route's movement gesture, so a thumb that pauses on the handle and then pulls
still opens the arc.

### Completion and cancellation

| Gesture | Completes when | Cancels when |
|---|---|---|
| Tap | Released within 0.3 s and 2 % of the screen height | Held longer (Incomplete) or moved (MovedOut) |
| Double tap | A second tap within 0.3 s and 6 % of the first | |
| Long press | Released after 0.45 s still | Moved past 2 % (MovedOut); revoked |
| Pull | Released while Armed | Released unarmed (back near the handle, between segments, lever cancelled); revoked |
| Drag | Released (a swipe is reported too when fast and straight enough) | Revoked |
| Pinch | One finger lifts (the other becomes inert) | Revoked |
| Two-finger tap | Both quick, both still, second finger within 0.15 s of the first | |

## Visual feedback (one motion language)

| Behaviour | How |
|---|---|
| Unfold | Segments sweep from straight above the handle to their slots as the pull progresses, following a smoothed value with a single soft overshoot of at most 4 %; alpha and a 6 % growth follow the same curve (`HudEasing.SoftSpring`) |
| Lift | The segment under the thumb grows 4 % and brightens over 0.08 s |
| Armed | Cyan fill (`HudStyle.Selected`) with dark text; a tick sound and the lightest haptic pulse when the thumb crosses onto a segment (`UiFeedback.Tick`, rate-limited by the haptic policy) |
| Engaged | Translucent cyan fill and a cyan dot at the segment's outer edge; label BRAKE SET, LDG LIGHT ON |
| Quiet / disabled | 55 % / 35 % opacity; a disabled segment still shows why under the finger ("Not available now") |
| Caption | One quiet line under the arc: "Pull to open", "Release to choose", "Release here: nothing changes" |
| Lever | Appears under the thumb; the knob settles on detents over 0.06 s; preview in cyan, committed in white; "Release to set" / "Release: flaps unchanged" / "Release to cancel"; cancelled state greys out |
| Ladder | Marker follows the finger continuously; previewed stop cyan, committed stop ringed; "Release to apply" |
| Hold ring | A thin ring fills clockwise under the finger from 20 % of the long-press threshold; it never appears for a quick tap |
| Explanation | A plate below (or above) the element with a bold title and two lines; fades with the usual 0.2 s and a 10-unit rise; never over the joystick region |
| Fold | 0.16 s ease, dimming towards the end; the handle's chevron returns |
| Reduced motion | Everything above is instant; nothing moves |

Nothing bounces, flashes or glows. Colours, sizes and radii are the existing tokens
(`docs/VISUAL_STYLE_GUIDE.md`); the arc segments are radial fills of a generated
annulus sprite, so no art is imported.

## Gesture priorities

1. Flight controls and HUD buttons (draw order and ownership).
2. A hotspot's route (handle, band) in the gesture-first interface.
3. An explainable element under the press (Target) in the gesture-first interface.
4. The cockpit in cockpit view.
5. The world in the external view, gesture-first only.

Inside a channel, the recognizer decides by movement and time: still and quick is a
tap, still and long is a long press, moving is a pull or drag; a second finger
turns a candidate into a pinch or a two-finger tap and ends a drag.

## Accessibility fallbacks

| Gesture | Fallback |
|---|---|
| Arc and lever | The legacy SYSTEMS rail (development setting), the cockpit switches and levers, keys `P` `[` `]` `L` `N` |
| HUD density pull | The HUD profile button (always shown), key `H` |
| Camera gestures | CAM button, key `C`; the chase view works without ever looking around |
| Explanations | Coaching explanations in the First Flight; nothing is only explained by a long press |
| Reduced motion | Follows the system or the setting; gestures are unchanged, transitions are instant |

Every target is at least 96 units tall (the handle, the band, each arc segment
along and across the band). Labels stay on every control.

## Configuration

| Where | What |
|---|---|
| `InteractionSurface.settings` (`GestureSettings`) | Tap, double-tap, long-press and dwell times; tap, drag and pull distances (fractions of the screen height), pull hysteresis; swipe distance, time, velocity and direction tolerance; second-finger window, two-finger tap time, pinch minimum change |
| `SystemsArcView.settings` (`SystemsArcSettings`) | The preferred inner and outer radius, outer ring gap and thickness, selection inner radius, first slot angle, slot step, segment gap, end tolerance, folded angle, selectable-from progress, hint time, unfold, fold and hover times; the fit to the screen (on, clearance, margin, largest step, thinnest band, shortest segment). The session fits a copy to the screen (`SystemsWheelLayout`) |
| `FlapsLeverView.settings` (`FlapsLeverSettings`) | Detent spacing and hysteresis, over-travel, cancel and un-cancel distances, settle time |
| `HudDensityLadder.settings` (`HudDensitySettings`) | Step distance and hysteresis |
| `FlightSession.gestureSettings` (`GestureSession.Settings`) | Lever handover rest time and overreach, look degrees and zoom metres per screen height, hint limit and spacing, hold ring start |
| `Assets/_Game/Settings/HUD/InteractionConfiguration.asset` (`InteractionConfiguration`, M14) | One asset the scene builder creates and `FlightSession.interactionConfiguration` references: `gestures`, `arc`, `lever`, `density`, `rotary`, `mapView`, `sheet`, `entry`; the views keep their own settings for standalone tests. Motion tokens (`HudStyle.UnfoldSeconds`, `FoldSeconds`, `HoverSeconds`, `SettleSeconds`, `ReturnSeconds`, `EmergeSeconds`, `SheetSettleSeconds`) are design tokens, in code |
| `GameOptions` (M14) | Gesture speed, camera gesture sensitivity, hints |
| `CameraRig` | Chase look limits, return time, chase distance limits |
| `FlightSession.interactionMode`, `pullGestureComparison` | Gesture-first (default) or legacy; the pull comparison (development builds only); F4 and a tap on the gesture inspector cycle tap wheel, pull, legacy; the `OpenSkies.Dev.InteractionMode` and `OpenSkies.Dev.PullComparison` preferences |

Detent labels come from `AircraftConfiguration.flapDetentDegrees` through
`AircraftSystems`; the explanation speeds come from the aircraft's envelope and the
warning limits. Nothing is typed in by hand.

## Architecture

| Assembly | New code |
|---|---|
| `OpenSkies.Gestures` (new; references FlightInput, Unity.InputSystem, UnityEngine.UI) | `GestureTypes`, `GestureSettings`, `GestureMath`, `GestureRecognizer` (pure state machine), `InteractionRouter` (pure policy), `InteractionSurface` (uGUI adapter, ownership, channels, cockpit forwarding), `GestureHotspot` |
| `OpenSkies.UI` (now references Gestures) | Models in pure C#: `SystemsArcModel`, `FlapsLeverModel`, `HudDensityModel`; views: `SystemsArcView`, `FlapsLeverView`, `HudDensityLadder`, `ExplanationPlate`; `HudExplanations` (texts from the aircraft data), `HudEasing`, `GestureHud` + `GestureHudFactory`, `GestureInspector`; `ContextualHud.SetLegacyRailEnabled`, `HudSprites.Annulus`, `HudButton.Report`, `UiFeedback.Tick` |
| `OpenSkies.CameraSystems` | Chase free look with spring-back and chase distance on `CameraRig`; `ChaseCameraMath.OrbitPosition` |
| `OpenSkies.Gameplay` | `GestureSession` (interaction state, feeds the views, raises requests), `InteractionMode`; `FlightSession` wires the surface, previews the HUD profile for disclosure, `PerformContextAction`; `HudPreferences` interaction mode; `PresentationSession` plays ticks |
| M14 additions | `OpenSkies.FlightState`: `InstrumentSettings`. `OpenSkies.UI`: `RotaryModel`, `MapViewModel`, `SheetModel`, `EntryStationModel` (pure), `PauseSheet` + factory, `EntryStationsScreen` + factory, `GestureHintView`, `InteractionConfiguration`, the GESTURES settings page, `SystemsArcModel.RevealSubArc`, `CockpitFocusPanel` emerge. `OpenSkies.Cockpit`: `CockpitInteractionManager` control drags and the two-finger tap, `CockpitSwitchView.SetPreview`. `OpenSkies.CameraSystems`: `CameraMode.Showcase`, `CameraRig.SetShowcase`. `OpenSkies.Missions`: `GestureId` and the learned gestures in `PlayerProgress`. `OpenSkies.Options`: the gesture options and their scales. `OpenSkies.Gameplay`: `FlapsLeverInteraction` (one lever for the arc and the cockpit), `InstrumentManipulation`, `EntryShowcase`, `GestureHints`, `InteractionDiary`; `GameModeSession` opens the sheet and the stations; `FlightSession.RequestInstrumentSetting`, `ApplyGestureOptions`, `SetGestureHints` |
| `OpenSkies.Editor` | The scene builder builds the surface, the cockpit interaction on its own object and the gesture HUD |

The development inspector (top right, under the disclosure inspector, Editor and
development builds only) shows the interface mode, pointer ownership, each active
channel's kind, state and progress, the last cancellation, what the session is
doing, the target, the armed action and the HUD profile with its preview. Tapping
it switches the interface mode.

## Tests

EditMode: `GestureRecognizerTests` (taps, double taps, rapid taps, long press and
its transition into a pull, pull peek / reveal / arming / hysteresis / target
arming / consumer cancel, dwell, drag and swipe, pinch, two-finger tap, third
finger, revocation, queue overflow, settings), `InteractionRouterTests`,
`GestureModelTests` (arc slots, wedges, emphasis, outer ring, unfold; lever
detents, hysteresis, cancel, committed change underneath; density levels and
hysteresis), `ChaseCameraMathTests` (orbit position).

PlayMode (`GestureInteractionTests`, real `FlightSession`, Rigidbody aircraft,
synthetic pointers at 16:9, 4:3 and 20:9): a joystick finger never reaches a
gesture; joystick and arc pull coexist with flight input untouched; legacy mode
routes nothing but the cockpit; a pull released on nothing changes nothing; release
on NAV makes the same request as the button; the parking brake through the gate
and its engaged emphasis; revocation mid-pull; the flap lever previews and commits
once on release, cancels sideways; the HUD band previews, reverts and commits; the
profile button still works; chase free look, spring-back and untouched controls;
double tap switches view; pinch within limits; explanations on the handle and on a
flight value; layout clear of the flight controls and the warning banner; reduced
motion.

M14: `InstrumentManipulationTests`, `GestureNavigationModelTests`,
`GestureDiscoveryTests` (hints shown once, hidden when learned, remembered
between sessions, time-out, cooldown, session limit, off switch, no view; gesture
options defaults, sanitising and scales; the diary's counts) in EditMode;
`CockpitManipulationTests`, `PauseSheetTests`, `EntryStationsTests` in PlayMode
(listed in the two M14 documents).

What the tests cannot show: feel, reach, accidental activations, discoverability.
That is the device plan.
