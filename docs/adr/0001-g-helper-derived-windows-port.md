# 0001 — A G-Helper-derived Windows app, in its own repository

- **Status:** Accepted
- **Date:** 2026-10-03 (recorded retrospectively 2026-10-04)

## Context

The target is a Framework Laptop 13 Pro (Intel Core Ultra X7 358H) running Windows 11. Framework
ships no equivalent of Armoury Crate or G-Helper: no fan curves, no per-mode power control, no tray
tool for the charge limit. [G-Helper](https://github.com/seerge/g-helper) has the right shape: a small
tray app, three modes, a fan-curve editor, no services and no bloat. But it is ASUS-specific all the
way down (ATKACPI WMI, ASUS fan tables, ASUS GPU modes).

A sibling project, [fw-helper](https://github.com/Wooloomooloo2/fw-helper), already covers the same
laptop on Linux. It is written in Rust and GTK4, with a privileged daemon, and shares no code with
G-Helper. That architecture is tied to Linux (systemd, D-Bus, polkit, sysfs), so it does not carry
over to Windows.

## Decision

Build a new Windows app in **its own repository** (`fw-helper-windows`). It should look and behave
like G-Helper: a tray icon, a compact main window, a separate "Fans + Power" window, and
Silent/Balanced/Turbo modes. It **reuses G-Helper code where it fits**: the rounded UI controls
(`RForm`, `RButton`, `Slider`), the PawnIO wrapper, the display/refresh-rate helpers, the
AppConfig pattern and Task Scheduler autostart. Everything ASUS-specific is replaced with Framework
EC access (ADR 0003).

Since code is adapted from G-Helper, the project is **GPL-3.0**.

## Consequences

- People who know G-Helper can use it straight away, and the UI work was mostly done already.
- License: GPL-3.0 is required, not chosen. Any file adapted from G-Helper should say so in its header comment.
- The Linux repo and this one evolve separately. Hardware facts found on one side (EC opcodes,
  sensor behaviour, power-limit findings) must be copied into the other side's docs by hand.
  `docs/references.md` and `docs/feature-parity.md` exist for this.
- Upstream G-Helper fixes do not flow in automatically. The adapted code is small and stable
  enough that this is acceptable.
