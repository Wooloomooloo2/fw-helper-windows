# Changelog

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
