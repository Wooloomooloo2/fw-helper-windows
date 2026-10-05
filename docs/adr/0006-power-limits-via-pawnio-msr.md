# 0006 — CPU power limits via PawnIO MSR writes: optional and experimental

- **Status:** **Superseded by [0016](0016-driverless-power-and-temperature-control.md)** (PawnIO removed, 2026-10-05)
- **Date:** 2026-10-03 (recorded retrospectively 2026-10-04)

## Context

PL1 (sustained) and PL2 (boost) live in `MSR_PKG_POWER_LIMIT (0x610)`. On Linux they can be
written through the kernel's RAPL powercap sysfs, with no extra software. Windows has no
supported user-mode route. MSR access needs a signed kernel driver. G-Helper uses
[PawnIO](https://pawnio.eu), a signed driver that runs small sandboxed modules, together with the
`IntelMSR` module.

Intel DTT can also lower or override the limits by itself, so a written value may not stick.

## Decision

- `Hardware/PawnIOWrapper.cs` (adapted from G-Helper) opens `\\?\GLOBALROOT\Device\PawnIO` and
  loads the **IntelMSR PawnIO module**, which is embedded as a resource
  (`Hardware/IntelMSR.bin`, taken from G-Helper / PawnIO.Modules).
- `Hardware/IntelPowerLimits.cs` reads the RAPL units (`0x606`) and writes PL1/PL2 with their
  enable bits into `0x610`. It **refuses if bit 63 (lock) is set**. It can also compute package
  power from `MSR_PKG_ENERGY_STATUS (0x611)`.
- **Off by default.** It is enabled per mode in Fans + Power. The defaults are Silent 15/30 W,
  Balanced 28/45 W and Turbo 45/64 W. The slider ranges are PL1 5–65 W and PL2 5–115 W, and PL2 ≥ PL1 is enforced.
- It needs **admin and PawnIO installed**. The UI shows "Restart as admin" or a "Get PawnIO" link.

## Consequences

- This is the only feature that needs elevation and third-party software, and it is the least verified.
  Logged status so far: "Requires admin", then "PawnIO not installed". **It has never written a value on hardware.**
- The default wattages are guesses, and the Linux measurements (`docs/hardware-baseline.md`) contradict them:
  - **The real PL1 ceiling on this board is about 35 W.** A 40 W setpoint drew 35.07 W, so Turbo's 45 W cannot be reached.
  - Stock PL2 is 60 W. So Silent's 30 W PL2 is a real cut, and Turbo's 64 W changes almost nothing.
  - In GPU-bound games, PL1 only binds up to about 21 W.
- **This may be the wrong register.** On Linux the MSR package limit (`0x610`) reads 200 W and
  governs nothing. The binding PL1 is the **MMIO/MCHBAR copy** (`intel-rapl-mmio`). Writing `0x610` may
  have no effect, or only take effect when lowering the limit. Check under sustained load for more
  than 32 s (the PL1 time window) and look into writing the MCHBAR copy.
- Firmware **re-derives PL1 when the platform profile changes**. On Windows, a power-overlay switch
  (ADR 0004) will probably overwrite our value a few seconds later. There is no re-assert loop yet.
  Linux re-asserts up to 5 times.
- MSR writes **persist until reboot** after the app exits. They are lasting state, like Linux ADR 0014.
  Either restore the defaults on exit or document clearly that they persist.
- A read-back is not proof the limit works. Verify with a sustained power measurement.
- Undervolting is out of scope. That matches the Linux ADR 0007, and Intel locks it on this platform anyway.
- Open question: is there a route without PawnIO (e.g. Intel DTT/IPF interfaces from user mode)?
  The Linux side does TDP "without external software" only because Linux has RAPL in the kernel.
