# Open Skies: architecture

## Goals

- The flight simulation is independent of input and presentation, so it can be
  tested without Unity scenes and replaced without touching UI or cameras.
- Each layer is small and replaceable: touch controls, a real flight model or
  an interactive cockpit slot in without rewiring everything.
- Everything that matters can be built and verified from code, because most
  development happens without an Editor.

## Layers and assemblies

All game code is under `Assets/_Game/Scripts/`, one assembly definition per
layer. The asmdef references are the dependency rules: if a reference is not
listed, the code cannot compile against it.

```
              ┌──────────────────────── Gameplay (composition root) ───────────────────────┐
              │                                                                             │
   Input ─────┤ FlightControls ──▶ Simulation ──▶ FlightTelemetry ──▶ Awareness ──▶ UI      │
              │                                        │            (phase, warnings,       │
              │                                        │             disclosure)            │
              │                                        └────────────▶ (Camera reads the     │
              │                                                       aircraft Transform)  │
              └─────────────────────────────────────────────────────────────────────────────┘
```

| Folder | Assembly | Namespace | References | Responsibility |
|---|---|---|---|---|
| `FlightState/` | `OpenSkies.FlightState` | `OpenSkies.FlightState` | none | Contracts: `FlightControls` (pilot commands), `FlightTelemetry` (read-only snapshot), `IFlightAssist`, `FlightUnits`; `AircraftSystems` (the one authoritative state of switches and levers), its policy and `SystemChangeGate`. |
| `Simulation/` | `OpenSkies.Simulation` | `OpenSkies.Simulation` | FlightState | `AircraftController` (Rigidbody forces), `AircraftTelemetry`, `AircraftConfiguration` (ScriptableObject); pure functions `Aerodynamics`, `EngineModel`, `LandingGear`, `Atmosphere`, `AttitudeMath`. See `docs/FLIGHT_MODEL.md`. |
| `Input/` | `OpenSkies.FlightInput` | `OpenSkies.FlightInput`, `.TouchControls` | FlightState, Unity.InputSystem, UnityEngine.UI | `IFlightControlSource`, `IFlightInputProvider`, `FlightInputRouter` (merges providers, owns the throttle), keyboard/gamepad provider, `ParkingBrake` provider, touch controls with `PointerOwnership` (see `docs/TOUCH_CONTROLS.md`). |
| `Camera/` | `OpenSkies.CameraSystems` | `OpenSkies.CameraSystems` | none | `CameraRig` (chase, cockpit with look, recentring and field of view), `ChaseCameraMath`, `CockpitLookMath`. |
| `Awareness/` | `OpenSkies.Awareness` | `OpenSkies.Awareness` | FlightState | Situational awareness in plain C#: `FlightPhaseEstimator`, `RunwayGeometry`, `FlightWarningMonitor`, `DisclosureController`, their settings and `FlightAwarenessConfiguration`. See `docs/CONTEXTUAL_UI.md`. |
| `UI/` | `OpenSkies.UI` | `OpenSkies.UI` | FlightState, Awareness, UnityEngine.UI | `HudPresenter`, `TelemetryReadout`, safe area; contextual HUD (`ContextualHud`, `ContextualHudFactory`, `HudButton`, `HudFade`, `DisclosureInspector`); instruments (`InstrumentFace`, `InstrumentFaceFactory`, `InstrumentMath`) the cockpit focus panel and cockpit view controls (LOOK FWD). |
| `Cockpit/` | `OpenSkies.Cockpit` | `OpenSkies.Cockpit` | FlightState, FlightInput, CameraSystems, Unity.InputSystem, UnityEngine.UI | Interactive cockpit: `CockpitInteractionManager` (touch routing with the shared pointer ownership), `CockpitInteractable`, `CockpitLayout`, `CockpitPicking`, switch, selection-frame and exterior light views. See `docs/COCKPIT.md`. |
| `Environment/` | (none yet; add `OpenSkies.World` with the first script) | `OpenSkies.World` | | Runtime world code (airport, water, time of day) when it exists. |
| `Gameplay/` | `OpenSkies.Gameplay` | `OpenSkies.Gameplay` | all runtime layers | `FlightSession` wires layers together (including awareness and the contextual HUD); `HudPreferences`, `AppBootstrap`, `GameConfig`. Future: modes, tutorials, challenges. |
| `Editor/` | `OpenSkies.Editor` | `OpenSkies.Editor` | runtime layers, URP, Input System | `BuildScript`, `FlightPrototypeSceneBuilder`, `RenderPipelineSetup`. |
| `Tests/EditMode`, `Tests/PlayMode` | `OpenSkies.Tests.*` | `OpenSkies.Tests` | runtime layers | Unit tests and player-loop tests. |

Namespaces avoid `Camera`, `Input` and `Environment` as final segments where
they would hide `UnityEngine.Camera`, `UnityEngine.Input` or `System.Environment`.

## Data flow per frame

Physics steps (`FixedUpdate`, default 50 Hz), in script execution order:

1. `FlightSession` (order −50): `FlightInputRouter.Read(dt)` (keyboard/gamepad
   and touch providers merged) → `AircraftSystems.ApplyTo` (parking brake, flaps,
   ignition) → `IFlightAssist.Apply` (pass-through for now) →
   `AircraftController.SetControls`. Then awareness on the latest
   telemetry sample: runway geometry, `FlightPhaseEstimator.Update`,
   `FlightWarningMonitor.Update`.
2. `AircraftController` (order 0): moves control surfaces and flaps towards the
   commands at limited rates, updates the engine (ignition states), computes aerodynamic forces and
   moments from body-axis velocity, adds thrust and landing-gear forces to the
   Rigidbody. Unity applies gravity and integrates.
3. `AircraftTelemetry` (order 50): samples the Rigidbody into `FlightTelemetry`
   (including height above ground from one downward raycast).

Frames:

4. `FlightSession.Update`: session commands (reset, camera, parking brake, HUD
   profile, inspector), telemetry to `HudPresenter`, throttle value and
   weight-on-wheels to the touch controls, `DisclosureController.Update`, then
   `ContextualHud.Present` (fades run in its `LateUpdate`, unscaled time), then
   `CockpitSession.Update` (cockpit on/off by view, instruments from telemetry,
   focus panel). Cockpit touches arrive from `EventSystem.Update` like the touch
   controls'.
   Touch widgets receive pointer events from `InputSystemUIInputModule` during
   `EventSystem.Update`; the provider reads their state at the next physics step.
5. Rigidbody interpolation moves the aircraft transform smoothly between steps;
   `CameraRig.LateUpdate` follows it.

Presentation never writes to the simulation. The only way to influence flight
is a `FlightControls` value from an `IFlightControlSource`.

## Key decisions

| Decision | Reason |
|---|---|
| Rigidbody physics, forces only | Unity integrates motion, gravity and collisions; the aircraft is never moved by setting its transform (except reset). |
| Aerodynamics, engine and gear as pure functions | Physics formulas are unit-tested in EditMode without scenes; `AircraftController` only gathers state and applies results. |
| Custom raycast landing gear, not `WheelCollider` | Predictable spring-damper and tyre friction tuned for an aircraft, plain code that can be tested; `WheelCollider` is tuned for cars. |
| Aircraft colliders on the built-in "Ignore Raycast" layer | Wheel raycasts can never hit the aircraft itself. Revisit when touch picking of the cockpit needs raycasts against it. |
| `IFlightAssist` between input and aircraft | Future assistance modifies commands only; the physics stays honest. |
| Input providers merged by `FlightInputRouter` | Touch, keyboard and gamepad work together; one throttle state; new devices need no physics changes. |
| One `PointerOwnership` table for all touch consumers | A touch keeps its first owner until release; later consumers (camera look, cockpit) must respect it. |
| Touch visuals generated at runtime, layout built in code | No art imports or hand-edited UI; identical layout in scene and tests. |
| SI units in simulation, pilot units only in telemetry | Avoids unit bugs in physics; one conversion point (`FlightUnits`). |
| Throttle is a lever with memory (`ThrottleLever`) | Matches a real throttle; touch and devices move it rather than hold it. |
| Camera reads transforms, not telemetry | Switching view cannot affect control (design pillar 2). |
| Phase of flight inferred in its own layer (`Awareness`), exposed apart from telemetry | Telemetry is measured, the phase is a belief with a confidence; UI, future assists and tutorials can share it without depending on Simulation. |
| Disclosure decided in plain C#, presented by the UI | The rules are testable with scripted telemetry; presentation only fades elements at fixed positions. Flight controls are outside the system, so no rule can hide them. |
| Awareness at the physics rate, disclosure per frame | Phase and warnings see every sample and are frame-rate independent; presentation follows the display. |
| One authoritative `AircraftSystems`, applied by the session to any control source | Cockpit, HUD and keyboard change it through one gate with one set of rules; the simulation and every view read it, so they cannot disagree, even when a test or future autopilot replaces the control source. |
| Cockpit picking by math, not colliders | Pick boxes larger than the visuals and a screen tolerance; no colliders that could touch the aircraft's physics or the gear raycasts. |
| Cockpit touch surface behind every control, sharing `PointerOwnership` | Flight controls and HUD buttons always win a touch; the cockpit claims what it uses, so revocation and liveness checks cover it. |
| Scenes generated by Editor code | Repeatable and reviewable without an Editor; no hand-written scene YAML or invented GUIDs. |
| uGUI with the built-in legacy font for now | Works without importing TextMeshPro resources; revisit with the HUD design milestone. |
| Mobile URP asset created in code | Settings are reviewable and do not depend on Editor clicks. |

## Scenes

| Scene | Source | Contents |
|---|---|---|
| `Assets/_Game/Scenes/FlightPrototype.unity` | `FlightPrototypeSceneBuilder` | Ground plane and 1.5 km runway 36/18 (with colliders), Rigidbody aircraft with compound collider, placeholder cockpit interior and exterior lights, sun, procedural sky, chase/cockpit camera, minimal HUD with development overlay, contextual HUD, cockpit touch surface and focus panel, EventSystem. Uses `Assets/_Game/Settings/Aircraft/TrainerConfiguration.asset` and `Assets/_Game/Settings/HUD/FlightAwarenessConfiguration.asset` (each created once, never overwritten). |

The builder rebuilds the scene from scratch each run (idempotent, no
duplicates) and reuses its materials in `Assets/_Game/Art/Materials/Prototype/`.
CI generates the scene when it is not committed. Once it has been generated
in an Editor, commit the scene, its materials and their `.meta` files.

## Testing strategy

- EditMode: everything in plain C# (aerodynamic coefficients and moments,
  engine, atmosphere, landing gear, units, attitude, throttle lever, camera
  math, telemetry formatting, safe area), and flight awareness driven by scripted
  telemetry sequences (phase estimation, warnings, disclosure, a complete flight).
- PlayMode: Rigidbody behaviour (rests on its wheels, accelerates, reset clears
  motion) and composition (session delivers controls; camera switching does
  not interrupt control; contextual HUD layout and accessibility; phase
  detection and HUD profile changes with the physics aircraft).
- Performance checks (frame time, allocations) arrive with device builds; see
  `docs/ROADMAP.md`.
