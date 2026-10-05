# 0015 — Command line over a per-user named pipe; overlay window

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

The Linux sibling has `fw-helperctl` (status, profile, charge limit, fan…) and a compact overlay window. On Windows:

- the app is a single GUI process (ADR 0002), and starting a second copy *replaces* the running one (ADR 0008);
- all live state (current profile, fan loop, PL keeper) is in that running process;
- a WinExe has no console of its own.

## Decision

**CLI** (`Helpers/Cli.cs`, `CommandProtocol.cs`, `CommandServer.cs`):

- `FwHelper.exe --status | --profiles | --mode <name|id> | --charge-limit <50-100> | --fan-floor on|off | --help`.
  These are recognised **before** single-instance handling, so a CLI call never closes the tray app.
- Commands that change state go to the running app over a **named pipe** (`FwHelper-<user>`), opened with
  `PipeOptions.CurrentUserOnly` on both ends. Other accounts can't connect. The protocol is one request line (`verb\targ`) and a text
  reply. The server **re-validates every request** (verbs, ranges, profile lookup) and runs it on the UI thread, exactly
  like a click.
- `--status` and `--profiles` also work when the app isn't running. Status then reads the EC directly and shows the *saved* mode.
- Output is plain ASCII, because console code pages mangle `·` and `°`. The exe attaches to the parent console. In PowerShell, pipe it
  (`| Out-String`) so the prompt waits for the output. Exit codes: 0 ok, 1 failed, 2 bad arguments.
- No fan duty or PL commands for now. They are the risky ones, and the GUI is where their guard rails are visible.

**Overlay** (`UI/OverlayForm.cs`): a borderless, topmost, non-activating tool window (no taskbar or Alt+Tab entry) with four
lines: CPU %/MHz/°C, GPU %/W, power, and fan/profile. It is fed by the same on-demand telemetry (ADR 0011) and toggled from the tray menu.
Drag to move (the position is remembered), right-click to close. It shows over windowed and borderless games, but **not** over
exclusive full-screen. That would need an in-game hook, like MangoHud on Linux, which is out of scope.

## Consequences

- Verified 2026-10-05: `--status` against the EC with no pipe server, argument errors and exit codes, and the running tray app left untouched.
  **Not yet verified:** the pipe path against a running new build. That is on the test plan.
- The pipe is a new input to the app. The verb list, validation and per-user ACL are what keep it as narrow as the GUI.
- Parsing, the wire format and profile resolution are pure and unit-tested (`CliTests`).
