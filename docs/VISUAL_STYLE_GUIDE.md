# Open Skies: visual style guide

How the interface looks and behaves, and why. The aircraft and the world are the
picture; the interface is a thin, calm layer on top of it. Every value below is in
code (`OpenSkies.UI.HudStyle`, mirrored for the touch controls in
`OpenSkies.FlightInput.TouchControls.TouchControlStyle`). Change the tokens, not
individual factories. An EditMode test (`VisualStyleTests`) fails if the two token
sets drift apart or if a critical colour pair loses contrast.

Status: designed and built in code (M12). **Not yet seen on a device or in the
Editor**: the cloud sessions have no Unity Editor, and CI cannot activate a Unity
licence yet. Treat every visual decision here as a first proposal to confirm on a
phone and a tablet (checklist in `docs/PROTOTYPE_TEST_PLAN.md`, section E).

## Principles

1. **The world first.** Nothing sits over the centre of the screen in flight.
   Panels open only in the free column between the joystick area and the action
   rail, and only when asked for.
2. **Quiet by default, loud when it matters.** Routine information is small,
   translucent and recedes in stable flight. Warnings are solid, saturated and never
   dimmed.
3. **One meaning per colour.** Colours follow the convention of modern glass
   cockpits, so a pilot reads them without learning a new code.
4. **Never colour alone.** Severity also has a shape (symbol), a word, and a sound;
   a selected option also has a filled shape and dark text.
5. **Fixed homes.** Elements fade in place; they never slide across the screen or
   reflow. Motion is short and can be turned off.
6. **Large, honest touch targets.** At least 96 units tall at the 1080-unit reference
   height; a press always answers at once.

## Colour roles

| Role | Token | Value (sRGB, alpha) | Used for |
|---|---|---|---|
| Normal information | `TextPrimary` | white, 0.95 | values, titles, captions on buttons |
| Secondary | `TextSecondary` | white, 0.74 | units, labels, descriptions |
| Quiet | `TextQuiet` | white, 0.55 | footnotes, hints |
| Text on fills | `TextOnAccent` | near-black (0.04, 0.06, 0.08) | selected buttons, caution banners |
| Selected / engaged | `Selected` | cyan (0.33, 0.80, 0.95), 0.92 | the pilot's choice, an engaged system, the open tab, coach highlights |
| Success | `Active` | green (0.40, 0.85, 0.50) | success coaching |
| Navigation | `Navigation` | magenta (0.93, 0.47, 0.93) | course arrow, centreline cue, checkpoint marker |
| Caution | `Caution` | amber (0.98, 0.72, 0.16), 0.94 | caution banners, safety coaching |
| Warning | `Warning` | red (0.82, 0.14, 0.11), 0.95 | warning banners, PAPI red |
| Alert value | `AlertValue` | light red (1, 0.60, 0.50) | a flight value tied to an active warning (airspeed in a stall) |

Surfaces:

| Token | Value | Used for |
|---|---|---|
| `ControlSurface` | (0.02, 0.03, 0.04), 0.42 | buttons drawn over the world (CAM, RESET, MENU, HUD, rail) |
| `ControlPressed` | white, 0.32 | any button while pressed |
| `PanelSurface` | (0.03, 0.035, 0.045), 0.82 | cards and panels (pause menu, settings, assistance, results) |
| `PanelControl` | white, 0.10 | buttons on a panel |
| `DataBacking` | black, 0.45 | the small plate behind each flight value and guide |
| `Scrim` | black, 0.45 (settings: 0.55) | dimming behind a modal card |

Rules:

- **Amber and red are reserved for alerts.** The earlier prototype used amber for
  "engaged" buttons and for coaching hints; both now use cyan, so amber on screen
  always means "caution".
- **No gradients, no glow, no glass blur.** Translucency is a single flat alpha.
- **Magenta is only navigation.** If something is magenta, it is about where to go.

### Contrast (WCAG 2.x ratios, checked by `VisualStyleTests`)

| Pair | Worst case background | Ratio | Target |
|---|---|---|---|
| White on a warning banner | white sky behind the banner | about 5.0 | 4.5 (AA) |
| Dark text on a caution banner | black behind the banner | about 9.6 | 4.5 |
| Dark text on a selected button | black behind | about 8.7 | 4.5 |
| Primary text on a panel | white behind | about 11 | 7 (AAA) |
| Secondary text on a panel | white behind | about 7.5 | 4.5 |
| Flight value on its plate | white sky behind | about 3.2 | 3.0 (AA, large text) |

Known limits, stated plainly:

- Flight **labels** (IAS KT, ALT FT) on their plate over a white sky are about 2.6:1:
  below AA for small text. They are secondary: the position and the large value
  carry the meaning. Over typical sky and ground the ratio is much higher.
- The **alert value** colour is a supporting cue only (about 1.6:1 against its plate
  over a white sky). The warning itself is carried by the banner, its symbol, its
  sound and, where enabled, a haptic pulse.
- The **HUD opacity** setting lowers routine information to as little as 60 % and
  therefore lowers its contrast. Warnings are never affected.

## Typography

The built-in legacy font (`LegacyRuntime.ttf`) is used until a licensed typeface and
TextMeshPro are adopted (see "Open decisions"). Sizes are canvas units at the 1080
reference height.

| Token | Size | Use |
|---|---|---|
| `DisplaySize` | 64 | entry screen title |
| `TitleSize` | 32 | card and screen titles (bold) |
| `ValueSize` | 42 | flight values (IAS, ALT, VS, HDG, GS) |
| `BodySize` | 24 | body text, row labels (26) |
| `ButtonSize` | 24 | button captions, upper case |
| `LabelSize` | 20 | value labels, two-line button captions (HUD SMART, CAM CHASE) |
| `CaptionSize` | 18 | the smallest text: footnotes, notes under a setting |

Rules: captions in upper case, sentences in sentence case, no italics, bold only for
titles and banner words. White text over the world always has the 1.5-unit drop
shadow (`TextShadow`, black 0.6).

## Space, shape and size

| Token | Value |
|---|---|
| `Space` | 8 (base step) |
| `Gap` | 16 (between neighbouring controls; rail step 112 = 96 + 16) |
| `ScreenMargin` | 24 from the safe-area edge (16 at the top) |
| `ControlRadius` | 14 (buttons, touch controls) |
| `PanelRadius` | 18 (cards, settings panel) |
| `BannerRadius` | 10 (warning banners, data plates) |
| `MinTouchTarget` | 96 × 96 |

Corners come from one rounded sprite generated at runtime; the radius is set with
the image's pixels-per-unit multiplier (`HudStyle.Round`). Small marks (PAPI lights,
the centreline cue) use discs, never sliced shapes.

## Iconography

Generated at runtime, white, tinted by role (`HudSprites`, `TouchControlSprites`):

| Symbol | Meaning |
|---|---|
| Octagon with "!" | warning (red banner) |
| Triangle with "!" | caution (amber banner) |
| Concave arrowhead | course to the destination (magenta) |
| Diamond outline | checkpoint (magenta) |
| Disc | PAPI light, centreline cue |
| Ring and disc | joystick base and knob |

Text labels stay on every control; there are no icon-only buttons.

## Motion and feedback

| Behaviour | Timing |
|---|---|
| Contextual element fade | 0.2 s smoothstep, with a 10-unit rise when it appears |
| Warning fade-in | 0.12 s, in place |
| Tab change on the settings screen | instant |
| Button press | instant 4 % shrink (`PressScale`) and pressed colour; no animation delays the action |
| Arc unfold / fold | 0.22 s / 0.16 s, ease-out with one soft overshoot of at most 4 % (`HudEasing.SoftSpring`); hover lift 0.08 s |
| Lever knob settle, ladder marker | 0.06 s / 0.04 s approach; chase free look returns in 0.45 s, eased |
| Gesture tick | the quiet tap sound and the lightest haptic pulse (`UiFeedback.Tick`), at most every 80 ms |
| Joystick knob held | 10 % larger, brighter |
| Brake held | cyan fill with dark text |
| Reduced motion | no movement, instant fades (system setting, or the player's choice in Settings > Display > Motion) |

Sound and haptics follow the same restraint: one quiet tick for a tap, a confirming
sound (and, where enabled, a light pulse) for important choices, a click and pulse
when a cockpit switch moves. Nothing vibrates continuously.

## Component rules

| Component | Rules |
|---|---|
| Flight data | Five fixed slots, top left; small plate, label above value; quiet (55 %) in stable flight. |
| Guides | Under the flight data; plate behind; magenta course and centreline cue; PAPI white/red. |
| Warning banners | Upper middle, most urgent first; solid fill, symbol left, word centred, bold; never dimmed, never take touches. |
| Action rail | Bottom right above SYSTEMS; engaged = cyan fill, dark text; disabled = 40 % opacity. |
| Top bar | HUD profile, RESET, CAM (CAM names the current view: CHASE or COCKPIT); MENU and SKIP below CAM. |
| Touch controls | White strokes with a dark edge for bright skies; idle 16-30 % opacity, held 55-80 %; throttle marks at 25, 50, 75 %. Their touch areas draw nothing. |
| Cards (pause, results, assistance, cockpit) | Free column only; `PanelSurface`; title, short lines, large buttons; RESUME and SETTINGS share the first row of the pause card. |
| Settings screen | Centred panel over a dimmed, paused world; tabs DISPLAY, CONTROLS, SOUND, GRAPHICS; label left, segments or stepper right; a footnote explains each page's limits. |
| Systems handle and arc (gesture-first) | A slim `ControlSurface` pill with SYSTEMS and a quiet chevron where SYSTEMS was; segments are radial fills of a generated annulus, `ControlSurface` at rest, lifted 4 % under the thumb, `Selected` with dark text when armed, translucent cyan with a dot when engaged, 55 % quiet, 35 % disabled; one quiet caption line. Unfolds with a single soft overshoot (4 %); folds in 0.16 s. |
| Flap lever, density ladder, explanation plate (gesture-first) | `PanelSurface` plates that appear under or next to the finger; preview in `Selected`, committed in white; hairline track or rail; captions say what releasing does. They fade like other panels and never cover the joystick region. See `docs/GESTURE_INTERACTION.md`. |

## Phones, tablets and high-density screens

The canvas scales with the screen **height** (1080 reference units), so the layout
is resolution-independent: a 1440p phone and a 1080p phone show the same physical
proportions, and high pixel density never makes elements smaller.

Physical size is the open issue. On a typical 6.5-inch phone in landscape the screen
is about 68 mm tall, so a 96-unit target is about 6 mm, smaller than the 7-9 mm usually
recommended for touch. On a 11-inch tablet the same target is over 12 mm. The
interface scale setting (90-110 %) helps at the margins, but the 1080-unit layout
does not have the vertical room for a much larger scale (see `UiScaleMath`: the
action rail under the top bar and the MENU/SKIP column over the throttle are the
limits). A phone-specific layout with fewer rail slots and a shorter throttle area is
the recommended next step once device tests confirm the problem (`docs/ROADMAP.md`).

Interface scale limits (largest scale where nothing overlaps; `UiScaleMath`):

| Screen | Largest scale | 110 % request becomes |
|---|---|---|
| 4:3 tablet | about 101 % | 101 % (the settings screen says so) |
| 16:10 tablet | about 110 % | 110 % |
| 16:9 phone | about 110 % | 110 % |
| 20:9 phone | about 110 % | 110 % |

## The world and the aircraft (visual upgrade, M15)

The picture under the interface. Everything below is generated by Editor code
(`AircraftMeshBuilder`, `TerrainBuilder`, `SceneryBuilder`, `PrototypeEnvironment`,
`RenderPipelineSetup`) from the pure functions in `MeshKit`, `TerrainShape` and
`ProceduralNoise`; textures are written once as PNGs (delete one to regenerate it from
new code), meshes are updated in place. Nothing is hand-modelled, so the look is
reviewable and reproducible in CI. Status: built in code, **not yet seen on a device**.

### Art bible

1. **Style.** A clear late-morning on a temperate coast, seen from a light aircraft:
   clean, slightly stylised realism. Simple, well-proportioned shapes with real
   materials (gloss paint, tinted glass, matte fields, a glinting sea); no outlines, no
   toon bands, no clutter.
2. **Palette.** Land `#5A7A3A` (pasture) to `#B39A54` (ripe cereal) with hedgerow
   `#2B451F`; woods `#26471F`; sea `#08304D` deep, haze `#BDCCE0`; sky from the
   procedural skybox; airframe white `#EDF0F2` with cheat-line blue `#1A4594` and a red
   pinstripe `#C71F1F`; scenery accents in `ScenePalette` (terracotta roofs, cream and
   ochre walls, orange windsock, red lighthouse bands). The interface keeps its own
   tokens (`HudStyle`).
3. **Mood.** One warm sun (`#FFF2DE`, 1.35) from the south-east over the sea, 44°
   up; soft shadows; blue sky ambient from above with warm earth bounce; the horizon
   dissolves into haze, so distance reads without a visible world edge.

### Pipeline and light

| Setting | Value | Where |
|---|---|---|
| Colour space | Linear | `ProjectSettings`, `BuildScript.ConfigureCommon` |
| URP asset | HDR on, soft shadows compiled in, 2 cascades (split 0.22), 150 m, 2048 shadow map, LDR colour grading (32 LUT), post-process data assigned | `RenderPipelineSetup.ApplyLook` |
| Post-processing | ACES tonemapping; exposure +0.15 EV, contrast +8, saturation +10; white balance +6; vignette 0.22; bloom threshold 1.05, intensity 0.35, scatter 0.65 | `RenderPipelineSetup.EnsurePostProcessProfile` (one global volume) |
| Camera | post-processing on, FXAA, HDR allowed | scene builder; `GraphicsQuality.ApplyCamera` switches it per preset |
| Sun | warm, 1.35, soft shadows, strength 0.85 | `PrototypeEnvironment.ApplyAtmosphere` |
| Ambient | trilight: sky (0.46, 0.58, 0.78), equator (0.55, 0.58, 0.62), ground (0.30, 0.28, 0.24) | same |
| Reflections | skybox cubemap, regenerated once at start (`EnvironmentLighting`, no lighting bake needed) | `OpenSkies.World` |
| Fog | exponential squared, 0.00027, haze colour matched to the sky's ground colour | same |

Presets: Battery Saver renders in SDR without post-processing and with hard shadows;
Balanced and High Quality use the full look (`GraphicsPresets`).

### The aircraft

A fictional four-seat high-wing trainer, 8.2 m long, 11 m span, modelled from
cross-sections: a lofted fuselage with real window openings and a cabin interior
behind them (floor, four seats, a baggage shelf; the instrument panel is the cockpit's),
a NACA 2412 wing with the flaps and ailerons as separate surfaces in their bays, a
stabiliser with a one-piece elevator, a swept fin with its rudder, a cowling with an
anti-glare panel and exhaust, a polished spinner with a two-blade propeller that becomes
a translucent disc at speed, spring main legs and a steering nose leg, all three wheels
in teardrop fairings, wing struts with jury struts, a pitot tube and an antenna. The
wheels sit where `AircraftConfiguration` puts them.

The livery is a generated 1024 px atlas (`AircraftMeshBuilder.LiveryColour`): white,
a blue cheat line sweeping up towards the tail with a red pinstripe under it, a blue nose
ring and tail cap, blue wing tips and fin top with red pinstripes, a dark walkway at the
wing root, door seams. Paint smoothness 0.78; glass is tinted, 42 % opaque, smoothness
0.97; the spinner is metallic.

`AirframeView` (`OpenSkies.Cockpit`) moves the parts from what the aircraft reports and
received: ailerons, elevator and rudder follow the applied commands (18°, 22°, 24°),
the flaps their actual travel, the nosewheel the rudder (20°), the propeller the engine
(650-2500 rpm, 220 cranking; the disc above 45 %). It only moves child transforms of
the visual model.

### The world

- **Terrain**: a height field over the 8 x 16 km land (`TerrainShape.Height`): exactly
  flat on the airfield and its approaches (within 900 m of the runway's 1.8 x 3 km
  surroundings), a rolling plain of a few metres, hills to about 350 m west of x = -3.3 km
  (outside every mission route), falling to the beach. Sixteen 2 x 4 km tiles with 80 m
  cells and mesh colliders; the wheels and the radio altimeter see them.
- **Land use** (`TerrainShape.Colour`, painted into a 2048 px albedo): a patchwork of
  fields (six crops, each with its own brightness), hedgerows on the field boundaries,
  woods, the mown airfield, moorland and rock on the hills, a road from the airfield to
  the town and a coast road, the town's streets, the beach. A 1024 px relief normal map
  from the same functions raises the hedgerows and gives the woods a canopy.
- **Trees**: 110 clusters of 10-16 trees (broadleaves from two or three lobes, conifers
  in tiers, five foliage colours from the palette) planted where the woodland function is,
  culled by distance (LOD bias).
- **Sea**: two drifting layers of wave normals (140 m swell, 31 m chop) with a fine
  ripple grain, smoothness 0.93: a moving surface with a sun glint, no simulation.
- **Sky**: the procedural skybox with a visible sun; a cloud deck of tileable cumulus at
  2,300 m drifting with the wind (two-sided, fogged into the haze).
- **Airfield**: asphalt with marked runway numbers (36 and 18) and touchdown bars,
  edge, threshold and taxiway lights (emissive, bloomed), a control tower, a windsock, a
  fuel station, hangars with doors, apron stand lines.
- **Town and coast**: a harbour town behind the beach (about eighty houses on a street
  grid, a church, warehouses) on the terrain; a quay with bollards and five boats in the
  harbour; a tapered lighthouse with red bands, gallery and lantern.

Budget notes and open risks are in `docs/MOBILE_PERFORMANCE_REPORT.md`.

## Open decisions for the owner

1. **Typeface.** A licensed, legible sans-serif with tabular figures (for example an
   open-licence font such as Inter or Atkinson Hyperlegible, both under the SIL Open
   Font License; please verify the licence of the exact files before adding them) and
   a move to TextMeshPro for sharper text at every size.
2. **Engaged colour.** Cyan for every selected and engaged state is proposed; green
   for engaged systems is the alternative.
3. **Phone layout** for larger physical touch targets (above).
4. **Colour space.** Switched to Linear with the visual upgrade (M15): the HDR,
   tonemapping and reflection path assume it. Reversible in `ProjectSettings` and
   `BuildScript.ConfigureCommon` if a device test says otherwise.
