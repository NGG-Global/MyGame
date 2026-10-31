# Unity official agent skills

The folders in this directory (except this file) are copied unmodified from the
`skills/` directory of Unity's official agent plugin.

| | |
|---|---|
| Source | https://github.com/Unity-Technologies/unity-agent-plugin |
| Path copied | `skills/` (every folder) |
| Source commit | `759ad5fe88dda155eab0eda2187ac92e5455fa46` (2026-10-30 13:26:36 -0400) |
| Copied on | 2026-10-30 |
| License | Unity Companion License, per the source repository's `LICENSE.md`: "Licensed under the Unity Companion License for Unity-dependent projects (see https://unity3d.com/legal/licenses/unity_companion_license)." |

The plugin's top-level `scripts/` and `assets/` folders were not copied: they are
plugin packaging tools and icons, and no skill references them.

## Using these skills in cloud sessions

Cloud sessions have no Unity Editor and no Unity CLI. Skills that drive the
Unity CLI (`unity-cli`, `new-unity-project`), a live Editor (Unity Search, MCP,
"run C# in the Editor"), or that ask to open a window, click a menu or enter
Play mode cannot run here. Follow their guidance and conventions; do not run
their commands. Turn the guidance into code (runtime code, Editor scripts run by
`BuildScript`, or tests) and let CI execute it.

## Updating

Re-copy `skills/` from a newer commit of the source repository, then update the
commit hash and date above. Do not edit the skill files in place, so updates stay
a straight copy.

## Skills included

- 2d-pixel-perfect
- asset-transformer-toolkit
- audio-setup-mixers
- build-gtk
- build-live-game
- craft-game-floors
- create-2d-physics
- create-tile-palette-or-rule-tiles
- generate-editor-search-query
- implement-in-app-purchases
- initialize-ai-navigation
- levelplay-unity-integration
- localization
- manage-sprite-atlas
- migrate-birp-to-urp
- new-unity-project
- optimize-audio
- optimize-text-mesh-pro
- optimize-web
- physics-3d-collision
- setup-multiplayer-services
- setup-vivox-voice-chat
- shader-graph-create-custom-node
- sprite-editor
- sprite-segment-3x3grid
- ui
- ui-imgui
- ui-ugui
- ui-uitk
- unity-cli
- unity-package-management
- urp-postprocessing
- validate-urp-render-graph-renderer-feature
