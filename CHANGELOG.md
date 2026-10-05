# Changelog

## 0.2.0 (unreleased, waiting on the [hardware test plan](docs/hardware-test-plan.md))

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

### Power limits (ADR 0010)
- Defaults and slider ranges now follow the measured board limits: PL1 8–35 W, PL2 15–80 W, and defaults of 15/30, 25/60 and 35/64 W.
- If firmware overrides a limit after a power-mode change, it is re-applied, up to 5 times.
- The original limits are restored when the override is turned off or FW-Helper exits.

### Monitoring (ADR 0011)
- New **Monitor** window: live charts of CPU/GPU load, CPU clock, power, temperatures, fan and memory. No admin needed.
- Sessions can be recorded to CSV and opened again later. Columns are read by name, so older recordings still open.
- With PawnIO and admin: CPU and GPU watts, plus what is limiting the clocks (EDP, PL1, thermal...).


### Profiles (ADR 0012)
- You can create **your own profiles** next to Silent/Balanced/Turbo, and rename or delete them.
  Switch to them from the main window header, the tray menu, or `Ctrl+Shift+F5`.
- **Named fan curves:** save a curve under a name and load it into any profile.
- Settings from 0.1.0 carry over unchanged.

### Command line and overlay (ADR 0015)
- `FwHelper.exe --status`, `--profiles`, `--mode <name|id>`, `--charge-limit <50-100>`, `--fan-floor on|off`. These control the running app over a
  named pipe that only your own account can open. `--status` also works without the app running.
- **Overlay:** a small always-on-top readout of CPU, GPU, power and fan (tray → Overlay).

### Tooling
- `--hwtest fansweep|watchdog|pl|all` runs targeted hardware experiments.
- CI builds with warnings as errors, runs 144 unit tests (including the fan loop against a fake EC) and publishes the exe on every push. Pushing a tag creates a draft release.
- Project documentation: `CLAUDE.md`, ADRs 0001–0015, the hardware baseline, the feature comparison with the Linux version, and the test plan.

## 0.1.0 (2026-10-03)

First version: Silent/Balanced/Turbo modes (Windows power mode, optional custom fan curve, optional PL1/PL2 via PawnIO),
charge limit, refresh rate, keyboard backlight and power LED, tray icon, autostart.
