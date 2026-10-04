# 0004 — Performance modes drive the Windows power mode; firmware does the rest

- **Status:** Accepted
- **Date:** 2026-10-03 (recorded retrospectively 2026-10-04)

## Context

On Intel Core Ultra platforms, the **Windows power mode** (the Settings slider, implemented as
"power overlay" schemes) is the input that Intel's Innovation Platform Framework / Dynamic Tuning
Technology (IPF/DTT) uses to pick firmware power and thermal limits. Writing PL1/PL2 directly
competes with DTT (ADR 0006). Following the power mode works with it.

On Linux, the sibling project made the same call: it delegates to `power-profiles-daemon` (its
ADR 0005).

## Decision

Each mode (Silent / Balanced / Turbo) is a bundle of:

1. **Windows power overlay** (required). Defaults are Silent → *Best Power Efficiency*
   (`961cc777-…`), Balanced → *Balanced* (`Guid.Empty`), Turbo → *Best Performance* (`ded574b5-…`).
   The user can change these per mode. They are set with `PowerSetActiveOverlayScheme`. The
   overlay is **skipped while Battery Saver is on**, so the app does not fight Windows.
2. **Fan**: Framework EC automatic (default), or a custom curve (ADR 0005).
3. **CPU power limits**: off by default. Optional PL1/PL2 override (ADR 0006).

As in G-Helper, the selected mode is **remembered separately for AC and battery**
(`mode_ac` / `mode_dc`). It is reapplied on plug/unplug (after a 1.5 s settle), on resume
(after 3 s) and at startup. `Ctrl+Shift+F5` cycles Balanced → Turbo → Silent.

## Consequences

- With default settings the modes only change the Windows power mode. The effect is real
  but modest, because DTT decides the limits. This is deliberate, but users who expect G-Helper-style
  big swings may see it as "doesn't do much".
- No admin is needed for modes.
- The Windows power slider in Settings stays in sync, because it is the same setting.
