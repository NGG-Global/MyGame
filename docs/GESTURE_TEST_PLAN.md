# Open Skies: gesture device test plan

How to judge the gesture-first interface on a phone and a tablet. Nothing in
`docs/GESTURE_INTERACTION.md` has been touched on a device; this plan produces the
first evidence. Read with `docs/PROTOTYPE_TEST_PLAN.md` (builds, keyboard) and
`docs/GESTURE_EXPERIMENTS.md` (what each test is trying to learn).

## Build

1. Before any device work: CI green (`tests.yml`: "editmode test results" and
   "playmode test results"). If CI cannot activate a licence, open the project in
   Unity 6000.6.0f1, check the Console for compile errors, run **Open Skies ›
   Rebuild Flight Prototype Scene**, then EditMode and PlayMode in the Test Runner.
2. Build a **development** Android build (`build.yml` with "Development build", or
   **Open Skies › Build Android APK (Development)**). Development builds show the
   gesture inspector and the touch diagnostics; release builds never do.
3. Devices: one 20:9 phone (about 6.5 inches), one 16:9 or 16:10 phone if
   available, one tablet (4:3 or 16:10). Note the model, screen size and Android
   version with every result.

## Procedure

Each tester flies the same sequence twice: once in the gesture-first interface, once
in the legacy interface (F4 or a tap on the gesture inspector; the mode is
remembered). Half the testers start with legacy. Record times with a stopwatch or
from a screen recording; record misfires from the inspector line (OWN, GESTURE, DO).

Do not explain the gestures before section A. Explain them before section B.

### A. Without explanation (discovery)

| # | Ask | Observe | Pass |
|---|---|---|---|
| A1 | "Set the flaps to 20°." (parked) | What the tester tries first; whether the handle is noticed; whether the hint (the arc showing itself) is seen | Done within 60 s by at least half of the testers; the handle is found by most |
| A2 | "Find out what VS means." | Whether a long press is tried | Found by at least half without a hint |
| A3 | "Make the screen show less." | Whether the band pull or the HUD button is used | Either way, done within 30 s |
| A4 | "Look at the aircraft from the side." (in the air) | Whether the sky is dragged | Found by at least half |

### B. With explanation (operation)

| # | Procedure | Pass |
|---|---|---|
| B1 | Reach the handle with the right thumb while the left thumb holds the stick. Three times. | No stretch, no grip change; the stick keeps steering (diagnostics) |
| B2 | Pull up: the arc unfolds with the thumb; let go on nothing (near the handle). Five times. | The arc follows without lag; nothing changes; no accidental selection |
| B3 | Pull and release on PARK BRAKE, APPROACH, NAV, ASSIST. Each three times. | Each selection on the first try after the first round; segments are readable; the armed state is unmistakable |
| B4 | Pull, slide to FLAPS, rest, then drag the lever to 20°, release. Then 30°, then UP. | One gesture each, no lifting; the detent clicks are felt or heard; the preview is understood as a preview; the aircraft's flaps move only after release (check the cockpit lever or the inspector) |
| B5 | Start the lever, move to 30°, move sideways to cancel, release. | Flaps unchanged; the cancel state is obvious |
| B6 | Pull, rest in the open arc for a second: the outer ring. Release on LDG LIGHT. | The ring is found; the light toggles (check the exterior lights in the chase view) |
| B7 | Tap the handle; hold the handle. | The arc shows itself and folds; the explanation appears while held |
| B8 | Pull the HUD band down and up; release at different stops; release where you started. | The ladder reads; the HUD previews; the profile changes only on release at a new stop; the HUD button still works |
| B9 | Drag the sky in the chase view while flying straight; let go. Pinch. Double tap. | The view orbits and springs back; the aircraft does not react; pinch zooms within limits; double tap goes to the cockpit and the cockpit gestures work as before |
| B10 | Hold a flight value; hold a warning banner when one appears (climb slowly to near a stall). | The plate explains; the banner stays; the stall warning's advice matches the coach's |
| B11 | Mid-pull, pull down the notification shade, return. Mid-lever, press Home, return. | No stuck arc or lever; nothing applied; the next pull works |
| B12 | Open the pause menu during a pull (MENU with the other thumb). | The gesture is cancelled; nothing applied; after RESUME the next pull works |
| B13 | Rotate the device between the two landscape orientations during a pull. | The arc stays attached to the handle; the gesture continues or cancels cleanly |
| B14 | Switch to legacy (F4 / inspector) and set flaps from the rail; switch back. | Both work; nothing is left half shown |
| B15 | Reduced motion on (Settings › Display › Motion). Repeat B2, B4, B8, B9. | Everything appears at once; the gestures behave the same |

### C. While flying (the test that matters)

Fly a full circuit in Free Flight from the runway with touch only, gesture-first:

1. Before take-off: release the parking brake from the arc.
2. Take-off, climb: set flaps UP from the lever during the climb.
3. Cruise: pull the HUD to Cinematic, then back to Smart; look around; zoom.
4. Approach: set flaps to 20° then 30° from the lever on final, without losing the
   runway; show the approach guide from the arc.
5. After landing: parking brake from the arc.

Record: every accidental activation (anything that happened that the tester did not
mean), every intended gesture that failed, and whether the tester ever lost control
of the aircraft because of a gesture. Repeat in legacy and compare counts and times.

### D. Cockpit direct manipulation (`docs/COCKPIT_DIRECT_MANIPULATION.md`)

Explain nothing for D1 and D4; explain before the rest.

| # | Task | Record |
|---|---|---|
| D1 | Open the heading indicator; "set the bug to 270" with no explanation | Found the sweep? Seconds; whether the centre cancelled it by accident |
| D2 | The same, explained | Seconds; ticks felt; cyan preview seen; value on the HUD afterwards |
| D3 | Set the altimeter subscale to 1030; read the altitude on the HUD | Seconds; whether the indicated change was noticed |
| D4 | "Switch the landing light on from the cockpit" with no explanation | Flick found / tapped the selector; a short flick that sprang back |
| D5 | Flaps 20° from the cockpit lever after B4 | Seconds; sideways cancels |
| D6 | Pan and zoom the map, double tap to recentre | Range flicker during a pinch; pan limit felt |
| D7 | Any of the above while flying straight and level | Joystick thumb kept its finger (inspector OWN); any flight input change |
| D8 | Press Home during a sweep; return | Setting unchanged; nothing stuck |

### E. Pause sheet and entry stations (`docs/GESTURE_NAVIGATION.md`)

| # | Task | Record |
|---|---|---|
| E1 | In flight, two fingers tap the sky; "find the settings" with no explanation | Pause found? Pulled the sheet further? Seconds |
| E2 | Pull the sheet down to resume; hold the joystick at once | Controls answered; no stuck throttle or stick (inspector OWN) |
| E3 | At the entry: "start the Landing Challenge" with no explanation | Swipe, chip or START used; seconds; the tester's words about the moving camera |
| E4 | Swipe quickly through all three stations and back | Settle felt; camera moved with the finger; any accidental START |
| E5 | Pinch in the sky; two thumbs on the joystick and throttle | No pause (zero expected) |
| E6 | Esc / back from the second stop | Back to the first stop, then resume |

### F. Discovery, cues and settings

| # | Task | Record |
|---|---|---|
| F1 | Fresh install (or Reset progress): fly a circuit | Which cues appeared, when, whether each was followed; "did the dot annoy you?" |
| F2 | Settings › GESTURES: Relaxed, then Quick; repeat B4 and D2 | Failed intended gestures per setting; the tester's preference |
| F3 | Camera gesture Low and High in a chase-view look | Preference |
| F4 | Hints off, then on | No cue appears while off; the learned ones never return |
| F5 | Development build: read the diary line on the inspector after a circuit | Completed / cancelled / quick cancels per route (copy the line) |

## Measurements

| Measure | Gesture-first | Legacy |
|---|---|---|
| Heading bug to 270 (D2), seconds | | n/a (selector) |
| Landing light from the cockpit (D4), found without explanation | | |
| Pauses not meant per circuit (E5, C) | | n/a |
| Settings found from the pause (E1), seconds | | (card: SETTINGS button) |
| Mode started from the entry (E3), seconds and method | | (mode select) |
| Cues followed / cues shown (F1) | | n/a |
| Diary: quick cancels per circuit (F5) | | n/a |
| Time and taps from "I want flaps 20°" to flaps 20° (B4) | | |
| Accidental activations per circuit (C) | | |
| Failed intended gestures per circuit (C) | | |
| A1 to A4 discovery (found / not found, seconds) | n/a | n/a |
| Tester's preference and one sentence why | | |

## Success criteria for the slice

- No gesture ever steals a joystick or throttle touch (zero cases).
- No aircraft system changes without a release on an armed control (zero cases).
- Flaps 20° in one gesture by most testers after the explanation, within 3 s.
- Fewer accidental activations per circuit than the legacy rail, or equal with
  faster intended actions.
- Reduced motion, pause, focus loss and rotation leave nothing stuck.
- At least half of the testers prefer gesture-first after both circuits.

## Failure cases to log

| Case | Note |
|---|---|
| Arc opens from a throttle attempt | Where the thumb landed; screen size |
| Flaps step on a release that was meant as a cancel | Whether the thumb was on FLAPS; consider "release on FLAPS opens the lever" |
| Lever cancels during a natural vertical motion | Lateral drift distance from the inspector |
| HUD changes without intent | Where the press started; whether the band is too tall or too close to the joystick region on this phone |
| Look drag meant as a joystick input | Where the press landed relative to the joystick region |
| Explanation plate covers the element or a flight control | Element and screen size |
| Stuck arc, lever or ladder | Exact steps; inspector line |
| Hold ring appears on a resting thumb before a pull | Whether it distracted |
| A sweep starts cancelled (thumb landed in the centre) | How far from the centre the thumb landed |
| Switch flick springs back when the tester meant it | Flick length; screen size |
| Sheet dismissed when the tester meant the first stop | Where the thumb released; screen height |
| Showcase camera moved under a tap on START | Whether the tap landed |
| A cue covered a flight value or a control | Which cue; screen size |

## Manual test instructions (Editor, mouse)

The mouse drives one pointer: pull the handle by press-dragging it; the arc, lever
and ladder work with a single pointer; pinch cannot be tested with a mouse. The
Device Simulator shows the layout at phone and tablet sizes. The gesture inspector
(top right, under the disclosure inspector) shows the route, state, progress and
what the session is doing. F4 switches interface; F3 collapses the disclosure
inspector. This checks logic and layout only; it is not evidence about touch.

Report results in `docs/GESTURE_EXPERIMENTS.md` under a new "Results" heading with
the device, the date, the variant settings used and the measurements table.
