# Open Skies: gesture-first interaction design (proposal)

Status: approved by the owner on 3 November 2026; the first vertical slice (Phase 1
plus the camera gestures of Phase 2) is implemented, see `docs/GESTURE_INTERACTION.md`,
`docs/GESTURE_EXPERIMENTS.md` and `docs/GESTURE_TEST_PLAN.md`. The text below is the
original proposal; where the implementation differs, those documents are authoritative.
Original status line: proposal for owner review, 3 November 2026. Nothing here is built. It
responds to the brief that the interface is too basic and that the differentiator
must be a modern, gesture-driven use of progressive disclosure that keeps cognitive
load low on a phone. It is written against the current code and the design rules in
`docs/GAME_DESIGN.md`, `docs/CONTEXTUAL_UI.md`, `docs/TOUCH_CONTROLS.md`,
`docs/COCKPIT.md` and `docs/VISUAL_STYLE_GUIDE.md`.

## 1. Where the current interface stands

What exists is a solid **visibility** system, not yet an **interaction** system.

| Area | Today | Why it reads as basic |
|---|---|---|
| Disclosure | Elements fade in and out by phase of flight (`DisclosureController`). | The player never *does* anything to disclose. Depth is reached through labelled buttons and cards, which is a conventional menu with good manners. |
| Touch vocabulary in flight | Drag (joystick, throttle), hold (brake), tap (every button). | Three gestures. No swipe, no long press, no two-finger gesture outside the cockpit, nothing that scales with how far the finger travels. |
| Systems access | SYSTEMS button plus a vertical stack of up to five 168 × 96 buttons. | A toolbar. It is the opposite of a calm screen and it is the first thing a phone player sees on the right. |
| HUD depth | A cycle button (SMART, DETAILED, CINEMATIC). | Cycle buttons hide the next state; the player cannot preview or reverse without going round. |
| Flaps, lights, ignition | Tap FLAPS, a card opens, tap an option, tap close. | Three or four taps for a one-dimensional lever. |
| Camera | CAM toggles two fixed views; free look exists only in the cockpit. | No way to glance around in the chase view, the view most players use. |
| Entry and pause | Three large buttons; a modal card; a separate tabbed settings screen. | Functional and clear, but indistinguishable from any utility app. |
| Feedback | Instant press shrink, one tick sound, optional pulse. | Good foundations; nothing yet tracks the finger. |

What is worth keeping unchanged, because it is better than most mobile games:

- The floating joystick and relative throttle, and the pointer ownership table
  (`PointerOwnership`). Ownership is exactly the arbitration a gesture layer needs:
  any finger that lands on a flight control is never a UI gesture.
- The phase estimator with hysteresis, the no-flicker rules and the warning rules.
- Fixed homes, the free column, the 96-unit targets, reduced motion, the colour roles.
- The cockpit pick tolerance and the "tap selects, drag looks" priority.

## 2. The idea in one sentence

**Disclosure should be proportional to the gesture.** A small movement peeks, a
longer one commits, releasing early cancels. Nothing deeper than the player's
finger has asked for is ever on screen, and nothing needs a second tap to undo.

This gives three new layers of depth that are reached by movement rather than by
navigation, on top of the phase-driven fades that already exist:

| Layer | Reached by | What appears |
|---|---|---|
| Peek | A short drag from a handle, or a long press | A preview: the lever, the arc of actions, the explanation of a value |
| Commit | Continuing the same drag past a threshold, then releasing | The change is made; a haptic tick and a sound confirm it |
| Dwell | Holding at the committed point | The next level of detail (an option, a sub-setting), still under the same finger |

Modes are spring-loaded: a glance, a peek, a free look all end when the finger
lifts. The player never has to remember to put the interface back.

## 3. The gesture vocabulary

Every gesture starts outside the joystick region and the throttle's touch area
(ownership guarantees this) and is cancelled by the same revocation rules as the
flight controls (focus loss, lost release).

| Gesture | Where | Result | Replaces |
|---|---|---|---|
| **Pull from the systems handle** (one thin tab above BRAKE, where SYSTEMS is now) | Right thumb | An arc of large segments fans out from the thumb: contextual actions first (by phase, as today), the rest further round. Slide onto a segment and release to act; release off the arc to cancel. | SYSTEMS and the five-slot rail |
| **Flaps lever** | Slide onto FLAPS in the arc and keep dragging up or down | A vertical lever with four detents follows the thumb; each detent ticks (haptic, sound). Release sets it. | FLAPS card (three taps) |
| **Long press** on any HUD value, guide, warning or rail item | Either thumb | A one-line explanation on a plate next to it ("VS: feet per minute up or down. Aim for 0 in cruise."). Lifts with the finger. | Nothing today; the coach's explanation texts are reused |
| **Swipe down / up on the top band** (above the joystick region, outside buttons) | Either | One layer more / one layer less of HUD (Cinematic, Smart, Detailed), with the strip showing what the next layer adds during the drag, so it can be reversed before release. | HUD profile cycle button (kept as fallback and for keyboard H) |
| **Drag on empty sky, chase view** | Either | Free look around the aircraft; springs back to the chase position on release. | Nothing today |
| **Pinch / spread** | Either view | Zoom: chase distance in the external view, field of view in the cockpit (same meaning in both views). | Cockpit pinch (unchanged), new in chase |
| **Double tap on empty sky** | Either view | Chase to cockpit and back, with a short camera move (0.3 s, instant with reduced motion). | CAM (kept) |
| **Two-finger tap** anywhere outside the flight controls | Either | Pause. | MENU (kept) |
| **Pull up the pause sheet** | Paused, free column | A sheet: the first stop shows RESUME, RESTART, MODE; pulling higher opens settings in the same sheet, grouped by what the player is doing (flying, looking, hearing, device). | Pause card plus the separate settings screen |
| **Swipe left / right on the entry screen** | Either | The camera moves around the parked aircraft between three stations (First Flight, Free Flight, Landing Challenge); the station's one-line description and progress are at the bottom; tap the aircraft or swipe up to start. | Three stacked buttons |

Rules the vocabulary obeys:

1. **A gesture never produces flight controls.** The gesture layer raises the same
   requests the buttons raise today (`FlightSession.RequestSystemChange`,
   `SetHudProfile`, `ToggleCamera`, pause). It cannot reach `FlightControls`.
2. **Every gesture has a visible, tappable fallback.** The handle, CAM, MENU, HUD
   and the cards stay for players who do not discover or cannot perform the
   gesture (accessibility, gamepad, keyboard, WebGL with a mouse).
3. **Peek before commit, always cancellable.** No gesture changes a system before
   the release, and releasing off-target does nothing.
4. **Hazardous changes keep their confirmation.** Engine off in flight stays a
   deliberate second action, through `SystemChangeGate` as today.
5. **Fixed homes still hold.** Arcs, levers and sheets grow from a fixed anchor and
   collapse back to it; nothing else on screen moves to make room.
6. **Warnings override.** A banner interrupts nothing and is interrupted by nothing;
   a long press on a banner explains what to do (the safety text the coach uses).
7. **Reduced motion** turns spring-backs and arc fans into instant states; the
   gestures still work.

## 4. Progressive disclosure, end to end

| Player | What they meet | What they never see unless they ask |
|---|---|---|
| First launch | The world, the joystick region, the throttle, one handle, a one-time hint "pull for systems" when the phase first offers something. | Rail, profile button, cards, settings. |
| Taking off, landing | The phase-driven elements as today, now with the contextual actions *inside* the arc, nearest the thumb. | The rest of the arc. |
| Curious | Long press for the meaning of anything on screen. | Any documentation screen. |
| Enthusiast | Swipe down for Detailed; cockpit; dwell in the arc for lights and ignition; the systems layer later hangs off the same arc. | Nothing is locked; depth is a matter of reach. |

Discovery is taught by use, not by a tutorial screen: a hint appears once, at the
moment the feature is first relevant, as the First Flight coaching already does.
`PlayerProgress` records which gestures the player has performed so the hint is
never repeated.

## 5. Constraints and risks, stated plainly

- **Android system gestures.** Android 10 and later reserve both screen edges for
  the back gesture and the bottom edge for home. Edge swipes are therefore not
  used; every gesture starts from a handle or from open space inside the safe area
  and the 24-unit screen margin. Unity exposes no public API for system gesture
  exclusion rects, and I would not add a native plugin for this.
- **Conflicts with the joystick.** The joystick region covers the lower-left
  42 % × 78 % of the screen. Gestures on "empty sky" are limited to the area above
  it and to the right of it. On 20:9 phones the band above the joystick region is
  about 240 canvas units tall; the swipe for HUD density has to work there. This
  needs a device test, not a guess.
- **Rudder.** A touch rudder or nosewheel control is still an open decision
  (`docs/ROADMAP.md`). The bottom edge between the joystick region and the throttle
  is kept free for it; no gesture in this proposal uses that space.
- **Discoverability versus calm.** Hidden gestures are the classic failure mode of
  gesture interfaces. The mitigations above (visible handle, one-time contextual hint,
  every gesture has a button twin) are the minimum; the measure of success is the
  device test in section 8, not the design.
- **The owner's existing decisions.** "No icon-only buttons" and "fixed homes" are
  kept. The arc segments carry short labels. The HUD profile and camera buttons stay.
- **Verification.** Nothing in this project has yet been seen on a device, and CI
  cannot activate a Unity licence at the moment. Gesture feel cannot be judged from
  tests. I would build the gesture recognition as pure C# with EditMode tests, drive
  the widgets with synthetic pointers in PlayMode as the touch controls do, and then
  stop for a phone test before building the second phase.

## 6. Architecture

```
Touch (uGUI) ─▶ joystick / throttle / brake / HUD buttons (first claim, unchanged)
             └▶ HudGestureSurface (OpenSkies.UI): full screen, drawn behind the buttons,
                in front of the cockpit touch surface; ignores pointers owned in PointerOwnership;
                claims the ones it uses (owner "Gestures")
                  └▶ GestureRecognizer (pure C#, OpenSkies.Input or a new OpenSkies.Gestures,
                     references nothing): tap, double tap, long press, swipe, pull, pinch, two-finger tap,
                     with slop, timing and thresholds in a settings class
                       ├▶ SystemsArc / FlapsLever / ExplanationPlate / PauseSheet (OpenSkies.UI) draw the peek
                       └▶ FlightSession requests (Gameplay): RequestSystemChange, SetHudProfile, ToggleCamera, pause
CameraRig (CameraSystems): chase free look with spring-back, chase distance
DisclosureController (Awareness): one new input, "arc open", replacing "rail expanded"; the per-phase
  action table is unchanged and the EditMode tests keep passing
```

- The cockpit view keeps `CockpitInteractionManager` for taps on instruments; the
  gesture surface sits in front of it and passes on what it does not recognise
  (single-finger drag and pinch in the cockpit stay cockpit look and field of view).
- Thresholds, timings and arc geometry go in a serialised settings class on the
  surface and in `HudStyle`, not in code, following the project's rule.
- No new packages. Input System 1.20.1 and uGUI 2.6.0 provide everything needed.

## 7. Phased plan

| Phase | Builds | Removes | Tests |
|---|---|---|---|
| 1. Gesture core and systems arc | `GestureRecognizer` + settings, `HudGestureSurface`, the arc with the contextual actions, the flaps lever, long-press explanations | SYSTEMS button and the rail (profile button and CAM stay) | EditMode: recognizer timing, slop, cancel, pinch maths. PlayMode: arc never overlaps the joystick region at 4:3, 16:9, 20:9; a finger owned by the joystick never opens the arc; release off the arc changes nothing; flaps detents through the gate. |
| 2. HUD density swipe and camera gestures | Swipe for profile with preview, chase free look with spring-back, pinch zoom in chase, double tap to switch view | Nothing (buttons stay as twins) | EditMode: spring-back maths. PlayMode: camera gestures never alter `FlightControls`; reduced motion. |
| 3. Pause sheet and entry stations | The pull-up sheet with settings grouped by activity; the entry screen as three camera stations | Separate settings screen and tabbed layout; the three-button entry screen | PlayMode: sheet stops, settings round trip, entry choice. |
| 4. Device pass | Thresholds tuned on a phone and a tablet; hint timing | | The checklist in section 8 |

Phase 1 is the one that changes how the game feels on the right thumb and it is
self-contained. I would stop after it for a device test.

## 8. What to measure on a device

- Taps and seconds from "I want flaps 20" to flaps 20, before and after.
- Misfires: an arc opened by a finger meant for the throttle, or a joystick touch
  that opened nothing (should be zero by construction; verify).
- Whether a new player finds the arc without the hint, and with it.
- Whether the HUD density swipe is ever triggered by accident on a 20:9 phone.
- Whether free look in chase is used, and whether anyone tries to fly with it.

## 9. Decisions needed from the owner

1. Approve the direction (section 2) and the vocabulary (section 3), or strike items.
2. Confirm that SYSTEMS and the rail may be removed in favour of the handle and arc
   (the contextual action table and its tests stay).
3. Confirm Phase 1 as the first build, with a device test before Phase 2.
4. Settle the touch rudder decision separately; this proposal reserves the space.
