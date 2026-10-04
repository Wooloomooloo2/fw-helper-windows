# CLAUDE.md

Guidance for Claude Code working in this repository. **This is the project's living context file.
Update "Current state" and "Resume here" at the end of every working session, and whenever a
fact in here stops being true.**

## What this is

`FW-Helper` for Windows is a G-Helper-style tray app for the **Framework Laptop 13 Pro**. It handles
performance modes, custom fan curves, battery charge limit, refresh rate, keyboard/power LED, and
(experimental) CPU PL1/PL2. It is derived from [G-Helper](https://github.com/seerge/g-helper) and is GPL-3.0.

Target machine: Framework Laptop 13 Pro, Intel Core Ultra X7 358H, board `FRANMJCP07`, BIOS 03.02,
EC `sakura-3.0.2`, Windows 11 Pro.

Linux sibling: [Wooloomooloo2/fw-helper](https://github.com/Wooloomooloo2/fw-helper) (Rust/GTK4, v0.6.4).
It is **ahead on features and on hardware verification**. Read its findings before re-measuring
anything. See [docs/feature-parity.md](docs/feature-parity.md).

## Docs map

| File | Purpose |
|---|---|
| `CLAUDE.md` | This file: state, how to resume, rules, traps |
| [docs/adr/](docs/adr/README.md) | Architecture decisions (MADR, numbered, never renumbered) |
| [docs/hardware-baseline.md](docs/hardware-baseline.md) | Measured hardware facts, each tagged [W] Windows / [L] Linux / [?] unverified |
| [docs/feature-parity.md](docs/feature-parity.md) | Linux vs Windows comparison, and the roadmap |
| [docs/references.md](docs/references.md) | External repos and specs we build on |
| `README.md` | User-facing |

## Current state (2026-10-04)

Version **0.1.0**, one commit (`596f0bf`). Builds with 0 warnings. Unreleased: `publish/` holds a
single-file exe and a zip, and is gitignored.

| Feature | Status | Evidence |
|---|---|---|
| EC connection (no admin) | ✅ verified | every log session, `admin=False` |
| Sensors: 5 temps, fan rpm, battery | ✅ verified | selftest 2026-10-03 |
| Charge limit 50–100 % | ✅ set/readback; 🟡 actual charge stop not observed on Windows | 85 % held across app restarts |
| Keyboard backlight, power LED | ✅ verified | selftest |
| Modes → Windows power overlay, AC/DC memory | ✅ verified | many plug/unplug switches in the log |
| Refresh rate 60/120/auto | ✅ verified | log |
| Fixed fan duty + hand back to EC | ✅ verified | selftest: 100 % → 7281 rpm, 20 % → 2635 rpm, auto → 0 |
| **Custom fan curve loop** | 🟡 **barely exercised** (2 × ~5 s) | no mode has `fan_custom` on in the user's config |
| Release fan on suspend / reapply on resume | 🟡 code only | no Suspend/Resume entry in the log yet |
| **PL1/PL2 via PawnIO** | 🟡 **never run** | "Requires admin", then "PawnIO not installed" |
| Autostart (Task Scheduler) | 🟡 not confirmed | |
| Tests / CI | ❌ none | `--selftest` is the only check (needs the real hardware) |

### Resume here

Documentation was set up on 2026-10-04 (this file, ADRs 0001–0008, hardware baseline, parity, references).
**No code has changed since v0.1.0.** The user has seen the status review and is about to give feedback on
v0.1.0. Ask for or apply that feedback first. Otherwise, follow the roadmap in `docs/feature-parity.md`, which begins with
fan safety parity (battery guard, hysteresis, stiction band, watchdog).

Open questions:
1. Fan scale: Windows 100 % → 7.3k rpm, but Linux full duty → about 5.2k. Which is right?
2. Does a PawnIO write to MSR `0x610` bind PL1 at all? Linux says the MMIO/MCHBAR copy governs.
3. Can the EC's own auto-mode duty be read on Windows? This is needed for a learned firmware floor (Linux ADR 0011).

## Layout

```
src/
  Program.cs              entry point, tray, power/session events, SafeShutdown
  Hardware/
    CrosEc.cs             raw IOCTL transport to \\.\GLOBALROOT\Device\CrosEC
    FrameworkEc.cs        EC features: sensors, fan, charge limit, battery, LEDs
    PawnIOWrapper.cs      PawnIO driver client (from G-Helper)
    IntelPowerLimits.cs   PL1/PL2 + package energy via MSRs
    IntelMSR.bin          embedded PawnIO module
  Features/
    Modes.cs              mode ids + per-mode config accessors (overlay, fan, PL)
    ModeControl.cs        apply a mode; AC/DC memory; Ctrl+Shift+F5 cycle
    FanControl.cs         curve loop (2 s timer), safety, hand-back
    FanCurve.cs           8-point curve, parse/normalize/interpolate
    PowerNative.cs        Windows power overlay (powrprof)
    BatteryControl.cs     charge limit persistence/reapply
    ScreenControl.cs      internal panel refresh rate (from G-Helper)
  Helpers/                AppConfig (JSON), Logger, Startup (Task Scheduler), ProcessHelper, SelfTest
  UI/                     SettingsForm (main), FansForm (Fans + Power), FanCurveEditor, RForm/RButton/Slider (G-Helper), ToastForm, TrayIcons
```

## Commands

```powershell
cd src
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\publish
..\publish\FwHelper.exe --selftest     # hardware test → %AppData%\FwHelper\selftest.txt (moves the fan!)
```

Runtime files are in `%AppData%\FwHelper\`: `config.json`, `log.txt`, `selftest.txt`. **The log is the
evidence of what has been verified on hardware.** Read it before claiming a feature works.

## Hard rules

1. **Fan control is a lease.** Every path that stops the curve must call `FrameworkEc.SetFanAuto()`.
   Never add a code path that writes a duty without a matching hand-back (ADR 0005, Linux ADR 0006).
2. **Recovery reads hardware, not flags.** `FanControl.Stop()` sends auto even when `IsCustomActive` is false. Keep it that way.
3. **Read-back is not efficacy.** A register that reads back the value you wrote has not proved the feature works. Verify the effect
   (charging stops, sustained watts over more than 32 s, rpm).
4. **EC opcodes come from real source** (`ec_commands.h`, framework-system, Framework EC), never from summaries.
5. Keep the CrosEC command buffer at exactly **248 bytes**.
6. No undervolting (locked on this CPU anyway). Power limits only.
7. Core features must keep working **without admin**. Only PL1/PL2 may need elevation.
8. Code adapted from G-Helper stays GPL-3.0 and says so in its header comment.
9. Record new decisions as ADRs, and new hardware facts in `docs/hardware-baseline.md` with a [W]/[L]/[?] tag.

## Traps

- **Hard kill leaves the fan stuck.** If the process is killed by Task Manager or `taskkill /f`, the last duty stays until the next start or reboot.
  `CloseOtherInstances` will `Kill()` an old instance that doesn't close within 1.5 s. The new instance recovers it straight away.
- **`--selftest` changes real state.** It sets the fan to 100 % and 20 %, and writes back the charge limit.
- **Charge-limit wire order is `[mode, max, min]`.** Swapping them sets a minimum instead.
- **The charge limit resets on reboot** (Linux finding). `BatteryControl.AutoLimit()` at startup is what keeps it set.
- **Power overlay changes are skipped while Battery Saver is on**, by design.
- **Firmware rewrites PL1 after a profile change.** Any PL write that comes before an overlay switch is likely to be lost.
- **PL1 ceiling is about 35 W** on this board. The default Turbo PL1 of 45 W cannot be reached.
- **Stiction:** the fan does not spin below about 8–12 % duty.
- Releasing to the EC is *quieter* than manual 100 %, not the same thing. The EC curve tops out at about 3.2k rpm.
- PECI reads in about 1 °C steps with ±1 °C jitter. Any threshold without hysteresis will flap.
- A process started from the autostart task has cwd = System32, which is how it decides to start hidden.

## Conventions

- The style follows G-Helper: static feature classes, `AppConfig` flat keys (`<name>_<mode>` for per-mode values),
  `Logger.WriteLine` for every hardware write together with its result.
- Nullable enabled, implicit usings, block-scoped `namespace FwHelper.<Folder> { }` (not file-scoped).
- Commit messages are imperative, and say *why* in the body.
