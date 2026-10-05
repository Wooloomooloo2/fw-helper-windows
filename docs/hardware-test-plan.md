# Hardware test plan

These checks need the real laptop, and they are bundled so they can be run in one session. After a run, record the results in
`CLAUDE.md` (Current state), in `docs/hardware-baseline.md` (new facts, tagged **[W]**), and in the relevant ADR.
Then tick the box here and add the date.

Before you start: **quit FW-Helper** (the running exe locks `publish\FwHelper.exe`), publish a fresh build (see `CLAUDE.md` → Commands), and run it. Keep `%AppData%\FwHelper\log.txt` open in an editor
that reloads the file.

## A. Automated (`FwHelper.exe --hwtest <name>`)

Each test closes the running tray app, runs, writes `%AppData%\FwHelper\hwtest-<name>.txt`, shows a summary, and then restarts the
tray app. Run them from `publish\`.

| ☐ | Command | Time | Answers |
|---|---|---|---|
| ☐ | `FwHelper.exe --hwtest watchdog` | ~20 s | Does the watchdog release the fan when the loop stalls, and does the loop take the fan back afterwards? (ADR 0009) |
| ☐ | `FwHelper.exe --hwtest fansweep` | ~2.5 min | Duty % → rpm on Windows, next to Linux's /255 table. Settles **open question 1** (7.3k vs 5.2k rpm) and where the stall band is. Best done cool and on AC |
| ☐ | elevated prompt, PawnIO installed: `FwHelper.exe --hwtest pl` | ~4 min, all cores at 100 % | Does MSR `0x610` bind package power? (**Open question 2**, ADR 0006/0010.) Plug in AC, and close heavy apps |

`--hwtest all` runs all three in sequence. `pl` is skipped unless the prompt is elevated.

## B. Manual

| ☐ | Test | Steps | Pass when |
|---|---|---|---|
| ☐ | Curve soak | Turn on a custom curve for the active mode. Run a sustained load (a game, or `--hwtest pl`-style stress) for 10 min or more | `Fan:` log transitions make sense, the rpm follows the duty, there's no audible pulsing, and the status line matches |
| ☐ | Suspend/resume | With the curve active: Start → Sleep, wait 1 min, wake | The log shows `Suspend`, `Fan control returned to EC`, `Resume`, `Custom fan curve on` |
| ☐ | End task | With the curve active: Task Manager → find FW-Helper → End task | The log shows `Guard: FW-Helper (…) exited, fan returned to EC`. Also note whether End task killed the guard too |
| ☐ | Battery guard | Charge from about 20 % with the CPU idle and the curve active. Watch `battery_temp` in Fans + Power | At ≥42 °C the status shows `battery guard` and the fan spins |
| ☐ | Charge stop | Set the limit to 80 % and charge from below it | At 80 %, battery W goes to about 0 (idle) and the % holds |
| ☐ | Charge limit after reboot | Set it to 80 %, reboot, and before FW-Helper starts, read it in the BIOS or with `framework_tool --charge-limit` | We expect 100 % (Linux finding), and FW-Helper puts back 80 % at startup |
| ☐ | Autostart | Settings → Run on startup. Sign out and back in | The tray icon appears, `starting` is in the log, the window stays hidden |
| ☐ | PL keep (only if the `pl` test passed) | Turn on the Turbo override, then switch the Windows power mode from Settings and back | Either no drift, or `PL drifted … rewriting` lines appear, at most 5 of them |
| ☐ | Monitor vs Task Manager | Open Monitor (main window or tray) next to Task Manager → Performance, and run a load | CPU % and GPU % follow Task Manager within a few %. Temperatures, fan and RAM look plausible. The window looks right in both light and dark theme |
| ☐ | Recording | Monitor → Record, wait 1 min, close Monitor, wait 1 min, tray → Stop recording, then Monitor → Open session | The CSV has about 120 rows and the session opens in the charts. Recording kept going while the window was closed |
| ☐ | Profiles | Fans + Power → New… (copy of Balanced), rename it, set a different Windows power mode, Use it. Check the header in the main window, the tray menu, and Ctrl+Shift+F5 cycling through it. Then Delete it while it's active | The header shows the name in the profile's colour. The hotkey cycles Balanced → Turbo → Silent → yours. Deleting it falls back to the AC/battery mode |
| ☐ | Named curves | Edit a curve → Curves ▾ → Save as "Test", Reset curve, then Curves ▾ → Test | The saved curve comes back. Delete saved curve removes it |
| ☐ | Upgrade path | Start this build over the v0.1.0 `config.json` (keep a copy first) | The same mode is restored per AC/battery. Nothing is reset |
| ☐ | EC floor learning | Leave all profiles on EC automatic for about 30 min of mixed use. Then open Fans + Power | "N/21 learned" has grown. `config.json` has `ec_floor`, and its values are close to the duty `--selftest` reports at similar temperatures |
| ☐ | EC floor binding | With learned points, turn on a very quiet custom curve (all 0 % up to 60 °C) and keep the floor switch on | The status shows `EC floor`, and the fan doesn't drop below what the EC ran at. Turning the switch off lets the curve go quieter |
| ☐ | PECI loss (code-reviewed only) | Not reproducible on demand. Check the log for `EC: no CPU temperature` if PECI ever drops out | The fan goes back to the EC. It never follows a board sensor |
| ☐ | CLI via pipe | With the new build running: `FwHelper.exe --status \| Out-String`, then `--profiles`, `--mode Turbo`, `--charge-limit 80`, `--fan-floor off`, then `--fan-floor on` | Status says "running". Each change shows up in the GUI and the log (`CLI: …`). The tray app keeps running |
| ☐ | Overlay | Tray → Overlay. Drag it, right-click it, reopen it. Run a borderless or windowed game | Values update every second, the position is remembered, it never takes focus from the game |
| ☐ | PL restore | With an override active, quit FW-Helper | `PL MSR restored` appears in the log |

## Results log

| Date | Test | Result | Notes |
|---|---|---|---|
| 2026-10-04 | Guardian vs forced kill (dummy parent) | ✅ | ~28 MB working set |
| 2026-10-05 | Read-only EC probe: `0x0027` fan duty, `0x0052` v2 is_auto, `0x0051` thresholds | ✅ | Duty is readable in auto mode. The EC follows the board sensors, not PECI (hardware-baseline.md) |
