# Open Skies: onboarding, First Flight and Landing Challenge

The first-flight experience teaches a new player to fly by flying: real take-off,
a short coastal flight and a landing, with the same aircraft, physics and
assistance as Free Flight. Guidance is small, contextual and skippable; it never
moves a control and never covers the runway, the instruments or the touch controls.

Status: implemented as code and tests in this milestone. Nothing here has been
played on a device yet; see **Verification status** at the end.

## Principles

| Principle | How it is met |
|---|---|
| Learn by doing | Every step completes on what the aircraft actually does (telemetry, the phase of flight, the world), never on a timer alone. Timers only delay hints and confirm that a condition holds. |
| Part of the simulator | One flight session, one flight model, one assistance layer. A mode only chooses where the flight starts, where RESET returns to, where the navigation guide points and what the coach says. |
| Never take control | Missions have no path to `FlightControls`. They read telemetry and the player's commands; the aircraft only receives the player's input (shaped by the chosen assistance profile, as in Free Flight). |
| Unobtrusive | One message at a time at the bottom of the free column; subtle highlights; no full-screen pop-ups during flight; no rings in the sky. |
| Respect the player | Experienced pilots skip in two taps or simply fly ahead: the tutorial follows them. Doing well makes the instruction fade. Mistakes lead to recovery, never to a "mission failed". |
| Safety first | A real warning (stall, sink rate, overspeed, engine off) replaces the lesson's message with what to do about it; the banner itself is the alert. |

## Entry screen and modes

The scene opens on a minimal entry screen over a dimmed, paused view of the
aircraft on the runway. MENU (top right, or **Esc**) pauses any flight and leads
back here.

| Mode | Starts | RESET returns to | Navigation guide |
|---|---|---|---|
| **Free Flight**, from the runway | At rest at the start of runway 36 | The runway start | The airfield, on request (NAV), as before. MENU › GUIDE cycles a landmark destination (the guide is then shown, with the checkpoint marker). |
| **Free Flight**, in the air | 1,500 ft, about 2.5 km south-west of the field, heading 045 towards the coast, clean, at the hands-off trimmed speed (about 95 kt) with level-flight power | The same airborne start | As above |
| **First Flight** | At rest at the start of runway 36 | The last checkpoint (below) | The current checkpoint, asked for by the tutorial |
| **Landing Challenge** | Established on final for runway 36 (below) | The start of final (a retry) | Only after a go-around: back to the final approach |

The entry screen recommends the First Flight (amber) until it has been flown or
skipped, shows "Completed" afterwards, and shows the best landing score.

## First Flight

### Route

```
                N
        ┌──────────────────────────── sea ───
        │                    (2) LIGHTHOUSE ◆   ← headland
        │      (1) turn ↗         │
        │   ▲ climb               │ (3) follow the coast south
        │   ║ runway 36           │
        │   ║                     ◆ HARBOUR (breakwater light)
        │   ║                   ↙ (4) head back
        │   ↑ final           
        │   ◇ FINAL 36  ←─────── (5) line up, approach, land
        └────────────────────────── shoreline x = 1,600 m
```

Take-off on runway 36, climb, turn towards the lighthouse on the headland, fly
over it, follow the coast south to the harbour, head back to the start of the final
approach (3 km before the threshold), line up and land. About 13-16 km in total;
roughly 6-8 minutes at the speeds below (an estimate from the route length, to be
confirmed in playtests).

### Steps

| # | Step | Teaches | Done when (telemetry, phase, world) | Main instruction (touch) | RESET returns to |
|---|---|---|---|---|---|
| 1 | Preparation | The stick | The stick has been moved at least halfway and released (or the throttle is already up) | "Left side: your stick. Slide your thumb, then let go" | Runway |
| 2 | Take-off | The throttle | Throttle at take-off power (90 %) and rolling at 15 kt or more | "Right side: slide the throttle all the way up" | Runway |
| 3 | Rotation | When to pull back | Airborne (debounced lift-off) and at least 20 ft above the ground | "Stick centred while the speed builds", then "55 KT: ease the stick back gently" | Runway |
| 4 | Climb | A stable climb | 500 ft above ground with wings within 20° and speed above 1.2 × stall | "Climb at about 70 KT, wings level" (Assisted: "...Let go: the trim holds it") | Runway |
| 5 | Turn | Gentle banking | Lighthouse within 15° of the nose, wings within 12°, for 2 s | "Turn right to the lighthouse: tilt the stick right gently" | Climb-out point, heading for the lighthouse |
| 6 | Cruise | Steering and looking around | Lighthouse, then harbour reached | "Fly over the lighthouse", "Follow the coast south to the harbour" | Last landmark reached, heading for the next |
| 7 | Return | Navigating home, descending | Approach phase **and** lined up (track within 30°, within 500 m of the centreline, before the threshold) | "Head back: follow the arrow to the runway" | Harbour, heading for the final approach |
| 8 | Approach and landing | Speed, alignment, descent, touchdown | Touched down without losing control and slowed below 5 kt | Situation by situation: high, low, fast, slow, off the centreline, flare, brake | Start of final approach, landing flaps set |

Each step is data (`TutorialStep`): callouts in priority order (the first whose
situation holds is the instruction), struggle hints, a short explanation, a success
message, waypoints, the HUD guides it needs and its checkpoint. The eight steps are
built by `TutorialCourse.Build` from the settings and the aircraft's speeds.

### Speeds and distances come from the aircraft

Nothing is typed in by hand. `TrainingSpeeds` is built from the aircraft envelope
(stall speeds computed from `AircraftConfiguration` by the flight model) and a
take-off run estimate (`FlightStartMath.TakeoffGroundRunMetres`, thrust, drag and
rolling resistance from the same data). Factors are in `TutorialSettings`.

| Reference | Formula (default factor) | Default trainer |
|---|---|---|
| Rotation | 1.1 × clean stall speed, rounded to 5 kt | 55 kt |
| Climb | 1.35 × clean stall | 70 kt |
| Approach, clean / flaps | 1.4 × stall for the flaps in use | 70 / 65 kt |
| "Too slow" | 1.2 × stall for the current flaps | about 61 kt clean |
| Take-off run | Ground run to 1.3 × clean stall (about 66 kt, the lift-off speed of the offline validation) | about 310 m (estimate) |
| Enough runway to try again | Take-off run × 1.15 | about 360 m |

Values in the right-hand column are approximate and computed from the default
configuration; the flight model's offline validation (lift-off at about 66 kt after
about 340 m) is in `docs/FLIGHT_MODEL.md`. They change automatically with the data.

## Coaching

### One message, chosen by priority

`Coach` shows at most one message, in this order:

1. **Safety**: what to do about the most urgent active warning (the same texts as
   the Assisted flying tips, `FlightGuidanceAdvisor.SafetyText`). The warning banner
   stays the alert; the lesson waits and resumes unchanged afterwards.
2. **Success**: a short confirmation for 2 s ("Airborne!", "Lighthouse reached").
3. **Hint**: help for a sustained struggle, for 5 s.
4. **Explanation**: the step's two-sentence "why", while the player has it open.
5. **Instruction**: the current callout, unless the player is doing well.

### Not nagging

| Rule | Default (`TutorialSettings`) |
|---|---|
| An instruction fades once the player has been on track this long | 8 s (immediately for full power; 6 s on the approach) |
| A new situation must last before its instruction replaces the current one | 0.6 s; corrections (high, low, fast, off course) 1.5 s |
| An instruction stays at least | 2.5 s |
| Hints: each at most once per step, at most 3 per step, at least 20 s apart, never during a warning | |
| Text changes only when the content changes (`CoachingCue.Version`) | |

A scripted, well-flown First Flight sees at most two hints (the optional "tap CAM"
and "FLAPS" suggestions) and no message twice in a row.

### Presentation

| Element | Where | Behaviour |
|---|---|---|
| Coaching message | Bottom of the free column (between the joystick region and the action rail), 12-108 units up | 96 units tall. Small step label ("3/8  ROTATION"), the message, a thin accent (white instruction, amber hint, green success, red safety) and "?" when an explanation is available. Tap: show or hide the explanation. Fades in place; replaces the Assisted flying tips while a mission speaks. |
| Control highlights | On the control itself | A soft amber outline that breathes slowly (steady with reduced motion). Stick, throttle, brake, FLAPS, CAM, RESET. Takes no touches; shown only while the control is visible. |
| SKIP TUTORIAL | Top right, under MENU, First Flight only | A first tap arms it ("TAP AGAIN TO SKIP", amber, 3 s); the second ends the tutorial. The flight carries on in Free Flight. |
| MENU | Top right, under CAM | Pauses the flight and opens a small card. |
| Cards (pause, results) | The free column's panel band, one panel at a time with the cockpit focus panel and the flight settings | Pause is modal: it dims the screen and takes every touch around it. Result cards leave the flight controls usable. |

Keyboard and gamepad players get keyboard texts ("Hold Left Shift for full power").
Assisted players get texts that match what the assistance does ("bank gently, then
let go").

## Adapting to the player

| Situation | Behaviour |
|---|---|
| Struggling to take off | Rotation hint ("A little more back pressure") at rotation speed + 12 kt on the ground. An abandoned take-off (slowed below 8 kt after a roll) returns to step 2 with "Full power, then at 55 KT ease the stick back to lift off"; without enough runway left, "Tap RESET to line up again" (RESET highlighted). Never a failure; counted in `FailedTakeoffs`. |
| Flying well | Instructions fade; nothing repeats. A pilot who takes off straight away skips the ground steps. |
| Approaching a stall | The stall warning and "Stall: lower the nose and add power" replace the lesson; the step is kept. |
| Missed checkpoint | Passing within twice the arrival radius counts. A wide miss shows "You passed the lighthouse: turn back to the arrow, or carry on" once; flying back reaches it, and flying on hands over to the next checkpoint once it is closer. |
| Lined up early | From the turn or the cruise, a descending, lined-up approach goes straight to the landing step. |
| Go-around | Recognised from the phase of flight (or a climb-away after touchdown): "Go-around: good call. Follow the arrow to try again", back to step 7. |
| Drifting off the approach | After 8 s out of the approach: back to step 7 with a hint. |
| Down in a field (crash) | The project has no crash model and this milestone adds none: the aircraft simply stops. After 3 s, "Tap RESET to carry on from the last checkpoint" (RESET highlighted). An uncontrolled touchdown on the approach suggests RESET to fly the approach again. |
| RESET | Back to the last checkpoint (table above), with "Back at the last checkpoint". Before lift-off: the take-off, not the preparation. |
| Camera change | Ignored by the tutorial: progress and timers continue. |
| Assistance profile change | The step continues; texts that depend on the profile follow it. |
| Pause | Time stops (`Time.timeScale` 0), the tutorial freezes, the message stays; resume continues exactly where it was. The app going to the background on a phone opens the pause menu. |

## Navigation

| Aid | Description |
|---|---|
| Navigation guide | The existing guide (top left) now shows a destination: an arrow relative to the nose, the destination name, the distance in NM and the course to steer (optional heading guidance). Default: the airfield on request (NAV), unchanged. |
| Mission guides | A mission asks the disclosure controller for guides (`DisclosureInput.MissionGuides`) and rail actions (`MissionActions`). They show in every profile and view (the cockpit GPS does not show mission checkpoints); the player can still hide a guide; warnings still win; flight controls are untouched. |
| Checkpoint marker | A small diamond with the landmark's name and distance over the active landmark, only while it is on screen; no marker on the runway approach. |
| Landmark lights | The lighthouse lantern and the harbour light flash slowly while they are the active checkpoint: a believable navigation aid. |
| Landmark registry | `OpenSkies.World`: `PrototypeWorld` (layout) and `LandmarkRegistry` (airfield, lighthouse, harbour, islet). The scene builder draws the placeholders from the same numbers, so navigation and scenery cannot disagree. |

The coastline and landmarks are **placeholders** built from primitives (sea plane
east of the shoreline, a low rocky headland with a lighthouse, a breakwater with a
light and moored boats, an islet). They are replaced in the regional airport and
coastline milestone. The sea has a collider but no water physics.

## Landing Challenge

**Start** (`LandingChallengeSettings`, converted to a physical state by
`MissionStarts.FinalApproach` with `FlightStartMath`):

| Item | Default |
|---|---|
| Position | On the extended centreline of runway 36, 3,000 m before the threshold |
| Height | On a 3° path to the approach guide's aim point (150 m past the threshold): about 540 ft |
| Configuration | Full flap (already extended, not travelling); engine running and settled |
| Airspeed | The aircraft's stick-free trimmed speed for full flap (about 70 kt), kept between 1.3 and 1.6 × the full-flap stall speed |
| Attitude and velocity | Wings level, heading 360, Rigidbody velocity along the 3° path, nose at the angle of attack that gives the lift, throttle that balances drag and the weight along the path (about 31 %) |

**Score** (`LandingScoringSettings`, all ranges configurable). Each category is 100
up to its "full" value and falls linearly to 0 at its "zero" value:

| Category | Weight | Full | Zero |
|---|---|---|---|
| Touchdown descent rate | 30 | ≤ 240 fpm | ≥ 720 fpm |
| Alignment (larger of heading and track error) | 15 | ≤ 3° | ≥ 12° |
| Distance from the centreline | 20 | ≤ 3 m | runway edge; 0 off the paved surface or before the threshold |
| Touchdown airspeed / stall speed for the flaps in use | 15 | 1.0-1.2 | ≥ 1.5 (fast), ≤ 0.85 (slow) |
| Control | 20 | 100, minus 35 per bounce, 60 for leaving the runway, and up to 40 for bank on the ground (5-15°) | |

Grades: Excellent ≥ 85, Good ≥ 70, Fair ≥ 50, otherwise "Keep practising". A
touchdown at 1,200 fpm or more, 30° of bank, or a nose-over is "Out of control"
(score 0). These are **game scores for feedback and replay value, not flight-training
or check-ride standards**; the result card says so.

**Result**: once the aircraft has slowed below 15 kt for 1 s, a card shows the grade,
the score, up to four short lines (descent, centreline and alignment, speed, control)
and RETRY / FREE FLIGHT. RETRY restarts on final; FREE FLIGHT carries on from where
the aircraft stopped. The best score is kept on the device.

## Architecture

```
FlightSession (Gameplay) ── telemetry, phase, warnings, runway, pilot commands ──▶ MissionInput
       ▲  start / checkpoint / navigation target / guides                               │
       │                                                                                ▼
GameModeSession (Gameplay) ◀── cue, target, guides, checkpoint ── FirstFlightTutorial / LandingChallenge (Missions)
       │                                                               │ TutorialStep data, Coach,
       ▼                                                               │ WaypointTracker, LandingEvaluator
ModeSelectScreen, CoachingPanel, ControlHighlights, CheckpointMarker, MissionCard (UI)
```

| Assembly | Code | References |
|---|---|---|
| `OpenSkies.World` (new, `Environment/`) | `Landmark`, `LandmarkRegistry`, `PrototypeWorld`, `LandmarkBeacon` | none |
| `OpenSkies.Missions` (new) | `GameMode`, `MissionInput`, `CoachingCue`, `Coach`, `TutorialStep` / `TutorialCourse` / `TutorialContext`, `FirstFlightTutorial`, `WaypointTracker`, `LandingEvaluator` / `LandingScoring`, `LandingChallenge`, `PlayerProgress`, settings and `MissionConfiguration` | FlightState, Awareness, World |
| `OpenSkies.Awareness` | `NavigationTarget`, `NavigationRelation`, `NavigationMath`; mission guides in `DisclosureController` | FlightState |
| `OpenSkies.Simulation` | `FlightStartMath` (steady flight, airborne start, take-off run); `AircraftController.ResetTo` | FlightState |
| `OpenSkies.UI` | `ModeSelectScreen`, `CoachingPanel`, `ControlHighlights`, `CheckpointMarker`, `MissionCard`, `MissionUiFactory`; destination name and course on the navigation guide | + Missions |
| `OpenSkies.Gameplay` | `GameModeSession`, `MissionStarts`, `PlayerPrefsProgressStore`; `FlightSession` start states, reset target, navigation target, mission guidance | + Missions, World |

**Tutorial versus phase detector.** The tutorial tracks what the player is trying to
accomplish (`FirstFlightTutorial`, Missions). The flight phase estimator tracks what
the aircraft is doing (`FlightPhaseEstimator`, Awareness). The tutorial reads the
estimate as one input among telemetry and geometry and never changes it; the
estimator knows nothing about missions.

**Lifecycle**: `Start`, `StartAt` (development and tests), `Pause` / `Resume`, `Skip`,
`Restart`, `OnFlightReset` (recover at the checkpoint), `Update` at the physics rate.
Completion raises `Completed`; the session shows the result card and records progress.

**No new physics.** `FlightSession.StartFlight` places the Rigidbody once (like the
existing reset) with a state computed from the aircraft data; from then on it flies
exactly as in Free Flight. Allocation-free per physics step after construction.

## Player progress

Stored on the device only (PlayerPrefs; no accounts, no cloud):

| Item | Key | Owner |
|---|---|---|
| First Flight: not started / skipped / completed | `OpenSkies.Progress.FirstFlight` | `PlayerProgress` |
| Best Landing Challenge score, landings flown | `OpenSkies.Progress.BestLanding`, `OpenSkies.Progress.Landings` | `PlayerProgress` |
| Assistance profile, sensitivity, options | `OpenSkies.Flight.*` | `FlightPreferences` (existing) |
| HUD profile, motion, cockpit field of view | `OpenSkies.Hud.*`, `OpenSkies.View.CockpitFieldOfView` | `HudPreferences` (existing) |
| Chase or cockpit view | `OpenSkies.View.CameraMode` | `HudPreferences` (new; `FlightSession.rememberCameraMode`, on in the scene) |

Each change is written at once (`PlayerPrefs.Save`): mobile apps can be killed
without a clean quit. Skipping never downgrades a completed First Flight.

## Tests

EditMode (`Tests/EditMode`, run outside Unity as well, see Verification status):

- `FirstFlightTutorialTests` (19): a complete scripted first flight through all eight
  steps to a scored landing; quiet instructions and no repeats; keyboard texts;
  skipping the ground steps; an abandoned take-off and a second attempt; not enough
  runway; a stall during the climb; a missed checkpoint flown back to, and one left
  behind; a go-around; down in a field and RESET to the checkpoint; RESET before
  lift-off; camera switching; an assistance profile change; pause and resume; skip;
  restart; the explanation on tap; course data.
- `LandingAndProgressTests` (11): scoring ranges and their configuration, totals and
  grades, smooth, hard and uncontrolled landings with scripted telemetry, a
  climb-away after touchdown, the Landing Challenge with a retry, tutorial completion
  and skip persistence, best score.
- `MissionSupportTests` (12): coach priorities and versioning, checkpoints, navigation
  geometry, the landmark registry, beacons, reference speeds, mission guides and
  actions in every profile and view, shared safety texts.
- `FlightStartMathTests` (4, need the engine): stick-free speeds, a level start that
  balances the forces of the flight model, the final-approach start, the take-off run.

PlayMode (`Tests/PlayMode/GameModeTests`, CI only): the Landing Challenge start with
the Rigidbody (on the centreline, flaps out, engine at power, recognised as an
approach, no warning); the airborne Free Flight start holding altitude; RESET as a
retry; the tutorial never moving a control and keeping progress across camera and
profile changes; the pause menu freezing the flight and resuming; SKIP needing a
second tap; the entry screen; a Free Flight destination; PlayerPrefs persistence;
layout at 4:3, 16:9 and 20:9.

## Verification status

- Compiled outside Unity (Roslyn, against the 6000.6.0f1 managed assemblies and
  package sources) in the Editor and Android player configurations: no errors, no
  warnings. EditMode tests run with Unity's Mono: all cases that do not need the
  native engine pass; the cases that need it (including `FlightStartMathTests`) and
  every PlayMode test need CI. **CI has not run any of this**, because the Unity licence
  secrets still stop it at activation (`docs/COCKPIT.md`). None of this is a claim that
  the game works on a device.
- The scene must be regenerated in the Editor (**Open Skies › Rebuild Flight Prototype
  Scene**) for the coastline, landmarks, entry screen and mission UI to appear.

## Needs real-device testing

1. Legibility of the coaching message (24-26 units, best fit down to 16) on a phone in
   sunlight; whether the bottom of the free column is where players look.
2. Whether the breathing outline is noticed without being distracting; reduced motion.
3. Reach and accidental taps for MENU and SKIP under CAM (top right) with the right
   thumb near the throttle; the two-tap skip.
4. Tap-for-explanation on the message: discoverability of "?".
5. The Android back button opening the pause menu (expected to arrive as Esc through
   the Input System; not verified) and the pause on returning from another app.
6. Pause: no throttle jump after dragging on a paused screen; time and audio stop.
7. Real Rigidbody behaviour of the airborne and final-approach starts in every
   assistance profile (the PlayMode tests check Assisted).
8. Whether new players complete the take-off without outside help (the acceptance
   criterion), the route length and the time it takes, the 500 ft climb target, and
   whether the approach callouts (high, low, fast, slow, centreline) feel helpful or busy.
9. The checkpoint marker position and lag at 30 FPS; the landmark lights at distance;
   depth precision on the coastline placeholders in WebGL.
10. Landing scores: whether the default ranges feel fair (tune `LandingScoringSettings`).

## Known limitations and open questions

- Texts are English only and written in code; localisation is not set up.
- The world is a placeholder (M5); no water physics: the sea behaves as flat ground.
- No crash model (by design for this milestone): an uncontrolled arrival is reported,
  not simulated.
- One runway, one route; the tutorial route is fixed in `TutorialCourse`.
- The landing score does not judge the touchdown point along the runway (it is shown
  in the data, `PastThresholdMetres`, but not scored).
- **Open question**: whether First Flight should start automatically on the very first
  launch instead of showing the entry screen (it is recommended, not forced).
