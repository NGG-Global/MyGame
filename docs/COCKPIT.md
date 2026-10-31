# Open Skies: interactive cockpit

Layer 2 of the design (`docs/GAME_DESIGN.md`): the player can fly entirely from
the cockpit view, read the instruments, and work the aircraft's systems with
touch, without precise taps on tiny switches and without ever losing the flight
controls.

## Principles in practice

| Principle | How it is met |
|---|---|
| Instruments are the primary information | Six instruments on the panel; in cockpit view the HUD hides its flight data and navigation guide (`cockpitHasInstruments`, design pillar 4). Warnings still appear. |
| No large duplicate overlays | One focus panel at a time, only on request, in the free column; it closes on any camera change. |
| No precise taps | Every instrument and control has a pick volume larger than its visible part, plus a screen-space tolerance (6 % of the screen height). At the resting view each target is at least about 110 × 150 px on a 1080-px-high screen. |
| Flight controls always accessible | The cockpit only receives touches no flight control or HUD element took, and never handles a pointer owned in the shared `PointerOwnership` table. Joystick, throttle and brake never move. |
| Progressive disclosure for systems | A tap on a switch opens a selector with large options; it never changes the system by itself. Hazardous changes need a second tap. |
| No accidental activation | Tap selection with a slop threshold; drags look around instead; selectors need a deliberate tap on a 96-unit option; engine off in flight needs confirmation; the parking brake cannot be set in the air. |

## Architecture

```
Touch (uGUI) ─▶ joystick / throttle / brake / HUD buttons (take their touches first)
             └▶ CockpitTouchSurface (full screen, behind everything, draws nothing)
                  CockpitInteractionManager ── claims unowned pointers in PointerOwnership
                     tap  ─▶ pick (ray vs pick boxes, then screen tolerance) ─▶ InteractableTapped / EmptyTapped
                     drag ─▶ CameraRig.AddCockpitLook      pinch ─▶ CameraRig.SetCockpitFieldOfView
                     double tap on nothing ─▶ CameraRig.RecentreCockpitLook

CockpitSession (Gameplay) ─▶ CockpitFocusPanel (UI): enlarged InstrumentFace, or system selector
       option chosen ─▶ SystemChangeGate (policy, confirmation) ─▶ AircraftSystems (authoritative)
ContextualHud rail (FLAPS, PARK BRAKE), keyboard ──────────────▶ SystemChangeGate ─▶ AircraftSystems
AircraftSystems ─▶ FlightSession applies it to every control source ─▶ AircraftController (flaps, brake, ignition)
                ─▶ CockpitSwitchView (levers, indicators), AircraftLightsView, DisclosureController (rail state)
FlightTelemetry + RunwayRelation ─▶ InstrumentReadings ─▶ cockpit InstrumentFaces and the focused face
```

| Assembly | New code |
|---|---|
| `OpenSkies.FlightState` | `AircraftSystems` (authoritative switch and lever state), `AircraftSystemsPolicy`, `SystemChangeGate`, `EngineState`; `FlightControls.Flaps` / `.Ignition`; telemetry `FlapPosition`, `FlapDegrees`, `EngineState`. |
| `OpenSkies.Cockpit` (new; references FlightState, FlightInput, CameraSystems, Input System, uGUI) | `CockpitInteractionManager`, `CockpitInteractable`, `CockpitItems` (names, accessible labels), `CockpitLayout`, `CockpitPicking`, `CockpitSwitchView`, `AircraftLightsView`, `TouchSurfaceGraphic`. |
| `OpenSkies.CameraSystems` | Cockpit look, limits, recentring and field of view on `CameraRig`; `CockpitLookMath`. |
| `OpenSkies.UI` | `InstrumentFace`, `InstrumentFaceFactory`, `InstrumentMath`, `InstrumentReadings`, `AirspeedMarkings`, `CockpitFocusPanel`, `CockpitFocusPanelFactory`, `FitToSquare`. |
| `OpenSkies.Simulation` | Flap aerodynamics and actuator, simplified ignition in `EngineModel`, `Aerodynamics.StallSpeed`. |
| `OpenSkies.Gameplay` | `CockpitSession`, `CockpitFactory`, `CockpitRig`; `FlightSession` owns `AircraftSystems` and the gate. |

The cockpit references no Simulation code. No cockpit object has a collider:
picking is pure math, so the cockpit cannot affect the aircraft's collisions,
mass properties or landing-gear raycasts.

## Touch routing and gestures

The touch surface is the first child of the safe area, so uGUI gives every
control drawn after it first claim on a touch. In addition the manager checks the
shared ownership table: a pointer someone else owns is ignored, and the manager
claims the pointers it uses (owner name "Cockpit"), so the touch controls'
liveness checks and focus-loss revocation also cancel cockpit gestures.

| Gesture (unclaimed touches, cockpit view only) | Result |
|---|---|
| Tap (< 0.35 s, < 2 % of screen height movement; 4.5 % when it started on a control) | Select the instrument or control under it, else the nearest within 6 % of the screen height. Selecting the open item again closes it. |
| Tap on nothing | Close the focus panel. |
| Double tap on nothing | Look forward again (0.35 s, eased). |
| Drag | Look around: 100° per screen height of drag; the view turns with the finger. |
| Two fingers | Pinch to change the field of view (40° per screen height). Never a tap or a look. |
| Third finger | Ignored. |
| App loses focus, pause, release lost | Gesture cancelled without a tap. |

Keyboard (Editor, WebGL): **V** look forward, **-** / **=** wider / narrower
field of view (held), **[** / **]** flaps up / down a detent, **L** landing
light, **N** navigation lights, **P** parking brake.

## Camera

| Setting (`CameraRig`) | Default |
|---|---|
| Resting direction | the eye anchor: 12° below the nose, so the panel is in view |
| Look limits from rest | yaw ±135°, pitch −40° to +45° |
| Return to rest | 0.35 s, smoothstep; automatic return after idle: off (`autoRecentreSeconds`) |
| Field of view | 62° (vertical), adjustable 50-80°; remembered between sessions |
| Look direction | kept across camera switches and flight resets during the session |

The camera only reads transforms; nothing in the cockpit view changes flight input.

## Panel layout

Items are placed by viewing angle from the pilot's eye (`CockpitLayout`) on one
flat panel facing the eye:

| Row | Items (yaw from the resting view) | Pitch |
|---|---|---|
| 1 | Airspeed (−1°), attitude (8°), altimeter (17°) | −8° |
| 2 | GPS map (−1°), heading (8°), vertical speed (17°), flap selector (26.5°) | −18° |
| 3 | Ignition (−2°), landing light (5.5°), navigation lights (13°), parking brake (20.5°) | −25.5° |

A test projects every item at the resting view and default field of view: on
16:9, 18:9 and 20:9 screens all centres lie outside the joystick region, throttle,
BRAKE, SYSTEMS/action rail and top bar, and every pick target is at least 96
units wide. **On 4:3 tablets** the altimeter and vertical speed indicator touch
the action rail column and the parking brake and flap selector sit under BRAKE
and the rail; uGUI gives those controls the touch, so look around (drag) to bring
them clear. Tablets are physically larger; this is a known compromise.

## Instruments

| Instrument | Shows | Source |
|---|---|---|
| Airspeed | needle 0-200 kt (0-40 compressed), white/green/yellow arcs and red line, digital window | `IndicatedAirspeedKnots`; arcs from `Aerodynamics.StallSpeed` (clean and full flap) and the warning limits (Vfe 85, Vno 125, Vne 160 kt) |
| Attitude | horizon card with pitch ladder (6 px/°, ±35°) and bank pointer, fixed bank scale and aircraft symbol | `PitchDegrees`, `BankDegrees` |
| Altimeter | hundreds and thousands needles, digital window | `AltitudeFeet` |
| Heading | rotating card, lubber line, digital window | `HeadingDegrees` |
| Vertical speed | needle ±2000 fpm, digital window | `VerticalSpeedFeetPerMinute` |
| GPS map | heading-up map with the home runway, range ring (2/5/10/20/50 NM with hysteresis), runway, distance, bearing, groundspeed, track | `RunwayRelation`, `GroundspeedKnots` |

`InstrumentReadings` is taken once per frame from the shared telemetry and runway
geometry; faces only place needles and text. There is no instrument simulation
(no gyro drift, lag or errors). Digital windows were added because a needle alone
is hard to read on a phone. Cockpit instruments are drawn only in cockpit view.

## Focus panel

One card at a time in the free column (right of the joystick region, left of the
action rail, 17-69 % of the safe-area height), sized in code for any landscape
screen and safe area: the instrument card is the largest square that fits (up to
560 units: about 560 at 16:9, 367 at 4:3); the selector puts options in one row
or two, at least 120 × 96 units each. It never pauses the game and never covers a
flight control; warnings draw over it.

Dismissal: the close button, tapping the enlarged instrument, tapping nothing in
the cockpit, tapping the same cockpit item again, or any camera change. A
dismissed instrument fades out and its face is deactivated, so only the cockpit
copy is drawn.

## Systems: one authoritative state

`AircraftSystems` holds the flap detent, parking brake, landing light, navigation
lights and ignition. Every change, from the cockpit selector, the contextual HUD
rail (FLAPS opens the same selector; PARK BRAKE toggles) or the keyboard, goes
through `SystemChangeGate`:

| Rule (`AircraftSystemsPolicy`) | |
|---|---|
| Parking brake | can be set only with weight on wheels; released anywhere |
| Ignition off in the air | needs the same option tapped again within 4 s ("Engine OFF in flight: tap OFF again to confirm") |
| Everything else | applied at once |

`FlightSession` applies the systems to whatever the control source produces
(touch and keyboard router, a test script, a future autopilot): full brakes while
the parking brake is set, the selected flap position, and the ignition. The
cockpit levers and indicators, the exterior lights and the HUD rail all read the
same object, so they cannot disagree.

Start state (also after RESET): engine running, navigation lights on, landing
light off, flaps up, parking brake released.

| System | Simulation |
|---|---|
| Flaps | Detents 0/10/20/30°. The flaps travel 6 s from up to full. At full flap: lift coefficient +0.5, critical angle −2°, parasitic drag +0.035, pitch moment −0.03 (nose down). Prototype tuning values (`AircraftConfiguration`). Stall speed (sea level) about 51 kt clean, about 46 kt full flap. |
| Ignition | Off: no power (decays with the engine time constant). On from off: cranks 1.5 s, then runs. No fuel, mixture or magneto checks. |
| Parking brake | Full main-wheel brakes. Holds the aircraft at full power; it may creep very slowly because the brake model fades friction near zero speed (estimated from the model at under 0.5 kt, not measured; the PlayMode test only requires under 1 kt). |
| Lights | Visual only: a spot light and lens for the landing light; red, green and white lenses for the navigation lights. |

The warning monitor adds an **ENGINE OFF** caution when the engine is not running
and power is expected (airborne, or throttle above 20 % on the ground).

## Accessible names

Every interactable has an accessible name and hint (`CockpitItems`, copied onto
`CockpitInteractable.displayName` / `accessibilityHint`), shown as the focus
panel's title and hint and as panel placards under the switches. They are not yet
connected to a platform screen reader: Unity's screen-reader support API was not
verified for this project and is left for a dedicated accessibility pass.

## Performance

- The cockpit hierarchy is inactive in chase view: nothing is drawn or updated.
- The touch surface emits no geometry (no full-screen overdraw).
- Instrument text is cached per value; rendering allocates nothing in steady flight.
- One world-space canvas for the panel; picking is a few box tests per tap.

## Unfinished visual assets

All cockpit visuals are placeholders built from primitives and flat colours:

- Modelled cockpit interior: panel shape, frame, windshield pillars, seats, yoke, rudder pedals, throttle quadrant.
- Instrument faces: designed dial art (bezels, typography, glass, lighting); the built-in font is used.
- Switch, lever and handle models with proper guards (ignition key, flap lever detents, T-handle).
- Panel placards and labels as artwork.
- Exterior light assets: landing light flare, navigation light glow, strobe/beacon.
- Night lighting for the panel (instrument backlighting).
- Sound: switch clicks, flap motor, engine start.

## Tests

EditMode:
- `AircraftSystemsTests`: start state, clamping, versions and events, policy, confirmation gate (confirm, expiry, cancellation, refusal), systems applied to any commands.
- `FlapsAndEngineTests`: flaps add lift, drag and nose-down moment, lower the stall speed and stall earlier; clean model unchanged; ignition off/start/run states.
- `CockpitMathTests`: every item tappable at 16:9, 18:9, 20:9 and at least 96 units wide; items on the panel and not overlapping; names and system mapping; ray/box and screen-tolerance picking; look clamping, recentring, field of view and drag scaling.
- `InstrumentMathTests`: dial angles, map positions and range hysteresis, readings from shared telemetry.
- Additions to `FlightWarningMonitorTests` (engine off) and `DisclosureControllerTests` (cockpit view).

PlayMode (`CockpitTests`, real session and Rigidbody aircraft):
- flying with an instrument focused (no pause, controls unchanged); closing panels without changing controls;
- switching camera with a panel open; interacting while banked and climbing;
- rapid repeated taps; joystick, cockpit tap and pinch at the same time, third finger ignored; losing app focus;
- a pointer owned by a flight control never activates the cockpit;
- settings changed from both the contextual HUD and the cockpit agree everywhere, including the simulation;
- engine off in flight needs confirmation and restarts; reset restores systems and keeps the view;
- drag-to-look limits, remembered direction and double-tap recentring;
- `CockpitFocusPanelLayoutTests`: the panel fits between the controls at 4:3, 16:9 and 20:9 with large options.

The tests prove routing, ownership, state and layout logic. They do not prove how
touches arrive on a device, legibility, or comfort: use the device checklist.

## Device checklist (phone, then tablet)

1. Cockpit view: the six instruments are readable at a glance (digital windows) and nothing covers them at rest.
2. Fly a full circuit from the cockpit only (take-off, climb, turns, approach with the GPS map, landing).
3. Tap each instrument with the thumb: the enlarged view opens at once; it closes by tap, close button, tapping the sky, or CAM.
4. Keep flying (joystick and throttle held) while an instrument is enlarged.
5. Tap each switch: a selector opens, nothing changes until an option is tapped. Try near misses: the right control is still selected.
6. Drag on the sky to look around; double tap the sky to look forward; pinch to change the field of view.
7. Hold the joystick with one thumb and tap or look with the other; try three fingers.
8. Set flaps from the HUD rail (FLAPS) and check the cockpit lever; set the parking brake in the cockpit and check the HUD.
9. In flight, tap OFF on the ignition: it asks for confirmation; restart the engine.
10. Pull down the notification shade during a look gesture and return: no stuck look, no unwanted selection.
11. Repeat 1-3 on a 4:3 tablet and note the controls under the rail (see Panel layout).
