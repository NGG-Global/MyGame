# Open Skies: gesture experiments

The hypotheses behind the gesture-first slice, the alternatives considered, the
variants that can be switched on a device, and the questions only a device can
answer. Keep this honest: an experiment that fails is a result.

## Hypotheses

| # | Hypothesis | Built as | How we will know |
|---|---|---|---|
| H1 | Proportional disclosure (peek, reveal, arm, release) is understood without instruction after one or two tries | The arc unfolds with the pull; a tap on the handle shows the arc as a hint; the first phase-relevant action shows it once | Device plan, task "open the arc with no explanation"; count of players who release on nothing |
| H2 | Fixed arc slots build muscle memory faster than contextual buttons | Slots never move; context changes emphasis only | After three flights, time from touching the handle to releasing on PARK BRAKE and FLAPS falls below two seconds |
| H3 | A lever that appears under the thumb, current detent at the finger, feels like a mechanical control | Lever handover from FLAPS; detents with hysteresis; rubber-banded stops; ticks | Players set a specific flap position in one gesture, without lifting; no accidental changes on release |
| H4 | A continuous preview of HUD density is more reversible and less mysterious than a cycle button | The ladder with a continuous marker; the HUD shows the previewed level while pulling | Players reverse and release at their original level when asked to "just look"; no accidental profile changes in flight |
| H5 | Spring-loaded free look is used in flight and does not interfere with flying | Drag the sky; springs back | Players look around in the circuit; zero cases of a look drag being meant as a joystick input |
| H6 | Long-press explanations are found and reduce "what is this number?" questions | Hold ring at 20 % of the hold; plate with the aircraft's own numbers | Players discover it without a hint at least half of the time |

## Alternatives considered and the choice made

| Topic | Options | Chosen for the slice | Why |
|---|---|---|---|
| Arc anchor | The press point; the handle centre | Handle centre | Geometry identical on every pull, so the thumb learns it (H2). The press point varies by 80 units inside the handle. |
| Arc shape | Full radial around the thumb; upper-left fan; vertical list | Upper-left fan, 98° to 198°, 25° per slot | The thumb at the bottom right has room only up and left; the throttle is to the right, the brake below. A full radial would put segments over the throttle. |
| Selection area | The band only; wedges from an inner radius outward | Wedges from 110 units outward | Large effective targets; beyond the band still selects (overshoot forgiven); near the handle is the cancel zone. |
| First slot position | Straight up (90°); slightly left (98°) | 98°, with 10° of end tolerance | A straight pull still lands on slot 0; the throttle side stays empty so a thumb drifting right selects nothing. |
| Flaps handover | Immediately on entering the wedge; after resting; after pulling through the band; release on FLAPS opens a selector | Resting 0.18 s **or** pulling 30 units beyond the band (both configurable); release on FLAPS without the lever steps one detent | Immediate handover would catch a thumb crossing FLAPS on its way to NAV. Both triggers are tunable to zero or to never, so the device test can compare. |
| Lever direction | Down extends (lever convention); up extends (throttle convention) | Down extends | The real lever and most pilots' expectation; the knob label and the cyan preview make the direction obvious. Switchable in a later variant if the test disagrees. |
| Lever cancel | Return to the handle; sideways distance; a cancel zone | Sideways 200 units (un-cancel at 150) plus "release at the original detent changes nothing" | A sideways escape is easy to find and hard to hit by accident with a vertical motion. |
| Outer ring trigger | Dwell; pull beyond the band; a second handle | Dwell 0.5 s anywhere in the open arc | Deeper content on holding still is the vocabulary's "dwell reveals depth"; no extra chrome. |
| HUD density direction | Down is more; up is more | Down is more | "Pull down for more" is the common idiom; the ladder shows the direction either way. |
| HUD preview mechanism | Blend elements by fraction; switch the disclosure input at the thresholds | Switch at thresholds with hysteresis; the normal 0.2 s fades | Faithful: the preview is exactly what the profile would show, and the controller's own anti-flicker applies. A fractional blend is a possible later refinement. |
| Edge gestures | Screen edges; handles inside the margin | Handles inside the 24-unit margin | Android reserves the edges for back and home. |
| Cockpit routing | Full-screen cockpit graphic behind the HUD; explicit forwarding from one surface | Explicit forwarding | One testable routing policy; no dependence on draw order for the cockpit. |
| Two-finger tap | Pause now; recognise only | Recognised, unused | Pause and the sheet belong to the next wave; the recognizer is ready. |

## Variants to compare on a device

Each is a setting, so a build can carry both sides.

| Variant | Setting | A | B |
|---|---|---|---|
| Lever handover | `GestureSession.Settings.leverHandoverSeconds` / `leverHandoverOverreach` | Rest 0.18 s, overreach 30 | Rest only (overreach 9999), or overreach only (rest 9999) |
| Arc reveal distance | `GestureSettings.pullRevealDistance` | 9 % of the height | 6 % (quicker) and 12 % (more deliberate) |
| Selectable from | `SystemsArcSettings.selectableFromProgress` | 0.55 | 0.3 (select earlier) and 0.8 |
| Slot step | `SystemsArcSettings.slotStep` | 25° | 28° on tablets |
| Detent spacing | `FlapsLeverSettings.detentSpacing` | 76 units | 60 and 96 |
| Density step | `HudDensitySettings.stepDistance` | 110 units | 80 and 150 |
| Long press | `GestureSettings.longPressSeconds` | 0.45 s | 0.35 s and 0.6 s |
| Look sensitivity | `GestureSession.Settings.lookDegreesPerScreenHeight` | 120° | 90° and 160° |
| Interface | `FlightSession.interactionMode` / F4 | Gesture-first | Legacy rail (comparison baseline) |

## Known usability risks

- **Discoverability.** Hidden gestures fail when nothing hints at them. Mitigations
  in the slice: a visible handle with a chevron and the word SYSTEMS, a tap that
  shows the arc, a one-time hint when the phase first offers an action (at most three
  times ever), every gesture has a button twin. If H1 fails on the device, the next
  step is a short interactive onboarding, not more chrome.
- **Small phones.** On a 20:9 phone the band above the joystick region is about
  240 units tall; the HUD band is inside it, but the density pull continues below it
  into the joystick region. The joystick claims nothing after the press, so the pull
  continues; the risk is a thumb meant for the joystick landing on the band. The
  band is at the very top and the joystick region starts 16 units from the bottom,
  so the overlap is only in the x direction, not in y; verify.
- **Arc reach on tablets.** The outer ring is 412 units from the handle; on an
  11-inch tablet that is a long thumb stretch. The dwell ring may be a hand gesture
  there, not a thumb gesture.
- **Accidental flap changes.** Release on FLAPS without the lever steps one detent.
  If the device test shows accidental steps, change it to "release on FLAPS opens
  the lever (no step)" with the next pull.
- **Free look while flying.** A drag on the sky with the right thumb while the left
  flies is intended; a right thumb that *meant* the throttle but landed left of it
  will orbit the camera instead. The throttle's touch area is wide (220 units);
  watch for it.
- **Long press versus pull on the handle.** A pause of 0.45 s on the handle shows
  the explanation; pulling afterwards still opens the arc (the long press converts).
  Watch whether the plate's appearance distracts.
- **Haptic ticks.** The arc and lever report ticks; the policy keeps them 80 ms
  apart. If a device feels buzzy, raise the interval or route ticks to sound only.

## Questions for the device

1. Does the pull feel attached to the thumb, or does the unfold lag? (Unfold time
   0.22 s against the pull; try 0.12 s.)
2. Is the first slot (PARK BRAKE at 98°) where a straight pull lands?
3. Does anyone release on FLAPS expecting a selector?
4. Is the lever's zero (current detent under the thumb) understood, or do players
   expect UP at the top of a fixed track?
5. Does the sideways cancel get triggered by a natural thumb arc?
6. Does the HUD preview feel like a preview, or like the HUD changing by itself?
7. Does the explanation plate ever cover the thing it explains?
8. Do players find the outer ring? Do they want ignition there (it needs a
   confirmation; the cockpit keeps it for now)?
9. Does the hold ring appear when a thumb simply rests on the handle before pulling?
10. In 4:3, does the arc's leftmost segment sit too close to the free column's cards?

## Improvements found while building

- The recognizer's "long press then move becomes the pull" rule came from writing
  the handle route: without it, a thoughtful pause before pulling cancelled the pull.
- The arc's emphasis reads the disclosure controller's existing per-phase action
  table, so the gesture-first and legacy interfaces can never disagree about what is
  relevant; the controller needed no change.
- `UiFeedback.Tick` (sound plus the lightest haptic) gives gestures a feel distinct
  from button taps without a second feedback system.
- A fractional HUD preview (fading each element by the pull fraction) would be a
  small extension of `ContextualHud.Present` now that the preview profile reaches it.
- The lever and the ladder are generic enough to serve a trim lever and a mixture
  lever later; the arc's outer ring can take more systems as data.

## Gesture-First 2.0 (M14): hypotheses and risks

| Hypothesis | How the device test judges it |
|---|---|
| A dial is set faster by sweeping around it than by a selector, and the centre is a natural "never mind" | D2 time to a heading bug of 270; cancel-zone entries that were not meant (inspector: DO) |
| A flick on a switch is read as the switch's own motion, so it needs no label | D4 without explanation: found / not found |
| The cockpit lever and the arc lever being one lever makes the second one free to learn | D5 after B4: time to flaps 20° from the cockpit |
| Emerging from the panel keeps the link to the instrument | Testers point at the right instrument when asked "which one is this?" |
| A sub-arc from a rested segment is found by accident before it is explained | A5 discovery |
| Two fingers to pause is never triggered by a pinch or by two flight-control thumbs | C: pauses not meant per circuit (zero expected) |
| A sheet with a visible second stop is understood as "more below" | E1 without explanation: did the tester pull further? |
| Stations with a moving showcase are read as a choice, not as a loss of control | E3: tester's words; whether START was found |
| One cue per gesture, once, is enough; more is nagging | F: cues seen, gestures performed afterwards, "did the dot annoy you?" |
| Relaxed gesture speed helps testers with slower thumbs more than it hurts others | F2 with Relaxed and Quick: failed intended gestures |

Risks noted while building: the cancel zone at the dial's centre is where a thumb
rests before it decides, so a slow start may begin cancelled (the model allows it
and un-cancels on leaving); a sub-arc centred on a rested segment can overlap the
throttle on narrow screens (the view clamps to the free bounds, to be checked at
20:9); the sheet's first stop at 300 units may be below a thumb's comfortable reach
on tall phones; the showcase camera moves the world under a finger that is about
to tap START; a cue's caption may cover a flight value (the hint view keeps the
caption inside the bounds, not off the data).

Alternatives considered and not built: a long press on a dial for a numeric
keypad (slower, modal); a toggle tap on switches with no preview (no disclosure);
a pause button in the arc (the arc is for the aircraft); a carousel with page
dots and a fade between framings (the camera would cut, which is a loss of place);
a first-run tutorial for gestures (a cue at the moment of need teaches better and
costs nothing when ignored).

## Next wave (not built)

Rotary gestures for an autopilot heading and altitude once an autopilot exists;
trim and mixture levers from the same lever; a hint for the two-finger pause at a
natural moment (none found yet: MENU remains its discoverable twin); a "show
hints again" action in the settings (`PlayerProgress.ForgetGestures` exists);
hints and gesture settings on the entry screen's SETTINGS before the first flight;
per-hand layout (a left-handed mirror); the diary compared between the two
interface modes over whole sessions.
