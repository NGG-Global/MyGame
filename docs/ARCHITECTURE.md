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
| `FlightState/` | `OpenSkies.FlightState` | `OpenSkies.FlightState` | none | Contracts: `FlightControls` (pilot commands), `FlightTelemetry` (read-only snapshot), `IFlightAssist`, `FlightUnits`; `AircraftSystems` (the one authoritative state of switches and levers), its policy and `SystemChangeGate`; `FlightAssistProfile`, `ControlSensitivity`, `AircraftEnvelope`. |
| `Assistance/` | `OpenSkies.Assistance` | `OpenSkies.Assistance` | FlightState | Flight assistance between the pilot and the aircraft (`FlightAssistController : IFlightAssist`): damping, coordination, bank and trim holds, gentle stall prevention, smooth profile blending. Plain C#. See `docs/FLIGHT_ASSISTANCE.md`. |
| `Simulation/` | `OpenSkies.Simulation` | `OpenSkies.Simulation` | FlightState | `AircraftController` (Rigidbody forces), `AircraftTelemetry`, `AircraftConfiguration` (ScriptableObject); pure functions `Aerodynamics`, `EngineModel`, `LandingGear`, `Atmosphere`, `AttitudeMath`. See `docs/FLIGHT_MODEL.md`. |
| `Input/` | `OpenSkies.FlightInput` | `OpenSkies.FlightInput`, `.TouchControls` | FlightState, Unity.InputSystem, UnityEngine.UI | `IFlightControlSource`, `IFlightInputProvider`, `FlightInputRouter` (merges providers, owns the throttle), keyboard/gamepad provider, `ParkingBrake` provider, touch controls with `PointerOwnership` (see `docs/TOUCH_CONTROLS.md`). |
| `Camera/` | `OpenSkies.CameraSystems` | `OpenSkies.CameraSystems` | none | `CameraRig` (chase, cockpit with look, recentring and field of view), `ChaseCameraMath`, `CockpitLookMath`. |
| `Awareness/` | `OpenSkies.Awareness` | `OpenSkies.Awareness` | FlightState | Situational awareness in plain C#: `FlightPhaseEstimator`, `RunwayGeometry`, `FlightWarningMonitor`, `DisclosureController`, their settings and `FlightAwarenessConfiguration`. See `docs/CONTEXTUAL_UI.md`. |
| `UI/` | `OpenSkies.UI` | `OpenSkies.UI` | FlightState, Awareness, Missions, Options, UnityEngine.UI | Design tokens (`HudStyle`, `docs/VISUAL_STYLE_GUIDE.md`), the settings screen (`SettingsScreen`, `SettingsScreenFactory`), interface scale limits (`UiScaleMath`); `HudPresenter`, `TelemetryReadout`, safe area; contextual HUD (`ContextualHud`, `ContextualHudFactory`, `HudButton`, `HudFade`, `DisclosureInspector`); instruments (`InstrumentFace`, `InstrumentFaceFactory`, `InstrumentMath`) the cockpit focus panel and cockpit view controls (LOOK FWD); mission UI (`ModeSelectScreen`, `CoachingPanel`, `ControlHighlights`, `CheckpointMarker`, `MissionCard`, `MissionUiFactory`). |
| `Cockpit/` | `OpenSkies.Cockpit` | `OpenSkies.Cockpit` | FlightState, FlightInput, CameraSystems, Unity.InputSystem, UnityEngine.UI | Interactive cockpit: `CockpitInteractionManager` (touch routing with the shared pointer ownership), `CockpitInteractable`, `CockpitLayout`, `CockpitPicking`, switch, selection-frame and exterior light views. See `docs/COCKPIT.md`. |
| `Environment/` | `OpenSkies.World` | `OpenSkies.World` | none | The world's landmarks: `Landmark`, `LandmarkRegistry`, `PrototypeWorld` (placeholder layout shared by the scene builder and the missions), `LandmarkBeacon` (checkpoint light). Later: airport, water, time of day. |
| `Missions/` | `OpenSkies.Missions` | `OpenSkies.Missions` | FlightState, Awareness, World | What the player is trying to do, in plain C#: `FirstFlightTutorial` (eight `TutorialStep`s as data, `Coach`, `WaypointTracker`), `LandingChallenge`, `LandingEvaluator` and scoring, `PlayerProgress`, `MissionConfiguration`. Reads telemetry, the phase estimate and the player's commands; never sends a control. See `docs/ONBOARDING_DESIGN.md`. |
| `Options/` | `OpenSkies.Options` | `OpenSkies.Options` | none | The player's presentation and comfort options (`GameOptions`: interface scale, HUD opacity, motion, haptics, volumes, graphics preset, frame rate), their limits and steps (`GameOptionsRules`) and the store interface. Plain C#. |
| `Audio/` | `OpenSkies.Audio` | `OpenSkies.Audio` | FlightState, Options | `AudioManager` (Audio Mixer routing, with a source-level fallback), `FlightAudioMath` (engine RPM and load, airflow, rolling, ambience, cockpit enclosure), `TouchdownDetector`, `AlertSoundScheduler`, `PlaceholderAudio` (generated placeholder clips), `GameAudioConfiguration`. Reads telemetry values; never input. |
| `Platform/` | `OpenSkies.Platform` | `OpenSkies.Platform` | Options, URP, UnityEngine.UI | Device services: `GraphicsQuality` and `GraphicsPresets` (runtime copy of the URP asset), `FrameRatePolicy`, haptics (`HapticPolicy`, `AndroidHaptics`, `HapticFeedback`), `PerformanceMonitor` and `FrameStatistics`. |
| `Gameplay/` | `OpenSkies.Gameplay` | `OpenSkies.Gameplay` | all runtime layers | `FlightSession` wires layers together (including awareness and the contextual HUD) and owns start states, the RESET target and the navigation target; `GameModeSession` runs the modes (entry screen, First Flight, Landing Challenge, pause, results, progress) on top of it; `PresentationSession` applies the player's options and runs sound, haptics, graphics presets and the settings screen; `MissionStarts`, `HudPreferences`, `PlayerPrefsProgressStore`, `PlayerPrefsOptionsStore`, `AppBootstrap`, `GameConfig`. |
| `Editor/` | `OpenSkies.Editor` | `OpenSkies.Editor` | runtime layers, URP, Input System | `BuildScript`, `FlightPrototypeSceneBuilder`, `PrototypeEnvironment` (atmosphere, beach, hills, trees, runway surroundings), `DisplayAircraft` (licensed display models baked into static meshes) and its import settings, `AudioMixerSetup`, `RenderPipelineSetup`. |
| `Tests/EditMode`, `Tests/PlayMode` | `OpenSkies.Tests.*` | `OpenSkies.Tests` | runtime layers | Unit tests and player-loop tests. |

Namespaces avoid `Camera`, `Input` and `Environment` as final segments where
they would hide `UnityEngine.Camera`, `UnityEngine.Input` or `System.Environment`.

## Data flow per frame

Physics steps (`FixedUpdate`, default 50 Hz), in script execution order:

1. `FlightSession` (order −50): `FlightInputRouter.Read(dt)` (keyboard/gamepad
   and touch providers merged) → `AircraftSystems.ApplyTo` (parking brake, flaps,
   ignition) → `IFlightAssist.Apply` (the `FlightAssistController` for the player's
   profile; the player's sensitivity is applied to the input first) →
   `AircraftController.SetControls`. Then awareness on the latest
   telemetry sample: runway geometry, navigation to the current destination,
   `FlightPhaseEstimator.Update`, `FlightWarningMonitor.Update`.
1b. `GameModeSession` (order −45): the active mission (`FirstFlightTutorial` or
   `LandingChallenge`) reads this step's telemetry, phase, warnings and the
   pilot's commands (`MissionInput`), then the session passes its navigation
   target, requested guides and RESET checkpoint to `FlightSession`. Missions
   never produce `FlightControls`.
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
5. `PresentationSession.Update` (order 60, after the modes): pause state of the
   sound (menu, entry screen, app in the background), the flight's sound from this
   frame's telemetry and view, alert sounds from the active warnings, a haptic pulse
   when a red warning appears, the settings screen. At the physics rate it detects
   touchdowns (sound and haptic). It never produces flight controls and never changes
   the physics step.
6. Rigidbody interpolation moves the aircraft transform smoothly between steps;
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
| `IFlightAssist` between input and aircraft | Assistance (`OpenSkies.Assistance`) modifies commands only, with rate limits and limited authority; every profile flies the same physics with the same telemetry. |
| One telemetry builder (`TelemetryMath`) | The game and the EditMode flight lab report identical telemetry from the same state, so assistance is tested against what it will see. |
| Input providers merged by `FlightInputRouter` | Touch, keyboard and gamepad work together; one throttle state; new devices need no physics changes. |
| One `PointerOwnership` table for all touch consumers | A touch keeps its first owner until release; later consumers (camera look, cockpit) must respect it. |
| Touch visuals generated at runtime, layout built in code | No art imports or hand-edited UI; identical layout in scene and tests. |
| SI units in simulation, pilot units only in telemetry | Avoids unit bugs in physics; one conversion point (`FlightUnits`). |
| Throttle is a lever with memory (`ThrottleLever`) | Matches a real throttle; touch and devices move it rather than hold it. |
| Camera reads transforms, not telemetry | Switching view cannot affect control (design pillar 2). |
| Missions in their own layer, apart from the phase estimator | The tutorial tracks what the player is trying to do; the estimator tracks what the aircraft is doing. Missions read the estimate and never change it, and have no path to the flight controls, so a tutorial cannot take over the aircraft. |
| Scenario starts computed from the aircraft data (`FlightStartMath`) | An airborne or final-approach start is a steady condition of the same flight model (speed, attitude, power, flaps), placed once like a reset; no tutorial physics. |
| Phase of flight inferred in its own layer (`Awareness`), exposed apart from telemetry | Telemetry is measured, the phase is a belief with a confidence; UI, future assists and tutorials can share it without depending on Simulation. |
| Disclosure decided in plain C#, presented by the UI | The rules are testable with scripted telemetry; presentation only fades elements at fixed positions. Flight controls are outside the system, so no rule can hide them. |
| Awareness at the physics rate, disclosure per frame | Phase and warnings see every sample and are frame-rate independent; presentation follows the display. |
| One authoritative `AircraftSystems`, applied by the session to any control source | Cockpit, HUD and keyboard change it through one gate with one set of rules; the simulation and every view read it, so they cannot disagree, even when a test or future autopilot replaces the control source. |
| Cockpit picking by math, not colliders | Pick boxes larger than the visuals and a screen tolerance; no colliders that could touch the aircraft's physics or the gear raycasts. |
| Cockpit touch surface behind every control, sharing `PointerOwnership` | Flight controls and HUD buttons always win a touch; the cockpit claims what it uses, so revocation and liveness checks cover it. |
| Scenes generated by Editor code | Repeatable and reviewable without an Editor; no hand-written scene YAML or invented GUIDs. |
| uGUI with the built-in legacy font for now | Works without importing TextMeshPro resources; a licensed typeface and TextMeshPro are an open decision (`docs/VISUAL_STYLE_GUIDE.md`). |
| Design tokens in one class per layer (`HudStyle`, mirrored by `TouchControlStyle`) | Every factory reads the same colours, sizes and timing; layers do not share code, so an EditMode test keeps the mirror in step. |
| Options apart from flight preferences | Assistance, sensitivity and the HUD profile keep their own single stores; `GameOptions` holds only presentation and comfort, and none of it can reach the flight model. |
| Audio through the Audio Mixer, with a fallback | Volumes and the cockpit filter are mixer parameters; the mixer is generated by Editor code through Unity's internal API (no public one exists), so `AudioManager` applies the same on its sources when the mixer or a parameter is missing. |
| Placeholder sounds generated once, not synthesised per frame | No third-party recordings, no audio assets to license, a few milliseconds at start-up; designed clips replace them in the configuration. |
| Graphics presets on a runtime copy of the URP asset | The asset on disk is never changed; only settings that need no other shader variants change at runtime (render scale, shadow distance and resolution, MSAA); the physics step never follows the frame rate. |
| Hidden HUD elements culled, touch areas draw nothing | `HudFade` culls fully hidden graphics; the joystick and throttle touch areas use a graphic without geometry. Less overdraw on mobile GPUs. |
| Haptics through the system's haptic feedback | Android `performHapticFeedback` follows the player's system setting and needs no vibration permission; a policy limits every event so nothing vibrates continuously. |
| Mobile URP asset created in code | Settings are reviewable and do not depend on Editor clicks. |

## Scenes

| Scene | Source | Contents |
|---|---|---|
| `Assets/_Game/Scenes/FlightPrototype.unity` | `FlightPrototypeSceneBuilder` | Land and sea planes with a placeholder coastline (headland and lighthouse, harbour breakwater, islet; `PrototypeWorld`), haze, daylight ambient, a sloping beach, two-scale ground and sea detail, hills to the west, tree clusters, runway edge and aiming-point markings, an apron with two hangars (`PrototypeEnvironment`), three parked display aircraft (`DisplayAircraft`: F-16, Spitfire, Beechcraft Model 17; scenery only), audio (`AudioManager` with the generated mixer), the presentation session, settings screen and performance monitor, 1.5 km runway 36/18 (with colliders), entry screen and mission UI, `GameModes` (`GameModeSession`), Rigidbody aircraft with compound collider, placeholder cockpit interior and exterior lights, sun, procedural sky, chase/cockpit camera, minimal HUD with development overlay, contextual HUD, cockpit touch surface and focus panel, EventSystem. Uses `Assets/_Game/Settings/Aircraft/TrainerConfiguration.asset` and `Assets/_Game/Settings/HUD/FlightAwarenessConfiguration.asset`, `Assets/_Game/Settings/Assistance/FlightAssistConfiguration.asset` and `Assets/_Game/Settings/Missions/MissionConfiguration.asset` (each created once, never overwritten). |

The builder rebuilds the scene from scratch each run (idempotent, no
duplicates) and reuses its materials in `Assets/_Game/Art/Materials/Prototype/`.
CI generates the scene when it is not committed. Once it has been generated
in an Editor, commit the scene, its materials and their `.meta` files.

## Testing strategy

- EditMode: everything in plain C# (aerodynamic coefficients and moments,
  engine, atmosphere, landing gear, units, attitude, throttle lever, camera
  math, telemetry formatting, safe area), and flight awareness driven by scripted
  telemetry sequences (phase estimation, warnings, disclosure, a complete flight),
  and the First Flight and landing scoring driven the same way (`MissionScript`).
- PlayMode: Rigidbody behaviour (rests on its wheels, accelerates, reset clears
  motion) and composition (session delivers controls; camera switching does
  not interrupt control; contextual HUD layout and accessibility; phase
  detection and HUD profile changes with the physics aircraft).
- EditMode also covers the presentation logic: options, audio math, touchdown and
  alert timing, placeholder sounds, graphics presets, frame-rate policy, haptic limits,
  frame statistics, design tokens and contrast, interface scale limits.
- PlayMode covers the settings screen, the HUD at the largest allowed interface scale,
  pause and resume of the sound, options reaching the session, warnings never dimmed,
  and rendering choices that never touch the physics step.
- Performance is measured on devices with `PerformanceMonitor`; protocol and results
  in `docs/MOBILE_PERFORMANCE_REPORT.md`.
