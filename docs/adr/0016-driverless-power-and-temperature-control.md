# 0016 — Driverless power and temperature control; PawnIO removed

- **Status:** Accepted. **Supersedes [0006](0006-power-limits-via-pawnio-msr.md) and [0010](0010-power-limits-keep-and-restore.md)**, and replaces the PawnIO parts of
  [0011](0011-monitoring-via-pdh-and-ec.md)'s amendment
- **Date:** 2026-10-05

## Context

The user asked for TDP control and a package temperature cap **without PawnIO** or any other third-party driver, and asked whether
undervolting is possible. Findings (2026-10-05, see `docs/hardware-baseline.md`):

1. **The EC owns the power limits.** Framework EC source (`fwk-sakura-20260429`, `zephyr/program/framework/sakura/src/cpu_power.c`)
   writes PL1 35 / PL2 60 / PL4 80 W (and PsysPL2 from the adapter) itself, via PECI `WrPkgConfig` over eSPI.
   - It re-sends whenever its inputs change: adapter, battery or EPR.
   - **No host command can change them.** The only override is the `cpupower` command on the EC's UART console.
   - So an MSR `0x610` write through PawnIO was at best a second, probably non-governing copy. Linux found the MSR copy reads 200 W and binds nothing.
2. **Power can be measured without a driver.** Windows' Energy Metering Interface exposes the RAPL domains as performance counters:
   `\Energy Meter(RAPL_Package0_PKG|PP0|PP1|DRAM)\Power` (mW).
   - These need no admin. Measured: package 13.4 W, cores 9.6 W, GPU 0.2 W, DRAM 0.9 W.
3. **The CPU frequency can be capped without a driver.**
   - The power-plan settings `PROCFREQMAX` (P-cores, class 0) and `PROCFREQMAX1` (efficiency class 1), in MHz with 0 = no cap, can be
     written by a normal user. A no-op write was checked without admin.
   - Whether the cap *binds* under load on this CPU is not yet verified.
4. **The EC can hard-throttle the CPU on temperature.** `EC_CMD_THERMAL_SET_THRESHOLD (0x0050)` v1 is compiled into this firmware.
   - Setting `peci-temp`'s HIGH threshold makes the EC assert PROCHOT#, which drops the CPU to minimum clocks.
   - The values live in EC RAM, so they are lost on an EC reset.
   - The handler overwrites the whole sensor config, including the fan ramp, so writes must be read-modify-write.
5. **Undervolting is not possible.** MSR `0x150` is locked on this CPU (Linux verified). Intel locks it on mobile parts since Plundervolt, and it would need a driver anyway.

## Decision

- **Remove PawnIO completely:** `PawnIOWrapper`, `IntelPowerLimits`, the embedded `IntelMSR.bin`, `PowerLimitControl`/`PowerLimitKeeper`,
  the admin prompt, and the PL1/PL2 sliders. Nothing in FW-Helper needs elevation any more.
- **Telemetry** reads package, CPU (PP0), GPU (PP1) and DRAM watts from Energy Meter. Throttle information comes from the EC
  (`0x3E22 GET_AP_THROTTLE_STATUS`: soft/hard) instead of the core/graphics limit-reason MSRs.
- **Power & temperature governor** (`Features/Governor*.cs`, with the policy kept pure and unit-tested):
  - Each profile can have a **power target** in watts, off by default. A global **temperature cap** in °C is also off by default.
  - A 1 Hz loop keeps a short average (EMA) of package watts and watches PECI. When either goes over its target, it lowers
    `PROCFREQMAX`/`PROCFREQMAX1` in proportion. When comfortably under, it raises them slowly. When the cap reaches the CPU's maximum, it removes it (0).
  - Floor: 800 MHz.
  - This limits the **CPU cores only**. GPU power is not capped, and the UI says so.
- **Hard backstop** (optional, on when the temperature cap is on): FW-Helper sets `peci-temp` HIGH = cap + 5 °C, with release = cap,
  through a read-modify-write of 0x0051/0x0050. It reapplies this at startup and on resume, and restores the original config when the cap is turned off or the app quits.
- **Never leave the user's power plan capped:**
  - The original frequency values are saved to config before the first write and restored on stop, quit, crash and session end.
  - At startup, a leftover cap from a hard kill is restored from config. The guardian also restores them (`powercfg` needs no admin).

## Consequences

- No driver, no admin, no third-party install. The EC keeps its own PL1/PL2. Ours is a *frequency* governor that works below them.
- The power target follows a ~5 s average, not Intel's PL1 time window. It reacts within a few seconds rather than instantly.
- **In GPU-bound games the package power is mostly GPU**, which this can't limit. Linux saw PL1 bind at about 21 W in games.
- Changing the power plan's frequency caps is visible in other tools, and persists if both the app and the guardian are killed.
- If a frequency cap doesn't bind on this CPU (to be tested), the governor would do nothing. Its status line would show the cap
  climbing with no effect on watts, and the fallback would be the "maximum processor state" percentage settings.

## Amendment (2026-11-04): CPU power cap and fixed CPU ceiling

**Context.** After BIOS 03.04 / EC sakura-3.0.4, games are held by a shared package budget set by Intel DTT per Windows power mode
(about 30 W sustained in Performance, 25 W in Balanced; see hardware-baseline.md → GPU) instead of the PL4 current limit. The CPU
and GPU now trade watts inside that budget: HZD gives 15 W to the CPU and 9 W to the GPU, and capping the CPU should hand the
difference to the GPU. A package power target can't do that, because it shrinks the whole budget, GPU included.

**Decision.** Two more per-profile governor inputs, both off by default, both through the same frequency-cap actuator, so the
save/restore and crash recovery above cover them unchanged:
- **CPU power** (`cpu_power_target_<mode>`, 3–30 W): the same proportional loop, steering on a 5 s EMA of RAPL *cores* watts
  (Energy Meter PP0) rather than the package. The pressure is the highest of package, cores and temperature.
- **CPU max** (`cpu_max_mhz_<mode>`, 1000–4700 MHz, 100 MHz steps): a fixed ceiling. Dynamic cuts start from it, never from max
  turbo, and it still applies when a dynamic target has no reading.

**Consequences.** Whether a plan frequency cap binds is still open question 2; the CPU max setting is the most direct way to answer
it. The cores reading includes nothing of the GPU, so a CPU power cap is game-independent in a way a frequency ceiling isn't.

## Amendment (2026-11-06): the cap is ignored under Best Performance; anti-windup

**Finding** (hardware-baseline.md → Driverless Windows interfaces): `PROCFREQMAX` binds under the Balanced overlay and is ignored
under Best Performance, whose own scheme sets it to 0. Writing to the overlay needs admin. "Maximum processor state" binds under
both, but below 100 % it switches turbo off (99 % ≈ 1.8 GHz), so it can't act as a fine-grained actuator.

**Decision, for now:**
- **Anti-windup.** The governor reads the fastest core's effective clock (its own PDH query, per-core `% Processor Performance` ×
  `Processor Frequency`). If it stays above `cap × 1.15 + 150` MHz for 3 ticks, the cap isn't being applied: the governor releases
  it, pauses cutting for 30 ticks, and reports "cap not binding". A cap that has no effect no longer winds down to the 800 MHz floor.
- **The policy resets on every profile switch and settings change.** One profile's cap and averages no longer carry over into
  the next. While a cap is being raised, the status says "easing off" instead of naming a target that is far away.
- Still open: what to offer in Performance mode, where the only reachable settings are "turbo on" and "≤ ~1.8 GHz".
