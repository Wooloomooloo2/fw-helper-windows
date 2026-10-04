# 0002 — C# / .NET 8 WinForms, one unprivileged tray process

- **Status:** Accepted
- **Date:** 2026-10-03 (recorded retrospectively 2026-10-04)

## Context

G-Helper is C# WinForms, so staying on that stack lets its code be lifted almost unchanged
(ADR 0001). The Linux sibling splits into a root daemon and an unprivileged GUI because sysfs
writes need root. On Windows, the Framework CrosEC driver accepts commands from a normal user
(ADR 0003). So nothing in the core feature set needs elevation.

## Decision

- One WinForms process (`FwHelper.exe`), targeting `net8.0-windows`, x64, with `RollForward=Major`
  so it also runs on newer installed desktop runtimes (e.g. .NET 10).
- Framework-dependent, single-file publish (`PublishSingleFile`, not self-contained). The exe is about 0.9 MB.
- **No Windows service and no driver of its own.** The manifest is `asInvoker`.
- Elevation is only needed for the optional CPU power limits (ADR 0006). The UI offers
  "Restart as admin", and an elevated autostart task (ADR 0009) avoids a UAC prompt at every logon.
- One NuGet dependency: `TaskScheduler` (dahall), for autostart.

## Consequences

- Install means copying the exe. Uninstall means deleting it (plus `%AppData%\FwHelper`).
- **Nothing outlives the process.** If the process is killed hard while it holds manual fan
  control, nothing hands the fan back (see ADR 0005). On Linux a separate restore binary and a
  watchdog cover this. Here they would need either a service or a watchdog process.
- The .NET Desktop Runtime is a prerequisite (8 or newer).
