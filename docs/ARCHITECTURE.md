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
| `Camera/` | `OpenSkies.CameraSystems` | `OpenSkies.CameraSystems` | none | `CameraRig` (chase with a spring-loaded free look and pinch distance, cockpit with look, recentring and field of view), `ChaseCameraMath`, `CockpitLookMath`. |
| `Gestures/` | `OpenSkies.Gestures` | `OpenSkies.Gestures` | FlightInput, Unity.InputSystem, UnityEngine.UI | The gesture layer: `GestureRecognizer` (pure state machine: tap, double tap, long press, swipe, pull, drag, dwell, pinch, two-finger tap), `GestureSettings`, `GestureMath`, `InteractionRouter` (pure routing policy), `InteractionSurface` (the one entry point for touches no control took; claims pointers in the shared `PointerOwnership`, forwards the cockpit route, consumes the tap that collapses an expanded interface), `GestureHotspot`, `HotspotShape` (a raycast target shaped by a filter), `DisclosureCoordinator` (pure: which transient interface the player expanded, the active branch, what an outside tap collapses), `ScreenRects`. Never produces flight input. See `docs/GESTURE_INTERACTION.md` and `docs/TAP_DISCLOSURE_DESIGN.md`. |
| `Awareness/` | `OpenSkies.Awareness` | `OpenSkies.Awareness` | FlightState | Situational awareness in plain C#: `FlightPhaseEstimator`, `RunwayGeometry`, `FlightWarningMonitor`, `DisclosureController`, their settings and `FlightAwarenessConfiguration`. See `docs/CONTEXTUAL_UI.md`. |
| `UI/` | `OpenSkies.UI` | `OpenSkies.UI` | FlightState, Awareness, Missions, Options, Gestures, UnityEngine.UI | Design tokens (`HudStyle`, `docs/VISUAL_STYLE_GUIDE.md`), the settings screen (`SettingsScreen`, `SettingsScreenFactory`), interface scale limits (`UiScaleMath`); `HudPresenter`, `TelemetryReadout`, safe area; contextual HUD (`ContextualHud`, `ContextualHudFactory`, `HudButton`, `HudFade`, `DisclosureInspector`); instruments (`InstrumentFace`, `InstrumentFaceFactory`, `InstrumentMath`) the cockpit focus panel and cockpit view controls (LOOK FWD); mission UI (`ModeSelectScreen`, `CoachingPanel`, `ControlHighlights`, `CheckpointMarker`, `MissionCard`, `MissionUiFactory`); the gesture-first HUD (`SystemsArcModel`/`View`, `FlapsLeverModel`/`View`, `HudDensityModel`/`Ladder`, `ExplanationPlate`, `HudExplanations`, `HudEasing`, `GestureHud`, `GestureHudFactory`, `GestureInspector`); M14: `RotaryModel`, `MapViewModel`, `SheetModel`, `EntryStationModel` (pure), `PauseSheet`, `EntryStationsScreen` and their factories, `GestureHintView`, `InteractionConfiguration` (`docs/COCKPIT_DIRECT_MANIPULATION.md`, `docs/GESTURE_NAVIGATION.md`). |
| `Cockpit/` | `OpenSkies.Cockpit` | `OpenSkies.Cockpit` | FlightState, FlightInput, CameraSystems, Unity.InputSystem, UnityEngine.UI | Interactive cockpit: `CockpitInteractionManager` (touch routing with the shared pointer ownership), `CockpitInteractable`, `CockpitLayout`, `CockpitPicking`, switch, selection-frame and exterior light views, `AirframeView` (propeller, control surfaces, flaps and nosewheel moved from the applied commands and telemetry; visual only). See `docs/COCKPIT.md`. |
| `Environment/` | `OpenSkies.World` | `OpenSkies.World` | none | The world's landmarks: `Landmark`, `LandmarkRegistry`, `PrototypeWorld` (placeholder layout shared by the scene builder and the missions), `LandmarkBeacon` (checkpoint light), `EnvironmentLighting` (skybox reflections at start, no bake), `ScrollingSurface` (the sea's wave layers and the cloud deck). Later: airport, water, time of day. |
| `Missions/` | `OpenSkies.Missions` | `OpenSkies.Missions` | FlightState, Awareness, World | What the player is trying to do, in plain C#: `FirstFlightTutorial` (eight `TutorialStep`s as data, `Coach`, `WaypointTracker`), `LandingChallenge`, `LandingEvaluator` and scoring, `PlayerProgress`, `MissionConfiguration`. Reads telemetry, the phase estimate and the player's commands; never sends a control. See `docs/ONBOARDING_DESIGN.md`. |
| `Options/` | `OpenSkies.Options` | `OpenSkies.Options` | none | The player's presentation and comfort options (`GameOptions`: interface scale, HUD opacity, motion, haptics, volumes, graphics preset, frame rate), their limits and steps (`GameOptionsRules`) and the store interface. Plain C#. |
| `Audio/` | `OpenSkies.Audio` | `OpenSkies.Audio` | FlightState, Options | `AudioManager` (Audio Mixer routing, with a source-level fallback), `FlightAudioMath` (engine RPM and load, airflow, rolling, ambience, cockpit enclosure), `TouchdownDetector`, `AlertSoundScheduler`, `PlaceholderAudio` (generated placeholder clips), `GameAudioConfiguration`. Reads telemetry values; never input. |
| `Platform/` | `OpenSkies.Platform` | `OpenSkies.Platform` | Options, URP, UnityEngine.UI | Device services: `GraphicsQuality` and `GraphicsPresets` (runtime copy of the URP asset), `FrameRatePolicy`, haptics (`HapticPolicy`, `AndroidHaptics`, `HapticFeedback`), `PerformanceMonitor` and `FrameStatistics`. |
| `Gameplay/` | `OpenSkies.Gameplay` | `OpenSkies.Gameplay` | all runtime layers | `FlightSession` wires layers together (including awareness, the contextual HUD and the interaction surface) and owns start states, the RESET target and the navigation target; `GestureSession` turns gestures into HUD previews and session requests; `FlapsLeverInteraction` is the one flap lever (arc and cockpit); `InstrumentManipulation` sets the heading bug, the subscale and the map view from the focus panel; `GestureHints` (cues, learned gestures in the progress) and `InteractionDiary` (development counts); `GameModeSession` runs the modes (entry stations or the mode select, First Flight, Landing Challenge, the pause sheet or the card, results, progress, `EntryShowcase`) on top of it; `PresentationSession` applies the player's options and runs sound, haptics, graphics presets and the settings screen; `MissionStarts`, `HudPreferences`, `PlayerPrefsProgressStore`, `PlayerPrefsOptionsStore`, `AppBootstrap`, `GameConfig`. |
| `Editor/` | `OpenSkies.Editor` | `OpenSkies.Editor` | runtime layers, URP, Input System | `BuildScript`, `FlightPrototypeSceneBuilder`, `MeshKit` (lofts, revolutions, aerofoils, solids: pure), `ProceduralNoise`, `GeneratedAssets` (PNG and mesh assets), `MaterialSetup` (URP material set-up from code), `AircraftMeshBuilder` (the flown trainer and its livery), `TerrainShape` (height and land use: pure) and `TerrainBuilder` (tiles, albedo, normal map, trees), `SceneryBuilder` (airfield dressing, town, harbour, lighthouse, clouds, water and surface textures), `ScenePalette`, `PrototypeEnvironment` (light, air, beach, markings, apron), `DisplayAircraft` (licensed display models baked into static meshes) and its import settings, `AudioMixerSetup`, `RenderPipelineSetup` (URP asset and the post-processing profile). |
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
| One `InteractionSurface` behind every control, sharing `PointerOwnership`; the cockpit routed through it | Flight controls and HUD buttons always win a touch; a pure `InteractionRouter` decides who gets the rest (gesture channel or the cockpit), so routing never depends on draw order; the surface claims what it recognises, so revocation and liveness checks cover gestures too. |
| Gestures as a pure state machine with explicit states (Candidate, Peek, ActiveManipulation, Armed, Completed, Cancelled) | Disclosure proportional to the gesture is testable with synthetic samples; "armed" is never "applied": a consumer acts only on Completed, and every system change still passes `SystemChangeGate`. |
| Gesture models in pure C# (`SystemsArcModel`, `FlapsLeverModel`, `HudDensityModel`), views draw only | Ergonomics (which wedge a thumb lands in, detent hysteresis, density steps) are unit-tested; the views carry no decisions. |
| One `DisclosureCoordinator` for everything the player expands, apart from `DisclosureController` | The controller decides what the flight context offers; the coordinator keeps the one active branch of expanded interfaces (arc, panels, sheet, settings), replaces clusters and collapses on outside taps, focus loss and resets. Owners register a hit test and a collapse callback; the surface consumes the collapsing tap on its `Dismiss` route, so nothing underneath (a switch, the free look) reacts, and no second full-screen raycast target exists. Pure and tested without a scene (`docs/TAP_DISCLOSURE_DESIGN.md`). |
| `InstrumentSettings` (FlightState) for the heading bug and the altimeter subscale, written only by `FlightSession.RequestInstrumentSetting` | As `AircraftSystems` for switches: one source of truth read by the cockpit face, the HUD and the manipulation; a preview (`RotaryModel`) never touches it; the flight model never reads it. |
| One `FlapsLeverInteraction` for the HUD arc and the cockpit lever | The same detents, hysteresis, cancel and gate request wherever the lever is grabbed; the two views draw one preview. |
| Pause sheet and entry stations as routed hotspots on the surface, with the M12 card and mode select kept behind the interface mode | A joystick finger never reaches them; the two entries can be compared on a device; keyboard twins stay. |
| Gesture cues as a policy (`GestureHints`) over `PlayerProgress`, with a view interface | Once per gesture, remembered locally, testable without a scene; no analytics service, nothing leaves the device. |
| HUD density preview as a disclosure input, not a profile change | The preview is exactly what the profile would show, with the controller's own anti-flicker; the committed profile changes only on release. |
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
| `Assets/_Game/Scenes/FlightPrototype.unity` | `FlightPrototypeSceneBuilder` | Height-field terrain in tiles with a painted land-use albedo and relief normal map (`TerrainBuilder`, `TerrainShape`), a moving sea, a cloud deck, the placeholder coastline (headland and lighthouse, harbour with quay and boats, islet; `PrototypeWorld`), a global post-processing volume and start-up reflections, haze, daylight ambient, a sloping beach, tree clusters, runway markings, numbers and lights, an apron with hangars, a tower, a windsock and a fuel station, a harbour town (`SceneryBuilder`, `PrototypeEnvironment`), three parked display aircraft (`DisplayAircraft`: F-16, Spitfire, Beechcraft Model 17; scenery only), audio (`AudioManager` with the generated mixer), the presentation session, settings screen and performance monitor, 1.5 km runway 36/18 (with colliders), entry screen and mission UI, `GameModes` (`GameModeSession`), Rigidbody aircraft with compound collider and the modelled airframe with its moving parts (`AircraftMeshBuilder`, `AirframeView`), placeholder cockpit interior and exterior lights, sun, procedural sky, chase/cockpit camera, minimal HUD with development overlay, contextual HUD, cockpit touch surface and focus panel, EventSystem. Uses `Assets/_Game/Settings/Aircraft/TrainerConfiguration.asset` and `Assets/_Game/Settings/HUD/FlightAwarenessConfiguration.asset`, `Assets/_Game/Settings/Assistance/FlightAssistConfiguration.asset` and `Assets/_Game/Settings/Missions/MissionConfiguration.asset` (each created once, never overwritten). |

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
