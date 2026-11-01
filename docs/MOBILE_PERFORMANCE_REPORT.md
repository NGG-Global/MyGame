# Open Skies: mobile performance report (M12)

## Summary

**No device measurement exists yet.** The Android build could not be produced: CI
stops at Unity licence activation (latest run on `main`, 1 November 2026: "User is
blocked due to continuous failures"), and cloud sessions have no Unity Editor or
Android device. Every figure in this report is therefore either a code-level estimate
(marked *estimate*) or a target. Nothing here is a measured result.

What this milestone delivers instead:

1. **Instrumentation** to take the measurements on a device: `PerformanceMonitor`
   (frame-time percentiles, CPU and GPU frame times, garbage collections, memory,
   draw calls, SetPass calls, batches, and a CSV log for a 15-minute flight).
2. **Changes justified by code analysis** that remove work without needing a profile
   to prove they help (overdraw from invisible full-screen graphics, hidden HUD
   elements still drawn).
3. **Graphics presets** whose values are first estimates, to be tuned from the
   measurements below.
4. **A measurement protocol** so the first device session produces numbers that can
   be compared from build to build.

The owner's next step is in "Owner actions" at the end.

## Targets

| Metric | Baseline device, 30 FPS | Capable device, 60 FPS |
|---|---|---|
| Average frame time | ≤ 33.3 ms | ≤ 16.7 ms |
| 95th percentile | ≤ 36 ms | ≤ 18 ms |
| 99th percentile | ≤ 45 ms | ≤ 25 ms |
| Frames over 50 ms in 15 min | under 1 per minute | under 1 per minute |
| GC collections in steady flight | 0 per minute (target: none) | same |
| Memory (System Used) | under 600 MB *estimate, to set from first measurement* | same |
| Draw calls in flight | under 150 *estimate* | same |
| Sustained: 15-minute average vs first minute | within 10 % (no thermal throttling) | same |

"Baseline" and "capable" devices still need to be named by the owner (for example a
2021 mid-range phone and a 2024 upper-mid phone, plus one tablet).

## Instrumentation added

| Tool | Where | What it gives | Availability |
|---|---|---|---|
| `PerformanceMonitor` | `OpenSkies.Platform`, scene object "PerformanceMonitor" | Rolling 600-frame window: average, p50, p95, p99, max; frames over 50 ms; CPU and GPU frame time (`FrameTimingManager`); GC collection count since start; System Used Memory, GC Reserved Memory, GC Allocated In Frame; Draw Calls, SetPass Calls, Batches (`ProfilerRecorder`) | Development builds (overlay line under the telemetry); release builds only with `runInReleaseBuilds` |
| CSV log | `PerformanceMonitor.logToFile` or `StartLog()` | One line every 10 s to `Application.persistentDataPath/openskies-perf-*.csv` | Development builds |
| Frame timing stats | `PlayerSettings.enableFrameTimingStats = true` in `BuildScript` | CPU/GPU times for the monitor and the Unity Profiler | All builds made by `BuildScript` |
| Unity Profiler, Frame Debugger | Editor, connected to a development build over USB | Per-system costs, render passes, overdraw review | Development builds |
| Android GPU Inspector or the vendor's GPU profiler | Device | GPU counters, fill rate, bandwidth | Device, development build |

Uncertainties to verify on the device (stated, not assumed):

- Some `ProfilerRecorder` counters (render counts in particular) may report nothing in
  some player configurations; the monitor shows "n/a" instead of a number then.
- `FrameTimingManager` GPU times depend on the device's graphics driver; some
  devices report 0 (shown as "n/a").

## Code-level analysis and changes (no measurement required)

| Finding | Evidence | Change | Expected effect |
|---|---|---|---|
| The joystick touch region (42 % × 78 % of the screen) and the throttle's touch area (220 × 620) were transparent `Image`s. Unless culled, a transparent quad is still rasterised and blended: fill-rate cost on mobile GPUs. | `TouchControlsFactory` (before) | Replaced by `RaycastOnlyGraphic`, which emits no geometry (the cockpit touch surface already did this) | Removes up to about a third of a screen of blended overdraw every frame *estimate* |
| Hidden HUD elements (alpha 0 through a `CanvasGroup`) stayed in the canvas batches: full-screen entry-screen scrim after a mode starts, pause-card scrim, cards, rail slots, banners, guides | `HudFade` (before) | `HudFade` now sets `CanvasRenderer.cull` on every graphic of a fully hidden element and restores it (with `OnCullingChanged`) before it appears; uGUI's raycaster and rebuild already honour `cull` (checked in the uGUI 2.6.0 source) | Hidden UI costs no draw time; no layout or rebuild work on the frames in between |
| Canvas rebuilds | Values refresh at 10 Hz and only when the shown number changes (cached strings); button captions change only on state changes | Kept; the new settings screen follows the same rule (`Present` is a no-op when nothing changed) | Low UI rebuild cost in flight *estimate, verify with the Profiler's UI module* |
| Per-frame allocations | New code reviewed for allocation in `Update`/`FixedUpdate`: audio, haptics, presentation session, settings screen | `GameOptionsRules.Same` avoids boxing in the per-frame comparison; one `AndroidJavaRunnable` reused per session; strings cached | Expected 0 B per frame in flight outside development overlays *verify with GC Allocated In Frame* |
| Audio | Six looping voices plus one-shots, 2D, PCM clips generated once | Silent loops stop after 0.5 s and restart when needed; no per-frame synthesis | A handful of voices; mixer effects are two lowpass filters *estimate* |
| Shadows | One directional light, hard shadows; cascades fixed at 1 in a newly created URP asset | Presets change distance, resolution and MSAA only (no shader-variant features at runtime) | Battery Saver removes the shadow pass entirely |
| Trees | 34 clusters, each one mesh with two materials; culled below 1.2 % of screen height (LODGroup) | Vegetation distance follows the preset's LOD bias | About 2 draw calls per visible cluster; clusters beyond 1.7-4.3 km are not drawn *estimate* |

Known remaining risks (to measure, not to guess):

- **Transparent HUD over the 3D view.** Translucent buttons and plates are blended
  over the scene. They are small (the largest is a 380 × 72 banner), so the overdraw is
  limited, but it should be confirmed with the Frame Debugger.
- **Fog and detail maps.** The ground and sea now sample two textures (detail map)
  and apply exponential fog per pixel. Ground and sea cover most of the screen, so this
  is the main new fragment cost. If the GPU is the bottleneck on the baseline device,
  the first candidate is to drop the detail map on Battery Saver (a second material).
- **Depth precision.** Coplanar layers (runway markings 1-2 cm above the runway) can
  shimmer at distance on 24-bit depth buffers. No new coplanar surfaces were added;
  the beach slopes under the sea instead of overlapping it.
- **Render scale.** Balanced renders at 90 % and Battery Saver at 75 %; the UI is
  always native. Text sharpness of the 3D view at 75 % needs a visual check.

## Graphics presets (first estimates)

| Setting | Battery Saver | Balanced (default) | High Quality |
|---|---|---|---|
| Render scale | 0.75 | 0.9 | 1.0 |
| Main light shadows | off | on | on |
| Shadow distance | - | 60 m | 120 m |
| Shadow map | - | 1024 | 2048 |
| MSAA | off | off | 2x |
| LOD bias (vegetation and detail distance) | 0.6 | 1.0 | 1.5 |
| View distance (far clip, hidden by haze) | 5000 m | 6000 m | 7000 m |

Frame rate is a separate choice: 30 FPS (default) or 60 FPS where the display refreshes
at 59 Hz or more. The physics step stays at 50 Hz in every case (a PlayMode test checks
that neither the preset nor the frame rate changes `Time.fixedDeltaTime`).

The presets change a runtime copy of the project's URP asset, never the asset on disk,
and restore it when the scene ends.

## Measurement protocol (first device session)

Use a **development build** for the overlay and the CSV log, then repeat the key runs
on a **release build** with `PerformanceMonitor.runInReleaseBuilds` (development builds
are slower and allocate for their overlays).

1. Note device model, Android version, battery level, room temperature; charger
   unplugged; screen brightness 50 %; other apps closed.
2. Build: Actions > Build > Run workflow with **Development build** ticked, or the
   **Open Skies > Build Android APK (Development)** menu.
3. In the game: Settings > Graphics > Balanced, 30 FPS.
4. Start the CSV log. In a development build it can be switched on by ticking
   `logToFile` on the PerformanceMonitor object before building; the file path is
   shown by `PerformanceMonitor.LogPath`. Retrieve it with
   `adb pull /sdcard/Android/data/<package>/files/` (the package is currently the
   placeholder `com.dor.openskies`).
5. **15-minute flight:** Free Flight from the runway; take off, fly the coast to the
   lighthouse and harbour, two minutes in cockpit view, one approach and landing,
   take off again and cruise until 15 minutes. Do not pause.
6. Repeat 5 minutes each with Battery Saver and High Quality, and with 60 FPS on a
   capable device.
7. Record a Unity Profiler capture of 300 frames in cruise and on short final
   (development build, USB), and one Frame Debugger snapshot in chase and cockpit view.
8. Fill in the table below.

### Results (to be completed on the device)

| Device | Preset / FPS | Avg ms | p95 | p99 | >50 ms / min | CPU ms | GPU ms | GC / min | Memory MB | Draw calls | Batches | 15-min drift |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| *(baseline phone)* | Balanced / 30 | | | | | | | | | | | |
| *(baseline phone)* | Battery Saver / 30 | | | | | | | | | | | |
| *(capable phone)* | High Quality / 60 | | | | | | | | | | | |
| *(tablet)* | Balanced / 30 | | | | | | | | | | | |

How to read them: CPU time near the frame budget with GPU time well below it means a
CPU bottleneck (look at physics, UI rebuilds, scripts in the Profiler); the opposite
means a GPU bottleneck (render scale, shadows, fog, detail maps, overdraw). A rising
frame time over the 15 minutes with an unchanged scene suggests thermal throttling.

## Audio (configuration and fallback)

`AudioMixerSetup` creates `Assets/_Game/Audio/OpenSkiesMixer.mixer` when the scene is
built (Master → Aircraft, Environment, Interface, Warnings; two lowpass effects;
seven exposed parameters). Unity has no public API for creating a mixer, so it drives
the Editor's own mixer controller by reflection; the member names were checked against
the 6000.6.0f1 Editor assemblies. If that ever fails, the builder logs a warning and
`AudioManager` applies volumes and the cockpit filter on its sources instead. To
create the mixer by hand: Create > Audio Mixer at that path, add the four groups under
Master, add a "Lowpass Simple" effect to Aircraft and Environment, expose the volumes
and the two cutoff frequencies with the names in `MixerParameterNames`, then assign
the mixer in `Assets/_Game/Settings/Audio/AudioConfiguration.asset`.

All sounds are **generated placeholders** (`PlaceholderAudio`: harmonic series,
filtered noise, short tones; no recordings). Replace them by assigning licensed or
designed clips in the audio configuration; nothing else needs to change.

## Owner actions

1. Fix the Unity licence secrets for CI (a Unity ID without two-factor authentication,
   ideally a CI-only account; the current account must be unblocked first).
2. Name the reference devices (baseline phone, capable phone, tablet).
3. Run the protocol above and commit the filled-in results table to this file.
4. Tune the presets in `GraphicsPresets` from the measurements.
