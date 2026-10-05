# 0013 — Learned EC floor, PECI-only control input, testable fan loop

- **Status:** Accepted. Corrects part of the reasoning in [0009](0009-fan-safety-parity-and-guardian.md)
- **Date:** 2026-10-05

## Context

Linux ADR 0011 bounds user curves by a **firmware floor** learned from the EC's own duty. Until now it was unknown whether
Windows could read that duty. Probing the EC on 2026-10-05 with read-only commands taken from the Linux kernel's
`drivers/hwmon/cros_ec_hwmon.c` and `cros_ec_commands.h` showed:

- `EC_CMD_PWM_GET_FAN_DUTY (0x0027)` v0 `{u8 fan}` → `{u32 percent}` works, **including in EC auto mode**. It read 25–27 %.
- `EC_CMD_THERMAL_AUTO_FAN_CTRL (0x0052)` v2 `{fan, cmd=GET, 0}` → `{u8 is_auto}` works. This is the hardware truth about who owns the fan.
- `EC_CMD_THERMAL_GET_THRESHOLD (0x0051)` v1 returns, per sensor, the host thresholds and the **EC fan ramp** (`fan_off → fan_max`):

  | Sensor | fan_off → fan_max | high / halt |
  |---|---|---|
  | local_f75397 | 40 → 75 °C | 88 / 98 |
  | cpu_f75303 | 40 → 78 °C | 88 / 98 |
  | battery_temp | **40 → 50 °C** | 50 / 60 |
  | ddr_f75303 | 40 → 50 °C | 87 / 97 |
  | peci-temp | 103 → 105 °C | 120 / 127 |

- **The EC drives the fan from the board sensors, not PECI.** PECI swung between 57 and 80 °C while the EC duty followed
  local/cpu_f75303 (45 ↔ 46 °C → 25 ↔ 27 %). The plain ChromeOS formula (highest per-sensor ramp position) ran about **10 points
  below** the real duty, so the EC adds its own mapping on top.

Two corrections follow:

1. ADR 0009 said the EC "would stop the fan with an idle CPU and a hot battery". **That is wrong:** the EC ramps the fan on
   battery temperature between 40 and 50 °C. Our battery guard (42 → 48 °C, holding 100 %) is still more conservative near
   the top of that range, so it stays.
2. `FrameworkEc.GetCpuTemp` falls back to the hottest board sensor when PECI is missing. That fallback is fine for display, but
   it is unsafe as the fan input: the curve would follow about 45 °C while the CPU could be at 90 °C, and the 95 °C rule would never fire.
   A `FanLoop` unit test against a fake EC is what found this.

## Decision

1. **PECI-only control input** (`FrameworkEc.GetControlTemp`). No PECI means the existing release-after-3-ticks rule hands the fan back to the EC.
2. **Learned floor** (`FirmwareFloor`, pure, unit-tested):
   - **Model.** The model value is the highest per-sensor position along the EC's own ramps (read once from the EC).
   - **Learning.** While the EC owns the fan, the loop samples the EC's duty every 5 s. Ownership is checked with `is_auto`, in hardware, not with our own flags.
     For each 5 % model bucket it keeps the **lowest** duty it has seen. That is the EC's rising branch. It can't get stuck
     high on an outlier, which is the open defect in Linux's sticky-maximum floor.
   - **Floor.** The floor is the running maximum over buckets up to the current one, so it never falls as the model rises. Unlearned buckets
     use the model itself, which sits below the EC, so the floor works from the first run. It is 100 % once any sensor reaches its `fan_max`.
   - **Storage.** It is saved to config (`ec_floor`), at most once a minute, and **tied to the EC firmware version** (`ec_floor_firmware`).
     After an EC update it starts again from scratch.
   - **Setting.** It is applied in `FanController` like the battery guard (status "EC floor"). It is a global switch, **on by default**:
     Fans + Power → "Never quieter than the EC's own fan control".
3. **Testable loop.** `FanControl` becomes a thin static front over `FanLoop`, which talks to the hardware only through `IFanHardware`.
   `FakeFan` in the tests simulates temperatures, ownership, failing writes and a hung loop. The tests cover:
   - taking and releasing the fan, including after an external release;
   - losing the sensor;
   - the watchdog releasing the fan and the loop taking it back;
   - failed writes being reported;
   - learning only while the EC owns the fan;
   - the floor being applied, or ignored when it is turned off.

## Consequences

- With the floor on (the default), a custom curve can never be quieter than the EC would be in the same conditions, as far as the floor
  has learned. "Quiet" curves below the EC's choice are still possible by turning the switch off. Linux ADR 0011's "quiet is a
  legitimate choice" survives as an explicit opt-out rather than a default.
- **Learning needs EC-auto time.** If the user always runs a custom curve, the floor stays at the model fallback, which is about 10 points below the EC.
- The loop thread now runs for the whole lifetime of the app. It reads the EC memory map at 1 Hz, and does 2 extra EC commands every 5 s while the EC owns the fan.
- The ramps and thresholds are now readable, so the hard-coded battery constants (42/48 °C) could be derived from the EC
  (`high` 50 °C) in future, as could the CPU release point.
