# Open Skies: contextual HUD (progressive disclosure)

Layer 1 of the design (`docs/GAME_DESIGN.md`): information and controls appear
when they are relevant, recede in stable flight and come back when needed. The
system infers the phase of flight, decides what to show, and presents it with
short, restrained transitions. It never takes control away from the pilot.

## Rules that do not bend

1. **Flight controls are outside the system.** Joystick, throttle, BRAKE, CAM and
   RESET are not managed by disclosure at all. No rule can hide or move them.
   BRAKE follows weight on wheels (a physical fact), never the estimated phase.
2. **Fixed homes.** Every HUD element and action has one position for the
   session. Disclosure only fades things in and out; nothing reflows or slides
   across the screen.
3. **Warnings override minimalism.** A warning banner appears in every profile,
   and the information related to it (airspeed for a stall) appears with it.
   Banners never take touches and are never modal.
4. **Engaged systems stay reachable.** An action whose system is on (parking
   brake set, a guide shown) stays visible in every profile so it can be undone.
5. **Manual access.** SYSTEMS (always visible) opens every available action, and
   the player can show or hide any guide. The player's choice wins.
6. **No flicker.** The phase estimator has hysteresis, confirmation times and a
   minimum phase duration; the HUD shows things at once but hides them only after
   a delay and a minimum visible time.

## Architecture

```
AircraftTelemetry ──▶ FlightTelemetry ─┬─▶ FlightPhaseEstimator ──▶ FlightPhaseEstimate (phase, confidence, reason)
 (+ height above ground)               │        ▲ RunwayGeometry (home runway)
                                       └─▶ FlightWarningMonitor ──▶ active warnings by priority
                                                        │
FlightSession (Gameplay) ── DisclosureInput ──▶ DisclosureController ──▶ visibility + reasons
 profile, view, systems                                                  │
                                                         ContextualHud (UI) ◀┘  fades, text, rail
                                                         DisclosureInspector (development only)
ContextualHud ── requests (action, SYSTEMS, profile) ──▶ FlightSession ──▶ ParkingBrake (input provider) / DisclosureController
```

| Assembly | Code | Notes |
|---|---|---|
| `OpenSkies.Awareness` (new, references FlightState only) | `FlightPhaseEstimator`, `FlightPhaseSettings`, `RunwayGeometry`, `FlightWarningMonitor`, `FlightWarningSettings`, `DisclosureController`, `DisclosureSettings`, `FlightAwarenessConfiguration` | Plain C#, deterministic, allocation-free per step. EditMode-tested with scripted telemetry. |
| `OpenSkies.UI` | `ContextualHud`, `ContextualHudFactory`, `HudButton`, `HudFade`, `HudMotionSettings`, `NumberStrings`, `DisclosureInspector` | Reads awareness state, raises requests, never changes the flight. |
| `OpenSkies.FlightInput` | `ParkingBrake` | An `IFlightInputProvider`: the latched brake reaches the aircraft through the router like any input. |
| `OpenSkies.Gameplay` | `FlightSession`, `HudPreferences` | Runs awareness at the physics rate, disclosure per frame, routes requests. |
| `OpenSkies.Simulation` | `AircraftTelemetry` | Adds `HeightAboveGroundFeet` (radio-altimeter style raycast). The flight model is unchanged. |

Phase estimation and warnings run in `FixedUpdate` on every telemetry sample, so
they behave the same at 30 and 60 FPS. Disclosure and presentation run per frame.

## Flight phase estimation

Phases: Parked, Taxi, Takeoff roll, Initial climb, Climb, Cruise, Descent,
Approach, Landing, Rollout. They are not assumed to follow each other.

**Signals:** debounced weight on wheels (lift-off needs 0.5 s off the ground,
touchdown 0.2 s on it), groundspeed and its smoothed rate of change, smoothed
vertical speed (2 s), height above ground, throttle, and the geometry to the home
runway (distance, height above the threshold, centreline offset, track error,
on/over the runway). History: time since lift-off and touchdown, and a climb-out
flag set by a lift-off or go-around.

**Scores.** Each phase scores 0 to 1 from soft conditions that ramp between two
values, so there is no single edge to flicker on:

| Phase | Main conditions |
|---|---|
| Parked | on the ground, groundspeed below 1-3 kt |
| Taxi | on the ground, moving, below 25-35 kt, no take-off power |
| Takeoff roll | on the ground, take-off power (throttle 50-70 %), accelerating or fast; 75 % score off the runway |
| Rollout | on the ground, 10-25 kt or faster, power reduced or decelerating, after a landing or take-off roll; suppressed in the first seconds after touchdown |
| Landing | short final (below 100-150 ft over the last 1 NM, on the centreline, lined up, not climbing, no go-around) or the first 1.5-3 s after touchdown |
| Initial climb | airborne, below 500-700 ft, climb power, not descending, within 120 s of lift-off or after a go-around |
| Climb | vertical speed above 150-300 fpm (above the initial climb band after lift-off) |
| Cruise | level within ±250-500 fpm, above 100-300 ft |
| Descent | descending faster than 150-300 fpm |
| Approach | inside the approach sector of a runway end: within 5 NM (fading to 6), below 1500 ft above the runway (fading to 1800), track within 45° of the runway, within a 30° wedge around the extended centreline; not climbing away or going around |

Near the runway, approach geometry is preferred over plain cruise or descent.

**Anti-flicker.**

| Mechanism | Default |
|---|---|
| Hysteresis: a challenger must beat the current phase's score by | 0.15 |
| Confirmation: the challenger must lead continuously for | 0.5 s (initial climb, landing) to 5 s (cruise) |
| Minimum phase duration | 2 s, skipped only when the aircraft changes between ground and air |
| Debounced weight on wheels | 0.5 s lift-off, 0.2 s touchdown |
| Smoothed vertical speed / acceleration | 2 s / 1 s time constants |
| Bad samples (NaN, infinity) | replaced by the previous value |

**Special cases.**

| Situation | Phases | Reason shown |
|---|---|---|
| Rejected take-off at speed | Takeoff roll → Rollout → Taxi → Parked | take-off rejected |
| Rejected take-off at low speed (below about 25 kt) | Takeoff roll → Taxi | take-off rejected |
| Touch-and-go | Approach → Landing → Takeoff roll → Initial climb | touch-and-go |
| Go-around (on approach or short final) | Approach/Landing → Initial climb → Climb | go-around |
| Go-around above the initial climb band | Approach → Climb | go-around |
| Bounce at idle | stays in Landing | - |

Rollout covers any high-speed deceleration on the runway, after a landing or a
rejected take-off.

**Output.** `FlightPhaseEstimate`, separate from telemetry: phase, confidence
(smoothed score of the phase, 0-1), time in phase, previous phase, reason, the
challenging phase and its confirmation progress, and debounced ground state. The
last eight transitions are kept with their reasons.

All thresholds are in `FlightPhaseSettings`, part of
`Assets/_Game/Settings/HUD/FlightAwarenessConfiguration.asset` (created once by the
scene builder, never overwritten). They are **prototype tuning values**; tune them
against recorded flights in the Editor with the inspector open.

## HUD profiles

The profile button (top right, or **H**) cycles Smart → Detailed → Cinematic. The
choice is remembered (PlayerPrefs). Changing profile never affects flight input.

| | Cinematic | Smart (default) | Detailed |
|---|---|---|---|
| Flight data, external view | hidden | by phase (table below), quieter in stable flight | all items, always |
| Flight data, cockpit view | hidden: the cockpit instruments show it | hidden | hidden |
| Approach guide | on request | Approach and Landing | airborne within 8 NM of the runway |
| Navigation guide | on request | on request (offered in climb, cruise, descent) | airborne, en route |
| Action rail | engaged actions only, or SYSTEMS | contextual by phase | every available action |
| Warnings and related data | always | always | always |

The cockpit has readable instruments and a GPS map (`docs/COCKPIT.md`), so
`cockpitHasInstruments` is on: in cockpit view the flight data and the navigation
guide hide in every profile (design pillar 4). The approach guide stays (the panel
has no glide path indicator), and a warning still brings its information up.

### Smart: flight data by phase

| Phase | IAS | ALT | VS | HDG | GS |
|---|---|---|---|---|---|
| Parked | | | | | |
| Taxi | | | | | ● |
| Takeoff roll | ● | | | | |
| Initial climb | ● | ● | ● | | |
| Climb | ● | ● | ● | | |
| Cruise | ● | ● | | ● | |
| Cruise, stable | ○ | ○ | | | |
| Descent | ● | ● | ● | | |
| Approach | ● | ● | ● | | |
| Landing | ● | | | | |
| Rollout | ● | | | | |

● shown, ○ quiet (55 % opacity). Stable flight: cruise held 8 s with confidence
of at least 0.7, bank within 10° and no warning. Any turn, warning or phase change
brings the information back at once. If the phase confidence is below 0.35, Smart
shows a safe set instead (airborne: IAS, ALT, VS; on the ground: IAS).

### Smart: contextual actions

| Action | Offered in | Notes |
|---|---|---|
| PARK BRAKE | Parked, Taxi | Sets only on the ground; releases anywhere. Shows "BRAKE SET" and stays visible while set. Keyboard **P**. |
| FLAPS | Taxi, Takeoff roll, Initial climb, Approach, Landing | Opens the same flap selector as the cockpit lever (`docs/COCKPIT.md`). Stays visible while the flaps are out. |
| APPROACH | Descent within 8 NM, Approach, Landing | Shows or hides the approach guide. |
| NAV | Climb, Cruise, Descent | Shows or hides the navigation guide. |
| ASSIST | Parked | Opens the flight assistance settings (profile, sensitivity, options; `docs/FLIGHT_ASSISTANCE.md`). Never "engaged". Available when the session has the settings card. Keyboard **M** cycles the profile. |

Player choices: showing a guide lasts until the player hides it; hiding a guide
lasts until it stops being relevant on its own, so it returns on the next
approach. SYSTEMS stays open until closed. A reset clears guide choices.

## Guides

- **Approach guide** (top left, under the flight data): runway, distance to the
  threshold, height above the runway, four PAPI-style lights (more white = higher;
  two white and two red on a 3° path to an aim point 150 m beyond the threshold)
  and a centreline marker that shows where the centreline is (steer towards it).
  The light angles approximate a standard 3° installation and are configurable.
- **Navigation guide**: an arrow pointing to the destination relative to the nose,
  the destination's name (AIRFIELD by default), the distance in NM and the course to
  steer. A mode can set another destination (a mission checkpoint, a landmark).

### Mission guides

A mission or tutorial can ask for guides (`DisclosureInput.MissionGuides`) and rail
actions (`MissionActions`). They go through the same controller with the reason
"mission": shown in every profile and view (the cockpit GPS does not show mission
checkpoints), still hidden by the player's own choice, still under warnings, and
never touching the flight controls. See `docs/ONBOARDING_DESIGN.md`.

## Warnings

`FlightWarningMonitor` keeps a trigger condition, a lower clear condition
(hysteresis), an optional delay and a minimum on-time per warning. Banners
appear in the upper middle, between the joystick area and the action rail, most
urgent first; red for warnings, amber for cautions; steady, no flashing.

| Warning | Condition (defaults) | Delay / minimum on | Priority |
|---|---|---|---|
| STALL | the aircraft's angle-of-attack stall warning (critical angle minus margin, airborne) | none / 1 s | 0 |
| SINK RATE (caution) | airborne, 30-2500 ft above ground, descent rate above a limit from 1000 fpm at 30 ft to 3000 fpm at 2500 ft; clears 200 fpm below | 0.8 s / 2 s | 1 |
| OVERSPEED | indicated airspeed above 160 kt; clears below 157 kt | 0.3 s / 1 s | 2 |
| ENGINE OFF (caution) | engine not running (starting does not count) while airborne, or with the throttle above 20 % on the ground | 1 s / 2 s | 3 |

These are **prototype values for the fictional trainer**, not certified limits or
a certified terrain-warning envelope. The never-exceed speed belongs to the
aircraft and should move into the aircraft data when there is more than one.

Each banner also carries a symbol (octagon for warnings, triangle for cautions), so
severity is never shown by colour alone. Sounds (M12, placeholder): the stall horn
while STALL is on, a short tone repeated while another red warning is on, one chime
when a caution appears; a haptic pulse (Android, if enabled) when a red warning
appears. Banners are never dimmed by the HUD opacity setting.

## Layout (canvas units, 1920 × 1080 reference, inside the safe area)

| Element | Position | Size |
|---|---|---|
| Flight data IAS, ALT, VS, HDG, GS | top left, x = 24 + 156·i, y = −16 | 148 × 84 each |
| Approach guide / navigation guide | top left, y = −112 (x 24 / 474) | 430 × 96 / 300 × 96 |
| Warning banners | anchor 56 % of the width, y = −250, −334, −418, −502 | 380 × 72 |
| HUD profile button | top right, left of RESET and CAM | 128 × 96 |
| LOOK FWD (cockpit view, not part of disclosure) | top right, left of the HUD profile button (x = −456) | 128 × 96 |
| SYSTEMS | bottom right, left of the throttle, directly above BRAKE | 168 × 96 |
| Action rail | stacked above SYSTEMS, 112 apart, nearest the thumb first: PARK BRAKE, (FLAPS), APPROACH, NAV, ASSIST | 168 × 96 each |
| Inspector (development only) | top right, under the buttons; built before the rail, so where the top slot (ASSIST) reaches under it the rail button takes the touch | 520 × 216 |
| Flight settings card | free column (as the cockpit focus panel, never both at once) | up to 600 × 486 |
| Flying tips (Assisted) | bottom of the free column, 12-76 units up, below the panel band; takes no touches | column width × 64 |
| Mission coaching (`docs/ONBOARDING_DESIGN.md`) | same place, 12-108 units up (never with the tips); tap for the explanation | column width × 96 |
| MENU / SKIP TUTORIAL | top right under CAM (x = −24, y = −128 / −240) | 128 × 96 |
| Pause card (M12) | free column; RESUME and SETTINGS share the first row | up to 600 wide, five buttons |
| Settings screen (M12) | centred over a dimmed, paused world | 1180 × 872 |

A PlayMode test checks at 4:3, 16:9 and 20:9 that no HUD target or banner
overlaps the joystick region, the throttle or BRAKE, and that every target is at
least 96 units tall. The development overlays (telemetry line, touch diagnostics)
moved below the guides.

**Touch safety.** HUD buttons fire only when a pointer is pressed and released on
the same button (uGUI click semantics). A finger that started on a flight control
is owned by it and was pressed elsewhere, so it can never trigger a HUD button by
sliding across. A hidden action stops taking touches at once, and an action is
never pulled away while a finger is pressing it.

## Motion and accessibility

| Setting (`ContextualHud.motion`) | Default |
|---|---|
| Fade in/out | 0.2 s, smoothstep |
| Warning fade-in | 0.12 s, in place |
| Rise into place when appearing | 10 units |
| Quiet opacity | 0.55 |
| Reduced motion | no movement; fades instant (configurable) |

`ContextualHud.motionPreference`: Follow system (default), Reduced, Full, chosen in
Settings > Display > Motion (M12) and applied to every panel at once.
On Android, "Follow system" reads the animator duration scale ("Remove
animations" sets it to 0) at start-up and when the app regains focus. Other
platforms have no built-in Unity API for this, so they use full motion unless the
preference is Reduced. The preference is stored under the same key by the options
store (`PlayerPrefsOptionsStore`).

**HUD opacity** (Settings > Display, 60-100 %) scales routine information and the HUD
buttons. Warnings stay at full strength; the flight controls follow only part of the
way (at least 80 % of their normal opacity). **Interface scale** (90-110 %) changes
the canvas reference height; `UiScaleMath` limits it per screen so nothing overlaps
(about 101 % on 4:3 tablets).

## Development inspector

Top right, Editor and development builds only (never in release builds). Tap it
or press **F3** to collapse it. It shows the phase with confidence and time in
phase, the reason it was entered, the challenging phase with its confirmation
progress and score, debounced ground state, smoothed vertical speed and
acceleration, HUD profile and view, stable flight, each visible element and
action with the reason (and whether it is only held by the anti-flicker delay),
active warnings, and the latest phase changes with their reasons.

## Tests

EditMode (`Tests/EditMode`), driven by `FlightScript`, a deterministic kinematic
telemetry script (not a flight model):

- `FlightPhaseEstimatorTests`: take-off to climb, cruise to approach (level and
  descending), rejected take-off at 45 and 12 kt, touch-and-go, full-stop landing,
  go-around from approach and from short final, a bounce at idle, brief fluctuations in cruise and
  on the ground (turbulence, a false weight-on-wheels sample, NaN/infinity
  samples, throttle blip, speed noise), minimum duration, transition log, reset.
- `NormalFlightTests`: one complete flight through the estimator and the Smart HUD,
  with the phase sequence, HUD checkpoints and a flicker check on every element
  and action.
- `DisclosureControllerTests`: Smart table for every phase, uncertain phase,
  stable flight, Cinematic, cockpit with instruments, warnings overriding
  Cinematic, profile switching during flight, hide delay and minimum visible time,
  contextual and engaged actions, SYSTEMS, guide choices, Detailed.
- `FlightWarningMonitorTests`, `RunwayGeometryTests`, `HudSupportTests`.

PlayMode (`Tests/PlayMode`):

- `ContextualHudTests`: layout clear of the flight controls at three aspect
  ratios; flight controls accessible and unmoved across every profile, phase and
  warning; warning shown in Cinematic; fade timing; reduced motion; rail taps;
  a pressed action is not hidden; engaged parking brake in Cinematic; profile button.
- `FlightSessionDisclosureTests` (Rigidbody aircraft): Parked then Takeoff roll from
  physics telemetry; profile switching during the take-off roll keeps the controls;
  airspeed shown on the take-off roll; parking brake holds at full power and
  releases; cannot be set in the air; reset clears awareness.

What the tests do not show: how the HUD looks and reads on a phone, whether the
thresholds suit real pilots, and phase detection on real flights in the Unity
flight model. Those need the manual checks in `docs/PROTOTYPE_TEST_PLAN.md`.

## Known limitations

- One home runway; approach detection and guides do not know other airfields.
- Warning sounds are generated placeholders.
- Reduced-motion detection is Android only (iOS and WebGL need native or
  JavaScript plugins).
- Uses the built-in legacy font, like the rest of the prototype HUD (typeface is an
  open decision, `docs/VISUAL_STYLE_GUIDE.md`).
- All thresholds are untuned prototype values.
