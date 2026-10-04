# 0007 — Charge limit via Framework's custom EC command 0x3E03

- **Status:** Accepted, verified on hardware
- **Date:** 2026-10-03 (recorded retrospectively 2026-10-04)

## Context

Windows has no generic API for a battery charge limit. Framework's BIOS has a setting, but changing
it needs a reboot. The Linux sibling first tried the kernel's charge-control path, found it
inert on this board (its ADR 0008, superseded), and settled on Framework's custom EC command
`0x3E03` (its ADR 0012). The same command can be reached from Windows through the CrosEC driver (ADR 0003).

## Decision

- `EC_CMD_CHARGE_LIMIT_CONTROL (0x3E03)`: `{0x02 SET, max, 0}` sets the limit and `{0x08 GET, 0, 0}` reads back `(max, min)`.
- The range is clamped to **50–100 %**. The tray menu has 60/80/100 shortcuts and the main window has a slider.
- The value is saved in config (`charge_limit`) and **reapplied on startup and resume** if the EC's value
  differs, in case an EC reset dropped it. If the user has never set it, the EC's value is shown as-is.

## Consequences

- Verified: write-back reads correctly, and 85 % has held across many app restarts (log, 2026-10-03).
  Not yet verified on Windows: that charging actually **stops** at the limit. Linux verified this
  (it stopped at exactly 80 %, `current_now=0`).
- The EC keeps the value while the app is not running, and across suspend. **Linux found it is
  lost on reboot** (it came back at 100 %), so reapplying it at startup is required, not optional.
  The Windows log so far only shows it surviving *app* restarts.
- Wire order is `[mode, max, min]`. Swapping max and min silently sets a *minimum* instead. Linux hit this trap.
- The UEFI battery-limit setting uses the same mechanism, so whichever writes last wins.
