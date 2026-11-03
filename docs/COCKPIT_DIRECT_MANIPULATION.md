# Open Skies: cockpit direct manipulation

Milestone "Gesture-First 2.0", increment A. The cockpit instruments stop being
pictures that open a card and become things the finger adjusts, with the same
vocabulary as the HUD gestures (`docs/GESTURE_INTERACTION.md`): a small movement
previews, release commits, moving into the dead zone cancels, holding explains.

Status: implemented with EditMode and PlayMode tests; compiled offline against
stand-ins for the Unity API; **not compiled by CI, not run in the Editor, not
touched on a device.** Every radius, gain and time below is a first proposal.

## What the finger can do in the cockpit

| Instrument or control | Gesture (on the enlarged instrument in the focus panel) | Preview | Commit | Cancel | Fallback |
|---|---|---|---|---|---|
| **Heading indicator** | Drag around the dial: the heading bug follows the finger's angle (1° per degree of arc; half gain within 150 units of the centre for fine setting) | A cyan bug and the previewed value on the face; a tick every 5° | Release sets the bug through `FlightSession.RequestInstrumentSetting` | Moving into the dial's centre (46 units, 10 of hysteresis): the committed bug returns, "Release to cancel" | The committed value is the one the HUD course readout uses; keyboard: none needed (a bug is never required) |
| **Altimeter** | Drag around the dial: the subscale (QNH, 950 to 1050 hPa) | A cyan subscale value; the needles keep the committed subscale until release | Release sets the subscale; indicated altitude = true altitude + (QNH − 1013.25) × 27 ft on the face and on the HUD | As above | 1013 by default; the HUD still shows altitude |
| **Navigation display** | Drag pans (up to 1.5 radii), pinch zooms (6 range steps per screen height of spread, 0.3 step hysteresis), double tap recentres | The map moves under the finger | Panning is presentation only: nothing is committed | A new flight or a closed panel recentres | NAV guide on the HUD |
| **Any instrument** | Hold (0.45 s, still) | The hold ring under the finger | The explanation plate, with the HUD's text for the same value | Lift | Coaching explanations |
| **Any instrument** | Tap | | Closes the panel (the map: a single tap does nothing, a double tap recentres) | | Close button |
| **Switches** (landing light, navigation lights, parking brake, ignition) | Press the switch on the panel and flick: up is on, down is off (5 % of the screen height) | The switch view tilts with the finger (`CockpitSwitchView.SetPreview`); the committed state is never touched | Release past the distance requests the change through `SystemChangeGate`; a hazardous system (ignition) opens the selector with its confirmation instead | Release short of the distance: the switch springs back | Tap the switch: the selector as before |
| **Flap lever** (cockpit) | Press the lever and drag vertically | The same `FlapsLeverInteraction` the HUD arc uses: detents from the aircraft data, hysteresis, sideways cancel; the cockpit lever and the HUD lever draw the same preview | Release sets the detent through the gate | Sideways, or revoked | Tap: the detent selector; `[` `]` keys |
| **Two-finger tap** | Anywhere in the cockpit | | Pause (`docs/GESTURE_NAVIGATION.md`) | | MENU |

Look around (drag on nothing), pinch for field of view on nothing, and double tap
to look forward are unchanged (`docs/COCKPIT.md`).

## Emerge

An instrument no longer appears in a card: it emerges from its place on the panel.
`CockpitFocusPanel.ShowInstrument` takes the screen point and scale of the cockpit
instrument and scales the enlarged face from there to its resting place in the
free column over `HudStyle.EmergeSeconds` (0.22 s, `HudEasing.EaseOut`); with
reduced motion it appears in place. The selection frame on the panel stays, so the
link between the two is visible throughout.

## One source of truth for instrument settings

`InstrumentSettings` (`OpenSkies.FlightState`) holds the heading bug and the
altimeter subscale, as `AircraftSystems` holds the switches. The cockpit face, the
HUD readout and the manipulation read it; only
`FlightSession.RequestInstrumentSetting(InstrumentSetting, int)` writes it (clamped
and wrapped by `InstrumentSettings.Set`). A preview lives in `RotaryModel` and is
drawn by the face in cyan; it never touches the settings. The flight model never
reads the instrument settings: a wrong subscale changes what is shown, never what
is flown.

## Rotary gesture

`RotaryModel` (pure C#, `RotarySettings` in `InteractionConfiguration.rotary`):

- `Begin(start, min, max, wrap, fingerFromCentre)`: the current value under the
  finger, wherever it lands on the dial.
- `Update(fingerFromCentre)`: the signed angle swept since the last sample,
  unwrapped across ±180°, scaled by `unitsPerTurn / 360` and by `fineGain` inside
  `fineRadius`; stepped to `step`; wrapped (heading) or clamped (subscale).
- Inside `cancelRadius` the preview returns to the start and `Cancelled` is true;
  leaving it by `cancelHysteresis` un-cancels. The dial centre is the natural
  "never mind" and the finger passes it on the way off the instrument.
- `TakeTicks()`: the number of `tickEvery` boundaries crossed since the last call,
  for the tick sound and haptic; counted once per crossing, never per frame.

## Map view

`MapViewModel` (`MapViewSettings` in `InteractionConfiguration.mapView`): pan in
face units limited to `maxPanRadii`, range index over the face's range table
(`InstrumentFace.RangesNm`), zoom by pinch spread with hysteresis so a wobbling
pinch does not flicker between ranges, `Recenter()` back to automatic range and no
pan. The face reads `Pan` and `RangeNm` each frame; the flight's navigation target
is untouched.

## Routing

The focus panel is a hotspot with the `Instrument` route: a press on the enlarged
face goes to the surface with that route and `InstrumentManipulation`
(`OpenSkies.Gameplay`) consumes the drag, pinch, double tap, long press and tap.
A press on the panel elsewhere (the cockpit behind it) is the `Cockpit` route as
before and reaches `CockpitInteractionManager`, which reports `ControlDragBegan` /
`ControlDragMoved` / `ControlDragEnded(revoked)` for switches and the lever; the
`CockpitSession` turns those into previews and gate requests. The manager claims the
pointer (owner "Cockpit") as before, so flight controls still win and revocation
still cancels. Nothing in this increment produces flight input.

## Configuration

| Where | What |
|---|---|
| `InteractionConfiguration.rotary` (`RotarySettings`) | `cancelRadius`, `fineRadius`, `unitsPerTurn`, `fineGain`, `step`, `tickEvery`, `cancelHysteresis` |
| `InteractionConfiguration.mapView` (`MapViewSettings`) | `maxPanRadii`, `zoomStepsPerScreenHeight`, `zoomHysteresis` |
| `CockpitSession.flickDistance` | Share of the screen height a flick must travel |
| `CockpitFocusPanel.emergeSeconds` | Emerge time (token `HudStyle.EmergeSeconds`) |
| `InstrumentSettings` constants | Subscale limits (950 to 1050 hPa), feet per hPa (27) |

## Tests

EditMode `InstrumentManipulationTests`: a quarter turn adds a quarter of the
range; wrapping through north and unwrapping many turns; fine gain beyond the fine
radius snapping to the step; the cancel zone with hysteresis; a tick every five
units; beginning inside the centre starts cancelled; map pan clamped and
recentred; pinch zoom in steps with hysteresis from the automatic range;
`InstrumentSettings` wrapping the heading, clamping the subscale and raising
`Changed`; the baro correction (standard is zero, a higher setting reads higher).
PlayMode `CockpitManipulationTests` (real `FlightSession`, synthetic pointers): the
heading bug previews during the drag and commits on release; moving into the
centre cancels; revocation mid-drag changes nothing; the subscale makes the
altimeter read higher while the aircraft stays; the map pans, zooms and recentres
with flight input untouched; a switch flick toggles the landing light through the
gate and a short flick springs back; an ignition flick in the air waits for
confirmation in the selector; the cockpit flap lever uses the shared lever and
commits on release; a two-finger tap asks for pause but a pinch does not; the
joystick keeps its finger while manipulating; the enlarged instrument emerges from
the panel instrument and returns.

Not shown by tests: whether a thumb can sweep a dial precisely on a 6-inch phone,
whether the cancel zone is hit by accident, whether the emerge reads as a link.
That is `docs/GESTURE_TEST_PLAN.md`, section D.
