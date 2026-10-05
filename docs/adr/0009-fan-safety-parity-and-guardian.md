# 0009 — Fan safety at Linux parity: a pure controller, a watchdog and a guardian process

- **Status:** Accepted. Amends [0005](0005-software-fan-curve-with-ec-handback.md)
- **Date:** 2026-10-04

## Context

ADR 0005 listed the places where the Windows fan curve falls short of the Linux sibling's safety rules (Linux ADR 0006/0011).
No battery guard, no hysteresis, no stall-band handling, a 2 s poll, no watchdog, and a hard kill
left a fixed duty in place. The fan is the one feature that can quietly damage hardware, so these
gaps were first on the roadmap.

## Decision

**1. Pure decision logic.** `Features/FanController.cs` contains every rule and does no I/O.
`FanControl` only reads sensors, writes the EC and runs the threads. The rules are unit-tested in
`tests/FwHelper.Tests`. Those tests include a 20,000-step random walk that asserts the safety invariants.

| Rule | Value | Linux equivalent |
|---|---|---|
| Poll | 1 Hz, on its own thread | 1 Hz |
| Hysteresis | Rises are followed at once. Falls lag by 2 °C (clamp the held value to `[t, t+2]`) | `HYSTERESIS_C = 2` |
| Ramp | Up: immediate. Down: 2 %/tick | Up 12/255, down 4/255 per tick |
| Stall band | Never 1–11 %. A small request becomes 12 %; a ramp toward 0 jumps from 12 % straight to 0 | Refuse 1–29/255 |
| CPU ≥ 95 °C | 100 % | Same |
| CPU ≥ 100 °C (Tjmax) | Release to the EC. Retake only below 95 °C | Release at min(crit−15, 100) |
| Battery guard | `battery_temp` ≥ 42 °C: at least 12 %, rising linearly to 100 % at 48 °C | Ramp from crit−8, **release** at crit−2 |
| No CPU temperature | Never take the fan. If the reading is lost, hold the duty for 2 ticks, then release on the 3rd | Release |
| Missing battery sensor | The curve keeps running without the guard | — |

The battery guard **deliberately differs from Linux**. Windows holds 100 % at crit−2 where Linux releases.
The EC's own curve follows CPU temperature: on the heating branch it does not start the fan below about 67 °C.
So releasing with an idle CPU and a hot, charging battery would *stop* the fan.

**2. Duty written every tick.** The duty is written every tick, not only when it changes. If something else hands the fan back to the
EC (an old instance's guardian, `framework_tool`), the next tick takes it back.

**3. Watchdog.** A separate thread (`FanWatchdog`, above-normal priority) that does not take the control lock.
If the loop hasn't completed a tick for 5 s, it sends EC auto and retries every second until the EC
accepts. When the loop recovers, it resets the controller and takes the fan again from a fresh reading.
A tick that throws doesn't update the heartbeat, so a loop that keeps failing is also released.

**4. Guardian process for hard kills.** Once the tray app reaches the EC, it starts
`FwHelper.exe --guard <pid>` (`Helpers/Guardian.cs`). The guardian waits for that process to exit,
by *any* route, and then sends EC auto. Sending auto is always safe, so it does not need to know whether a curve was active.

## Consequences

- Verified on hardware (2026-10-04): a guardian watching a process killed with `Stop-Process -Force` logged
  `Guard: ... fan returned to EC` and exited. The guardian costs about 28 MB working set, of which about 7.6 MB is private.
- **Still not covered:** "End process tree" or killing the guardian first. A crash of the whole session or the OS
  is also not covered (on reboot the EC goes back to auto). Task Manager's *End task* on a grouped app may kill the
  child as well. **This needs checking.**
- `CloseOtherInstances` kills the old guardian too. That's fine, because the new instance takes over at once.
- Two `FwHelper.exe` processes appear in Task Manager. The README says so.
- The curve now runs one EC command per second even when the duty is unchanged.
- Not yet adopted from Linux: a learned firmware floor (Linux ADR 0011). It needs the EC's auto-mode duty, and
  it hasn't been confirmed that Windows can read it. Also not adopted: reading `crit` values from the EC. The thresholds are constants
  taken from the Linux measurements.
- **Still not soak-tested.** The curve loop needs a real run under load plus a sleep/resume cycle.
