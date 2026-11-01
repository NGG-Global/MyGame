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

## Open decisions for the owner

1. **Typeface.** A licensed, legible sans-serif with tabular figures (for example an
   open-licence font such as Inter or Atkinson Hyperlegible, both under the SIL Open
   Font License; please verify the licence of the exact files before adding them) and
   a move to TextMeshPro for sharper text at every size.
2. **Engaged colour.** Cyan for every selected and engaged state is proposed; green
   for engaged systems is the alternative.
3. **Phone layout** for larger physical touch targets (above).
4. **Colour space.** The project renders in Gamma space. Linear is the usual choice
   for URP and gives more natural lighting; switching changes the look of every
   material and should be decided with a device test.
