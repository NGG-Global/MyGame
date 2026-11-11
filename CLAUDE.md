# Open Skies: notes for Claude

Semi-realistic mobile flight simulator (working title). Android first, landscape.
Differentiator: touch-first UX with progressive disclosure. Detail lives in
`docs/GAME_DESIGN.md`, `docs/ARCHITECTURE.md`, `docs/FLIGHT_MODEL.md`,
`docs/TOUCH_CONTROLS.md`, `docs/CONTEXTUAL_UI.md`, `docs/COCKPIT.md`,
`docs/FLIGHT_ASSISTANCE.md`, `docs/ONBOARDING_DESIGN.md`, `docs/VISUAL_STYLE_GUIDE.md`,
`docs/GESTURE_INTERACTION.md`, `docs/COCKPIT_DIRECT_MANIPULATION.md`,
`docs/GESTURE_NAVIGATION.md`, `docs/TAP_DISCLOSURE_DESIGN.md`, `docs/MOBILE_PERFORMANCE_REPORT.md`
and `docs/ROADMAP.md`.
Read them before changing design, architecture, physics, input, assistance, the HUD,
the cockpit, gestures, game modes, tutorials, visuals, audio, settings or performance.

## Essentials
- Unity **6000.6.0f1** (`ProjectSettings/ProjectVersion.txt`), URP 17.6.0,
  Input System 1.20.1, uGUI 2.6.0, Test Framework 1.8.0. Do not change versions
  without approval; verify any package version against the Unity registry.
- Cloud sessions have **no Unity Editor**. CI (GameCI) is the compiler and test
  runner; the WebGL build on GitHub Pages is the playtest.
- **Never claim code compiles, tests pass or a feature works until CI has
  passed.** Check runs with the GitHub MCP tools (`actions_list`,
  `get_job_logs`), or tell the owner exactly which check to look at.
- Do not commit or push without the owner's approval.

## Architecture rules (see docs/ARCHITECTURE.md)
- One assembly per layer under `Assets/_Game/Scripts/`. Dependencies point
  inward to `OpenSkies.FlightState`; asmdef references enforce this:
  - `FlightState`: `FlightControls` (commands in) and `FlightTelemetry`
    (snapshot out). No dependencies.
  - `Simulation`: Rigidbody flight (`AircraftController`), telemetry,
    `AircraftConfiguration`, and pure physics functions. Knows nothing about
    input, cameras or UI.
  - `Awareness`: phase of flight, warnings, flying tips and HUD disclosure
    decisions in plain C# (references FlightState only). See `docs/CONTEXTUAL_UI.md`.
  - `Assistance`: the flight assistance profiles (`FlightAssistController :
    IFlightAssist`) in plain C# (references FlightState only). See
    `docs/FLIGHT_ASSISTANCE.md`.
  - `World` (`Environment/`): landmark registry and the placeholder world layout
    (`PrototypeWorld`), shared by the scene builder and the missions. No dependencies.
  - `Missions`: game modes, the First Flight tutorial (steps as data), the Landing
    Challenge, landing scoring and local progress in plain C# (references FlightState,
    Awareness, World). Missions read telemetry, the phase estimate and the pilot's
    commands; they never produce flight controls and never change the phase estimator.
    See `docs/ONBOARDING_DESIGN.md`.
  - `Cockpit`: cockpit touch routing, picking, layout, switch and light views
    (references FlightState, FlightInput, CameraSystems). See `docs/COCKPIT.md`.
  - `Gestures`: the gesture recognizer (pure state machine), the routing policy and
    the `InteractionSurface` that receives every touch no control took (references
    FlightInput). Never produces flight input. See `docs/GESTURE_INTERACTION.md`.
  - `Options`: the player's presentation and comfort options (`GameOptions`) and
    their rules. No dependencies. Assistance, sensitivity and HUD profile keep their
    own stores; never duplicate them here.
  - `Audio`: `AudioManager` on the Audio Mixer (source-level fallback), pure
    `FlightAudioMath`, generated placeholder clips (references FlightState, Options).
    Reads telemetry values only.
  - `Platform`: graphics presets, frame rate, haptics, performance monitor
    (references Options, URP).
  - `Input`, `Camera`, `UI`, `Cockpit`, `Gestures`: never reference `Simulation`. Input produces
    `FlightControls`; presentation reads `FlightTelemetry`, awareness state or transforms.
    `UI` references `Gestures` for the pure gesture maths and types only; it never touches
    the surface.
  - `Gameplay`: composition root (`FlightSession`, `GameModeSession` for the modes,
    `PresentationSession` for options, sound, haptics and graphics). The only place
    layers meet.
  - `Editor`: build script and scene builders.
- Flight is Rigidbody physics: forces and torques in `FixedUpdate` only. Never
  move the aircraft with `Transform.Translate/Rotate` or set its pose, except
  in reset and in a mode's start (`FlightSession.StartFlight` with a state from
  `FlightStartMath`, computed from the aircraft data). No fake auto-levelling in physics; assistance goes through
  `IFlightAssist` (`OpenSkies.Assistance`) and only changes commands: rate-limited,
  limited authority, never the throttle, never the aircraft's constants. Telemetry
  is built only by `TelemetryMath` (game and test lab alike).
- Physics formulas live in pure static functions with EditMode tests.
- Camera switching and UI never change flight controls. Presentation (graphics
  presets, frame rate, sound, haptics, settings) never changes the physics step,
  the aircraft or its controls; presets change only a runtime copy of the URP asset
  and only settings that need no other shader variants.
- Switch and lever positions (flaps, parking brake, lights, ignition) live only
  in `AircraftSystems`; change them only through `FlightSession.RequestSystemChange`
  / `SystemChangeGate`. Never keep a second copy of a system's state in a view.
  Instrument settings (heading bug, altimeter subscale) live only in
  `InstrumentSettings`; change them only through `FlightSession.RequestInstrumentSetting`.
  The flight model never reads them.
- Cockpit objects never get colliders; picking uses `CockpitPicking`.
- Flight controls (joystick, throttle, brakes) and the command cluster (camera, options,
  pause; RESET inside its options panel) are outside HUD disclosure: no disclosure rule
  (profile, phase) may hide or move them. The camera stays one tap away; RESET sits one
  level deep by the owner's choice (milestone 3), never because of a disclosure rule. Missions ask for guides and
  rail actions only through `FlightSession.SetMissionGuidance` (disclosure), never
  bypassing the player's choice or the warnings. Tutorial thresholds come from the
  aircraft data (`TrainingSpeeds`) and `MissionConfiguration`, not from code. Disclosure thresholds go
  in the settings classes (`FlightAwarenessConfiguration`), not in code.
- All flight input goes through `FlightInputRouter` providers. Any new code that
  reacts to touches (camera look, cockpit, menus) must ignore pointers owned in
  `TouchFlightControls.Ownership`. New Input System only; no `UnityEngine.Input`.
- Touches nothing else took go through `InteractionSurface` and `InteractionRouter`
  (`OpenSkies.Gestures`); never add another full-screen raycast target. A gesture is
  recognised by `GestureRecognizer` (explicit states; Armed is never Applied) and acts
  only on a Completed event, through `FlightSession` requests (`RequestSystemChange`,
  `PerformContextAction`, `SetHudProfile`, `ToggleCamera`, `RequestInstrumentSetting`)
  or `CameraRig`. Previews (lever detent, HUD density, dial value, switch tilt, sheet
  depth) never change committed state. Gesture thresholds and geometry live in the
  serialised settings classes (`InteractionConfiguration` asset for the scene), not in
  code; motion times are `HudStyle` tokens. Every gesture keeps a tappable fallback,
  a keyboard twin where one exists, and works with reduced motion. New gesture
  surfaces (sheets, screens, panels) are `GestureHotspot`s with their own
  `InteractionRoute`, never raycast targets of their own. The one flap lever is
  `FlapsLeverInteraction`. Gesture cues go through `GestureHints` and are remembered
  in `PlayerProgress`; the interaction diary is local, development-only, and never
  sends anything anywhere. See `docs/COCKPIT_DIRECT_MANIPULATION.md` and
  `docs/GESTURE_NAVIGATION.md`.
- Tap-first disclosure (`docs/TAP_DISCLOSURE_DESIGN.md`): every transient interface
  (the arc and its options, the focus panel, the flight settings, the coaching
  explanation, the pause sheet and its settings, the settings screen) registers with the
  one `DisclosureCoordinator` (`OpenSkies.Gestures`, owned by the surface,
  `FlightSession.Coordinator`) and tells it when it opens or closes itself. It decides
  what an outside tap collapses; the surface consumes that tap on the `Dismiss` route.
  `DisclosureController` keeps deciding eligibility; the coordinator only manages what
  the player expanded. No X-shaped close controls; no transient that needs a drag to
  open or close; paused interfaces stay through outside taps (resume is deliberate);
  warnings are never registered. A new transient surface is registered with a screen
  hit test and a collapse callback, never with a scrim handler of its own; a panel
  whose presses would bubble to a scrim handler carries a catch-all `HudButton`.
- The systems wheel is tap-first (`docs/TAP_DISCLOSURE_DESIGN.md`, milestone 2): tap
  SYSTEMS, tap an action, tap its option; the handle goes back one level, an outside tap
  folds everything. An action with options focuses the wheel (its options take the band,
  its name is on the hub); FLAPS shows the aircraft's own detents. Every option applies
  through `FlightSession.RequestSystemChange` (a hazardous one waits for its second tap;
  leaving the focus drops it). The wheel's geometry is fitted to the screen by
  `SystemsWheelLayout` (pure, EditMode-tested) on a copy of the settings, never the asset;
  it must stay clear of the flight controls, the handle, the warning banners and the top
  bar, with segments of at least 96 units. The M13/M14 pull gestures on the handle are a
  development-only comparison (`FlightSession.pullGestureComparison`, F4 cycles; never in
  a release build). Keep six primary segments at most: a seventh does not fit at 4:3 with
  full-size targets (group related systems as options instead, as the switch panel does).
- The top-right command cluster (`docs/TAP_DISCLOSURE_DESIGN.md`, milestone 3) replaces the
  old CAM, RESET, HUD profile, LOOK FWD, MENU and SKIP buttons; never add equal-weight
  buttons back to the top bar. Primary row: camera (corner, one tap, names the view),
  options (⋯) and pause (the quietest); LOOK FWD under the camera only while useful; the
  options panel holds HUD (a direct selector: CINEMATIC, SMART, DETAILED) and the tertiary
  session actions RESET, SETTINGS and SKIP TUTORIAL. Geometry lives in `CommandClusterLayout`
  (pure, EditMode-tested): visuals and 96-unit touch targets are sized separately, the panel
  stays inside the rail's inset (clear of the warnings) and above the throttle. State lives
  in `CommandClusterModel`; `CommandClusterSession` (Gameplay) registers the panel ("Command
  options") and the selector (its child) with the coordinator and applies requests through
  `FlightSession` (`ToggleCamera`, `SetHudProfile`, `ResetFlight`). Destructive session actions
  (RESET, SKIP) need a second tap between `confirmMinSeconds` and `confirmWindowSeconds`
  (`CommandClusterSettings`); every collapse drops a waiting confirmation, so dismissing the
  panel can never reset the flight. Keys are unchanged (`C`, `H`, `R`, Esc).
- The top-left flight information cluster (`docs/TAP_DISCLOSURE_DESIGN.md`, milestone 4) replaces the
  old flight data strip and guide plates; never add separate top-left plates back. Collapsed (the
  capsule: the trigger, at most three readings, one guide chip), Expanded (the panel: every reading,
  the navigation and approach details) and Focused (a reading or guide enlarged and explained in the
  panel's lower row). What is eligible stays with `DisclosureController`; how far it is open is the
  player's (`FlightInfoModel`, `FlightInfoSession`): a profile, phase or warning never opens or folds
  it, and collapsing never changes a guide, the profile or the flight. The panel and its focus are
  `DisclosureCoordinator` surfaces; a camera change, a pause and a reset fold it. Values come from
  the session's telemetry (altitude with the subscale), `NavigationRelation` and `RunwayRelation`,
  formatted only by `FlightReadouts`. Geometry lives in `FlightInfoLayout` (pure, EditMode-tested):
  96-unit targets, the open panel above the joystick region (`TouchControlsFactory.JoystickRegionTop`,
  known to `UiScaleMath`) and left of the command cluster's row. The HUD band's pull is part of the
  development pull comparison only.
- Visuals (M15): the project renders in **Linear** colour space with HDR and a global
  post-processing volume; the URP asset and the profile are made by `RenderPipelineSetup`
  (change it, not the assets). The aircraft, the terrain, the scenery and their textures
  are generated by Editor code from pure functions (`MeshKit`, `TerrainShape`,
  `ProceduralNoise`; builders `AircraftMeshBuilder`, `TerrainBuilder`, `SceneryBuilder`);
  colours of props come from `ScenePalette`. Textures are written once as PNGs under
  `Assets/_Game/Art/Textures` (delete one to regenerate it); meshes are updated in place.
  `AirframeView`, `ScrollingSurface` and `EnvironmentLighting` are visual only: they read
  telemetry and commands and never touch physics or controls. Keep the art bible in
  `docs/VISUAL_STYLE_GUIDE.md` ("The world and the aircraft") when changing the look.
- Prefer free, built-in Unity features. No new dependencies without approval.

## Conventions
- C#: namespaces `OpenSkies.<Layer>`; PascalCase types and members,
  `_camelCase` private fields, public fields only for Inspector-wired references.
- Logic in plain C# classes with EditMode tests; MonoBehaviours stay thin.
- No per-frame allocations in hot paths (no LINQ, `new`, string building,
  closures or `GetComponent` in `Update`/`FixedUpdate`).
- Simulation uses SI units (m, s, m/s); convert to knots/feet only for display
  (`FlightUnits`).
- UI: everything under a `SafeAreaFitter`, touch targets at least 96 px at
  1080p reference height, minimal by default. Colours, sizes, radii and timing come
  from `HudStyle` (mirrored by `TouchControlStyle`); amber and red are for alerts
  only; warnings are never dimmed and never colour alone. Hidden elements fade
  through `HudFade` (which culls them); touch areas draw nothing.
- Audio: tuning in `GameAudioConfiguration`, not code. No third-party recordings
  without a licence; generated sounds are labelled PLACEHOLDER. Haptics are short,
  rate-limited and optional; nothing vibrates continuously.

## Working without an Editor
- A `MonoBehaviour` (any component, including `Graphic`) or `ScriptableObject` class lives in a file
  of the same name, one per file. Otherwise Unity cannot save it into the generated scene or an asset:
  it works in code and in every test, but the player reads the scene as corrupted ("The file 'level0'
  is corrupted"). `ScriptFileTests` and the build script's missing-script check enforce this.
- Every new file or folder under `Assets/` needs a `.meta` with a fresh GUID:
  `python3 tools/unity_meta.py <paths>`. Models, images, audio and fonts get a
  GUID-only `.meta` (`--guid-only`); never hand-write their importer settings (use an
  `AssetPostprocessor`). Never change an existing GUID; move files together with
  their `.meta`.
- Third-party assets need a licence confirmed by the owner and a row in
  `docs/THIRD_PARTY_ASSETS.md` before they are committed. The flown aircraft stays
  fictional; the display aircraft keep real names by owner decision.
- Scenes are generated by Editor code, not edited by hand.
  `FlightPrototype.unity` comes from `FlightPrototypeSceneBuilder` (menu
  **Open Skies > Rebuild Flight Prototype Scene**). Change the builder, not the
  scene. Never hand-edit `.unity`/`.prefab` YAML unless there is no
  alternative, and tell the owner if you do.
- Skills in `.claude/skills/` that drive the Unity CLI or a live Editor cannot
  run in cloud sessions: follow their guidance, not their commands.

## Verification
| What | Where / command |
|---|---|
| Meta files | `python3 tools/unity_meta.py --check` (runs anywhere) |
| Device performance | `PerformanceMonitor` in development builds; protocol in `docs/MOBILE_PERFORMANCE_REPORT.md` |
| EditMode + PlayMode tests | `tests.yml` on every PR (checks "editmode/playmode test results") |
| WebGL + Android builds | `build.yml` on `main` or manual dispatch; APK artifact, WebGL on Pages |
| Android launch test | `build.yml` dispatched with **Development build**: the `launch-test` job runs the APK on an emulator (logcat and screenshots as an artifact) |
| Local Editor (owner) | `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml` |
| Generate scene (local) | `Unity -batchmode -quit -projectPath . -executeMethod OpenSkies.Editor.FlightPrototypeSceneBuilder.BuildFromCommandLine` |

CI secrets: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`. Never create,
request or print credentials.
