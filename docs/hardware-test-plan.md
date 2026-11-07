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
| ☐ | `FwHelper.exe --hwtest governor` (no admin) | ~3 min, all cores at 100 % | **Does a power-plan frequency cap bind on this CPU** (cap 2000 MHz → watts and MHz drop)? Does the governor hold 15 W? (ADR 0016.) Plug in AC, close games and heavy apps |

`--hwtest all` runs all three in sequence. Nothing needs an elevated prompt.

## B. Manual

| ☐ | Test | Steps | Pass when |
|---|---|---|---|
| ☐ | Curve soak | Turn on a custom curve for the active mode. Run a sustained load (a game, or `--hwtest governor`-style stress) for 10 min or more | `Fan:` log transitions make sense, the rpm follows the duty, there's no audible pulsing, and the status line matches |
| ☐ | Suspend/resume | With the curve active: Start → Sleep, wait 1 min, wake | The log shows `Suspend`, `Fan control returned to EC`, `Resume`, `Custom fan curve on` |
| ☐ | End task | With the curve active: Task Manager → find FW-Helper → End task | The log shows `Guard: FW-Helper (…) exited, fan returned to EC`. Also note whether End task killed the guard too |
| ☐ | Battery guard | Charge from about 20 % with the CPU idle and the curve active. Watch `battery_temp` in Fans + Power | At ≥42 °C the status shows `battery guard` and the fan spins |
| ☐ | Charge stop | Set the limit to 80 % and charge from below it | At 80 %, battery W goes to about 0 (idle) and the % holds |
| ☐ | Charge limit after reboot | Set it to 80 %, reboot, and before FW-Helper starts, read it in the BIOS or with `framework_tool --charge-limit` | We expect 100 % (Linux finding), and FW-Helper puts back 80 % at startup |
| ☐ | Autostart | Settings → Run on startup. Sign out and back in | The tray icon appears, `starting` is in the log, the window stays hidden |
| ☐ | Power target in a game | Set a 20 W power target on the active profile, then play for 10 min with the overlay on | PKG holds about 20 W when CPU-bound. Note the FPS change, and that GPU watts aren't capped |
| ☐ | CPU max in a game | Performance profile, CPU max 2600 → 2000 → 1600 MHz, Cyberpunk 1080p medium, 3 min recorded each. Compare with the uncapped B run `session-20261104-183152` | `cpu_mhz` stays at or under the cap (open question 2: the plan cap binds), `freq_cap_mhz` shows it. Note FPS, `gpu_mhz`, `cpu_w`, `gpu_w`: the package should stay at about 30 W, with the CPU's share moving to the GPU |
| ☐ | CPU power cap in a game | Performance profile, CPU power 6 W, Cyberpunk and HZD, 3 min each | `cpu_w` holds about 6 W, the status says `cpu …W / 6W`. Note whether the GPU clock rises and whether HZD (CPU-heavy) loses FPS |
| ☐ | Temperature cap | Set an 80 °C cap with the backstop on, then run a sustained all-core load | PECI holds about 80 °C through the frequency cap. The log shows `EC backstop: PROCHOT at 85°C`, and the Monitor shows no `EC: PROCHOT` unless 85 is reached. Turn the cap off: the log shows `EC backstop removed` |
| ☐ | Overlay over a game | Tray → Overlay, then a borderless game, then the same game in exclusive full-screen | FPS matches the in-game counter. It shows over borderless (not exclusive). It never takes focus |
| ☐ | Power mode vs caps | With a power target binding, switch Efficiency → Performance in Fans + Power | The cap keeps working: a scheme re-activation doesn't reset it, and the overlay doesn't drop it |
| ☐ | Monitor vs Task Manager | Open Monitor (main window or tray) next to Task Manager → Performance, and run a load | CPU % and GPU % follow Task Manager within a few %. Temperatures, fan and RAM look plausible. The window looks right in both light and dark theme |
| ☐ | Recording | Monitor → Record, wait 1 min, close Monitor, wait 1 min, tray → Stop recording, then Monitor → Open session | The CSV has about 120 rows and the session opens in the charts. Recording kept going while the window was closed |
| ☐ | Profiles | Fans + Power → New… (copy of Balanced), rename it, set a different Windows power mode, Use it. Check the header in the main window, the tray menu, and Ctrl+Shift+F5 cycling through it. Then Delete it while it's active | The header shows the name in the profile's colour. The hotkey cycles Balanced → Turbo → Silent → yours. Deleting it falls back to the AC/battery mode |
| ☐ | Named curves | Edit a curve → Curves ▾ → Save as "Test", Reset curve, then Curves ▾ → Test | The saved curve comes back. Delete saved curve removes it |
| ☐ | Upgrade path | Start this build over the v0.1.0 `config.json` (keep a copy first) | The same mode is restored per AC/battery. Nothing is reset |
| ☐ | EC floor learning | Leave all profiles on EC automatic for about 30 min of mixed use. Then open Fans + Power | "N/21 learned" has grown. `config.json` has `ec_floor`, and its values are close to the duty `--selftest` reports at similar temperatures |
| ☐ | EC floor binding | With learned points, turn on a very quiet custom curve (all 0 % up to 60 °C) and keep the floor switch on | The status shows `EC floor`, and the fan doesn't drop below what the EC ran at. Turning the switch off lets the curve go quieter |
| ☐ | PECI loss (code-reviewed only) | Not reproducible on demand. Check the log for `EC: no CPU temperature` if PECI ever drops out | The fan goes back to the EC. It never follows a board sensor |
| ☑ 2026-10-05 | CLI via pipe | With the new build running: `FwHelper.exe --status \| Out-String`, then `--profiles`, `--mode Turbo`, `--charge-limit 80`, `--fan-floor off`, then `--fan-floor on` | Status says "running". Each change shows up in the GUI and the log (`CLI: …`). The tray app keeps running |
| ☐ | Overlay | Tray → Overlay. Drag it, right-click it, reopen it. Run a borderless or windowed game | Values update every second, the position is remembered, it never takes focus from the game |
| ☐ | Battery log: events | Unplug, use for 15 min, turn the screen off for 10 min, plug back in. Then open Battery from the main window and from the tray, and run `--battery \| Out-String` | `battery\events.csv` has a line for each % step, the unplug, the screen off/on and the plug-in. The window shows the session, screen-off time and "Last charge", and its "updated" time ticks every 5 s. The log has `Battery log: Power …` lines |
| ☐ | Battery log: standby | On battery, close the lid overnight (Modern Standby) | The next morning the table shows the night as screen-off time, with a plausible W for standby drain (well under 1 W) |
| ☐ | Standby split | Battery window → Sleep after = 10 min. On battery, let the screen time out (don't close the lid), leave it 30 min, then wake it | Windows Settings → Power shows 10 min too. The log has a `Standby:` line with about 10 min awake, then asleep. "Last standby" shows the asleep part at about 0.33 W |
| ☐ | Standby catch-up | Quit FW-Helper, close the lid for 15 min, open it, start FW-Helper | The log has `standby sessions read from the System log`, and the Battery window shows that session |
| ☐ | Battery log: idle cost | Idle 10 min on battery with Monitor and overlay closed; measure FW-Helper `TotalProcessorTime` over it, and compare with 0.2.0 | No measurable difference. In Process Explorer, the `BatteryLog` thread shows EcoQoS/efficiency mode and has close to 0 cycles |
| ☐ | Battery log: charge limit | With the charge limit at 80 %, leave it on AC until full | "Last charge" stops at about 80 %: this is also the 🟡 "actual charge stop" check |
| ☐ | Windows power mode: shown | Switch between the three profiles. Then open Windows Settings → Power and pick another mode | The main window line shows the mode in effect and its budget. The log has `Windows power mode notifications: on`. With "Keep Windows in step" on, Windows' slider jumps back within about a second, a tray notification explains it, and the log has `changed to … outside FW-Helper → back to …` |
| ☐ | Windows power mode: not in step | Untick "Keep Windows in step" (Fans + Power), then change the mode in Windows Settings | The main window line turns amber ("changed outside FW-Helper") with a **Fix** button that puts the profile's mode back. The tray tooltip and the overlay's PWR row show ⚠ |
| ☐ | Windows power mode: Battery Saver | On battery, turn Battery Saver on, then off | While on: "Battery Saver is on" in amber, no Fix button, nothing put back. When it turns off, the profile's mode comes back (keep-in-step on) |
| ☐ | Windows power mode: Game Mode | Start a game with Windows Game Mode on | The line ends with "· Game Mode" while the game has focus |
| ☐ | CPU boost off | Fans + Power → the active Performance profile → tick "CPU boost off". Check `powercfg /qh SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX`. Record Cyberpunk 3 min with it, then 3 min without (name both); repeat in HZD. Then quit FW-Helper and query again; repeat with End task | The log says `CPU boost off (max processor state 99 %…): OK`; cpu_mhz stays at or under about 1.8 GHz; the main window and the overlay say "boost off". Compare the summaries: FPS, gpu_mhz, gpu_w, cpu_w. After quit and after End task, PROCTHROTTLEMAX is back to 100 (`CPU boost back on` in the log) |
| ☐ | Run summary | Record a game for 2–3 min, then stop (Monitor or tray). Click the tray notification. Then Monitor → Open session… → pick the `.html` → Summary | The report opens in the browser; the median FPS matches the in-game counter; hovering any chart shows all readings at that second; the loading start and the menu at the end are shaded. Opening the `.html` loads its recording |
| ☐ | Compare runs | Monitor → Compare… → Add runs… → pick the 4 Cyberpunk A/B recordings of 2026-11-04 (17:20, 17:24, 18:29, 18:31). Rename them, pick 17:20 as baseline, name the project, Save and open report. Close, reopen Compare…, pick the project from the list | The report opens: 03.04 Performance fastest at about 51 FPS, +20 % on the baseline; What differs shows only Profile; one "Every run" warning about the Windows mode. Reopening the project restores the names and baseline |
| ☐ | Unplug the external screen | Open the main window, Fans + Power, Monitor and the overlay on the external screen. Unplug it, then plug it back in and repeat | Within about 2 s every window is reachable on the laptop panel; Fans + Power is never left minimised; tray click brings the main window back to the tray corner. The log may say `… off screen at … moved to …` |
| ☐ | Restore on quit and hard kill | With a power target binding: quit FW-Helper, then `powercfg /qh SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX`. Repeat with End task | Both times: `CPU frequency caps restored` (from the app or the guard) and PROCFREQMAX back to 0 |

## Results log

| Date | Test | Result | Notes |
|---|---|---|---|
| 2026-10-04 | Guardian vs forced kill (dummy parent) | ✅ | ~28 MB working set |
| 2026-10-05 | Read-only EC probe: `0x0027` fan duty, `0x0052` v2 is_auto, `0x0051` thresholds | ✅ | Duty is readable in auto mode. The EC follows the board sensors, not PECI (hardware-baseline.md) |
| 2026-10-05 | Guardian vs a forced kill of the **real** tray app (`Stop-Process -Force`) | ✅ | `Guard: FW-Helper (25968) exited, fan returned to EC`. End task in Task Manager is still to check |
| 2026-10-05 | CLI over the pipe against the running 0.2.0 build | ✅ | `--status` says running; `--mode`, `--fan-floor`, `--profiles` work; 8 back-to-back calls all answered (after two pipe fixes) |
| 2026-10-05 | Driverless reads: Energy Meter (pkg/PP0/PP1/DRAM), PROCFREQMAX read + no-op write as non-admin, EC 0x3E22, peci thermal config | ✅ | All work without admin |
| 2026-10-05 | FPS via ETW without admin (Performance Log Users) | ✅ | Dark Souls III: steady 60 FPS |
