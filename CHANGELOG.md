# Changelog

## Unreleased

### CPU power cap and CPU max (ADR 0016 amendment)
- Fans + Power has two new per-profile limits: **CPU power** (watts for the CPU cores only) and **CPU max** (a fixed MHz
  ceiling). The CPU and GPU share one power budget, so in GPU-bound games capping the CPU leaves more for the GPU, which a package
  power target can't do. Both use the existing governor, so the power plan is restored on quit, crash and hard kill as before.
- The live line in Fans + Power shows CPU watts next to package watts.
- **Known limit:** Windows ignores CPU frequency caps while the power mode is **Performance** (Best Performance overlay), and
  only admin could change that. CPU power, CPU max and the power target therefore only take effect in Balanced and Efficiency.
  In Performance the governor now notices this: it shows "cap not binding" and releases the cap, instead of cutting it to 800 MHz.
  Fans + Power says so under the limits whenever the profile's Windows power mode is Performance.
- The governor starts afresh on every profile switch, so a Balanced profile no longer inherits the previous profile's cap for
  half a minute. While a cap is being raised, its status says "easing off".

### CPU boost off (ADR 0016, 3rd amendment)
- New per-profile switch in Fans + Power: **CPU boost off**. It sets Windows' "Maximum processor state" to 99 %, which turns
  turbo off (about 1.8 GHz). It is the one CPU limit Windows still obeys in **Performance**, so in GPU-bound games the
  watts the CPU gives up can go to the GPU. CPU-heavy games may lose FPS, so it's per profile and off by default.
- Your own power-plan value is restored when you switch to a profile without it, quit, crash (at the next start) or end the task
  (the guard).
- The main window, the overlay's PWR row, recordings (`cpu_boost_off`) and run summaries show when it is on.
- Not yet measured in a game: see the hardware test plan.

### Windows power mode, made visible
- The Windows power mode sets the sustained power budget the CPU and GPU share (about 15 W in Efficiency, 25 W in Balanced and
  30–33 W in Performance on BIOS 03.04). It matters more than the profile's name. The main window now shows the mode in effect,
  under the profile buttons, with its budget.
- FW-Helper follows Windows' own power mode notifications (no polling). If something else changes the mode (Windows Settings,
  another app), the new **Keep Windows in step with the profile** option (Fans + Power, on by default) puts the profile's mode back,
  and a tray notification says so. With it off, the line turns amber and offers **Fix**. Battery Saver is shown, never overridden,
  and when it turns off the profile's mode comes back.
- Fans + Power explains each power mode under its buttons. The tray tooltip, the overlay (new PWR row) and recordings (new
  `win_mode` column) show the Windows mode too.

### Run summaries
- A recorded session can now be summarised. **Monitor → Open session… → Summary** writes a report next to the CSV
  (`session-*.html`) and opens it in the browser. When a recording stops, a tray notification offers the same.
- The report covers only the seconds in which the game was presenting frames. It skips the first 10 s and leaves out menus and
  loading screens, and says which seconds it used.
- **Tiles:** median and 5th-percentile FPS, package watts split into CPU, GPU and the rest, FPS per watt, what held the GPU
  clock back, the GPU clock and the CPU temperature.
- **Charts:** second-by-second charts in which hovering shows every reading at that second, and where the run's watts went
  against the PL1 for its Windows power mode.
- **Tables:** every reading's average, median, 5th percentile, min and max.
- It warns about runs that are hard to compare: too short, partly on battery, or with the profile or Windows mode changed
  part-way through.
- The report is one file that works offline and can be kept or shared. Open session… also accepts a report and opens its
  recording.
- **Name a recording when it starts:** Monitor → Record asks for an optional name (game, settings, what you changed). It
  suggests the game that's running and the profile. The name goes into the file name
  (`session-20261107-140512 Cyberpunk 1080p medium.csv`) and becomes the summary's title label. Cancel doesn't record.
- A file renamed after its timestamp (`session-20261104-172058cyber_punk_bal.csv`) uses the extra text as the run's label.

### Record from inside a game
- **Ctrl+Shift+F6** starts and stops recording from anywhere, including in a game. The tray menu has the same item
  (Start recording / Stop recording). It doesn't ask for a name: the file is named after the game presenting frames (when
  telemetry is already running, e.g. with the overlay on) and the profile, such as `session-… Cyberpunk2077 Balanced.csv`.
- A toast says "● Recording" or "■ Recording saved", and the overlay shows a red **REC 3:12** row on top while recording.
  The "Recording saved" notification still offers the summary.

### Compare runs
- **Monitor → Compare…** puts several recordings side by side. Add runs (pick several at once), name each one, and
  choose the baseline with its round button. **Save and open report** saves the project to
  `Documents\FW Benchmark\projects\<name>.json` and opens `<name>.html` in the browser. Pick the project from the list,
  or open its `.json` from Open session…, to add runs or remake the report. When a session is open in the Monitor,
  Compare… starts with it.
- **The report:**
  - tiles for the fastest run (and its gain over the baseline), what held its GPU clock, and the most efficient run;
  - a line per run with the numbers ("+20 % FPS (43 → 51), +5.5 W package, FPS per watt −2 %");
  - bars for median and 5th-percentile FPS, the CPU/GPU/rest watts against each run's PL1, and FPS per watt;
  - **What differs:** a table of only the recorded settings that differ (profile, Windows mode, fan, power source, CPU boost,
    power target, CPU cap), with changes from the baseline highlighted;
  - a run-by-run table with the change from the baseline.
- It warns when runs don't compare: different games, AC against battery, very different lengths, runs under 60 s, or
  recordings from different days (firmware or drivers may have changed in between).
- Each run uses the same steady seconds as its run summary.

### Windows on two displays at different scales
- Windows opened on a display with a different scale from the main one (the laptop panel at 200 % next to an external screen at
  150 %) came out 25 % too small, and the Monitor and Battery windows then saved the smaller size each time. Windows now rescale
  when they open, and open at exactly their saved size.
- Unplugging the external screen could leave a FW-Helper window stranded. Windows minimises the windows that were on that
  screen, and they came back at their old position on a screen that no longer exists. Fans + Power has no taskbar button, so it
  stayed minimised for good.
- Now, after any display change, FW-Helper un-minimises windows that have no taskbar button. It brings back any window whose title
  bar isn't on a screen, and the main window restores before moving to the tray corner.
- Fans + Power opens beside the main window on the same screen, also when that screen is left of the main one. The overlay
  keeps its position on such a screen too.

### Keyboard backlight state
- Under the Keyboard slider, a line shows what the EC is actually doing: on or off, its level, and the ambient light in lux.
- The Framework EC has an **Auto** mode, which Fn+Space enters after High. In Auto the EC turns the backlight off above 5 lux
  (most rooms) and overrides the slider. When a slider change is overridden like this, the line says so and points to
  Fn+Space, which is the only way out of Auto. There is no idle timeout to set: the EC has none.
- The slider follows Fn+Space level changes, so startup no longer puts back an old level.

### Monitor and Battery windows
- Both windows can now be resized and maximized. They open wider than before (Monitor 1080 × 820, Battery 900 × 880, kept
  inside the screen) and remember their size (`monitor_size`, `battery_size` in config).
- The text grows with the window, up to 1.5×. In the Monitor window, each chart's text follows the size of its card. In the
  Battery window, the whole page zooms with the window width, as long as the history table still has room for about 8 rows.
  Extra height goes to the table.
- A chart's latest values no longer run into its title in a narrow card (Power, with five values). The note is dropped first,
  then the values' text shrinks.
- The Monitor's buttons no longer leave a gap where the hidden "Live" button is.

## 0.3.0 (2026-10-11)

**Verified on the target laptop:** the battery log (percent steps, plug and unplug, screen on/off), the import of Windows' standby
records, and `--battery`. **Not yet verified:** changing "Sleep after" from the Battery window and the standby split it produces,
and the idle-cost comparison. Everything listed as unverified in 0.2.0 still is. See the [hardware test plan](docs/hardware-test-plan.md).

### Battery monitor (ADR 0018)
- A **Battery** window, one click from the main window or the tray menu, refreshes every 5 s while open. It shows:
  - time on battery, energy used, average draw with the screen on and off, and an estimate of the time left;
  - what the battery was last charged to;
  - health and cycles.
- It has a charge chart for 24 h or 7 days, with on-AC and screen-off stretches shaded.
- The history table is kept as 30 days in detail, then monthly totals for a year, then yearly totals.
- `FwHelper.exe --battery` prints the same summary, even when FW-Helper isn't running. The tray tooltip shows the time on battery.
- **It's built to cost nothing measurable.** There's no polling: Windows reports each 1 % step, each plug or unplug, and each
  screen on/off. One low-priority efficiency-mode (EcoQoS) thread then logs one line. While on battery with the screen on, it also
  logs every 10 minutes.
- **Standby split:** screen-off time is split into *awake* (the screen is off but Windows hasn't slept yet, about 1–1.7 W here)
  and *asleep* (about 0.33 W), with deep-idle %. The data comes from Windows' own record of each standby session, including the
  ones from before the update.
- **Sleep after the screen turns off** (on battery) can be set in the Battery window: 5 min to 2 h, or never. It is Windows'
  own setting, so Windows Settings shows the same value. Closing the lid always sleeps at once.
- Not yet verified on hardware: see the hardware test plan.

## 0.2.0 (2026-10-07)

**Verified on the target laptop:**
- EC access, sensors, charge limit, modes and refresh rate;
- the fan guard after a forced kill, and the CLI;
- Energy Meter power readings, FPS capture without admin, and the idle cost.

**Not yet verified on hardware:**
- custom fan curves under sustained load, and the fan watchdog;
- whether the CPU frequency cap binds, which the power target and temperature cap depend on;
- the EC temperature backstop.

The custom fan curve, power target and temperature cap are all **off by default**. See the [hardware test plan](docs/hardware-test-plan.md).

### Fixes
- FPS capture now filters events in the kernel, so the overlay no longer adds 1–1.5 W at idle.
- The release is a single exe again: TraceEvent's unused native support files are left out.

### Fan safety (ADR 0009)
- The fan curve runs at 1 Hz, with 2 °C hysteresis and a gentler ramp-down. It never asks for a duty in the 1–11 % band, where the fan stalls.
- **Battery guard:** from 42 °C at the battery the fan runs at 12 % or more, rising to 100 % at 48 °C, whatever the curve says.
- When the CPU reaches Tjmax (100 °C), the fan goes back to the EC.
- A watchdog hands the fan back to the EC if the control loop stalls for 5 s.
- A guard process (`FwHelper.exe --guard`) hands the fan back if FW-Helper is killed forcibly.
- The fan status line shows when an override is active ("battery guard", "CPU ≥95°C", "EC: …").

### Never quieter than the EC (ADR 0013)
- The fan floor is learned from what the EC runs the fan at by itself, and applied on top of any custom curve. It is on by default and can be switched off in Fans + Power.
- The fan curve follows PECI only. If PECI is lost, the fan goes back to the EC instead of following a board sensor.
- `--selftest` also reports the EC's own duty, who owns the fan, and each sensor's EC thermal thresholds.

### Power & temperature limits, without a driver (ADR 0016)
- **Power target per profile** (8–35 W): a governor holds the package at that average by lowering the CPU's maximum frequency in the Windows
  power plan. It limits the CPU cores; GPU power isn't capped.
- **Temperature cap** (70–95 °C, all profiles): the same governor holds the CPU package temperature. An optional **EC backstop** makes the EC
  hard-throttle the CPU 5 °C above the cap.
- **PawnIO has been removed.** Nothing needs admin rights or a third-party driver. The EC owns PL1/PL2 on this laptop (35/60 W) and can't be overridden from Windows.
- The original power-plan values and EC thresholds are always restored: when you quit, after a crash or a forced kill (by the guard), and at the next start.
- Undervolting isn't supported: Intel locks it on this CPU.

### Monitoring (ADR 0011, 0017)
- New **Monitor** window: live charts of CPU/GPU load, CPU clock (and the governor's cap), package/CPU/GPU/DRAM watts, temperatures, fan,
  memory, **FPS** and battery. No admin needed.
- Sessions can be recorded to CSV and opened again later. Columns are read by name, so older recordings still open.

### Profiles (ADR 0012)
- You can create **your own profiles** next to Silent/Balanced/Turbo, and rename or delete them.
  Switch to them from the main window header, the tray menu, or `Ctrl+Shift+F5`.
- **Named fan curves:** save a curve under a name and load it into any profile.
- Settings from 0.1.0 carry over unchanged.

### Command line and overlay (ADR 0015)
- `FwHelper.exe --status`, `--profiles`, `--mode <name|id>`, `--charge-limit <50-100>`, `--fan-floor on|off`. These control the running app over a
  named pipe that only your own account can open. `--status` also works without the app running.
- **Overlay** (tray → Overlay, ADR 0017): FPS and the game's name, package °C, CPU/GPU load and watts, power against the limit, RAM, GPU memory and battery.
  It stays on top of windowed and borderless games without taking focus.

### Tooling
- `--hwtest fansweep|watchdog|governor|all` runs targeted hardware experiments.
- CI builds with warnings as errors, runs 155 unit tests (including the fan loop against a fake EC) and publishes the exe on every push. Pushing a tag creates a draft release.
- Project documentation: `CLAUDE.md`, ADRs 0001–0017, the hardware baseline, the feature comparison with the Linux version, and the test plan.

## 0.1.0 (2026-10-03)

First version: Silent/Balanced/Turbo modes (Windows power mode, optional custom fan curve, optional PL1/PL2 via PawnIO),
charge limit, refresh rate, keyboard backlight and power LED, tray icon, autostart.
