# 0008 — G-Helper-style runtime plumbing: flat JSON config, Task Scheduler autostart, newest instance wins

- **Status:** Accepted
- **Date:** 2026-10-03 (recorded retrospectively 2026-10-04)

## Context

These are small decisions, grouped together because they all copy G-Helper and are easy to
change by accident.

## Decision

1. **Config**: one flat JSON object in `%AppData%\FwHelper\config.json` (`Helpers/AppConfig.cs`).
   Int and string values only. Per-mode keys use the form `<name>_<mode>` (e.g. `fan_curve_1`,
   `pl_custom_2`, `overlay_0`). Every `Set` writes the file **atomically** (tmp + move). A config file that
   can't be parsed is copied to `.bak` and the app starts with empty settings.
2. **Log**: `%AppData%\FwHelper\log.txt`, with size-capped append. Every hardware write is logged with its
   result. This log is the main evidence of what has been verified (see `CLAUDE.md`).
3. **Autostart**: a Task Scheduler logon task named `FwHelper`. It runs at `Highest` when created from
   an elevated process, so power limits work at logon without UAC. Its path is fixed at startup
   if the exe has moved. A process started from the task (cwd = System32) or with `-tray` starts
   hidden.
4. **Single instance**: the new instance closes the others (`CloseMainWindow`, then `Kill` after
   1.5 s). This matches G-Helper, and means re-running the exe after an update just works.
5. **Hardware self-test**: `FwHelper.exe --selftest` exercises every EC feature (including fan
   100 % / 20 % / auto) and writes `%AppData%\FwHelper\selftest.txt`.

## Consequences

- There is no config schema or migration. Renaming a key silently resets that setting.
- Point 4 means an old instance can be hard-killed while it holds a fan duty. The new instance's
  `ApplyAll` recovers it within milliseconds (see ADR 0005).
- `--selftest` changes real hardware state (fan duty, and charge limit written back to its current value).
  It is a hardware test, not a unit test.
