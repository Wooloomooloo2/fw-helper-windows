# 0005 — Software fan curve that always hands the fan back to the EC

- **Status:** Accepted. **Known gaps against the Linux sibling's ADR 0006 (see below).**
- **Date:** 2026-10-03 (recorded retrospectively 2026-10-04)

## Context

The Framework EC's fan curve lives in firmware and cannot be replaced. What can be done is:

- `EC_CMD_PWM_SET_FAN_DUTY (0x0024)`: hold a fixed duty. **This turns off EC automatic control
  until it is turned back on.**
- `EC_CMD_THERMAL_AUTO_FAN_CTRL (0x0052)`: give control back to the EC.

So a custom curve means polling temperature and writing a duty in a loop. If the app stops
while it holds a fixed duty, the fan stays at that duty. If that duty is low, the CPU can cook
without any audible warning. The Linux project's ADR 0006 sets out this danger, and its
reasoning applies here unchanged.

## Decision

`Features/FanControl.cs`:

- An 8-point curve per mode (`FanCurve`, stored as `"t:d,..."`, made monotonic on load), with
  linear interpolation. The input is the **PECI CPU temperature** (falling back to `cpu_*`, then
  the hottest sensor that isn't the battery).
- Polls every **2 s** and **ramps down at most 4 % per tick** to avoid audible pulsing. Ramp-up
  is immediate.
- **≥ 95 °C forces 100 %**, whatever the curve says.
- **3 failed temperature reads in a row** stop the curve and return control to the EC.
- `SetFanAuto` is sent on **every** stop path: switching to a mode without a curve, quit, unhandled
  exception, `ProcessExit`, `SessionEnding` and suspend. `Stop()` sends it even when no curve was
  active, so starting the app clears a fixed duty left behind by a previous crash.
- The default is **EC automatic** for every mode. A curve is opt-in per mode.

## Consequences / known gaps

Compared with the Linux ADR 0006 checklist:

| Linux safety point | Windows status |
|---|---|
| 1. Restore on every exit path | **Partial.** Clean, crash and session-end paths are covered. A hard kill (Task Manager → End task, `taskkill /f`) is **not** covered and leaves the last duty in place until the next start or reboot. Note: `CloseOtherInstances` calls `Kill()` on an old instance that does not close within 1.5 s. The new instance re-applies the mode straight away, so the gap is brief. |
| 2. Release across suspend | **Done** (`PowerModes.Suspend` → `Stop`; resume → `ApplyAll`). Not yet seen in a log on hardware. |
| 3. Watchdog thread | **Missing.** A deadlocked UI cannot stall the threadpool timer, but a hung EC call inside `_lock` would. |
| 4. Never below the firmware floor | **Missing.** A user curve can be quieter than the EC would be. Linux later relaxed this point (its ADR 0011, "quiet is a legitimate choice"). Check that reasoning before adding a clamp. |
| 5. Temperature ceiling | **Different.** Windows forces 100 % at 95 °C. Linux *releases to the EC* at a `temp_crit`-derived threshold. Both are safe. Releasing to firmware is arguably more correct. |
| 6. No temperature, no manual fan | **Done** (after 3 failed reads). |

More gaps, taken from the Linux findings (see `docs/hardware-baseline.md`):

- **No battery guard.** `battery_temp` has the lowest crit on the board (49.85 °C) and no
  self-protection. It reaches about 42 °C *while charging with the CPU idle*. A curve that follows only PECI cannot see this.
- **No hysteresis.** PECI moves in about 1 °C steps with ±1 °C dither, so the duty can flap at curve points. Linux
  follows a rise immediately and holds a fall back by 2 °C.
- **Stiction.** The fan does not spin at 1–11 % duty (about 1–29/255). The 4 %-per-tick ramp-down passes through that
  band on its way to 0. Linux refuses those duties.
- **Polling every 2 s.** PECI can rise about 4 °C/s. Linux polls at 1 Hz.
- Releasing to the EC is not the same as maximum cooling. The EC's own curve tops out at about 3.1–3.3k rpm,
  well below manual 100 % (about 5.2k rpm on Linux; 7.3k as reported by the Windows self-test, so check the unit/scale).

Status on hardware: fixed duties (100 % ≈ 7.3k rpm, 20 % ≈ 2.6k rpm) and hand-back were checked
by `--selftest`. The curve loop itself has only run for seconds. **It needs a soak test, and a
sleep/resume cycle, before anyone relies on it.**
