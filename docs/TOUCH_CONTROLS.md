# Open Skies: touch flight controls

Layer 0 of the progressive-disclosure design: the minimum needed to fly, with
nothing over the centre of the screen.

## Layout (landscape, inside the safe area)

| Control | Where | Thumb | Behaviour |
|---|---|---|---|
| Floating joystick | Invisible touch region over the lower-left ~42 % × 78 % of the screen | Left | The base appears where the thumb lands (kept fully inside the region); the knob follows the thumb. Drag **down = stick back = nose up**; left/right = roll. Release returns the stick to neutral. It never levels the aircraft. |
| Throttle | Lower right: slim 64 × 460 track inside a 220 × 620 touch area | Right | Drag up/down to change 0-100 %. Relative drag: touching never makes it jump. Stays where it is left. The label shows the percentage. |
| Brakes | Left of the throttle, 168 × 112 | Right | Hold to brake (main wheels). Shown only with weight on wheels; fades out after lift-off and releases any finger on it. |
| CAM | Top right | Either | Switches chase/cockpit. Never changes flight input. |
| RESET | Top right, left of CAM | Either | Back to the runway start at rest, throttle idle. |
| HUD profile | Top right, left of RESET | Either | Cycles Smart, Detailed, Cinematic. Never changes flight input. |
| LOOK FWD | Top right, left of the HUD profile button | Either | Cockpit view only, while the view is turned more than 10° away: returns it forward smoothly. Never changes flight input. |
| SYSTEMS and action rail | Left of the throttle: SYSTEMS directly above BRAKE, contextual actions stacked above it | Right | SYSTEMS (always visible) opens every available action; actions appear by phase of flight. ASSIST opens the flight assistance and stick sensitivity settings. See `docs/CONTEXTUAL_UI.md`, `docs/FLIGHT_ASSISTANCE.md`. |
| Cockpit (cockpit view) | Any touch none of the above took | Either | Tap an instrument or switch, drag to look, pinch for field of view, double tap to look forward. See `docs/COCKPIT.md`. |

Sizes are canvas units at the 1920 × 1080 reference; the canvas scales by
height, so controls keep the same proportion of screen height on phones and
tablets (the player's interface scale, 90-110 %, multiplies this; see
`docs/CONTEXTUAL_UI.md`). All visuals are translucent outlines and fills generated at
runtime (`TouchControlSprites`) with a soft dark edge for bright skies; there are no
opaque panels. The joystick region and the throttle's touch area draw nothing
(`RaycastOnlyGraphic`): they catch touches without overdraw.

Active states (M12): the held joystick knob grows by 10 % and brightens; the held
brake turns cyan with dark text; the throttle brightens while held and shows marks at
25, 50 and 75 %. Feedback is immediate: no animation delays any input. The player's HUD
opacity dims the controls only partly (never below 80 % of their normal opacity).

Rudder has no touch control yet. In the air, the Assisted and Standard
profiles coordinate turns automatically (`docs/FLIGHT_ASSISTANCE.md`); in Manual a
touch player has no rudder and no trim. **On the ground, touch-only players cannot
steer the nosewheel**.
That needs a design decision (rudder strip, auto-coordination assist, or
roll-to-steer on the ground), see `docs/ROADMAP.md`.

## Architecture

```
Keyboard/Gamepad ─┐  IFlightInputProvider    FlightInputRouter          ControlResponse   FlightAssistController   AircraftController
Touch controls ───┴──────────────────────▶  (merge, owns throttle) ──▶ (sensitivity) ──▶ (player's profile) ──▶ (physics unchanged)
                                                  ▲ throttle value for display
TouchFlightControls ◀── FlightSession (telemetry: weight on wheels; commands for diagnostics)
```

- `FlightInputRouter` (`Input/`) merges all providers each physics step:
  axes add up and are clamped, brake takes the strongest request, throttle
  changes only by explicit deltas. It owns the single throttle lever, so
  keyboard and touch always agree and nothing else can reset it (except the
  R/reset command, which deliberately returns to idle).
- `TouchFlightControls` (`Input/TouchControls/`) is one provider. It shapes the
  joystick (dead zone, response curve, sensitivity, 40 ms smoothing) and
  collects throttle movement and brake state. The aircraft never knows input
  came from touch, and no aerodynamic code changed.
- Visual layout is built by `TouchControlsFactory` (used by the scene builder
  and the tests). Tuning lives on the components in the Inspector.

## Pointer ownership rules

`PointerOwnership` is the single table of which control owns each active pointer.

1. A pointer is claimed by the control it lands on, at press time, and only if
   it is free. Each control accepts one finger at a time.
2. It keeps that owner until release, cancellation or revocation, wherever it
   moves. uGUI keeps routing drag/release events to the pressed control, and the
   table rejects any other claim for that id.
3. Revocation (control goes neutral; throttle value is kept):
   - app focus loss or pause: everything;
   - a control is disabled or hidden (e.g. brakes after lift-off): its pointers;
   - safety net every frame: a touch whose `touchId` is no longer in progress on
     `Touchscreen.current`, or a mouse/pen pointer whose button is up, is revoked
     even if its release event never arrived.
4. Anything added later that reacts to pointers (camera look, cockpit
   instruments, menus) **must ignore pointers that are owned** in this table.
   The contextual HUD buttons (`HudButton`) act only on a press and release on
   the same button, so a pointer owned by a flight control (pressed elsewhere)
   can never trigger them; hidden HUD actions take no touches. The cockpit
   (`CockpitInteractionManager`) sits on a touch surface behind every control,
   ignores owned pointers and claims the ones it uses, so revocation covers it too.
5. Screen-size, orientation and safe-area changes re-clamp the joystick base
   inside its region without ending the gesture.
6. Camera mode changes do not affect touch controls or input.

Only the modern Input System is used (`InputSystemUIInputModule`,
`Touchscreen`, `Mouse`, `Pen`); no `UnityEngine.Input`.

## Tuning (Inspector)

| Component | Setting | Default |
|---|---|---|
| `FloatingJoystick.settings` | radius (canvas units) | 130 |
| | dead zone (fraction of radius) | 0.08 |
| | sensitivity | 1.0 |
| | response exponent (1 = linear) | 1.6 |
| | smoothing time (s) | 0.04 |
| `ThrottleSlider` | drag gain (1 = track height covers 0-100 %) | 1.0 |
| `TouchFlightControls` | opacity | 0.9 |
| | visibility: Auto (mobile, touchscreen present, or Editor) / Always / Never | Auto |
| | show diagnostics (development builds only) | on |

## Testing in the Editor

- Keyboard flight controls work as before (`docs/FLIGHT_MODEL.md`), alongside
  the touch controls.
- The mouse drives **one** touch control at a time through the same UI events:
  press-drag in the lower left for the joystick, or on the right for throttle
  and brakes. This checks layout, feel and single-pointer logic only. **It is
  not evidence that real multitouch works.**
- The diagnostics line (top left, under the telemetry) shows each active
  pointer id, its type and owner, and the merged pitch/roll/yaw/throttle/brake
  sent to the aircraft.
- The Device Simulator (Window › General › Device Simulator) is useful to
  check layout and safe areas on phone and tablet resolutions.

Automated tests (`Tests/EditMode`: `JoystickMathTests`, `PointerOwnershipTests`,
`FlightInputRouterTests`; `Tests/PlayMode`: `TouchControlsTests`) drive the real
widgets with synthetic pointer events. They prove the ownership and routing
logic, not the device's touch delivery.

## Testing on Android

### Getting a build

- **From CI:** Actions › Build › Run workflow, tick **Development build**, run
  on the branch. Download the `OpenSkies-android-apk` artifact, unzip, then
  `adb install -r OpenSkies.apk` (or open the APK on the device with "install
  unknown apps" allowed). Requires the Unity licence secrets.
- **From a local Editor:** **Open Skies › Build Android APK (Development)**
  (applies the same player settings as CI), then `adb install -r`. Or **Open
  Skies › Apply Android Player Settings** followed by Build Profiles › Build And
  Run. Plain Build Profiles without applying the settings gives the wrong
  orientation, scripting backend and package name. Full steps:
  `docs/PROTOTYPE_TEST_PLAN.md`.

A development build is needed for the diagnostics and telemetry overlays.

### Checklist (phone and, if possible, tablet)

1. Controls are translucent, in the corners, and the aircraft and runway centre are clear.
2. Left thumb anywhere in the lower left: the joystick appears under it. Drag
   down: nose up. Left/right: roll. Release: diagnostics show pitch/roll 0.
3. Right thumb on the throttle: drag up to ~70 %. Release: stays at 70 %.
4. **Simultaneous:** hold the joystick deflected and move the throttle at the
   same time. Diagnostics show two pointers with owners Joystick and Throttle.
5. Release the joystick while holding the throttle: throttle unchanged.
6. Start a touch on the joystick and slide it across to the throttle and the
   camera button: it keeps steering; throttle and camera do not react.
7. On the ground, hold BRAKE with the right thumb while steering roll with the
   left thumb. After lift-off the brake button fades out.
8. Switch camera (CAM) during a turn: stick and throttle keep working without
   a re-touch.
9. Mid-gesture, pull down the notification shade or press Home, then return:
   diagnostics show no pointers, stick neutral, throttle unchanged, and the
   controls respond to new touches.
10. Rotate the device between the two landscape orientations during a gesture:
    the joystick base stays inside its region and control continues.
11. Three or more fingers: extra fingers on empty areas do nothing; each
    control still follows only its own finger.
12. Fly a full circuit with touch only (take-off, turns, landing). Note that
    nosewheel steering is not available from touch yet.

Report any step that fails, with the diagnostics line visible in a screenshot.
