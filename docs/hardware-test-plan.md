# Hardware test plan

These checks need the real laptop, and they are bundled so they can be run in one session. After a run, record the results in
`CLAUDE.md` (Current state), in `docs/hardware-baseline.md` (new facts, tagged **[W]**), and in the relevant ADR.
Then tick the box here and add the date.

Before you start: publish a fresh build (see `CLAUDE.md` → Commands) and run it. Keep `%AppData%\FwHelper\log.txt` open in an editor
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
| ☐ | PL restore | With an override active, quit FW-Helper | `PL MSR restored` appears in the log |

## Results log

| Date | Test | Result | Notes |
|---|---|---|---|
| 2026-10-04 | Guardian vs forced kill (dummy parent) | ✅ | ~28 MB working set |
