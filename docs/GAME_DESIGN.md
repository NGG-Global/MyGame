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

- Controls: elevator, ailerons, rudder, throttle, wheel brakes, parking brake,
  flaps (0/10/20/30°), ignition (simplified), landing and navigation lights.
  Later: elevator trim, mixture, carburettor heat (**Open question**: how many
  engine controls to expose).
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

Layer 2 has a first playable version: a placeholder cockpit with six readable
instruments, touch selection with enlarged views, and working flaps, parking
brake, lights and ignition (`docs/COCKPIT.md`).

Layer 1 is implemented as the contextual HUD with three player-selectable
profiles (Cinematic, Smart by default, Detailed), driven by an estimate of the
phase of flight. Rules, tables and limits: `docs/CONTEXTUAL_UI.md`.

Rules:
- Each layer is optional and reversible; nothing forces a deeper layer.
- Flight controls are never part of disclosure: they are always reachable and
  never move. Warnings override minimalism. Engaged systems stay visible.
- A HUD element exists only if the same information is not visible in the
  current view. In cockpit view, the instruments are the information.
- The current prototype's developer telemetry line is a temporary debugging
  aid, not part of the design.

## Assistance

Three profiles, chosen from ASSIST on the action rail and remembered
(`docs/FLIGHT_ASSISTANCE.md`): **Assisted** (strong damping, bank and trim hold
on stick release, rudder coordination, gentle stall prevention, flying tips),
**Standard** (light damping, coordination, automatic trim as an option, no
auto-levelling) and **Manual** (direct control, manual rudder and trim). Warnings
stay on in all of them. Stick sensitivity is a separate setting. All profiles fly
the same flight model.

**Open question**: default level for new players. The prototype starts in
Assisted on first launch (`FlightAssistConfiguration.defaultProfile`); confirm or
change. Also open: a touch control for rudder and trim (Manual on touch has neither).

## Cameras

- **Third-person chase**: default. Follows heading and keeps the horizon level.
- **First-person cockpit**: pilot's eye view, foundation for the interactive
  cockpit.
- Switching is instant and never interrupts control input.
- Later (**Open question**): free look by drag, external tower view.

## World

One regional airport with a single runway and the surrounding coastline. The
prototype uses a flat ground plane, a primitive runway and placeholder coastal
landmarks (a lighthouse on a headland, a harbour, an islet) in `PrototypeWorld`. Real-world locations
or airport names are not used (**Open question**: fictional name and layout).

## Modes

Chosen on a minimal entry screen (`docs/ONBOARDING_DESIGN.md`); every mode flies the
same aircraft with the same physics and the player's assistance profile.

- **Free Flight**: from the runway or already in the air; navigation to the airfield on
  request, or to a landmark chosen from the menu.
- **First Flight**: a guided take-off, a short coastal flight past the lighthouse and
  the harbour, and a landing, learnt by flying. Skippable at any time.
- **Landing Challenge**: start established on final, land, get a game score with short
  feedback, retry.
- Later: more lessons (circuits, crosswind, engine failure) and challenges.

## Platform and performance

- Android first, landscape only (both orientations).
- Stable 30 FPS on baseline devices; 60 FPS as an option on capable devices
  (Settings > Graphics, with three presets: Battery Saver, Balanced, High Quality).
- Settings for comfort and access: interface scale, HUD opacity, reduced motion,
  haptics, stick sensitivity, assistance, five volumes (`docs/VISUAL_STYLE_GUIDE.md`).
- Sound: engine, airflow, wheels, surroundings, alerts and interface, enclosed in the
  cockpit; generated placeholders until designed sound exists.
- Touch targets at least 96 px tall at 1080p reference height. All UI respects
  the device safe area.

## Out of scope for now

Weather, time of day, world streaming, multiplayer, retractable gear, multiple
aircraft, damage models, monetisation.
