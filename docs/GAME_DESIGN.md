# Open Skies: game design

Working title. Status: foundation. Items marked **Open question** need an
owner decision before they are built.

## Vision

A beautiful, semi-realistic flight simulator designed for phones and tablets.
It must feel like a real flight simulator, not an arcade flying game. Its
differentiator is touch UX: the player can fly with an almost empty screen,
move seamlessly into an interactive cockpit, and reach deeper aircraft systems
without the screen ever becoming crowded.

## Design pillars

1. **The world is the interface.** The environment and the aircraft fill the
   screen. UI appears when it is needed and fades when it is not.
2. **Always in control.** Essential flight controls (pitch, roll, yaw,
   throttle) are always reachable. Nothing (camera changes, menus, tutorials)
   takes them away mid-flight.
3. **Progressive disclosure.** Complexity is offered in layers, so a new player
   and an enthusiast use the same aircraft at different depths.
4. **One source of truth.** Information lives in one place. The HUD does not
   repeat what the cockpit instruments already show.
5. **Believable, not punishing.** Semi-realistic flight behaviour, with optional
   assistance a player can turn down as they improve.

## Aircraft

A fictional four-seat, high-wing, single-engine light trainer with fixed
tricycle landing gear. It is not modelled on a specific real aircraft and must
not use any manufacturer's name or branding.

- Controls: elevator, ailerons, rudder, throttle. Later: flaps, elevator
  trim, mixture, brakes, carburettor heat (**Open question**: how many engine
  controls to expose).
- Handling target: stable, forgiving, slow stall, clear feedback.
- Performance figures are invented prototype tuning in
  `AircraftConfiguration`, not real-aircraft data (see `docs/FLIGHT_MODEL.md`).

## Progressive disclosure layers

| Layer | Who it serves | What is on screen |
|---|---|---|
| 0: Fly | Everyone, default | World, aircraft, a flight control zone, a throttle, camera button. No numbers unless needed. |
| 1: Context | Taking off, landing, turning | Elements that appear only when relevant (e.g. flaps near the ground, a landing cue on approach) and fade afterwards. |
| 2: Cockpit | Players who want instruments | First-person cockpit with readable, touchable instruments and controls. Replaces HUD information instead of adding to it. |
| 3: Systems | Enthusiasts | Engine, electrical, fuel and avionics interactions, reached from the cockpit, never shown by default. |

Rules:
- Each layer is optional and reversible; nothing forces a deeper layer.
- A HUD element exists only if the same information is not visible in the
  current view. In cockpit view, the instruments are the information.
- The current prototype's developer telemetry line is a temporary debugging
  aid, not part of the design.

## Assistance

Optional and adjustable, off by default for experienced players
(**Open question**: default level for new players). Candidates: auto
coordination (rudder), attitude hold on stick release, stall protection,
landing guidance.

## Cameras

- **Third-person chase**: default. Follows heading and keeps the horizon level.
- **First-person cockpit**: pilot's eye view, foundation for the interactive
  cockpit.
- Switching is instant and never interrupts control input.
- Later (**Open question**): free look by drag, external tower view.

## World

One regional airport with a single runway and the surrounding coastline. The
prototype uses a flat ground plane and a primitive runway. Real-world locations
or airport names are not used (**Open question**: fictional name and layout).

## Modes

- **Free flight**: first and only mode for now.
- Later: tutorials (one skill per lesson) and challenges (landings, circuits).

## Platform and performance

- Android first, landscape only (both orientations).
- Stable 30 FPS on baseline devices; 60 FPS as an option on capable devices.
- Touch targets at least 96 px tall at 1080p reference height. All UI respects
  the device safe area.

## Out of scope for now

Weather, time of day, world streaming, multiplayer, retractable gear, multiple
aircraft, damage models, monetisation.
