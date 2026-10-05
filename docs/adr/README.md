# Architecture Decision Records

Format: [MADR](https://adr.github.io/madr/), lightly adapted. This is the same convention the Linux sibling
[fw-helper](https://github.com/Wooloomooloo2/fw-helper/tree/main/docs/adr) uses. Each file holds one decision.
Files are numbered in order and never renumbered. To supersede a decision, add a new ADR and set the
old one's status to `Superseded by NNNN`.

ADRs 0001–0008 were written retrospectively on 2026-10-04 by reading back through v0.1.0. They
record decisions already built into the code, not plans.

When a Windows ADR refers to a Linux ADR, it says "Linux ADR NNNN". The two series are numbered separately.

| # | Decision | Status |
|---|---|---|
| [0001](0001-g-helper-derived-windows-port.md) | G-Helper-derived Windows app, in its own repo, GPL-3.0 | Accepted |
| [0002](0002-dotnet-winforms-single-process.md) | C#/.NET 8 WinForms, one unprivileged tray process, no service | Accepted |
| [0003](0003-ec-via-framework-crosec-driver.md) | EC access through Framework's CrosEC driver IOCTLs (no admin) | Accepted |
| [0004](0004-modes-delegate-to-windows-power-mode.md) | Modes drive the Windows power mode; Intel DTT does the rest | Accepted |
| [0005](0005-software-fan-curve-with-ec-handback.md) | Software fan curve that always hands back to the EC | Accepted, amended by 0009 |
| [0006](0006-power-limits-via-pawnio-msr.md) | PL1/PL2 via PawnIO MSR writes: optional, experimental | Accepted (unverified), amended by 0010 |
| [0007](0007-charge-limit-via-ec-0x3e03.md) | Charge limit via Framework EC command 0x3E03 | Accepted, verified |
| [0008](0008-g-helper-style-runtime-plumbing.md) | Flat JSON config, Task Scheduler autostart, newest instance wins | Accepted |
| [0009](0009-fan-safety-parity-and-guardian.md) | Fan safety at Linux parity: pure controller, watchdog, guardian process | Accepted |
| [0010](0010-power-limits-keep-and-restore.md) | Power limits: measured defaults, re-assert against firmware, restore on exit | Accepted |
| [0011](0011-monitoring-via-pdh-and-ec.md) | Monitoring: PDH counters + EC, sampled on demand, recorded as CSV | Accepted |
| [0012](0012-user-profiles-and-named-curves.md) | User profiles with stable ids, named fan-curve library | Accepted |
