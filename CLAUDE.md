# Open Skies: notes for Claude

Semi-realistic mobile flight simulator (working title). Android first, landscape.
Differentiator: touch-first UX with progressive disclosure. Detail lives in
`docs/GAME_DESIGN.md`, `docs/ARCHITECTURE.md`, `docs/FLIGHT_MODEL.md`,
`docs/TOUCH_CONTROLS.md`, `docs/CONTEXTUAL_UI.md`, `docs/COCKPIT.md`,
`docs/FLIGHT_ASSISTANCE.md`, `docs/ONBOARDING_DESIGN.md`, `docs/VISUAL_STYLE_GUIDE.md`,
`docs/MOBILE_PERFORMANCE_REPORT.md` and `docs/ROADMAP.md`. Read them before changing
design, architecture, physics, input, assistance, the HUD, the cockpit, game modes,
tutorials, visuals, audio, settings or performance.

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
  - `Options`: the player's presentation and comfort options (`GameOptions`) and
    their rules. No dependencies. Assistance, sensitivity and HUD profile keep their
    own stores; never duplicate them here.
  - `Audio`: `AudioManager` on the Audio Mixer (source-level fallback), pure
    `FlightAudioMath`, generated placeholder clips (references FlightState, Options).
    Reads telemetry values only.
  - `Platform`: graphics presets, frame rate, haptics, performance monitor
    (references Options, URP).
  - `Input`, `Camera`, `UI`, `Cockpit`: never reference `Simulation`. Input produces
    `FlightControls`; presentation reads `FlightTelemetry`, awareness state or transforms.
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
- Cockpit objects never get colliders; picking uses `CockpitPicking`.
- Flight controls (joystick, throttle, brakes, CAM, RESET) are outside HUD
  disclosure: no disclosure rule may hide or move them. Missions ask for guides and
  rail actions only through `FlightSession.SetMissionGuidance` (disclosure), never
  bypassing the player's choice or the warnings. Tutorial thresholds come from the
  aircraft data (`TrainingSpeeds`) and `MissionConfiguration`, not from code. Disclosure thresholds go
  in the settings classes (`FlightAwarenessConfiguration`), not in code.
- All flight input goes through `FlightInputRouter` providers. Any new code that
  reacts to touches (camera look, cockpit, menus) must ignore pointers owned in
  `TouchFlightControls.Ownership`. New Input System only; no `UnityEngine.Input`.
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
| Local Editor (owner) | `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml` |
| Generate scene (local) | `Unity -batchmode -quit -projectPath . -executeMethod OpenSkies.Editor.FlightPrototypeSceneBuilder.BuildFromCommandLine` |

CI secrets: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`. Never create,
request or print credentials.
