# Plan: run summaries, compare projects and game profiles

Status: **proposed** (2026-11-07). Nothing is built yet. When this is approved, record the decisions as ADR 0019 (recording and
reports) and ADR 0020 (game profiles and auto-switch).

## Goal

The aim is a profile for each game that gets the most out of it. The intended workflow:

1. Record a run without leaving the game.
2. Get a summary of that run: FPS, power, temperatures, CPU and GPU usage, and **what held the game back**.
3. Change one thing (power mode, fan curve, CPU limit) and run again. Group the runs in a **compare project** and get a report
   like [example_summary.html](example_summary.html).
4. Save the winning settings as a profile for that game, which switches on by itself when the game starts.

Separately, and first: make it obvious **which Windows power mode is actually in effect**, and what it does to performance.

## What we have today

| Piece | State |
|---|---|
| Recording | Monitor window → Record. It writes 1 Hz samples to `Documents\FW Benchmark\session-*.csv` (32 columns, read by name). It stops after 12 h or from the tray "Stop recording" |
| FPS | ETW present events, one value per second per process. Stores the game's process name (`fps_app`). No per-frame times are kept |
| Power | Package, CPU, GPU and DRAM watts (Energy Meter), and the EC's PL1 |
| GPU | Busiest engine %, asked and actual clock, throttle reason (IGCL) |
| CPU | Total utility % and effective MHz. **No per-core figures**, so a game held back by one thread looks idle |
| Run context | The column `mode` holds the FW-Helper profile name only. **The Windows power mode that was actually in effect is not recorded**, nor the fan curve, the limits or the firmware version |
| Overlay | Click-through, never takes focus. Shows the game's FPS and the live readings |
| Hotkeys | Only Ctrl+Shift+F5 (cycle profiles) |
| Windows power mode | Each profile stores one and applies it on switch (Silent → Efficiency, Balanced → Balanced, Turbo → Performance). **The main window never shows it.** If Windows changes it afterwards (Settings, Battery Saver, Game Mode), FW-Helper neither notices nor says so |

The firmware A/B found two things that shape this plan:
- The Windows power mode sets the sustained power budget (Efficiency 15 W, Balanced 25 W, Performance 30–33 W on BIOS 03.04).
  It is worth about 10 % FPS in games.
- In Performance mode, Windows ignores FW-Helper's CPU limits.

---

## Phase 0: make the Windows power mode visible (small; ships first)

**Problem:** the FW-Helper profile and the Windows power mode are two separate settings. You can sit in "Turbo" while Windows
runs Balanced, or set a profile to Performance, as your Balanced profile is now, without seeing it anywhere. They also fall out of step:
- Windows Settings → Power mode overrides the profile until the next profile switch.
- Battery Saver makes FW-Helper skip the change.
- Unplugging switches to the battery profile.

**Changes:**

1. **Always show both in the main window header**, with the power budget each one means:
   ```
   Turbo                                      Windows: Best Performance · up to 33 W
   ```
   When they differ:
   ```
   Turbo                    ⚠ Windows: Balanced (changed in Windows Settings) · up to 25 W   [Use Performance]
   ```
2. **Follow what Windows actually does.** `PowerRegisterForEffectivePowerModeNotifications` (powrprof, no admin) reports each
   change: Battery Saver, Better Battery, Balanced, High Performance, Max Performance, and also Game Mode. FW-Helper keeps the
   current value. When it differs from what the profile asked for, it shows the reason where it can tell it (Battery Saver on;
   changed outside FW-Helper).
3. **One setting:** "Keep Windows in step with the profile", on by default. When Windows changes the mode, FW-Helper puts back the
   profile's setting, except while Battery Saver is on. When it's off, the profile follows what you pick in Windows, and the
   header says so.
4. **Explain it where it is set.** Under the three buttons in Fans + Power, add one line per mode, from our own measurements:
   - Efficiency: sustained budget about 15 W. Quietest; the CPU limits work.
   - Balanced: about 25 W. Best FPS per watt; the CPU limits work.
   - Performance: 30–33 W, about 10 % more FPS in games. Windows ignores FW-Helper's CPU limits.
   The figures belong to this BIOS, so keep them in one table in code, with the BIOS version they were measured on.
5. **Name the profile buttons by what they do.** Each profile button in the main window gets a small second line with its Windows
   mode, so a "Balanced" profile set to Performance is visible at a glance.
6. **Tray tooltip and overlay** add the Windows mode, plus ⚠ when it doesn't match the profile.
7. **Recording:** the per-sample `win_mode` column and the run's "context" both record it (see Phase 1).

Pure parts to unit-test: map the effective mode to a label and budget, and decide whether there is a mismatch and why.
Check on hardware: change the mode in Windows Settings, turn Battery Saver on, start a game with Game Mode, and see each
one reported.

---

## Phase 1: recording you can drive from inside a game

1. **Start and stop without the Monitor window:**
   - A global hotkey, Ctrl+Shift+F6 (not yet confirmed: see "Decisions" below), starts and stops a recording. It works in
     fullscreen games, because `RegisterHotKey` doesn't need focus.
   - The tray menu gets "Start recording" next to the existing "Stop recording", plus a **Benchmark run** submenu (item 3).
   - A toast confirms each start and stop: "Recording Cyberpunk2077 · Turbo · Windows Performance".
2. **Overlay recording indicator:** a red ● REC and the elapsed time (or a countdown for a benchmark run). The overlay stays
   click-through on purpose: clicking it would take focus from the game, so the hotkey is how you control it. The overlay can
   be shown only while recording, as an option.
3. **Benchmark run (repeatable):** a fixed warm-up (for example 15 s, not counted), then a fixed length (60/120/180 s), then it
   stops by itself. This fixes the problem from the firmware A/B, where one Cyberpunk run lasted 80 s and another 190 s, so
   their 5th percentiles couldn't be compared.
4. **Auto-record (optional, off by default, confirmed 2026-11-07):** start when a game begins presenting frames (the FPS process detection we
   already have). Stop when it exits. Ignore anything under 30 s.
5. **Run context:** a sidecar file `session-*.json`, written at start and finished at stop. It holds:
   - the game's exe and window title, start and end, and the warm-up/benchmark window;
   - the FW-Helper profile and the Windows power mode it asked for;
   - the fan curve or EC fan, the limits (power target, CPU power, CPU max, temperature cap) and AC or battery;
   - the BIOS, EC and GPU driver versions;
   - a free-text **label** ("1080p medium, FSR off"), which can be edited later. We can't read game settings, so this is the
     only record of them.
6. **New columns** (appended, so old sessions still load):
   - `win_mode`: the effective Windows power mode for each sample.
   - `cpu_core_max_pct`: the busiest core's utility. This is what shows a game held back by one thread.
   - `ft_p99_ms` and `ft_max_ms`: the frame times for that second.
7. **Frame times:** while recording, write each present of the game process to `session-*.frames.csv` (frame time in ms). At
   60 FPS for 3 min that's about 11k lines. It gives true **1 % and 0.1 % lows** and a **stutter count**. Today we only have the
   5th percentile of the per-second FPS, which hides stutter.

Cost check (CLAUDE.md): measure FW-Helper's CPU use while recording. Recording only runs during a game, but it must still stay
small. The frame list goes out through a buffered writer once a second.

---

## Phase 2: summary of a single run

**Where it opens:** after a recording stops, the toast offers "View summary". Recordings can also be listed in the Monitor
window, with a **Summary** button.

**Format:** a self-contained HTML report written next to the CSV (`session-*.html`), opened in your default browser. It looks
like [example_summary.html](example_summary.html), with two changes: system fonts instead of Google Fonts, so it works offline,
and the data embedded as JSON. Reasons for HTML rather than a WinForms window:
- The charts and tables in the example are much easier to build in HTML.
- A report is easy to keep, share and print.
- A pure C# generator can be unit-tested against fixed CSVs.

The Monitor window keeps its live charts. The report is for after the run.

**Contents:**
1. **Header:** the game, label, length, profile, Windows power mode, fan, limits and firmware. ⚠ if the Windows mode didn't match
   the profile, or the run was on battery.
2. **Tiles:**
   - median FPS, 1 % low and 0.1 % low;
   - average package watts and **FPS per watt**;
   - the **main limiter**, in a few words ("GPU power-limited", "CPU-bound, one thread", ...).
3. **Charts** over time:
   - FPS, with the frame-time p99 band;
   - power stacked (CPU, GPU, rest of package) against the PL1 line;
   - CPU and GPU clocks, GPU asked against actual;
   - temperatures and fan.
4. **Limiter breakdown:** the share of time spent in each state (see Phase 4), and the GPU throttle reasons.
5. **Suggestions** (Phase 4), each with its evidence and a button-like link "Try it", which writes a draft profile.

**Steady state:** loading screens and menus distort the averages. Use the benchmark window if there is one. Otherwise use the
seconds where the game's own process is presenting frames, and trim the first 10 s. The report says which seconds it used.

---

## Phase 3: compare projects

**A project** is a named set of runs, such as "Cyberpunk: power mode and fan". It is stored as
`Documents\FW Benchmark\projects\<name>.json`. It lists the runs (by CSV path), each with a short name ("A: Balanced, EC fan"),
and which run is the **baseline**.

**How runs get in:**
- When you start a recording or benchmark run, pick "Add to project…".
- Or add an existing recording from the recordings list. Old sessions work too: the firmware A/B runs could be loaded into a
  project as they are, with their context filled in by hand.

**The report** (`<project>.html`) follows the example:
- Tiles: the best run, its gain over the baseline, and the limiter that changed.
- **FPS:** a dumbbell or bar chart of median and 1 % low for each run, with the change from the baseline.
- **Power:** stacked CPU/GPU/rest for each run, against the PL1 for its Windows mode.
- **Efficiency:** FPS per watt for each run.
- **What differs:** a table of only the settings that differ between runs. This is generated from the run contexts, so a
  forgotten change (Windows in Balanced, a different fan curve) shows up.
- **Comparability warnings:** runs of very different length, a different game exe, AC against battery, a different firmware or
  driver, or a run that's too short for a 1 % low.
- **Conclusions:** written by the rules in Phase 4, with the numbers in each line ("Performance adds 11 % FPS for 6 W").

---

## Phase 4: what held the game back, and suggestions

This is a pure, unit-tested rules engine (`RunAnalysis`). For each steady-state second it decides the most likely limiter. A
run's diagnosis is the share of seconds in each state, so "GPU power-limited 78 %, CPU-bound 15 %".

| State | Signal (per second) | What it suggests |
|---|---|---|
| **GPU, power-limited** | GPU busy ≥ 90 %, GPU actual clock below asked, throttle reason power/PL1, package within about 1 W of the mode's PL1 | Performance mode (+5–8 W). A custom fan curve (ITMT3 raises PL1 from 30 to 33 W when it's cooler). Take watts from the CPU: in Balanced, CPU power or CPU max; in Performance, "CPU boost off" |
| **GPU, current-limited (PL4)** | throttle reason PL4/current | Firmware territory: say so (it bound on BIOS 03.02) |
| **GPU, thermal** | throttle reason thermal, or high temperatures | A fan curve; check the vents |
| **GPU-bound, not limited** | GPU busy ≥ 95 %, clock at what it asked for | Already at its limit: lower the settings or resolution, or use upscaling |
| **CPU, one thread** | GPU busy < 80 %, busiest core ≥ 90 % | More CPU clock: Performance mode, and don't cap the CPU. Lowering GPU settings won't help |
| **CPU, power-starved** | CPU clock low, package at PL1, the CPU's share falling as the GPU takes it | Raise the budget (Performance or fan) rather than cap anything (HZD on 03.02) |
| **CPU or package thermal** | EC throttle status, temperature cap or backstop reached, Tjmax | Fan curve; raise or remove the temperature cap |
| **Frame cap** | FPS flat at the refresh rate or at a round limit, GPU < 90 % | Capped by vsync or a limiter. To save power and heat, try Balanced: the report states the watts that would save |
| **GPU memory** | Shared GPU memory near its budget, with stutter spikes | Lower texture settings |
| **Wrong setup** | Windows mode ≠ profile's mode, Battery Saver, on battery | Fix that first; the rest of the run is suspect |

**Rules for suggestions:**
- Each one names the evidence ("GPU limited by power in 78 % of seconds, package at 30.9 of 31 W").
- Each one names the expected effect from what we've measured, or is honest that it isn't known.
- Each one names **one** change to try, so the next run is a clean A/B. "Try it" writes a draft profile ("Cyberpunk – try:
  Performance") and offers to add the next run to the same project.
- No suggestion may need admin (hard rule 7). The "CPU boost off" option from the ADR 0016 discussion comes in here, as a
  per-profile switch.

**A goal for each power source** (the user's idea, 2026-11-07). The best profile for a game on mains isn't the best on battery,
so each suggestion is made against a goal:
- **On mains: performance.** Rank changes by FPS (median and 1 % low), and accept more watts and fan noise for them. An example:
  "Performance mode, +11 % FPS for +6 W".
- **On battery: battery life.** Rank changes by **FPS per watt** and by the hours the battery would last (system watts from
  `battery_w`), while keeping the game playable. Playable means a target the user sets per game, with a default of 30 FPS
  median and a 1 % low over 20. Example suggestions:
  - "Balanced + CPU boost off: 31 FPS at 14 W, about 4 h 20 min, against 38 FPS at 22 W, about 2 h 50 min";
  - "Cap the frame rate at 30: the GPU drops to 9 W".
- A run's goal follows how it was recorded (AC or battery). The report names the goal. A project can compare runs from both, but
  a comparability warning keeps AC and battery runs apart.
- Suggestions on battery may lower settings that suggestions on mains never touch: Efficiency mode, a lower FPS cap, 60 Hz.

The thresholds are starting values, tuned against the A/B sessions we already have. Those sessions become the unit-test
fixtures: HZD must come out "CPU-starved" on 03.02, and Cyberpunk must come out "GPU power-limited" on 03.04.

---

## Phase 5: game profiles that switch by themselves

1. **Create from a run:** "Save as profile for this game" in a summary or project report. It copies the run's context (Windows
   mode, fan curve, limits) into a new user profile named after the game. Profiles (ADR 0012) already hold everything needed.
2. **Bind executables:** a profile gets a list of exe names (`Cyberpunk2077.exe`). They're set in the profile editor, and
   filled in automatically when the profile is made from a run.
3. **Auto-switch:**
   - When a bound exe starts presenting frames or comes to the foreground, switch to its profile.
   - When it exits, go back to the profile in use before it.
   - Toast and log each switch. Manual switching still works and wins until the game exits.
   - Detection uses process start/stop events (WMI `Win32_ProcessStartTrace` needs admin, so we can't use it). Use the FPS
     process detection while telemetry runs, plus a cheap foreground-window hook (`SetWinEventHook` EVENT_SYSTEM_FOREGROUND,
     event-driven, no polling) at other times.
4. **A pair of profiles for each game, one for mains and one for battery** (the user's idea, 2026-11-07). A game binds to two
   profiles: "Cyberpunk · AC" (performance goal) and "Cyberpunk · battery" (battery-life goal). FW-Helper switches between them
   when the charger is plugged in or out mid-game, the same way the AC/DC profile memory already works.
   - Either one can be left empty. With no battery profile, the normal battery profile stays.
   - "Save as profile for this game" fills the slot that matches the run's power source.
   - Every per-profile setting comes along by itself: Windows mode, fan curve, limits and **CPU boost off** (built 2026-11-07). So a
     game where boost off helps gets it, and a CPU-heavy game doesn't.

---

## Order and size

| Phase | Depends on | Size | Value on its own |
|---|---|---|---|
| 0 Windows power mode visible | — | small | Yes: fixes the confusion now |
| 1 Recording from the game | — | medium | Yes: repeatable benchmark runs |
| 2 Single-run summary | 1 (works on old CSVs with less detail) | medium–large | Yes |
| 4 Limiter + suggestions | 1, 2 | medium | Makes 2 and 3 useful |
| 3 Compare projects | 2 | medium | Yes |
| 5 Game profiles + auto-switch | 0, (2) | medium | Yes |

Suggested order: **0 → 1 → 2 + 4 together → 3 → 5**. Each phase ships on its own, with tests, and gets its checks added to
[hardware-test-plan.md](hardware-test-plan.md).

## Decisions (2026-11-07)

1. **Report format:** HTML in the browser. Reports must also be **easy to save and to load again from the app**: the Monitor
   window gets a reports/recordings list that opens any saved report or session, and a project report can be regenerated from its
   project file.
2. **Hotkey:** Ctrl+Shift+F6 starts and stops recording.
3. **Keep Windows in step with the profile:** on by default. **Built in Phase 0** (see the CHANGELOG).
4. **Auto-switch game profiles:** on by default for bound games.
5. **Auto-record when a game starts:** off by default (confirmed by the user 2026-11-07).

## Phase 0 as built (2026-11-07)

- `WindowsPowerMode` (pure: budgets, explanations, mismatch, resync decision; `WindowsPowerModeTests`) and `PowerModeWatcher`
  (powrprof effective power mode notifications V2, which include Game Mode; resync after 0.5 s; gives up after 3 resyncs in a minute).
- Main window: a line under the profile buttons, with **Fix** on a mismatch. Each built-in profile button's tooltip names its Windows mode.
- Fans + Power: an explanation under the mode buttons, "Keep Windows in step", "Windows now: …".
- Tray tooltip, overlay PWR row, CSV column `win_mode`.
- Not done: a second line on the profile buttons themselves (the tooltip and the line cover it).

## Phase 2 as built (2026-11-07), ahead of Phase 1

Built first because it works on the recordings that already exist.
- `RunSummary` (pure, `RunSummaryTests`), which works out the steady seconds:
  - the game is the process that presented frames for the most seconds;
  - the first 10 s are trimmed;
  - seconds above 2× the median FPS are left out as menus or loading screens.
- From those seconds it computes:
  - min, average, median, 5th percentile and max for every column;
  - the shares of the GPU and EC throttle reasons and of the profile and Windows mode;
  - warnings: a short run, battery, a mode change, Battery Saver, or the Windows mode not recorded.
- The label comes from a renamed file (`session-…cyber_punk_bal.csv` → "cyber punk bal").
- Checked against the A/B sessions: medians and watts match hardware-baseline.md within about 1 FPS and 0.5 W.
- `SummaryReport` (pure) writes a self-contained HTML file with system fonts and embedded JSON:
  - header, warnings and 6 tiles;
  - 8 time charts with a shared crosshair, a readout of every value at the hovered second, keyboard arrows and touch; the
    seconds that don't count are shaded;
  - the CPU/GPU/rest split against PL1 for the Windows mode;
  - the GPU throttle reasons explained, and a table of every reading.
- The report is written as `session-*.html` next to the CSV and opened in the default browser. Its meta tag `fwhelper-session`
  names the CSV, so **Monitor → Open session…** also accepts a report and opens its recording.
- Where it opens from:
  - **Monitor → Summary** (shown while a session is open);
  - the tray balloon "Recording saved · click for its summary" when a recording of 10 s or more stops.
- Monitor → Record asks for an optional name, which goes into the file name (`SessionRecorder.FileName`) and comes back as the label.
  Phase 1's hotkey start can't prompt in a game: it will take the name afterwards (the summary or the sidecar's label).
- Waiting on Phase 1: 1 % and 0.1 % lows (need per-frame times), the run-context sidecar (fan curve, limits, firmware,
  free-text label).
- Waiting on Phase 4: the main-limiter tile and suggestions. The report says a diagnosis comes later.

## Phase 3 as built (2026-11-07), ahead of Phase 1

Built next because the user can test it at once on the firmware A/B recordings.
- `CompareProject` (pure): name, baseline index and runs (recording + short name), saved as
  `Documents\FW Benchmark\projects\<name>.json`. Recordings in the sessions folder are stored by file name, others by full path.
  A new run is named after its recording's name, or else its game, profile and start time.
- `CompareReport` (pure, `CompareTests`): one self-contained HTML file, `<name>.html` next to the project. Its charts are
  static SVG with hover titles, so it has no script.
  - Tiles: the fastest run and its gain, what held its GPU clock (baseline → fastest), the most efficient run.
  - Conclusions: a line per run against the baseline with the numbers, then the fastest and the most efficient.
  - Bars: median FPS with the 5th-percentile second under it, CPU/GPU/rest watts with each run's PL1, FPS per watt.
  - **What differs:** only the recorded settings that differ (game, profile, Windows mode, fan, power source, CPU boost, power
    target, CPU cap), with cells that differ from the baseline highlighted, and a line listing what's the same.
  - Warnings: different games, AC against battery, steady lengths more than 2× apart, runs under 60 s, different days, and each
    run's own warnings (said once when every run has the same one).
- `CompareForm`, from **Monitor → Compare…**:
  - project list;
  - one row per run, with a baseline radio button, the name, game/length/FPS and remove;
  - **Add runs…** (multi-select; reports work too);
  - **Save and open report**.
  Open session… also opens a project `.json`.
- Not done: "Add to project…" when a recording starts (comes with Phase 1); run contexts (fan curve, firmware, in-game settings)
  for What differs (Phase 1 sidecar); conclusions by the Phase 4 rules; 1 % lows (Phase 1 frames).

## Constraints carried over

- No admin, no third-party driver (hard rule 7). Every API above works without admin.
- Unit tests never touch `AppConfig`. The analysis, report data, project model, mode-mismatch logic and auto-switch decisions
  are all pure.
- Idle cost: nothing new runs while no game is running. The foreground hook and power-mode notification are event-driven.
- CSV columns are append-only. Old sessions must keep loading and produce a (thinner) summary.
