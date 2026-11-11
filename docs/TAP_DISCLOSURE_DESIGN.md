# Open Skies: tap-first disclosure (UI redesign, milestones 1 to 4)

The transient interfaces of the HUD, the cockpit, the coaching, the settings and
the pause no longer need a hold or a drag to open, to go deeper or to close. A
visible trigger is tapped, an option is tapped, and a tap anywhere else puts the
HUD back in its quiet state. One coordinator knows what the player has expanded
and decides what an outside tap collapses; the interaction surface consumes that
tap so nothing underneath can react to it.

Milestone 1 made every transient interface tap-first under one coordinator. Milestone 2
turns the systems arc into a polished tap-first **systems wheel** (the section of that
name below); the wheel-picker concept stays, by the owner's decision. Milestone 3
replaces the row of equal top-right buttons with a compact, hierarchical **command
cluster** (the section of that name below). Milestone 4 replaces the separate top-left
flight data and guide plates with one progressively disclosed **flight information
cluster** (the section of that name below). Together they complete Stage 1 of the UI
redesign.

Status: implemented with EditMode and PlayMode tests. Milestones 1 to 3 are on `main`,
where both test jobs passed after the merge (`tests.yml` run 48). Milestone 4 has not been
compiled or run anywhere yet (the session that wrote it had no Unity Editor and no C#
compiler): CI is its first compiler and test run, and its status is in its pull request. **None of the four has
been run in the Editor by hand or touched on a device.** Not in these milestones: the
tutorial redesign, final artwork and animation. Stage 1 ends with an APK checkpoint
(`docs/ROADMAP.md`).

## The contract

Every transient interface follows these rules:

1. **Tap a visible trigger to expand.** SYSTEMS opens the arc; a cockpit instrument
   or switch opens its focus panel; ASSIST (on the arc or the rail) opens the flight
   settings; the coaching message opens its explanation; MENU opens the pause sheet;
   SETTINGS opens the settings; the flight information capsule opens its panel.
2. **Tap an option to select, or to open deeper functionality.** An arc segment acts
   (as the rail button would) or fans its options out on the outer ring; a detent, a
   profile, a destination is chosen by a tap.
3. **Tap outside to collapse.** A tap clearly outside the active transient branch
   returns to the quiet HUD state.
4. **Tap the trigger again to collapse it.** SYSTEMS again folds the arc; ASSIST again
   closes the settings; the open action again folds its options; a tap on the
   enlarged instrument closes it.
5. **A different cluster replaces the previous one.** Tapping another trigger closes
   what was open and opens the new one (the arc gives way to the flap selector, the
   flight settings give way to the arc).
6. **No X-shaped close controls anywhere.** The close buttons of the cockpit focus
   cards and the flight settings card are gone; the coaching message shows "–"
   instead of "×" while its explanation is open. DONE on the settings screen,
   RESUME on the pause sheet, RETRY and FREE FLIGHT on a result are navigation and
   confirmation actions, not closes, and stay.
7. **No invisible required close gesture.** Every close has a tap: outside, or the
   trigger. The drags that remain (the sheet pulled down, the arc pulled) are
   alternatives, never the only way.
8. **No accidental activation underneath.** The dismissing tap is consumed by the
   interaction surface before anything behind it can see it: not a cockpit switch,
   not the free look, not a double tap that would switch the view.
9. **Warnings are not transient overlays.** Banners are never registered, never
   dismissed by a tap and never dimmed; a tap on a banner is a tap outside the
   open interface.
10. **Flight controls stay active and independently owned.** A finger the joystick,
    the throttle or the brake owns is never an outside tap. Nothing here changes
    `PointerOwnership`.

## The flight information cluster (milestone 4)

### Why

The top left used to hold up to seven separate plates: five flight values in a 780-unit
strip and two guides (approach, navigation) in a second row under it, 1,500 units of
horizontal space between them on a wide phone. Each plate faded in and out on its own, so
the corner changed shape with every phase of flight, and Detailed kept all of it on screen
all the time. The cluster puts the same information in one place with one entry point, a
small resting state and the detail on demand.

### Hierarchy

| Tier | Element | Where | How it looks | How it acts |
|---|---|---|---|---|
| Resting | Capsule | Top-left corner of the safe area | A 72-unit dark pill (the flight-data plate's contrast) in a 96-unit touch target. Left end: the **trigger**, a gauge symbol. Then up to three reading cells (label and value) and at most one guide chip | One tap opens the panel. Never moves; it widens or narrows from its right end only |
| Resting | Guide chip | In the capsule, after the readings | Navigation: magenta arrow relative to the nose, the destination's name, the distance. Approach: the runway and the four glide-path lights | Part of the capsule's tap |
| Expanded | Panel | Opens from the same corner, over the capsule's place | A `PanelSurface` plate, 818 × 196 | Registered root of the coordinator ("Flight information") |
| Expanded | Trigger | The panel's first cell, exactly where the capsule's trigger was | The same gauge on a lighter square (not cyan: cyan means a selection) | One tap folds everything |
| Expanded | Reading tiles | First row, after the trigger: IAS, ALT, VS, HDG, GS, always in that order | 136 × 88 tiles, label above a 42-unit value | One tap focuses that reading |
| Expanded | Navigation tile | Lower row, left (region A) | Arrow, destination, distance (NM), course to steer | One tap focuses it |
| Expanded | Approach tile | Lower row, right (region B) | Runway, distance to the threshold, height above it, glide-path lights, centreline marker | One tap focuses it |
| Expanded | Hint | Lower row, when there is no guide to detail | "Tap a reading to explain it" | Nothing (a press on the plate goes nowhere) |
| Focused | Enlarged reading | Region A (the guides give way) | The reading's title and its value at the display size, on a cyan-tinted plate | One tap goes back to the panel |
| Focused | Explanation | The region the focused element's own tile does not use (B for a reading or the navigation guide, A for the approach guide) | Two or three lines from `HudExplanations`, the same texts as the long-press explanations | One tap goes back to the panel |

A focused tile is also tinted cyan, but the focus is never shown by colour alone: the
lower row changes to the enlarged value and its explanation.

### States and transitions

```
                         tap capsule
        ┌────────────┐ ─────────────────▶ ┌────────────┐  tap a tile   ┌──────────────────┐
        │ COLLAPSED  │                    │  EXPANDED  │ ────────────▶ │ FOCUSED(element) │
        │  capsule   │ ◀───────────────── │   panel    │ ◀──────────── │  panel, lower    │
        └────────────┘   tap trigger,     └────────────┘  tap the same │  row = detail    │
              ▲          outside tap,           ▲         tile, or the └──────────────────┘
              │          another cluster,       │         detail            │   │ tap another
              │          camera change,         │                           │   │ tile: focus
              │          pause, reset,          │                           │   ▼ moves
              └─────────────────────────────────┴─── same collapses ────────┘
```

| From | Event | To | Coordinator |
|---|---|---|---|
| Collapsed | Tap the capsule (not over a frozen flight) | Expanded | `Open("Flight information")`: replaces any other root (the wheel, the command options, a cockpit card, the flight settings, the coaching detail) |
| Expanded | Tap a reading tile, or a guide tile the panel shows | Focused | `Open("Flight information focus")`, a child of the panel |
| Focused | Tap the focused tile again, the enlarged reading or the explanation | Expanded | `Close(focus, ParentTap)` |
| Focused | Tap another tile | Focused (the new element) | the child stays open |
| Expanded or Focused | Tap the trigger | Collapsed | `Close(panel, Trigger)` |
| Expanded or Focused | Tap outside (sky, cockpit, a banner) | Collapsed | `OutsideTap`: the surface consumes the tap on the `Dismiss` route; nothing underneath reacts |
| Expanded or Focused | Another cluster opens (SYSTEMS, ⋯, a cockpit item, ASSIST, the coach) | Collapsed | `Replaced` |
| Expanded or Focused | Camera change (chase ↔ cockpit) | Collapsed | `Close(panel, Owner)`, so the cockpit view always starts quiet |
| Expanded or Focused | The flight freezes (pause, settings, entry screen) | Collapsed | `Blocked`; the trigger refuses to open over a frozen flight |
| Expanded or Focused | New flight (RESET, a mode's start), the app loses focus | Collapsed | `Reset`, `FocusLost` |
| Focused on a guide | The guide is no longer offered (the destination went away) | Expanded | `NotifyClosed(focus)` |
| Any | HUD profile change, phase change, warning | **unchanged** | none |

The last row is the rule the owner asked for: the selected HUD profile and the expansion
state are independent, a phase change never forcibly expands a collapsed cluster (and
never folds an open one), and a warning never opens it. A pilot who opens the panel and
keeps flying with the stick and throttle keeps it open: a finger the flight controls own
is never an outside tap.

### What each profile shows

The flight context still decides eligibility through `DisclosureController`, unchanged:
profile, phase (with its anti-flicker), stable flight, the cockpit's instruments, the
player's guide choices, mission guides and warnings. The cluster only lays that decision
out in one place (`FlightInfoModel.Compose`).

| | Collapsed capsule | Expanded panel |
|---|---|---|
| Cinematic | The trigger only (the quietest rest). A guide the player asked for, and a warning's reading, still appear | Every reading and the guides' details, on demand |
| Smart (default) | The phase's essentials (the Smart table in `docs/CONTEXTUAL_UI.md`, at most three), quiet in stable flight; the requested or relevant guide | The same |
| Detailed | Three readings always (IAS, ALT, VS), so the capsule stays compact; the guide en route or on approach | The same: HDG and GS live here, no longer across the top |
| Cockpit view, any profile | The trigger only: the instruments carry the readings and the GPS map the way to the airfield. A warning's reading, the approach lights (the panel has no glide-path indicator), a mission checkpoint and a guide the player asked for still appear | Every reading on demand |

Capsule rules (`FlightInfoSettings`, tuning not code): at most `capsuleReadings` (3)
readings; warning-related readings are chosen first, the others in the fixed order IAS,
ALT, VS, HDG, GS; they are shown in that fixed order. One chip: the approach guide when it
is shown, otherwise the navigation guide when it is shown. The expanded panel offers the
navigation details whenever there is a destination, and the approach details when the
approach guide is shown or the aircraft is airborne within the guide's range of the runway
(`DisclosureController.ApproachGuideInRange`, the same rule Detailed uses for the guide).

### Information: one calculation each

| Shown | Source (computed once, by the session) | Formatting |
|---|---|---|
| IAS, VS, HDG, GS | `FlightTelemetry` (`TelemetryMath`) | `FlightReadouts`: whole knots, 50 fpm, whole degrees (north 360) |
| ALT | Telemetry plus the altimeter's subscale (`InstrumentSettings`), as before: one altitude everywhere | tens of feet |
| Destination, distance, course, arrow | `NavigationRelation` (`FlightSession.Navigation`, from `NavigationMath`) | tenths of a NM, compass degrees |
| Runway, distance, height, lights, centreline | `RunwayRelation` (`RunwayGeometry`), `ApproachGuidance` | the same as the old guide |
| Explanations | `HudExplanations` (from the aircraft's envelope), now built once per session for the long presses and the cluster | |

`FlightReadouts` is the old `ContextualHud` formatting moved, not copied: the capsule, the
panel and the focused value read one quantised value per refresh (0.1 s), from cached
strings, so steady flight allocates nothing.

### Navigation

- The navigation guide stays a player choice held by `DisclosureController` (the NAV
  action on the wheel, or a mission). The cluster never changes it: collapsing,
  expanding, focusing or a camera change leaves the player's "show" in place, and the
  capsule keeps the destination chip after every collapse.
- In the cockpit the GPS map shows the way to the airfield, so the chip is not shown there
  unless the player asked for the guide or a mission set a checkpoint (the GPS does not
  show checkpoints).
- Near the runway the approach chip takes the capsule's one chip slot; the navigation
  guide is still on and its tile is still on the panel.

### Warnings

Banners are unchanged: never registered with the coordinator, never covered by the panel
(the panel ends 38 units above the first banner slot), never dimmed. A warning also brings
its reading into the collapsed capsule in every profile and view (airspeed for STALL and
OVERSPEED, altitude and vertical speed for SINK RATE), first in line and in the alert
colour; in the panel the same tile turns to the alert colour.

### Placement

Canvas units at the 1080 reference height, from the safe area's top-left corner
(`FlightInfoLayout`, pure and EditMode-tested; the factory builds exactly these rectangles).

| Element | Touch target | Visual |
|---|---|---|
| Trigger (capsule and panel) | x 24 to 120, y −16 to −112 | 72 × 72, the gauge 36 |
| Capsule | x 24 to 24 + width, y −16 to −112; width 96 (trigger only) to 600 (three readings and a chip) | pill 72 tall; cells 112, chip 192 |
| Reading tiles | x 124 + 144·i, 140 wide, y −16 to −112 | 136 × 88 |
| Region A (navigation, focused reading, approach explanation) | x 24 to 400, y −114 to −210 | 372 × 88 |
| Region B (approach, explanation) | x 404 to 840, y −114 to −210 | 432 × 88 |
| Panel plate | x 24 to 842, y −16 to −212 | `PanelSurface`, radius 18 |

- **The top band.** The joystick region starts 22 % of the safe height below the top edge
  (`TouchControlsFactory.JoystickRegionTop`, now a named constant). The open panel ends 212
  units down, no deeper than the old guides. `UiScaleMath` now knows it: the interface-scale
  limit keeps the panel above the joystick region and left of the command cluster's row
  (`InfoClusterBottom`, `InfoClusterRight`, `TopBarWidth`). On a full-height screen this
  limit is 111 %, above the 110 % option. It is slightly tighter (0.3 %) than the open
  command panel over the throttle, which set the vertical limit before, so on a safe area
  shorter than about 99 % of the screen the cluster now sets it. (The old guides fitted
  under that limit by 1.4 units; the limit now states the rule instead of relying on it.)
- **Clear of everything permanent.** The panel stays above the warning column (it starts
  250 units down), left of the command cluster (its right edge at 842; at 4:3 the cluster's
  row starts at about 1,083), and the cockpit focus card and the flight settings start
  lower in the free column. It and the command options panel never overlap, and never
  show together (one coordinator).
- **Development overlays.** The telemetry line (220 down) and the touch diagnostics stay
  under the panel. The disclosure inspector can reach under the panel's right end on a 4:3
  tablet in development builds; the panel draws over it and takes the touch.
- **Draw order.** The cluster is built after the gesture HUD (it draws over the HUD band
  and takes its own taps) and before the command cluster, the coaching highlights, the
  mission card, the pause sheet and the entry screen.

Checked at 4:3, 16:10, 16:9, 19.5:9 and 20:9, at 90 %, 100 % and the largest allowed
interface scale, and inside a cut-out's safe area.

### Interaction details

- **Tap-first only.** Every action is one tap on a 96-unit target; nothing needs a hold
  or a drag, and there is no X. The panel's plate catches presses between tiles, so they
  never reach the world.
- **The HUD band.** Its pull (density preview) sat over the old data strip; it would now
  compete with the capsule's tap and blur the line between the profile and the expansion.
  It joins the pull gestures of the systems wheel as a **development comparison**
  (`GestureHud.SetBandEnabled`, switched with `FlightSession.pullGestureComparison`, never in
  a release build). The profile is chosen in the command cluster's selector or with `H`.
  A long press on a reading under the band in the comparison still explains it
  (`FlightInfoCluster.ElementAt`).
- **Reduced motion.** The capsule and the panel swap at once; otherwise they cross-fade in
  0.2 s (`HudMotionSettings`). The coordinator's opening and closing states follow.
- **Opacity.** The capsule follows the player's HUD opacity like the old plates; the open
  panel stays at full strength (the player asked for it and it carries text), like the
  command options panel. Warnings are never dimmed.

### Animation preparation

The final motion belongs to a later milestone. The structure is ready for it:

- One corner: the capsule, the panel and the trigger share the top-left anchor; the
  trigger's symbol is at the same point in both states.
- One order: capsule cells and panel tiles use the same left-to-right order, so a reading
  can travel from its cell to its tile (`FlightInfoLayout.CapsuleCell`, `Tile`;
  `FlightInfoCluster.RectOf`).
- One row for detail: a focused element's detail always opens in the lower row directly
  below the first row, next to the tile it came from (regions A and B).
- The coordinator already reports Opening, Open, Closing and Closed for both surfaces.

### Code

| Where | What |
|---|---|
| `FlightInfoModel` (UI, pure) | Levels, focus, tap rules; `Compose` (the capsule's readings and chip, the panel's guides, alerts) from `DisclosureController`; `FlightInfoSettings` |
| `FlightInfoLayout` (UI, pure) | Every rectangle from the safe area's top-left corner |
| `FlightReadouts` (UI, pure) | Quantisation and cached text of the readings and guides (moved from `ContextualHud`) |
| `FlightInfoCluster` (UI, MonoBehaviour) | The view: capsule, panel, lower row, fades; raises taps |
| `FlightInfoClusterFactory` (UI) | Builds it in code |
| `FlightInfoSession` (Gameplay) | Registers the panel and its focus with the coordinator, applies taps, folds on camera change and pause, presents per frame |
| `FlightSession` | `flightInfo`, `FlightInfo`, `Explanations` (built once); presents the cluster after disclosure; opacity and motion |
| `ContextualHud`, `ContextualHudFactory` | Keep the warnings, the legacy rail and SYSTEMS, the inspector; the plates are gone |
| `DisclosureController` | `ApproachGuideInRange` (shared by the Detailed guide rule and the panel) |
| `GestureHud`, `GestureSession` | The band only in the pull comparison; long-press targets resolve through the cluster |
| `UiScaleMath`, `TouchControlsFactory` | The cluster in the scale limit; `JoystickRegionTop` |
| `HudSprites` | `HudIcon.FlightData` (the gauge) |
| `FlightPrototypeSceneBuilder` | Builds the cluster after the gesture HUD and wires it |

### Tests

EditMode `FlightInfoTests`: the levels and the tap rules; guides focusable only while
offered and a lost destination going back to the panel; every profile expanding and
collapsing independently, a profile change never moving the level; phase changes and
warnings through every phase and profile never opening a collapsed cluster or folding an
open one; the Smart capsule per phase in the fixed order; stable cruise quiet; Detailed
compact with every reading focusable; Cinematic just the trigger; a stall surfacing the
airspeed in every profile and view; warning-related readings first when the capsule is
full; the cockpit showing no duplicated readings but keeping the approach lights, a
mission checkpoint and a requested guide; the chip rules; a requested navigation guide
surviving rounds of expanding, focusing and collapsing; the approach details' range rule;
sanitised settings; the readouts' rounding, caching and bad samples; the trigger's single
home, the capsule growing from its corner; tiles and regions as full targets without
overlap; on 4:3, 16:10, 16:9, 19.5:9 and 20:9 at 90 %, 100 % and the largest allowed scale,
every rectangle on screen and clear of the joystick region, the throttle, the brake, the
handle, the warning slots and the command cluster; shorter safe areas keeping the panel
above the joystick region; the scale limit knowing the cluster; the gauge symbol.

PlayMode `FlightInfoPlayModeTests` (real `FlightSession`, Rigidbody aircraft, touch
controls, both HUDs, the command cluster, the focus panel, the flight settings): tap to
expand, tap a reading to focus and explain it, another reading moves the focus, the detail
goes back, an outside tap folds everything and is consumed, the trigger folds from a
focus, no X; the command options and the systems wheel replace it and it replaces them;
the band inactive in the tap-first interface and drawn under the cluster; every profile
collapsing independently with the profile unchanged, a profile change keeping it open,
Detailed compact; real phase changes (a take-off roll) never opening it or folding it; a
stall surfacing the airspeed in Cinematic with the banner shown and never covered; the
cockpit view starting quiet with no duplicated readings and the readings one tap away; a
requested navigation guide surviving collapses with its chip; the readings equal to the
session's telemetry, the altitude following the altimeter's subscale, the navigation
values equal to the session's; the stick's and throttle's fingers keeping their input
through expanding, focusing and an outside tap; pause, reset and focus loss folding it;
nothing overlapping or clipping at 4:3, 16:10, 16:9, 19.5:9 and 20:9 at the largest
allowed scale (text widths checked where the test player has font metrics); a cut-out's
safe area. `ContextualHudTests`, `GestureInteractionTests`, `PresentationTests`,
`CockpitTests`, `CommandClusterTests`, `CommandClusterPlayModeTests` and
`FlightSessionDisclosureTests` were updated for the retired plates.

### Retired elements

| Before | Now |
|---|---|
| `ContextualHud.dataItems`, `dataValues`, `approachGuide` and its texts and lights, `navigationGuide` and its texts and arrow, `backings`, `quietAlpha`, `valueRefreshSeconds`, the value colours and the old `Present` overloads | `FlightInfoCluster` (capsule cells, tiles, guide tiles, chip); `FlightInfoSettings`; `ContextualHud.Present(disclosure, warnings, profile)` |
| `ContextualHud.DataItemCount`, `ContextualHudFactory.DataItemSize`, `DataItemSpacing` and the plate builders | `FlightReadouts.ReadingCount`, `FlightInfoLayout` |
| The HUD band over the data strip in the release interface | The band in the pull comparison only |

A scene generated before this milestone must be rebuilt (**Open Skies > Rebuild Flight
Prototype Scene**).

### Decisions and open points

- **Expanded is transient, not a pinned mode.** The panel follows the tap-first contract
  like every other expanded interface, so an outside tap folds it. A pilot who wants more
  at rest chooses Detailed (three readings in the capsule) or asks for a guide. Should
  device tests show players want the panel pinned in Detailed, a pin is a small change to
  the session, but it would have to give up "tap outside to collapse" for that state.
- **Three readings at rest.** The capsule's largest width (600) is three quarters of the
  old flight data strip alone (780) and takes one row instead of two; four readings would
  cost 112 units more. `capsuleReadings` can be lowered, not raised.
- **The band's pull moved to the development comparison** (see above). The owner may want
  it back in release builds on a different surface.
- **On-demand readings in the cockpit** show every reading, including those the
  instruments carry: the player asked, and the instruments can be out of view when looking
  around. The resting state never duplicates them.
- **No new data.** Height above ground, true airspeed and the flaps are not shown; they
  were not asked for and stay out of scope.
- Device questions: whether the gauge symbol reads as "flight data"; whether the 72-unit
  capsule is comfortable at arm's length; whether players find the focus without the hint;
  whether the explanation's three lines read in the narrower region on a 4:3 tablet (the
  text shrinks to 16 units rather than clip).

## The command cluster (milestone 3)

### Hierarchy

The top right used to hold six controls of the same size and weight: CAM, RESET, the HUD
profile (a one-way cycle), LOOK FWD, MENU and SKIP TUTORIAL. They are now one cluster whose
visual weight follows how often and how urgently each command is needed.

| Tier | Command | Where | How it looks | How it acts |
|---|---|---|---|---|
| Primary | Camera | Top-right corner | The largest visual: a 92 × 72 rounded plate with a camera symbol and the current view's name (CHASE or COCKPIT) | One tap switches the view, from any state of the cluster. Flight input is never touched. |
| Primary | Options (⋯) | Left of the camera | A 64-unit disc with three dots; lighter while its panel is open | One tap opens the options panel; again folds it |
| Primary | Pause | Left of the options | The smallest and quietest: a 52-unit disc, fainter surface, two bars | One tap pauses (the pause sheet, or the card in the legacy interface) |
| Contextual | LOOK FWD | Under the camera | A compact 104 × 52 pill | Shown only in the cockpit view while it is turned away from rest; returns the view forward |
| Secondary | HUD | Options panel, top row | A full-width row: HUD, the current profile (cyan, in words), a chevron | Opens the HUD selector in the panel's place |
| Secondary | HUD selector | Same place as the options | A title row (back chevron, "HUD: SMART") and three profiles: CINEMATIC, SMART, DETAILED | A tap on a profile applies it and folds the cluster; the title row goes back |
| Tertiary | RESET | Options panel, lower row, left slot | A smaller, quieter pill (18-unit caption) | First tap: "TAP AGAIN TO RESET" (engaged); a second, deliberate tap within the window resets |
| Tertiary | SETTINGS | Lower row, middle slot | Same | Pauses the flight and opens the settings (the sheet's settings stop, or the settings screen over the card) |
| Tertiary | SKIP TUTORIAL | Lower row, right slot, First Flight only | Same | First tap: "TAP AGAIN TO SKIP"; the second ends the tutorial |

There is no permanent card and no row of equal rectangles: the primary row has no backing,
each control carries its own small visual, and the three visuals differ in size and
strength. The panel's plate exists only while it is open.

### Placement

All measures are canvas units at the 1080 reference height, from the safe area's top-right
corner (`CommandClusterLayout`, pure and EditMode-tested; the factory builds exactly these
rectangles).

| Element | Touch target | Visual |
|---|---|---|
| Camera | x −136 to −24, y −112 to −16 (112 × 96) | 92 × 72, centred |
| Options | x −240 to −144, same height (96 × 96) | 64 × 64 disc |
| Pause | x −344 to −248, same height (96 × 96) | 52 × 52 disc |
| LOOK FWD | x −136 to −24, y −216 to −120 (112 × 96) | 104 × 52 pill |
| Options panel | x −404 to −24, y −324 to −120 | plate, `PanelSurface`, radius 18 |
| Secondary row | the panel's inner width (372), 96 tall | 80 tall |
| Lower row | three slots of 121 × 96 | 113 × 64 |

- **Visual size and touch size are independent.** Every control is a 96-unit invisible
  touch target (`RaycastOnlyHudGraphic`) around its visual; a coach's highlight frames the
  visual, not the touch area.
- **Frequency decides the place.** The camera is in the corner, the place a right thumb
  finds without looking; pause is the farthest, so a reach for the camera never pauses.
- **The panel opens against the right edge, under the row**, and stays inside the action
  rail's inset from the right edge (404 < 436 units), so the warning column can never be
  under it at any interface scale. Its bottom (324 units below the top) is above the
  throttle at every allowed scale: the interface-scale limit now uses the panel
  (`UiScaleMath.Layout.SessionColumnBottom`), and it is no tighter than the old MENU/SKIP
  column (336). Wide phones now get the full 110 %.
- **Safe areas.** The cluster is a child of the `SafeAreaFitter` area and measured from its
  corner, so a cut-out moves it in.
- The development disclosure inspector moved left of the cluster's column (x −640 to −152)
  to leave LOOK FWD's place free.
- In the legacy rail comparison, the open panel may cover the rail's top slot while it is
  open (it is transient and draws on top); the permanent controls never meet the rail.

### Interaction and disclosure

1. **Camera**: one tap, always. With the panel open it switches the view and folds the
   panel. The fingers on the stick and the throttle keep their pointers and their input.
2. **Options**: tap ⋯ to open, tap ⋯ again or anywhere outside to fold. No X. The panel is a
   root surface of the `DisclosureCoordinator` ("Command options"): opening it replaces any
   other expanded cluster (the systems wheel, a cockpit card, the flight settings, the
   coaching explanation), and opening any of those folds it. An outside tap is consumed by
   the interaction surface on the `Dismiss` route, so nothing underneath reacts; a drag
   outside does nothing. A press between the panel's rows lands on its plate and goes
   nowhere.
3. **HUD**: tap HUD, then tap a profile. The selector is a child surface ("HUD selector") in
   the panel's place, so the cluster never grows towards the warnings; the current profile
   is marked (cyan) and named in the title row (never colour alone). The choice applies
   through `FlightSession.SetHudProfile` and the cluster folds. The `H` key still cycles.
   The HUD band's pull (top left) remains the gesture; the selector is its tap twin.
4. **RESET**: behind the options, with a confirmation. The first tap only arms it; the
   second confirms only if it comes at least 0.4 s and at most 3 s later
   (`CommandClusterSettings`). A double tap or a bounce never confirms. Any collapse (an
   outside tap, the trigger, another cluster, the pause, focus loss, a new flight), the HUD
   row or another action drops the waiting confirmation, so **trying to dismiss the menu can
   never reset the aircraft**. RESET is never where the trigger is. The `R` key is unchanged.
5. **SKIP TUTORIAL** follows the same confirmation and is offered only while the First
   Flight runs.
6. **Pause** and **SETTINGS** fold the cluster and freeze the flight; the trigger never opens
   the panel over a frozen flight (the pause interfaces are deliberate to leave).
7. **Warnings** are untouched: never registered, never covered (see the placement), never
   dimmed. The camera and the cluster are never hidden or moved by HUD disclosure (profile
   or phase); RESET is now one level deep by the owner's request for this milestone, never by
   a disclosure rule.

### Tutorial highlights

The coach's frames follow the cluster (`GameModeSession`): "tap CAM" frames the camera;
"tap RESET" frames the options trigger while the panel is folded and RESET itself once the
panel shows it. The frames take no touches and show only while their control is visible.
The tutorial's own text and steps are unchanged.

### Retired elements

| Before | Now |
|---|---|
| `HudPresenter.cameraButton`, `resetButton`, `cameraLabel`, `opacityGroups` (uGUI buttons built by the scene builder) | The cluster's camera and RESET. `HudPresenter` keeps only the development telemetry overlay. |
| `ContextualHud.profileButton`, `profileGroup`, `ProfileCycleRequested`, `ProfileLabel` | The cluster's HUD row and selector |
| `MissionUiFactory.BuildMenuButton`, `BuildSkipButton`, `MenuPosition`, `SkipPosition`, `SessionButtonSize`; `GameModeSession.menuButton`, `skipButton`, `skipConfirmSeconds`, `SetHudOpacity` | The cluster's pause and SKIP TUTORIAL; the confirmation moved into `CommandClusterModel` |
| `CockpitFocusPanelFactory.BuildViewControls`, `LookForwardPosition` | `CommandClusterFactory.BuildLookForward` (the same `CockpitViewControls`, under the camera, suppressed while the panel is open) |

No development comparison of the old top bar is kept: it was a layout, not an interaction
grammar worth comparing on a device, and the history holds it. The scene builder builds
the cluster instead of the old buttons; a scene generated before this milestone must be
rebuilt (**Open Skies > Rebuild Flight Prototype Scene**).

### Code

| Where | What |
|---|---|
| `CommandClusterModel` (UI, pure) | Levels (collapsed, options, HUD selector), the confirmation of RESET and SKIP, profile order and cached labels; `CommandClusterSettings` (the confirmation window and minimum) |
| `CommandClusterLayout` (UI, pure) | Every rectangle of the cluster from the safe area's corner |
| `CommandCluster` (UI, MonoBehaviour) | The view: buttons, the panel's fade, presenting the model; raises requests |
| `CommandClusterFactory` (UI) | Builds the cluster and LOOK FWD in code |
| `CommandClusterSession` (Gameplay) | Registers the panel and the selector with the coordinator, applies requests through `FlightSession`, raises pause, skip and settings for the game modes |
| `HudSprites` | Three more generated symbols: camera, more (three dots), pause |
| `FlightSession` | `commandCluster`, `Commands`; LOOK FWD taken from the cluster |
| `GameModeSession` | Pause, SKIP and SETTINGS from the cluster; the highlight targets |
| `GestureSession`, `UiScaleMath` | The wheel's keep-outs and the scale limit use the cluster's geometry |
| `FlightPrototypeSceneBuilder` | Builds the cluster after the HUDs and before the highlights, the card, the sheet and the entry screen |

### Tests

EditMode `CommandClusterTests`: the levels; RESET armed by one tap, confirmed by a second
inside the window, never by a double tap or a bounce, lapsing after the window, dropped by a
collapse, a level change or another action, ignored while not shown; SKIP likewise and
withdrawn with the tutorial; sanitised settings; the three profiles by direct index in
density order with cached labels; the primary row ordered by frequency with falling visual
weight and visuals inside their touch targets; the panel's rows and slots as full targets
without overlap, RESET away from the trigger; the panel inside the rail's inset; on 4:3,
16:9 and 20:9 at the smallest and largest interface scales, every target on screen and
clear of the joystick region, the throttle, the brake, the handle, the warnings, the flight
data and the guides, in the right half above the throttle; a cut-out's safe area; the scale
limit no tighter than before.

PlayMode `CommandClusterPlayModeTests` (real `FlightSession`, Rigidbody aircraft, touch
controls, both HUDs, the flight settings card, the cockpit focus panel and, where needed,
the game modes): the camera one tap away from the folded cluster, the open panel and the
selector, and in every HUD profile; a camera switch keeps the stick's and the throttle's
fingers and their input; profiles chosen directly with the current one shown, the title row
going back, the `H` cycle kept, flight input untouched; RESET never by accident (hidden,
first tap, double tap, outside tap, trigger, another cluster, a lapsed window) and reset by
a deliberate second tap; the panel opened and folded by taps, the outside tap consumed and
a drag ignored, cluster replacement both ways, a new flight and focus loss folding it;
pause and SETTINGS freezing the flight and the trigger refused over it; one of each command
and none of the retired ones; reach and large targets at 4:3, 16:9 and 20:9 at the scale
limits; a cut-out's safe area; the tutorial's highlights following the cluster and SKIP
TUTORIAL from the cluster. `FlightSessionTests`, `GameModeTests`, `PresentationTests`,
`CockpitTests`, `GestureInteractionTests`, `ContextualHudTests` and
`FlightSessionDisclosureTests` were updated for the retired controls.

### Decisions and open points

- SKIP TUTORIAL moved behind the options (a less frequent session action); the pause menu
  keeps its own SKIP choice. The owner may want it back on the surface for first-time players.
- SETTINGS from the cluster pauses the flight (the settings live over a paused world).
- LOOK FWD stays a one-tap chip (it is contextual and directly useful), rather than a
  second tap inside the panel; the panel takes its place while open.
- Device questions: whether the 52-unit pause disc is discoverable enough; whether the ⋯
  symbol reads as "more" to every player; whether 0.4 s is the right bounce time for RESET;
  how the camera caption reads at arm's length.

## The systems wheel (milestone 2)

### Interaction

1. **Tap SYSTEMS.** The wheel unfolds from the handle and stays (no finger needed).
2. **Tap an action.** PARK and APPROACH act at once (through the gate, as the rail does)
   and the wheel folds. FLAPS, SWITCHES, NAV (when the mode gives it destinations) and
   ASSIST have options: the wheel **focuses** on them.
3. **Tap an option.** It applies through the authoritative request
   (`FlightSession.RequestSystemChange` for the aircraft, the owner's callback for a
   destination or a profile) and the wheel folds: the selection is complete.
4. **The handle goes back one level**: from the options to the wheel, from the wheel to
   folded. In the focused state the handle carries the action's name and a chevron
   pointing back, so the player always sees where they are.
5. **A tap anywhere else** folds everything and applies nothing. The tap is consumed
   (milestone 1): nothing underneath reacts.

No press-and-hold, no pull, no X. A finger that slides on the open wheel and is released
on a segment or an option chooses it, exactly as a tap there would (a sloppy tap is not
lost); a press that moves on the folded handle and is released on it opens the wheel; a
slightly slow tap (longer than a tap, shorter than a long press) on the handle or the
wheel is still a tap. A long press on the handle still explains it.

### The wheel

| Slot | Segment | Symbol | State line | Options (focused) |
|---|---|---|---|---|
| 0 | PARK | P in a ring | SET when set | none: toggles the parking brake (ground only to set; dimmed in the air) |
| 1 | FLAPS | wing, flap lowered | the detent (UP, 10°...) | one per detent of the aircraft (`AircraftConfiguration.flapDetentDegrees`) |
| 2 | SWITCHES | toggle switch | ENGINE OFF, or LDG ON | LANDING, NAV LTS, ENGINE (each shows ON or OFF; a tap flips it) |
| 3 | APPROACH | runway | ON when shown | none: toggles the approach guide |
| 4 | NAV | compass | ON when shown | the destinations in Free Flight (AIRFIELD, landmarks); otherwise none: toggles the guide |
| 5 | ASSIST | sliders | the profile | SETTINGS (the card), ASSISTED, STANDARD, MANUAL |

- **Fixed positions.** Slots keep their order and angle for the session; the screen fit
  keeps the preferred first angle wherever there is room, so 16:9 and 20:9 phones put
  every action in the same place. A slot the aircraft or the airfield lacks is not shown.
- **Emphasis by phase** comes from the same rules as the rail (`DisclosureController`):
  full where relevant, quiet otherwise, engaged (cyan, with a mark) while on, dimmed when
  not possible now. Nothing is hidden: every system stays reachable by hand. The switch
  panel's relevance is `DisclosureController.SwitchPanelRelevant` (parked; take-off roll,
  initial climb, approach, landing: the landing light).
- **Readable.** Each segment has a symbol (generated in code, `HudSprites.Icon`, no
  imported art), a short label and a state line; the symbols always sit next to words.
  PARK, not BRAKE: the hold-to-brake button is right below the handle.
- **The switch panel.** The landing light, the navigation lights and the engine are one
  segment, as on the aircraft's switch panel. Seven primary segments do not fit on a 4:3
  tablet at full touch size (see the geometry below); grouping these three costs no tap
  (each of them was going to be two taps anyway) and leaves the lights and the engine
  manually reachable from the HUD for the first time.

### The focused state (secondary selection)

The tapped action's options take the band around the action's own angle: the primary
segments fold into it and fade, the options unfold out of it (instant with reduced
motion). One level only; nothing is nested deeper. The options keep the slot step where
they fit and are held between the first slot and the last angle the screen allows
(`SystemsArcModel.Focus`). The hub (the handle) shows the action's name and a back
chevron; the caption beside the wheel says what a tap does (FLAPS: tap a setting · the
handle goes back). The set option is engaged (cyan with a mark, SET or ON).

For the coordinator the focus is a child of the wheel (`Systems wheel options`): the
handle closes it alone; a tap outside the wheel closes both; another cluster (the cockpit
card, the flight settings, the pause) replaces both.

### FLAPS, the reference interaction

Tap SYSTEMS → tap FLAPS → tap a detent. The detents are the aircraft's configured ones,
labelled from its data (UP, 10°, 20°, 30° on the trainer; a configuration with 15° and
40° shows 15° and 40°), in order down the wheel like a lever: further round is more flap.
The current detent is marked; a detent changed by the keys or the cockpit while the
options are out moves the mark at once. The tap sets the detent with one request through
the gate. The cockpit's flap lever keeps its direct manipulation (drag, flick, the
selector) through the same `AircraftSystems` and the same gate: the HUD and the cockpit
can never disagree. The rail's FLAPS (legacy) still opens the cockpit selector.

A focused wheel was chosen over a lever-shaped selector because it is the same picker the
player already holds in mind (no second widget, no drag), the detents keep the wheel's
touch size, and the order down the wheel reads like a lever anyway.

### Hazardous changes

`SystemChangeGate` stays the only way a system changes. ENGINE off in flight is the one
hazardous change: the first tap returns `AwaitingConfirmation`, nothing is applied, the
wheel stays focused and the option turns red with the word CONFIRM and the state ENGINE
OFF (never colour alone); the caption says "Hazardous: tap CONFIRM again, or tap outside
to cancel". The second tap within the gate's window applies it. An outside tap, the
handle (back), another cluster, focus loss or a reset drop the waiting change
(`FlightSession.ClearPendingSystemChange`), so nothing is left armed. If the window runs
out the option simply returns to ENGINE ON.

### Geometry, fitted to the screen

`SystemsWheelLayout.Fit` (pure, `OpenSkies.UI`) chooses the band from the preferred
settings (`SystemsArcSettings`: band 196 to 300 units, first slot at 98°, at most 26°
between slots) and the space around the handle. It searches the largest outer radius,
the preferred band thickness first and then the minimum (96), for which the longest free
run of angles (every radial line through the band clear of the joystick region, the
throttle, the brake, the handle, the four warning slots, the top bar and LOOK FWD under
the camera, 8 units of clearance and 16 from the safe-area edges) holds every slot with
segments of at least 96 units along the middle of the band. The largest band that keeps
the preferred first slot wins, as long as it is within 32 units of the largest band that
fits (`PreferredSlotWindow`, which also bounds the search: a one-off cost on the first
frame and on a screen change); otherwise the largest that fits. It works on a copy of the
settings (the tuning asset is never written at runtime), runs when the screen or the
entries change and never while the wheel is open.

| Screen (1080 units high) | Band | Slots | Segment along the band |
|---|---|---|---|
| 4:3 (1440 wide) | 172 to 276 | 85.8° + 25.7° × i | 100 |
| 16:9 (1920) | 196 to 300 | 87.3° + 24.5° × i | 106 |
| 20:9 (2400) | 196 to 300 | 87.3° + 24.5° × i | 106 |

(Six segments; the numbers come from the fit's algorithm, cross-checked by an independent
port of it. The EditMode and PlayMode tests, which pass in CI, assert that every case fits
with full-size segments and covers nothing; they do not pin the exact radii.) On 4:3 the band shrinks because the warning column comes close above the
handle; nothing else moves. At the smallest interface scale (0.9) every aspect fits at
the preferred size; the largest scale is already limited on 4:3 by `UiScaleMath`. The
caption sits left of the wheel, in the free space before the joystick region, and hides
itself if fewer than 140 units are left.

The open wheel takes presses only on its segments (`HotspotShape` filtered by the
segments of the current level); the hole between the handle and the band and everything
beyond the band are outside.

### Input

Unchanged architecture: the handle and the wheel are `GestureHotspot`s on the
`SystemsHandle` route of the one `InteractionSurface`; the surface ignores every pointer
the joystick, the throttle or the brake owns (a flight-control finger is never a
dismissal), so flying with the wheel open and focused works as before; the coordinator
decides outside taps. Keyboard and gamepad keep their keys (`[` `]` flaps, `L` `N` lights,
`P`, F4); they change the same systems and the open wheel follows them.

### The pull comparison (development only)

The M13/M14 gesture model (pull the handle to unfold, rest for an action's options on the
outer ring, slide from FLAPS into the lever) remains behind
`FlightSession.pullGestureComparison` for comparison on a device. F4 (or a tap on the
gesture inspector) cycles: tap wheel → pull comparison → legacy rail. It is never
available in a release build (`GestureSession.SetPullComparisonAllowed(Debug.isDebugBuild)`),
whatever a saved preference says. The lights ring of milestone 1 is gone in both: the
lights are on the switch panel.

### Code

| Where | What |
|---|---|
| `SystemsArcModel` (UI, pure) | items gain a state line and a symbol, `ArcEmphasis.Confirm`; the focused state (`Focus`, `Unfocus`, `OptionItem`, `OptionAngle`, `OptionAt`, `HoveredIsOption`, `HoveredItem`, `SlotCentreMax`); `SetSettings` for the fitted geometry |
| `SystemsArcSettings` | new preferred band (196 to 300), end tolerance 4°, the fitting fields, `Clone` |
| `SystemsWheelLayout` (UI, pure) | the fit to the screen |
| `SystemsArcView`, `GestureHudFactory` | symbols, state lines, option segments on the band, the focus transition, the confirmation colour, the caption beside the wheel |
| `GestureHud` | the hub (`SetHub`: the action's name and a back chevron) |
| `HudSprites` | `HudIcon` and `Icon`: the six symbols, 64 × 64, generated once |
| `DisclosureController` (Awareness) | `SwitchPanelRelevant`, `SwitchPanelProminence` |
| `GestureSession` (Gameplay) | the wheel's entries (the switch panel after FLAPS), the tap flow, the options and their requests, the confirmation, the fit, the pull comparison |
| `FlightSession` | `TryGetPendingSystemChange`, `ClearPendingSystemChange`, `pullGestureComparison` |
| `FlightPrototypeSceneBuilder` | an `InteractionConfiguration` asset still holding the untouched M13 arc defaults is moved to the new preferred band once; tuned values are left alone |

### Tests

EditMode `SystemsWheelTests`: six segments fitted at 4:3, 16:9 and 20:9 are full touch
targets and every wedge (with its end tolerance), and every option slot a focus may use,
stays on screen and off the joystick region, the throttle, the brake, the handle, the four
warning slots and the top bar; the preferred first slot is kept wherever there is room
and 16:9 and 20:9 match; the smallest interface scale fits; the preferred settings are
never changed; too little space returns the best geometry and says so; the focus lays the
options out from their parent, held inside the allowed span, narrowing the step only when
needed; the finger finds options, not primary segments, until unfocused; a confirmation
is selectable, a disabled item is not; the switch panel's relevance (never hidden); the
symbols are generated once. The arc model tests of M13 still pass on the new defaults.

PlayMode `SystemsWheelPlayModeTests` (real `FlightSession`, Rigidbody aircraft, touch
controls, both HUDs, the flight settings card, the cockpit focus panel): a tap opens the
wheel with six segments, symbols and labels, no finger held, and the same trigger closes
it; a sloppy tap on the handle still opens it and a pull unfolds nothing; PARK and
APPROACH act through the gate and fold the wheel, changing nothing else; FLAPS shows the
aircraft's own detents (a 0/15/25/40 configuration), the hub says FLAPS, a detent applies
once and the wheel folds, the segment then shows 40° and the option SET; the cockpit
selector and the wheel share the flap state, the open options follow a key change; each
switch option flips its own system only; an owner's options (destinations) and the
assistance profiles are chosen on the wheel; an outside tap on the focused wheel is
consumed, folds everything and applies nothing; active states (SET, 20°, LDG ON, the
switches ON) are drawn; engine off in flight needs the second tap (red, CONFIRM, the
caption), and an outside tap or the handle drops the confirmation; the joystick and the
throttle keep working with the wheel open and focused; the pull comparison never turns on
in a release build; no segment or option overlaps a flight control, the handle or a
warning slot at 4:3, 16:9 and 20:9, the segments are at least 96 units and the caption
stays off the joystick region. `TapDisclosureTests` and `GestureInteractionTests` were
updated: FLAPS now focuses the wheel instead of opening the cockpit card, the options take
the band instead of the outer ring, and the pull tests switch the comparison on.

### Decisions and open points

- **Six primary segments at most** (owner may revisit): at 4:3 the warning column above
  the handle limits the band to an outer radius of about 278 units, and seven segments
  would be about 80 units long. The switch panel groups the landing light, the navigation
  lights and the engine.
- The handle goes back one level instead of folding everything when options are out
  (milestone 1 folded everything); outside taps fold everything in one tap.
- An action's options replace the primary segments on the band rather than opening an
  outer ring, so the wheel never grows towards the warnings.
- Device questions: whether the band at 172 to 276 on a tablet and 196 to 300 on a phone
  is comfortable for a thumb; whether the hub's back chevron is understood; whether the
  slide tolerance ever chooses by accident; how the symbols read at arm's length.
- An `InteractionConfiguration.asset` made before milestone 2 with tuned arc values keeps
  them: the fit starts from them (it never writes them).

## Outside taps

| The tap lands on | What happens |
|---|---|
| Empty space (the sky, the cockpit panel behind a card) | The whole branch collapses, deepest surface first, down to the first surface that stays through outside taps (a paused interface) |
| A parent while a child is open (the arc while an action's options are out; the pause sheet while its settings are up) | The children collapse; the parent stays |
| The deepest open surface (a segment, a detent button, a settings row) | Not an outside tap: the surface handles it |
| A warning banner, the HUD band (pull comparison) | Outside: the branch collapses; the banner is untouched |
| The flight information capsule or a tile | Not an outside tap: a button of the flight information cluster, which opens or focuses it (and replaces another cluster) |
| Another trigger (the handle, MENU, a rail button, a cockpit item in the legacy comparison) | Not an outside tap: the new cluster replaces the old one |
| A finger the flight controls own | Nothing: the pointer is never routed |

The press itself decides: when the surface receives a press on the open world (nothing
drawn on top took it) and the coordinator says the point is outside the active branch,
the press is claimed on the `Dismiss` route, which listens for a tap only. The tap
collapses; a drag, a hold or a second finger on that pointer does nothing at all. Once
the branch has collapsed the next press is the world's again (free look, cockpit).

**Paused interfaces.** The pause sheet and the legacy pause card are registered as
surfaces that outside taps may not dismiss. A tap on the dimmed world at the sheet's
first stop is consumed and does nothing; at the second stop it folds the settings
back to the first stop. A tap on the dimmed world around the full settings screen
closes the settings back to the pause card or the entry screen, which stay. Resuming
is RESUME, MENU, Esc or the deliberate pull down of the sheet.

**Safety.** Previews never change aircraft state (unchanged). A hazardous change
waiting for its confirmation (engine off in flight) is dropped when the selector
collapses on an outside tap: the panel closes through `CockpitSession.Close`, which
clears the gate. `SystemChangeGate` remains the only way a system changes. No reset,
engine shutdown or mode change can come from a dismissal, and flight input ownership
is never altered.

## Architecture

```
Press on the open surface ──▶ InteractionSurface.Begin
    │  owned pointer? ignored.  hotspot route? kept (another trigger).
    │  DisclosureCoordinator.WantsOutsideTap(point)? ──▶ route Dismiss (claimed, tap only)
    │                                                        │ tap Completed
    │                                                        ▼
    │                                        DisclosureCoordinator.OutsideTap(point)
    │                                             collapses the branch, deepest first,
    │                                             through each owner's collapse callback
    └─▶ otherwise the routes of docs/GESTURE_INTERACTION.md (World, Cockpit, Target...)

Owners register their surfaces once, open and close them as they always did, and tell the coordinator:
  GestureSession ─ Systems arc (root), Systems sub-arc (child of the arc)
  FlightSession ─ Flight settings card
  CommandClusterSession ─ Command options (root), HUD selector (child of the options)
  FlightInfoSession ─ Flight information (root), Flight information focus (child of the panel)
  CockpitSession ─ Cockpit panel (instrument card or selector)
  GameModeSession ─ Pause sheet (stays), Pause sheet settings (child), Pause card (stays), Coaching detail
  PresentationSession ─ Settings screen (child of the pause card when it opened from it)
```

### `DisclosureCoordinator` (`OpenSkies.Gestures`, plain C#)

The one place that knows which transient interface the player has expanded. It does
not replace `DisclosureController`: the controller (awareness layer) decides what the
flight context makes *eligible*; the coordinator manages what the player *expanded*.

| Responsibility | How |
|---|---|
| Register transient surfaces | `Register(name, contains, collapse, dismissOnOutsideTap, parent)`: a screen hit test, a collapse callback, whether outside taps may dismiss it, an optional parent. Fixed capacity (16), ids are small integers; `Unregister`, `SetParent`, `Clear`. |
| Maintain the active branch | One branch from the root to the deepest surface (`Depth`, `ActiveRoot`, `Deepest`, `LayerAt`, `IsActive`). |
| Parent and child | `Open(child)` under an open parent closes the parent's other children; `Open(root)` replaces every other cluster (`Replaced`); a child whose parent is closed opens as a root. |
| Opening, replacement, dismissal | `Open`, `Toggle` (the trigger again), `Close(id, reason)` (with its descendants), `NotifyClosed(id)` (the owner closed itself: no callback), `CollapseAll(reason)`, `CollapseDismissable(reason)`. |
| States | `Closed`, `Opening`, `Open`, `Closing`; `Opening` and `Closing` settle after `TransitionSeconds` (the fade time, 0 with reduced motion) in `Tick`; `NotifyOpened` settles early. Opening counts as active; closing does not. `StateChanged` for visual components. |
| Focus loss and transitions | `OnFocusLost`: a branch whose root an outside tap could dismiss folds; a paused interface is left as it is. `Reset` (a new flight) collapses everything. The flight's `Restart` calls it; the game modes collapse everything when the flight freezes (`Blocked`). |
| Outside taps | `WantsOutsideTap(point)` (the press-time question) and `OutsideTap(point)` (the tap). The branch is cut before any owner is told, so an owner that opens something else from its collapse callback keeps it. |

Collapse reasons: `OutsideTap`, `ParentTap`, `Trigger`, `Replaced`, `FocusLost`,
`Reset`, `Blocked`, `Owner`.

### Routing (`InteractionRouter`, `InteractionSurface`)

A new route, `Dismiss` (tap only), is chosen for a press on the open surface when the
coordinator wants it, in every view and in both interfaces, after the `Blocked` check
and before the Target, Cockpit and World routes. Hotspot routes (the handle, the band,
the instrument face, the sheet, the entry) are never redirected: a press on another
trigger replaces the cluster. The surface owns the coordinator (`InteractionSurface.Disclosure`),
ticks its states, hands it focus loss and performs the outside tap itself in `Drain`, so
no session wiring can forget it. No full-screen raycast blocker was added: the surface
that already receives every untaken press does the consuming.

### The arc, tap-first (milestone 1; superseded by the systems wheel above)

As milestone 1 delivered it, kept for the record: the pull comparison still behaves this
way, except that the lights ring is gone and the handle goes back one level.

- **Tap SYSTEMS**: the arc unfolds and stays open, with every reachable system on it:
  the actions on the band and the lights on the outer ring (no resting needed).
- **Tap a segment**: an action without options acts (PARK BRAKE, APPROACH, FLAPS
  opens the detent selector as the rail does) and the arc folds. An action with
  options (NAV, ASSIST) fans them out on the outer ring as a child surface; by
  convention the first option is the action itself (GUIDE, SETTINGS), so the tap path
  loses nothing the rail offers. Tap an option: chosen, the arc folds. Tap the open
  action again: back to the arc. A segment that is not available now (the parking
  brake in the air) answers with a tick and stays.
- **Tap the handle again, or outside**: folds.
- **The pull remains** as the continuous way through the same arc, and a slide that
  starts on the open arc and releases on a segment chooses it.
- The open arc takes presses through a `HotspotShape` (a raycast target shaped by a
  filter): only the band and, when shown, the outer ring are targets, so the hit area
  never covers the throttle, the brake or a banner, and a folded arc intercepts nothing
  (its `CanvasGroup` blocks raycasts only while open).

### What else changed

| Interface | Before | Now |
|---|---|---|
| Cockpit focus cards | × close button, tap the instrument, tap nothing in the cockpit | tap outside (consumed), tap the instrument, the trigger again, a camera change; no close button |
| Flight settings card | × close button | tap outside (consumed), ASSIST again; no close button |
| Coaching explanation | "×" glyph; tap the message to fold | "–" glyph; tap the message or outside it to fold |
| Pause sheet scrim | a tap resumed | a tap folds the settings to the first stop, or does nothing; RESUME resumes |
| Settings screen scrim | took touches, did nothing | a tap closes the settings (as DONE) back to the pause card or the entry screen |
| HUD band | pull only | unchanged as a continuous preview; a tap on it counts as a tap outside; the command cluster's HUD selector is the tap twin (milestone 3) |
| Explanation plate (hold) | shown while held | unchanged: it is tied to the held finger, never a surface of its own |

Presses on a panel that has no handler of its own bubble to the nearest ancestor with
one in uGUI; the settings panel and the pause sheet's surface therefore carry a
catch-all `HudButton` (no feedback), so a tap between rows never reaches the scrim's
handler. The legacy comparison mode (SYSTEMS and the rail) keeps its behaviour; its
panels follow the same dismissal contract.

## Tests

EditMode `DisclosureCoordinatorTests`: opening and closing with states and reasons;
an owner closing itself; toggle; nested surfaces closing with their parent, a child
without its parent as a root, reopening a parent; cluster replacement with children,
siblings under one parent; outside taps outside everything, inside the deepest
surface, on a parent; surfaces without a hit test; a paused interface through outside
taps with its settings folding back to it; `CollapseAll`; focus loss folding flight
transients and leaving a paused interface alone; reset; opening and closing states
settling after the transition and bad time steps; rapid repeated opening and closing;
capacity, unregistering, reparenting, clearing; a collapse callback that opens
something else. `InteractionRouterTests`: the Dismiss route in every view and
interface, hotspots and overlays keeping their routes, tap-only mask.

PlayMode `TapDisclosureTests` (real `FlightSession`, Rigidbody aircraft, touch
controls, both HUDs, the flight settings card, the focus panel, and the game modes with
the pause sheet where needed): the handle opens and keeps the arc, again closes;
a segment acts like the rail and the arc folds; FLAPS opens the detent selector, a
detent applies through the gate, an outside tap closes it; a tap outside the arc folds
it, is consumed and never becomes a look; a drag outside does nothing; the band is
inside, the gap to the ring is outside; ASSIST fans out, again returns, outside folds
everything; SETTINGS on the sub-arc switches to the flight settings cluster, which an
outside tap closes; a profile on the sub-arc; opening the arc closes the flight
settings; a joystick finger is never an outside tap and keeps flying; the hit area
covers no flight control and takes nothing when folded; a pending engine-off
confirmation is cancelled by the outside tap with nothing applied; warnings persist
through outside taps and a tap on a banner; the paused sheet never resumes on an
outside tap and folds its settings back; rapid repeated taps; focus loss folds the arc
with nothing applied; reset folds the flight settings and the arc; the pull still works
and a slide on the open arc chooses. `CockpitTests` and `FlightAssistPlayModeTests`
were updated for the removed close buttons.

What the tests cannot show: whether a tap on the arc band is comfortable for a thumb,
whether an outside tap is ever an accident, how the folded "–" reads. Device plan.

## Limitations and open points

- The arc hit area is shaped by the session's filter at runtime; a press on the arc
  while it is folding (the 0.16 s fold) goes through to the world, as intended.
- A segment with options opens them on a tap, but a pull released on the same
  segment still performs the action (the gesture grammar of M13: release commits).
  The two grammars are each consistent; a device test should say whether one should win.
- The lights ring shows on every tap-open of the arc. Progressive disclosure of that
  ring (hidden until needed) is a layout decision for the next milestone.
- Focus loss folds the flight-time transients; a paused interface keeps its state.
  Coming back from another app while the sheet was at the settings finds it there.
- The HUD band has no tap-first ladder; the command cluster's HUD selector is its tap twin.
  Since milestone 4 the band is part of the pull comparison only.
- The explanation plate (hold to explain) is not a registered surface by design.
- Nothing here is compiled or run until CI does it (`tests.yml`).
