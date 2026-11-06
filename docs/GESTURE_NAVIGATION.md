# Open Skies: gesture navigation (pause sheet and entry stations)

Milestone "Gesture-First 2.0", increment C. Leaving and entering the flight with
the same language as flying it: pull, rest, release, and never a modal that
appears from nowhere.

Status: implemented with EditMode and PlayMode tests; compiled offline against
stand-ins for the Unity API; **not compiled by CI, not run in the Editor, not
touched on a device.** Stop heights, widths and times are first proposals.

## Pause

| Way in | Where | Result |
|---|---|---|
| Two-finger tap | The sky in chase view or the cockpit | The flight freezes (`Time.timeScale` 0, as MENU) and the pause sheet rises to its first stop |
| MENU | Top right, as before | The same sheet (gesture-first) or the pause card (legacy interface) |
| Esc, the Android back button | | As MENU |

The two-finger tap is recognised by `GestureRecognizer` (two fingers down within
0.15 s, both up within 0.3 s, neither moving) and raised by `GestureSession` and
`CockpitSession` as `PauseRequested`; `GameModeSession` freezes and opens. A finger
the flight controls own never counts: with a thumb on the joystick, a two-finger
tap elsewhere is one finger to the surface, so it is a tap, not a pause.

## The pause sheet

A sheet from the bottom edge with two stops (`SheetModel`, pure C#):

| Stop | Height (units) | Content |
|---|---|---|
| Closed | 0 | |
| First | 300 | A grip, the heading ("PAUSED", the mode), RESUME and the mode's actions (the same actions as the old card: SKIP, RETRY, MODE, GUIDE as the mode offers them) |
| Second | 860 | The settings, embedded (`SettingsScreenFactory.BuildEmbedded`), with the first-stop row still at the top |

- **Pull the grip band** at the top of the sheet (a hotspot with the `Sheet`
  route): the sheet follows the finger with 60 units of over-travel; release projects the velocity
  0.18 s ahead and settles on the nearer of the two stops the finger is between
  (`HudStyle.SheetSettleSeconds`, 0.28 s, `HudEasing.SoftSpring`; instant with reduced
  motion). A flick never skips a stop, however fast: from the second stop a pull down
  reaches the first, and only a finger below the first stop can dismiss.
- **Pull down past 55 % of the first stop** and release: dismissed, which resumes.
- **Tap the dim** above the sheet (the scrim): at the second stop the settings fold
  back to the first stop; at the first stop nothing happens, so resuming stays a
  deliberate action (RESUME, MENU, Esc, the pull down; `docs/TAP_DISCLOSURE_DESIGN.md`).
  A tap on the grip jumps to the other stop. **Esc** or the back button: from the
  second stop back to the first, then resume; with the full settings screen open,
  that closes first (M12).
- The depth (`SheetModel.Depth`, 0 at the first stop, 1 at the second) fades the
  settings in and dims the heading, so the second stop is visible before it is
  reached: disclosure proportional to the pull.
- Buttons on the sheet are HUD buttons and take their taps first; only the grip
  band drags the sheet, so a tap on RESUME is never a pull.

`PauseSheet` (`OpenSkies.UI`) is the view; it takes the `Sheet` route from the
surface (the sheet is a hotspot) and raises `ButtonClicked`, `DismissRequested`
and `SettingsShown`. `PresentationSession` listens to the embedded settings as it
does to the full screen (`settingsInSheet`), so an option changed on the sheet is
the same option. The settings screen of M12 remains for the legacy interface and
for the entry screen's SETTINGS.

Resume never leaves a control stuck: the sheet closing unfreezes through the same
`Unfreeze` the card used, the touch controls were revoked at the freeze, and a
finger that was on the sheet is released with it.

## Entry stations

The entry screen becomes three stations side by side, one per mode, with the
aircraft presented by a showcase camera for each:

| Station | Framing (`EntryShowcase`) | Text |
|---|---|---|
| First Flight | Close, from the front left, low (field of view 50°) | Recommended until flown or skipped; "Completed" afterwards |
| Free Flight | High and wide, over the aircraft towards the coast (58°) | FROM RUNWAY or IN THE AIR, a pair of buttons |
| Landing Challenge | Behind the aircraft, down the runway (46°) | The best score |

- **Swipe** left or right (`EntryStationModel`: station width 900 units, 25 %
  overscroll at the ends, release projected 0.2 s, settle 0.35 s; a flick carries the
  presentation to the next station at most, never past one the finger did not reach):
  the stations slide and the camera moves between the framings in proportion to the position
  (`CameraRig.SetShowcase`, `CameraMode.Showcase`), so the showcase is part of the
  swipe, not a cut after it.
- **Tap** START, or the scene itself (route `Entry`, once the stations have
  settled): starts the centred station's mode. Free Flight's two start buttons
  start at once with that choice.
- **Chips** name the three stations, show which is centred and select one directly.
- **Keyboard and gamepad**: left and right arrows or the d-pad step stations,
  Enter or Space start the centred one (`KeyboardGamepadInputProvider.MenuStepPressedThisFrame`,
  `MenuConfirmPressedThisFrame`). SETTINGS is a button as before.
- **Reduced motion**: the stations snap, the camera cuts.

The old `ModeSelectScreen` remains and is used by the legacy interface, so the
entry can be compared on a device. `GameModeSession` opens one or the other
(`entryScreen` or `modeSelect`), and `EntryStationsScreen.Chosen` is the same
`(GameMode, FreeFlightStart)` the old screen raised. While the stations are open the
camera is in showcase mode and returns to the mode's camera on start; the flight is
frozen as before, so nothing moves under the showcase.

## Rules kept

- Pause and resume never produce flight input; the sheet and the stations are
  routed hotspots on the surface, so a joystick finger never reaches them.
- The sheet's first stop is a preview of the second: nothing in the settings
  changes until a control on it is used.
- Every path has a tap: MENU, RESUME, DONE, START, the dots.
- The sheet never changes a warning's state: it covers the lower part of the
  screen while the flight is frozen and leaves the banners as they were.

## Configuration

| Where | What |
|---|---|
| `InteractionConfiguration.sheet` (`SheetSettings`) | `firstStop`, `secondStop`, `dismissShare`, `velocityProjectionSeconds`, `overTravel`, `settleSeconds` |
| `InteractionConfiguration.entry` (`EntryStationSettings`) | `stationWidth`, `velocityProjectionSeconds`, `settleSeconds`, `overscroll` |
| `EntryShowcase` | The three framings (position, look-at, field of view) relative to the aircraft |
| `GestureSettings` | Two-finger tap time and second-finger window |

## Tests

EditMode `GestureNavigationModelTests`: the sheet opens to the first stop, drags
and releases to the nearest stop, closes below the dismiss share, rubber-bands
above the top and is limited to the screen, settles with a single soft overshoot;
the stations show the next on a drag and snap to the nearest, compress the
overscroll and clamp steps, and a settle is interruptible. PlayMode
`PauseSheetTests` (real `FlightSession`, synthetic pointers): a two-finger tap on
the sky pauses and raises the sheet at the first stop; joystick and throttle
together never pause; pulling up reveals the settings and pulling down dismisses;
a scrim tap never resumes and RESUME does, with the flight controls working again; the sheet's
buttons change mode and restart like the card; reduced motion puts the sheet at
its stop at once; the sheet fits the screen and its buttons are touch targets.
PlayMode `EntryStationsTests`: opens on the recommended station with the showcase
camera; a swipe left moves to the next station and the camera moves with it; a
small drag snaps back and a flick carries over; chips select directly, START
begins the mode and the camera comes back; a tap on the scene starts the selected
station; keyboard steps and the Free Flight start choice; MODE from the pause
reopens the stations.

Not shown by tests: whether the sheet's first stop is high enough for a thumb,
whether a swipe across stations ever starts a mode by accident, whether the
showcase reads as a presentation or as a loss of control. Device plan, section E.
