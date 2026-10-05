# 0010 — Power limits: measured defaults, re-assert against firmware, restore on exit

- **Status:** Accepted. Amends [0006](0006-power-limits-via-pawnio-msr.md)
- **Date:** 2026-10-05

## Context

ADR 0006 shipped PL1/PL2 writes with guessed defaults, nothing to handle firmware overriding them, and no clean-up.
The Linux sibling measured the same board (`docs/hardware-baseline.md`):

- The real PL1 ceiling is about 35 W. A 40 W setpoint drew 35.07 W.
- Stock PL2 is 60 W.
- 25 W is the gaming sweet spot: the same fps as 35 W, and quieter.
- **Firmware re-derives PL1 a few seconds after a platform-profile change.** On Windows, the mode switch
  changes the power overlay just before writing the limits (ADR 0004), so our write is likely to be overwritten.
- A written PL is lasting state (Linux ADR 0014). It survives our process.

Still unknown: whether MSR `0x610` governs at all on this board, since on Linux only the MMIO/MCHBAR copy does.
`--hwtest pl` measures this. If it doesn't, this ADR's mechanisms are still correct, but they need a different register.

## Decision

1. **Defaults:** Silent 15/30 W, Balanced 25/60 W, Turbo 35/64 W. **Ranges:** PL1 8–35 W, PL2 15–80 W, with PL2 ≥ PL1.
   Saved values outside the range are clamped when applied.
2. **Re-assert** (`PowerLimitKeeper`, pure and unit-tested): every 5 s, read the MSR. If PL1 or PL2 is more than 1 W away from the
   target, rewrite it. After **5 rewrites per target**, stop, and show "firmware keeps overriding" in the UI and the log.
   This is the same budget as Linux's `MAX_POWER_CORRECTIONS`. Setting a new target resets the budget.
3. **Restore:** the raw MSR value is captured just before our *first* write. It is written back when:
   - a mode without a PL override is applied,
   - the app quits, crashes, or the session ends (`SafeShutdown`),
   - and after each `--hwtest`.

## Consequences

- With PL turned off (the default), the app no longer leaves an earlier override in place.
- **Not covered:** a hard kill while a PL override is active. The guardian (ADR 0009) only restores the fan. It may not run
  elevated, and it doesn't know the original value. The override then persists until reboot, when firmware reprograms it.
  This is acceptable, because a lower PL is safe and a higher one is capped at 35 W by firmware anyway.
- The captured "original" is whatever firmware had programmed at that moment. If firmware would have re-derived a different value
  for a later overlay, we restore the older one until the next profile change re-derives it.
- One MSR read every 5 s while an override is active.
