# Open Skies: prototype test plan

First hands-on test of the playable prototype: one aircraft, one runway, free
flight. Everything in this plan exists in code; **none of it has yet been run
in Unity or on a device.** Results from this plan are the first real evidence.

Read with: `docs/FLIGHT_MODEL.md` (physics, tuning) and `docs/TOUCH_CONTROLS.md`
(touch design and ownership rules).

## 1. Open the scene (Unity Editor, 6000.6.0f1)

1. Clone the repository with Git LFS installed (`git lfs install`), open the
   folder in Unity Hub with Unity **6000.6.0f1** (add the Android Build Support
   module, including OpenJDK and Android SDK & NDK Tools).
2. Wait for the first import. The Console must show **no compile errors**; if it
   does, stop and report them (this code has never been compiled before).
3. Menu **Open Skies › Rebuild Flight Prototype Scene**. This creates
   `Assets/_Game/Scenes/FlightPrototype.unity`, the mobile URP asset, the
   prototype materials and `Assets/_Game/Settings/Aircraft/TrainerConfiguration.asset`,
   and (M12) the audio mixer `Assets/_Game/Audio/OpenSkiesMixer.mixer`, the audio
   configuration `Assets/_Game/Settings/Audio/AudioConfiguration.asset`, the ground and
   sea detail textures in `Assets/_Game/Art/Textures/Prototype/` and the tree meshes in
   `Assets/_Game/Art/Meshes/Prototype/`, and the baked display aircraft in
   `Assets/_Game/Art/Meshes/Display/`. Check the Console for warnings from
   `AudioMixerSetup` (audio still works without the mixer) and from `DisplayAircraft`
   (a missing model or a nose part not found skips that aircraft; a proportion warning
   means its size needs a look).
4. Open `FlightPrototype` and press Play.
5. Run **Window › General › Test Runner**: EditMode › Run All, then PlayMode › Run All.
6. Commit the generated scene, materials, settings assets and their `.meta`
   files, so CI and other machines use the same scene.

## 2. Keyboard reference (Editor, WebGL)

| Key | Action |
|---|---|
| W / S | Stick forward (nose down) / back (nose up) |
| A / D | Roll left / right |
| Q / E | Rudder left / right; steers the nosewheel on the ground |
| Left Shift / Left Ctrl (hold) | Throttle up / down (stays where left) |
| Space (hold) | Wheel brakes |
| C | Chase / cockpit camera |
| R | Reset to the mode's start or checkpoint (runway start in Free Flight) |
| Esc | Pause menu; closes the settings screen first when it is open |
| P | Parking brake (set on the ground / release) |
| H | Next HUD profile (Smart, Detailed, Cinematic) |
| F3 | Collapse or expand the HUD inspector (development) |
| [ / ] | Flaps up / down one detent |
| L / N | Landing light / navigation lights |
| V, - / = | Cockpit view: look forward; wider / narrower field of view (hold) |

Gamepad: left stick pitch/roll (up = nose down), right stick X rudder, triggers
throttle, south button brakes, north button camera, select/back reset.

## 3. Mobile reference (landscape)

| Control | Where | Use |
|---|---|---|
| Joystick | Anywhere in the lower-left area (appears under the thumb) | Drag down = nose up; left/right = roll; release = neutral |
| Throttle | Lower right, vertical track | Drag up/down, 0-100 %; stays where left |
| BRAKE | Left of the throttle, only on the ground | Hold |
| CAM | Top right | Chase / cockpit; the button names the current view (CAM CHASE, CAM COCKPIT) |
| RESET | Top right, left of CAM | Back to the runway start, idle |
| HUD | Top right, left of RESET | HUD profile: Smart, Detailed, Cinematic |
| SYSTEMS | Above BRAKE | Opens every available action (parking brake, approach and navigation guides) |
| MENU | Top right, under CAM | Pause menu: resume, settings, restart, skip tutorial, change mode, guide destination |
| SETTINGS | Pause menu (first row, next to RESUME) and the entry screen | Display, controls, sound and graphics settings over a paused world |
| SKIP TUTORIAL | Under MENU, First Flight only | Two taps end the tutorial; the flight carries on |
| Coaching message | Bottom centre, between the joystick area and the rail | Tap for a short explanation ("?") |

No touch rudder yet: nosewheel steering is keyboard/gamepad only.

## 4. Android build

Requirements: Android Build Support module with its SDK/NDK/JDK (installed
through Unity Hub). Player settings are applied **in code**, so always build
through the Open Skies menu (or CI), not with default Build Profile settings.

**From the Editor (recommended for the first device test):**

1. Connect an Android phone with Developer options › USB debugging on.
2. Menu **Open Skies › Build Android APK (Development)**. The first time, Unity
   switches the platform to Android (long reimport). The script applies:
   landscape (both directions), IL2CPP, ARM64, target API 36, package
   `com.dor.openskies` (placeholder), mobile URP asset, prototype scene.
3. The APK is written to `Builds/Android/OpenSkies-dev.apk` and revealed in the
   file browser. Install: `adb install -r Builds/Android/OpenSkies-dev.apk`.
4. Alternative: **Open Skies › Apply Android Player Settings**, then
   File › Build Profiles › Android › Development Build ✓ › Build And Run.

**From CI:** Actions › Build › Run workflow › tick **Development build**.
Download the `OpenSkies-android-apk` artifact, unzip, `adb install -r OpenSkies.apk`.
Needs the `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` secrets.

Use a **development build** for testing: it shows the telemetry line and the
touch diagnostics. Release builds hide all development overlays.

**Frame rate:** 30 FPS by default; Settings > Graphics > Frame rate offers 60 FPS where
the display supports it. (`GameConfig.targetFrameRate` only sets the value used before
the settings are applied at start-up.)

## 5. Manual test checklist

Record device model, Android version, build type and result per step.

### A. Player journey (Editor with keyboard, then phone with touch)

| # | Step | Expected |
|---|---|---|
| 1 | Open FlightPrototype, Play / launch app | Aircraft on the runway, chase view, level, still. Brake button visible (touch). |
| 2 | Start on the runway | Overlay: IAS 0, PWR 0 %. No creeping. |
| 3 | Adjust throttle (Shift / drag throttle up) | PWR rises; throttle stays where left. |
| 4 | Accelerate at full power | Straight acceleration; IAS rises steadily. |
| 5 | Take off: gentle back stick from ~50 kt | Lift-off around 55-65 kt, well before the runway end. Brake button fades out. |
| 6 | Fly a turn (bank with A/D or joystick) | Turns towards the low wing; some back pressure holds altitude. |
| 7 | Switch to cockpit (C / CAM) | Pilot view over the cowling; horizon and runway visible. |
| 8 | Keep flying in cockpit view | Stick and throttle respond with no re-touch; throttle unchanged by the switch. |
| 9 | Switch back to chase | Camera behind and above, level horizon. |
| 10 | Descend and land: reduce power, line up, flare | Touch-down on the main wheels; brake button reappears. |
| 11 | Brake (Space / hold BRAKE) | Stops within a few hundred metres, no nose-over. |
| 12 | Reset (R / RESET) and repeat | Runway start, at rest, throttle 0 %, chase view snaps back. |

### B. Touch and multitouch (phone; tablet if available)

1. Joystick and throttle used at the same time; diagnostics list two pointers
   (Joystick, Throttle).
2. Release the joystick while holding the throttle: throttle unchanged; pitch/roll 0.
3. Slide a joystick finger over the throttle, BRAKE, CAM and RESET: only the
   joystick reacts.
4. A second finger in the joystick area while the first is held: ignored.
5. Notification shade / Home / app switch mid-gesture, then return: no stuck
   input, throttle unchanged, controls respond to new touches.
6. Rotate between the two landscape orientations mid-flight: controls stay in
   their corners, joystick base stays inside its area.
7. Phone with a notch or punch-hole: no control under the cutout; nothing
   under the gesture bar.

### B2. Contextual HUD (Editor with the inspector open, then phone)

Reference: `docs/CONTEXTUAL_UI.md`. Note the inspector line for any step that fails.

| # | Step | Expected |
|---|---|---|
| 1 | Start, Smart profile | Inspector: PARKED. No flight data on screen; PARK BRAKE offered above SYSTEMS. |
| 2 | P (or PARK BRAKE), full throttle | "BRAKE SET"; the aircraft holds. Release: it rolls. |
| 3 | Take-off roll | TAKEOFF ROLL within ~2 s; IAS appears. |
| 4 | Lift-off, climb | INITIAL CLIMB (reason lift-off), then CLIMB above ~600 ft; ALT and VS shown. |
| 5 | Level off, wings level for 10 s | CRUISE, then "stable": IAS and ALT recede, HDG goes; NAV offered. |
| 6 | Bank into a turn | Information returns at once. |
| 7 | NAV | Arrow and distance to the airfield; stays until NAV is tapped again. |
| 8 | Fly to ~5 NM on the extended centreline, below 1500 ft | APPROACH; approach guide with lights and centreline marker. |
| 9 | Go around (full power, climb) from short final | INITIAL CLIMB, reason go-around; no ground phase. |
| 10 | Land; touch-and-go | LANDING, then TAKEOFF ROLL (touch-and-go), INITIAL CLIMB. |
| 11 | Land to a stop | LANDING, ROLLOUT, TAXI, PARKED; PARK BRAKE offered again. |
| 12 | Rejected take-off at ~40 kt | ROLLOUT, reason take-off rejected. |
| 13 | H through all profiles during flight | Stick and throttle keep working; joystick and throttle never move. Cinematic: no routine data in chase view. Detailed: all data and actions. |
| 14 | Stall at altitude in Cinematic | STALL banner and airspeed appear; nothing blocks the controls. |
| 15 | Dive steeply below 2000 ft | SINK RATE (amber); above 160 kt OVERSPEED (red). |
| 16 | Android: Developer options › Remove animations, return to the app | HUD changes without fades or movement. |

### B3. Cockpit

Run the device checklist in `docs/COCKPIT.md` (fly a circuit from the cockpit,
instrument focus, switch selectors, look and pinch, multitouch, focus loss, HUD
and cockpit agreeing on flaps and parking brake, engine off confirmation).

### C. UX and legibility

1. Centre of the screen (aircraft, runway ahead) unobstructed in both views.
2. Controls translucent; aircraft visible through them.
3. Development overlays readable on the smallest test phone; absent in a
   release build.
4. CAM and RESET reachable but not hit by accident while flying.

### D. Performance (development build, then release build)

Follow the protocol in `docs/MOBILE_PERFORMANCE_REPORT.md` (15-minute flight with the
CSV log, presets, Profiler and Frame Debugger captures) and fill in its results table.
Quick checks:

1. The PERF line (development builds, under the telemetry) shows 30 FPS with p95
   under 36 ms on the runway and in flight.
2. No hitching when pressing CAM, RESET, MENU or SETTINGS, or when changing a preset.
3. GC count on the PERF line does not rise during steady flight.
4. Device temperature after 15 minutes of flight: note it.

### E. Presentation (M12): look, sound, haptics, settings

Reference: `docs/VISUAL_STYLE_GUIDE.md`. Phone first, then a tablet. Note device,
build and result per step.

| # | Step | Expected |
|---|---|---|
| 1 | Launch | Entry screen over the runway: three modes and SETTINGS; hazy horizon, beach between grass and sea, hills to the west, trees around the airfield, apron with two hangars. |
| 1b | Taxi towards the apron; look at the three display aircraft | F-16, Spitfire and Beechcraft parked nose east, on their wheels (not floating or sunk), real proportions (Spitfire wider than long, F-16 longer than wide), flat colours (grey, dark green, red); taxiing into a fuselage stops the aircraft; they disappear smoothly with distance, no hitch when they appear. |
| 2 | SETTINGS on the entry screen | Settings panel over a dimmed world: DISPLAY, CONTROLS, SOUND, GRAPHICS; every button easy to hit; DONE returns to the entry screen. |
| 3 | Free Flight, look at the HUD in Smart, Detailed, Cinematic | Flight values on small dark plates, readable against bright sky; cyan for engaged buttons; nothing amber unless it is a caution. |
| 4 | Stall at altitude; dive below 2000 ft | STALL banner red with an octagon symbol and a horn; SINK RATE amber with a triangle and one chime; OVERSPEED repeats a short tone. Banners stay fully opaque at 60 % HUD opacity. |
| 5 | Throttle idle to full on the runway | Engine sound rises smoothly in pitch and loudness, brighter at full power; no clicks or steps. |
| 6 | Take-off roll, lift-off, climb to 120 kt | Rolling rumble fades at lift-off; wind noise grows with speed; more wind in a sideslip and with flaps. |
| 7 | CAM to cockpit and back during the climb | Cockpit sounds enclosed (muffled engine and wind); chase sounds open; the change takes about 0.15 s. CAM label names the view. |
| 8 | Landing: soft, then firm | A thump on touchdown, louder and lower when firm; a short haptic pulse (stronger when firm) if haptics are on. |
| 9 | Cockpit: tap a switch and choose a position | A click and a short tick from the phone; HUD and cockpit agree. |
| 10 | Settings > Controls > Haptic feedback OFF; repeat 8 and 9 | No vibration at all. With the phone's system touch feedback off, no vibration either. |
| 11 | Settings > Sound: each slider to 0 and back | The matching sounds go and return; warnings stop at 20 %, not at 0; interface taps follow the Interface level. |
| 12 | Settings > Display > Interface scale 90 / 110 % | The whole interface shrinks or grows; nothing overlaps; on a 4:3 tablet 110 % is limited and the panel says so. |
| 13 | Settings > Display > HUD opacity 60 % | Data, guides and buttons fainter; flight controls only slightly fainter; warnings unchanged. |
| 14 | Settings > Display > Motion REDUCED | No fades or rises anywhere (HUD, cards, panels); FOLLOW SYSTEM follows Android "Remove animations". |
| 15 | Settings > Graphics: Battery Saver, Balanced, High Quality | Battery Saver: no shadows, softer image, trees end closer; High: sharper, longer shadows. Flight behaviour identical (fly the same take-off in each). |
| 16 | Settings > Graphics > 60 FPS on a 60/90/120 Hz device | PERF line near 60 FPS; on a device that cannot, the option is disabled with a note. |
| 17 | Esc (keyboard) with settings open over the pause menu | Settings close; the pause menu stays. |
| 18 | Relaunch the app | Every setting kept (scale, opacity, motion, haptics, volumes, quality, frame rate, HUD profile, assistance). |

### F. Pause, resume and backgrounding (phone)

| # | Step | Expected |
|---|---|---|
| 1 | MENU in flight | Everything freezes; aircraft sounds stop at once; menu taps still click. |
| 2 | RESUME | Sounds continue where they were; no stick or throttle movement until a new touch. |
| 3 | Hold the joystick and throttle, press Home, return | Pause menu shown (mobile); no sound until RESUME; no pointers in the diagnostics; throttle unchanged; stick neutral. |
| 4 | Pull down the notification shade mid-gesture, return | Controls released and answer new touches; the flight did not pause (only focus was lost). |
| 5 | A phone call or alarm during flight, then return | As step 3; audio resumes only after RESUME. |
| 6 | Settings open, press Home, return | Settings still open over the paused game; DONE returns to the pause menu. |
| 7 | Change mode from the pause menu several times | No stuck sounds from the previous flight, no duplicated loops. |

## 6. Known issues and limitations

- **Unverified in Unity:** nothing has been played, built or tested inside Unity yet
  (no Unity Editor in the development environment; CI stops at licence activation).
  Cloud sessions compile every assembly against the 6000.6.0f1 managed assemblies
  and run the EditMode tests that need no native engine; PlayMode tests have not run.
- **No flight instruments in release builds.** There are no cockpit
  instruments yet, and the telemetry line is development-only, so a release
  build shows no airspeed or altitude. Test with development builds.
- **No touch rudder:** touch-only players cannot steer the nosewheel on the
  ground; take-offs are straight, after landing use RESET.
- **Untuned flight model:** values are provisional (see `docs/FLIGHT_MODEL.md`).
- **Contextual HUD thresholds untuned:** phase, warning and disclosure values are
  provisional (`FlightAwarenessConfiguration`).
- **Placeholder audio:** every sound is generated (`PlaceholderAudio`); no recorded
  or designed sound yet. The mixer is generated by Editor code through Unity's internal
  mixer API; if that fails the game still plays (see `docs/MOBILE_PERFORMANCE_REPORT.md`).
- **Haptics are Android-only** and unverified on a device; iOS and WebGL have none.
- **Environment is placeholder** (primitives, generated textures); trees have no
  colliders; the apron and hangars are not the M5 airport.
- **No performance measurement yet**: the presets are first estimates.
- **Cockpit is a placeholder:** primitive shapes and flat dials (`docs/COCKPIT.md`
  lists the visual assets still to make). Flap values are untuned. On 4:3 tablets
  some cockpit controls sit under the action rail at the resting view.
- **RESET has no confirmation:** an accidental tap restarts the flight.
- **Placeholder package name** `com.dor.openskies` must be replaced before any
  store upload.
- **Leftover sample** `ScoreCounter` (Gameplay) is unused; delete when approved.
- **Stale package lock:** `Packages/packages-lock.json` predates the added
  packages; Unity regenerates it on first open. Commit the regenerated file.

## 7. Deliberately deferred

Touch rudder / nosewheel steering; modelled cockpit art; trim; propeller effects,
wind, ground effect; the M5 airport and coastline; recorded or designed sound; a
licensed typeface; any ads, analytics, accounts or online services (none are
included or planned for the prototype).
