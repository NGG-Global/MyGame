# Open Skies: flight model

How the prototype trainer flies, how to tune it, and how to test it.
All values in `AircraftConfiguration` are **prototype tuning values**: chosen to
be plausible for a light four-seat trainer, not validated data for any real
aircraft.

## Conventions

- Units inside the simulation: metres, kilograms, seconds, newtons, radians.
  Knots, feet and feet per minute appear only in `FlightTelemetry` and the UI.
- Axes (Unity, left-handed), body frame: +X right wing, +Y roof, +Z nose.
- Torque signs in Unity body axes: +X lowers the nose, +Y yaws the nose right,
  +Z rolls left (raises the right wing). Code works in pilot convention (nose
  up / roll right / yaw right positive) and converts in one place
  (`Aerodynamics.Compute`).
- Angle of attack α = atan2(−v_y, v_z) (nose above the flight path is positive).
  Sideslip β = atan2(v_x, v_z) (airflow from the right is positive).

## Forces and moments (each physics step)

| Source | Model | Code |
|---|---|---|
| Gravity | Unity Rigidbody gravity on `mass` at `centerOfMass` | Rigidbody |
| Lift | L = q·S·C_L(α), perpendicular to the airflow in the plane of symmetry; q = ½ρV² | `Aerodynamics.Compute` |
| Lift curve | C_L = C_L0 + a·α up to the critical angle, then a smooth (smoothstep) fall over `stallTransition` to flat-plate lift k·sin2α; same on the negative side | `Aerodynamics.LiftCoefficient` |
| Drag | C_D = C_D0 + C_L²/(π·e·AR) + flat-plate term when stalled; opposes the airflow | `Aerodynamics.DragCoefficient` |
| Side force | C_Y = C_Yβ·β | `Aerodynamics.Compute` |
| Pitch moment | q·S·c̄·(C_m0 + C_mα·α + stall break + elevator), plus damping C_mq | `Aerodynamics.Compute` |
| Roll moment | q·S·b·(aileron + C_lβ·β), plus damping C_lp | `Aerodynamics.Compute` |
| Yaw moment | q·S·b·(rudder + C_nβ·β), plus damping C_nr | `Aerodynamics.Compute` |
| Thrust | min(static thrust · power, η·P/V) along the nose through the centre of mass; power lags throttle (first-order) | `EngineModel` |
| Landing gear | Per wheel: raycast along the strut, spring-damper normal force, tyre side friction (saturating), rolling resistance and brakes (smoothed near zero speed), nosewheel steering from the rudder pedals | `LandingGear`, `AircraftController.ApplyLandingGear` |
| Air density | ISA troposphere: ρ = 1.225·(T/288.15)^4.2559 | `Atmosphere` |

Control effectiveness scales with dynamic pressure, so the controls are soft
at low speed and firm at cruise. Damping moments scale with airspeed. There is
no auto-levelling in the physics: any assistance must go through
`IFlightAssist` and only change pilot commands.

Not modelled yet: wind, ground effect, flaps, trim, propeller torque, P-factor
and slipstream, adverse yaw, spins, compressibility, crash damage.

## Indicated airspeed

`IndicatedAirspeedKnots` is equivalent airspeed: TAS·√(ρ/ρ₀). Without wind,
TAS is the Rigidbody's speed. It ignores compressibility and instrument
errors, which is acceptable below about 200 kt.

## Controls

Touch controls (joystick, throttle, brakes) are described in
`docs/TOUCH_CONTROLS.md`. All sources are merged by `FlightInputRouter`.

### Keyboard (Editor and WebGL)

| Key | Command | `FlightControls` value |
|---|---|---|
| W | Stick forward: nose down | Pitch −1 |
| S | Stick back: nose up | Pitch +1 |
| A / D | Roll left / right | Roll −1 / +1 |
| Q / E | Left / right rudder; also steers the nosewheel | Yaw −1 / +1 |
| Left Shift (hold) | Throttle up, 50 % per second | Throttle → 1 |
| Left Ctrl (hold) | Throttle down | Throttle → 0 |
| Space (hold) | Wheel brakes (main wheels) | Brake 1 |
| R | Reset to runway start, at rest, idle | session command |
| C | Chase / cockpit camera | presentation command |

Gamepad: left stick pitch/roll (up = nose down), right stick X rudder, right/left
trigger throttle, south button brakes, north button camera, select/back reset.

The throttle is a lever: it stays where it is left. The aircraft starts at
idle with brakes off.

## Tuning guide

Edit `Assets/_Game/Settings/Aircraft/TrainerConfiguration.asset` in the
Inspector (changes during Play mode are lost; mass properties apply on start).

| Symptom | Adjust |
|---|---|
| Takes too long to lift off | `maxStaticThrust`, `liftCoefficientAtZeroAlpha`, `elevatorAuthority` |
| Nose will not come up on the runway | `elevatorAuthority`, or move main wheels closer behind `centerOfMass.z` |
| Pitch oscillates (porpoising) | more negative `pitchDamping` or `pitchStability` |
| Rolls too fast / slow | `aileronAuthority`, `rollDamping` |
| Turns feel skiddy | `directionalStability`, `yawDamping` |
| Stall too gentle / violent | `stallTransition`, `stallPitchBreak`, `flatPlateLift` |
| Bounces on landing | `damperRate`, lower `springRate` |
| Veers or wobbles on the ground | `lateralFriction`, `lateralSlipSpeed`, `maxNoseWheelSteering` |

## Debugging

- **Development overlay** (top left, Editor and development builds only):
  IAS, altitude, vertical speed, throttle, angle of attack, STALL warning.
  Toggle with `HudPresenter.showDeveloperOverlay`.
- **Force gizmos** (`AircraftController.drawForceGizmos`): at the centre of
  mass, lift green, drag red, side force magenta, thrust blue, weight white;
  landing-gear forces yellow at each contact point. Visible in the Scene view,
  and in the Game view with Gizmos enabled. Scale: `gizmoMetresPerNewton`.

## Offline validation (not a Unity test)

Before the C# was written, the same equations, gear model and a rigid-body
integrator with Unity's axes were run in a Python script at the default 50 Hz
step. With the default configuration it gave: lift-off at about 66 kt after
about 340 m with gentle back pressure; climb about 900 fpm at about 85 kt; a
classic stall break at full back stick and idle (angle of attack peaks at
about 23°, the nose drops by itself) with recovery in about 4 s; touchdown and a
braked stop within about 200 m; stable at rest with no creep. This only
checks that the equations are coherent. It is **not** evidence that the Unity
build behaves the same; only the manual test below and CI can show that.

## Manual flight test (Editor)

1. Open `FlightPrototype` (build it first with **Open Skies › Rebuild Flight
   Prototype Scene** if it does not exist) and press Play.
2. **Rest:** the aircraft settles on its wheels, level, and does not creep.
   Overlay shows IAS 0, PWR 0%.
3. **Take-off:** hold Shift to full power. Expect lift-off at roughly 55-65 kt
   with light S (back stick) from about 50 kt, well before the runway end.
4. **Climb:** hold about 5-10° nose up; VS should be positive (several hundred fpm).
5. **Turns:** bank with A/D; the aircraft turns towards the low wing. Some
   back pressure (S) is needed to hold altitude in a turn.
6. **Descent:** reduce power with Ctrl; the aircraft descends without
   diving uncontrollably.
7. **Stall:** at altitude, idle and hold S. The STALL warning appears a few
   degrees before the critical angle, then the nose drops. Release S, push
   slightly forward, add power: it recovers.
8. **Circuit and landing:** fly a rectangular circuit back to the runway, idle
   on short final, flare with S just above the runway, touch down main
   wheels first, hold Space to brake to a stop.
9. **Ground handling:** at low speed Q/E steer the nosewheel.
10. **Reset:** press R at any time: the aircraft returns to the runway start
    at rest, throttle idle.
11. **Camera:** press C or the CAM button during any manoeuvre; control is
    not interrupted.

Report anything that diverges (numbers from the overlay help), with the
configuration values used.
