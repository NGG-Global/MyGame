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
   prototype materials and `Assets/_Game/Settings/Aircraft/TrainerConfiguration.asset`.
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
| R | Reset to the runway start |

Gamepad: left stick pitch/roll (up = nose down), right stick X rudder, triggers
throttle, south button brakes, north button camera, select/back reset.

## 3. Mobile reference (landscape)

| Control | Where | Use |
|---|---|---|
| Joystick | Anywhere in the lower-left area (appears under the thumb) | Drag down = nose up; left/right = roll; release = neutral |
| Throttle | Lower right, vertical track | Drag up/down, 0-100 %; stays where left |
| BRAKE | Left of the throttle, only on the ground | Hold |
| CAM | Top right | Chase / cockpit |
| RESET | Top right, left of CAM | Back to the runway start, idle |

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

**Frame rate:** 30 FPS by default. To test the optional 60 FPS target: Create ›
Open Skies › Game Config in a folder named `Resources` (for example
`Assets/_Game/Settings/Resources/GameConfig.asset`), set Target Frame Rate to
60, rebuild.

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

### C. UX and legibility

1. Centre of the screen (aircraft, runway ahead) unobstructed in both views.
2. Controls translucent; aircraft visible through them.
3. Development overlays readable on the smallest test phone; absent in a
   release build.
4. CAM and RESET reachable but not hit by accident while flying.

### D. Performance (development build, then release build)

1. Frame rate steady at 30 FPS on the runway and in flight (Android GPU
   inspector or Unity Profiler connected over USB).
2. No hitching when pressing CAM or RESET.
3. Device temperature after 10 minutes of flight: note it.

## 6. Known issues and limitations

- **Unverified:** nothing has been compiled, unit-tested, played or built yet
  (no Unity in the development environment; CI waits for licence secrets).
- **No flight instruments in release builds.** There are no cockpit
  instruments yet, and the telemetry line is development-only, so a release
  build shows no airspeed or altitude. Test with development builds.
- **No touch rudder:** touch-only players cannot steer the nosewheel on the
  ground; take-offs are straight, after landing use RESET.
- **Cockpit view is a camera position only:** no modelled interior or instruments.
- **Untuned flight model:** values are provisional (see `docs/FLIGHT_MODEL.md`).
- **RESET has no confirmation:** an accidental tap restarts the flight.
- **Placeholder package name** `com.dor.openskies` must be replaced before any
  store upload.
- **Leftover sample** `ScoreCounter` (Gameplay) is unused; delete when approved.
- **Stale package lock:** `Packages/packages-lock.json` predates the added
  packages; Unity regenerates it on first open. Commit the regenerated file.

## 7. Deliberately deferred

Touch rudder / nosewheel steering; cockpit interior and instruments; contextual
layer-1 UI (flaps, trim, landing cues); flight assistance (interface only);
settings menu (including the 60 FPS option and control sensitivity); flaps,
trim, propeller effects, wind, ground effect; scenery, coastline and airport
buildings; audio; tutorials and challenges; any ads, analytics, accounts or
online services (none are included or planned for the prototype).
