# Open Skies: flight assistance

Three control profiles make the trainer approachable for new players without a
second, "arcade" flight model. The aerodynamics, the aircraft's constants and the
telemetry are the same in every profile; assistance only changes the commands the
aircraft receives, the way a fly-by-wire computer or an autopilot servo would.

## Profiles

| | Assisted | Standard | Manual |
|---|---|---|---|
| Pitch and roll damping | stronger | light | none |
| Phugoid (speed) damping | yes | light | none |
| Bank, stick released | levels below 6°, holds up to 30°, eases steeper banks back to 30° | not touched (no auto-levelling) | not touched |
| Rudder | automatic coordination | automatic coordination | the pilot's |
| Pitch trim | automatic | automatic (option, on by default) | manual (keys / d-pad) |
| Turn compensation (trim adds the angle of attack a level turn needs) | yes | no | no |
| Stall prevention | gentle, option (on by default) | warnings only | warnings only |
| Flying tips | yes, option (on by default) | no | no |
| Warnings (stall, sink rate, overspeed, engine off) | yes | yes | yes |

**First launch: Assisted** (`FlightAssistConfiguration.defaultProfile`). This is a
design decision the owner should confirm: `docs/GAME_DESIGN.md` lists the default
level for new players as an open question. Players change it at any time and the
choice is remembered.

## Architecture

```
FlightInputRouter (touch, keyboard, gamepad)
  └▶ ControlResponse (player's sensitivity)         OpenSkies.FlightInput
       └▶ AircraftSystems.ApplyTo (brake, flaps, ignition)
            └▶ FlightAssistController : IFlightAssist   OpenSkies.Assistance (new, references FlightState only)
                 └▶ AircraftController.SetControls       OpenSkies.Simulation (unchanged)
FlightTelemetry (TelemetryMath, shared) ──▶ FlightAssistController, FlightGuidanceAdvisor (Awareness)
AircraftEnvelope (from AircraftConfiguration) ──▶ stall margins, zero-lift angle, speeds in the tips
```

| Assembly | Code |
|---|---|
| `OpenSkies.FlightState` | `FlightAssistProfile`, `ControlSensitivity`, `AircraftEnvelope`; telemetry body rates (`PitchRate…`, `RollRate…`, `YawRateDegreesPerSecond`). |
| `OpenSkies.Assistance` (new) | `FlightAssistController`, `FlightAssistTuning` / `FlightAssistGains`, `FlightAssistConfiguration` (asset), `AssistMath` (pure functions). |
| `OpenSkies.Simulation` | `TelemetryMath`: one pure function builds the telemetry for the game and the test lab; `Envelope` from the aircraft data. The stall warning now moves with the flaps. No aerodynamic change. |
| `OpenSkies.FlightInput` | `ControlResponse` (sensitivity); keyboard M (profile), Home/End and d-pad (trim). |
| `OpenSkies.Awareness` | `FlightGuidanceAdvisor` (tips); `ContextAction.Assistance` (ASSIST on the rail). |
| `OpenSkies.UI` | `FlightSettingsPanel` and `GuidanceLine` with `FlightSettingsPanelFactory`. |
| `OpenSkies.Gameplay` | `FlightSession` wiring, `FlightPreferences` (remembered settings). |

The controller is plain C#, deterministic and allocation-free per step. It runs in
`FlightSession.FixedUpdate` on the previous step's telemetry, like every control source.

## How the assistance behaves

**Corrections, not replacement.** The pilot's stick goes to the aircraft; assistance
adds corrections on top with `AssistMath.Compose`, which keeps full authority: stick
centred gives the correction, full deflection always gives full deflection.

**Continuous and predictable.**

| Rule | Default |
|---|---|
| Every correction changes at most | 1.5 of full travel per second (0.03 per 20 ms step) |
| Authority per axis | pitch 0.3, roll 0.3, rudder 0.6; bank hold 0.12 (about 6°/s of roll) |
| Trim motor | 0.12 per second; trim limited to ±0.4 |
| Profile or option change | blends over 2 s (smoothstep); the trim is kept |
| Engage | in the air from 35 kt, fades in over 1.5 s; stays engaged down to 15 kt; off within 0.3 s of touchdown (the rudder steers the nosewheel) |

**The pilot wins.** Damping and coordination fade out as the stick (or rudder) moves
from 15 % to 70 % deflection, so a strong, deliberate input reaches the aircraft
unchanged. Holds (bank, trim) only act while that axis is released, and only after it
has settled (0.4 s and less than 4°/s).

**No hidden autopilot.** Assistance never moves the throttle and never flies a
heading, altitude or path. The bank hold keeps the bank you left; the automatic trim
keeps the angle of attack (so the airspeed) you left, as real trim does: change the
power and the aircraft climbs or descends at that speed.

**Honest physics.** Stall prevention only limits nose-up elevator, from 6° below the
critical angle of attack (2° with full limit, never below 0.15 elevator, so a slight
pull remains) and from 15° nose-up attitude (full limit at 30°). It does not add lift
or power. When the aircraft cannot be helped (too slow, engine off, a yank at cruise
speed), it does what the flight model says. Example: full aft stick at cruise and
idle peaks at 14.8° angle of attack with a 1.2 s stall warning in Assisted, below the
15° critical angle, against 27.7° and a full stall in Manual.

### Control laws (per step)

| Axis | Correction |
|---|---|
| Pitch | − pitch damping × pitch rate + speed damping × airspeed trend (phugoid damper; zero in a steady climb or descent); faded by the pilot's pitch input |
| Pitch trim | stick released and settled: capture the angle of attack (never closer than 7° to the critical angle); integrate the trim towards it (Assisted adds the 1/cos(bank) load factor in turns). On release after a steady hold, 85 % of the held stick moves into the trim at the correction rate, so letting go does not drop the nose. On the ground (automatic trim) it returns to neutral. |
| Pitch limit | `min(command, nose-up limit)` from the stall and attitude protections |
| Roll | − roll damping × roll rate (faded by the pilot's roll input) + bank hold (stick released) |
| Yaw | coordination gain × sideslip − yaw damping × (yaw rate − the rate a coordinated turn needs); faded by the pilot's rudder |

Why a phugoid damper: pitch-rate damping alone made the slow speed-and-altitude
oscillation grow (the lab showed ±1100 to ±2000 fpm after a single pull). Feeding back
the airspeed trend damps it without fighting a deliberate climb or descent.

## Settings

| Setting | Where | Remembered |
|---|---|---|
| Profile: ASSISTED / STANDARD / MANUAL | ASSIST on the action rail (offered when parked; any time through SYSTEMS), or **M** | yes |
| Stick sensitivity: LOW / MEDIUM / HIGH | same card | yes |
| Assisted: STALL AID, TIPS; Standard: AUTO TRIM | same card, the selected profile's options | yes |

The card sits in the free column like the cockpit focus panel (one of them at a time;
opening either closes the other), never pauses the game and never covers a flight
control. Sensitivity shapes the pilot's input before assistance, so it is independent
of the profile, and keeps full authority at full deflection: Low is 0.5·x + 0.5·x³
(half the response near the centre), Medium is linear, High is 1 − (1 − x)^1.6. The
touch joystick's own calibration (dead zone, curve) is separate.

Manual trim: **Home** nose down, **End** nose up (held), gamepad d-pad up/down.
**Touch has no trim or rudder control yet**, so Manual on a phone means no rudder and
no trim; Standard and Assisted coordinate and trim automatically. A touch trim
control is a design decision for a later milestone.

## Flying tips (Assisted)

One tip at a time on a line along the bottom of the free column (takes no touches,
fades). Safety tips come first and stay while needed: stall, sink rate, engine off,
overspeed. Cautions while they last: steep bank (over 45°), low speed (under 1.2 × the
stall speed for the flaps). Phase advice shows once per phase for 8 s: take-off power,
the rotation speed, climb speed, approach set-up, flare, braking. Speeds in the tips
come from the aircraft data (55 kt rotation, 70 kt climb, 65 kt approach for the
prototype trainer). A tip shows at least 2.5 s and lingers 1 s after its reason has
gone, so it never flickers.

## Tuning

`Assets/_Game/Settings/Assistance/FlightAssistConfiguration.asset` (created once by
the scene builder, never overwritten). All values are **prototype tuning values**
chosen in the EditMode flight lab, not validated in a real flight test.

| Gain (`FlightAssistGains`) | Assisted | Standard | Manual |
|---|---|---|---|
| `pitchDamping` (stick per °/s) | 0.006 | 0.002 | 0 |
| `speedDamping` (stick per kt/s) | 0.015 | 0.012 | 0 |
| `rollDamping` (stick per °/s) | 0.01 | 0.003 | 0 |
| `yawDamping` (rudder per °/s) | 0.02 | 0.008 | 0 |
| `coordination`, `autoTrim` | 1, 1 | 1, 1 | 0, 0 |
| `bankHold`, `turnCompensation`, `stallPrevention` | 1, 1, 1 | 0, 0, 0 | 0, 0, 0 |

Shared limits are in `FlightAssistTuning` (rates, authorities, thresholds above).

## Tests

**EditMode flight lab** (`Tests/EditMode/FlightLab.cs`): one aircraft in the air, flown
with the game's own pure flight model (`Aerodynamics`, `EngineModel`, surface and flap
rates) and the shared `TelemetryMath`, integrated like the physics engine at 50 Hz;
optional wind for disturbances (the game has no wind yet). It is not the Rigidbody: no
gear, no collisions, no gyroscopic term.

`FlightAssistScenarioTests`, each scenario in every profile. At every step they also
check that each correction moves no faster than its rate limit, the trim no faster
than its motor, and nothing is NaN. Measured values, prototype trainer:

| Scenario | Manual | Standard | Assisted |
|---|---|---|---|
| Hands-off cruise, 60 s at 94 kt | +4 ft, wings level | +3 ft | +3 ft |
| 30° turn, stick released (bank at 10/30/60 s) | 23/11/2° (the aircraft's own stability) | 31/29/21° | 30/29/30°, −86 ft |
| Pull for 1 s, release (last 40 s of 80) | 90-100 kt | 96-109 kt | 93-104 kt |
| Climb held, released at full power | −2352 to +3254 fpm | +110 to +1909 fpm | +20 to +1974 fpm, 73-89 kt |
| Stall practice (idle, nose raised to hold altitude) | 22.9° AoA, warning 21 s | 23.0°, 14 s | 11.5°, no warning |
| Full aft stick at cruise, idle | 57° pitch, 27.7° AoA | same | 27° pitch, 14.8° AoA |
| 16 kt crosswind gust | 5.7° bank | 3.8° | 2.7°, back to wings level |
| Full-flap approach, released at 60 % power | 64-76 kt, −938 to +394 fpm | 61-64 kt, −489 to −77 fpm | 61-64 kt, −485 to −84 fpm |
| Profile change every 4 s in a 30° climbing turn | largest command step 0.004 per 20 ms; pitch rate under 3.6°/s, roll rate under 1.9°/s | | |

Also: Manual through the assistance layer flies exactly like no assistance (identical
trajectory), and no scenario changes any field of the aircraft data.

`FlightAssistControllerTests`, `AssistMathTests`, `FlightGuidanceTests`: pass-through,
ground behaviour, engage and disengage, full authority, no fighting strong input, bank
rules, coordination and deliberate slips, stall prevention by profile and with flaps,
trim take-over and limits, manual trim, blending, bad data, reset, sensitivity curves,
telemetry rates and flap-aware stall warning, envelope, tips and the ASSIST action.

**PlayMode** (`FlightAssistPlayModeTests`, real `FlightSession` and Rigidbody): the
aircraft launched in flight, profile changes in a turn with no command jump and no jolt,
Manual exact for roll and rudder; sensitivity in every profile; the settings card from
the rail (profile, sensitivity, options, one panel at a time); tips only in Assisted;
remembered settings; the card and tip line clear of every control at 4:3, 16:9 and
20:9 with targets of at least 96 units.

What the tests cannot show: how it feels. Use the checklist below.

## Device and Editor checklist

1. First launch: ASSIST is on the rail while parked; the card shows ASSISTED selected.
2. Assisted, touch only: take off, let go of the stick in the climb (it keeps
   climbing), roll to about 25° and let go (it holds the turn), level off and let go.
3. Reduce power in Assisted: it descends at the same speed. Pull hard at low speed:
   the nose eases off before the stall warning.
4. Switch to Standard in a turn: no jolt; the turn is no longer held but stays coordinated.
5. Switch to Manual (keyboard): the rudder is yours, trim with Home/End. Note that touch
   has no rudder or trim.
6. Try each sensitivity on the touch joystick: the centre feels softer (LOW) or quicker
   (HIGH), full deflection is unchanged.
7. Restart the app: profile, sensitivity and options are as you left them.

## Known limitations

- Prototype tuning, tuned in the lab; not yet flown in the Unity build (CI blocked at
  licence activation, see `docs/COCKPIT.md`).
- No wind in the game; crosswind is tested only in the lab.
- No touch rudder or trim control (Manual on touch has neither).
- The HUD does not show which profile is active outside the settings card; the dev
  inspector does not show the assistance state yet.
- Spins, adverse yaw, propeller effects are not modelled (`docs/FLIGHT_MODEL.md`), so
  assistance is not tested against them.
