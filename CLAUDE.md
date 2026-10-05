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
| [docs/hardware-test-plan.md](docs/hardware-test-plan.md) | **Every pending on-hardware check, bundled into one run.** Add new ones here instead of testing ad hoc |
| [docs/references.md](docs/references.md) | External repos and specs we build on |
| `README.md` | User-facing |

## Current state (2026-10-04)

Version **0.1.0**, plus the fan safety work (ADR 0009), which is not yet released. Builds with 0 warnings, and 20 unit tests pass.
`publish/` holds the v0.1.0 single-file exe and zip, and is gitignored.

| Feature | Status | Evidence |
|---|---|---|
| EC connection (no admin) | ✅ verified | every log session, `admin=False` |
| Sensors: 5 temps, fan rpm, battery | ✅ verified | selftest 2026-10-03 |
| Charge limit 50–100 % | ✅ set/readback; 🟡 actual charge stop not observed on Windows | 85 % held across app restarts |
| Keyboard backlight, power LED | ✅ verified | selftest |
| Modes → Windows power overlay, AC/DC memory | ✅ verified | many plug/unplug switches in the log |
| Refresh rate 60/120/auto | ✅ verified | log |
| Fixed fan duty + hand back to EC | ✅ verified | selftest: 100 % → 7281 rpm, 20 % → 2635 rpm, auto → 0 |
| **Custom fan curve loop** (1 Hz, hysteresis, stall band, battery guard, Tjmax release) | 🟡 unit-tested; **not run on hardware** since the rewrite | `tests/FwHelper.Tests` |
| Fan watchdog (5 s) | 🟡 code only | |
| Guardian process (hard-kill fan restore) | ✅ verified with `Stop-Process -Force` on a dummy parent | log 2026-10-04 21:57; about 28 MB working set |
| Release fan on suspend / reapply on resume | 🟡 code only | no Suspend/Resume entry in the log yet |
| **PL1/PL2 via PawnIO** | 🟡 **never run**. Defaults moved to the measured ceiling, re-asserted against firmware, original restored on exit (ADR 0010) | "Requires admin", then "PawnIO not installed". `--hwtest pl` is ready |
| Hardware test mode `--hwtest fansweep\|watchdog\|pl\|all` | 🟡 built, not yet run | `docs/hardware-test-plan.md` |
| CI (GitHub Actions: build `-warnaserror`, test, publish artifact) | ✅ green | `.github/workflows/ci.yml` |
| Autostart (Task Scheduler) | 🟡 not confirmed | |
| Monitor window (6 live charts) + CSV session recording | 🟡 built. PDH counters checked on this machine; window rendered offscreen; not yet used live | ADR 0011 |
| User profiles (ids 3+) + named fan-curve library | 🟡 built and unit-tested (pure parts). Window rendered offscreen; not yet used live | ADR 0012 |
| Unit tests | 🟡 FanController, FanCurve, PowerLimitKeeper, telemetry, profiles/curve library (83) | `dotnet test tests/FwHelper.Tests` |

### Resume here

2026-10-04: docs were set up (ADRs 0001–0008). Then roadmap item 1, fan safety parity, was built (ADR 0009):
`FanController` (pure, tested), a 1 Hz loop thread, a watchdog thread and the `--guard` guardian process. The UI now shows why
the fan is overriding the curve ("battery guard", "CPU ≥95°C", "EC: …").

2026-10-05: **The user's direction is to build as much as possible and run the hardware tests together later.** Don't stop to
test on hardware. Add each new check to `docs/hardware-test-plan.md`. Done so far today: CI and more unit tests, the
`--hwtest` mode (fansweep, watchdog, pl), the power-limit rework (ADR 0010), monitoring with recording (ADR 0011), and user
profiles with named fan curves (ADR 0012).

**Next on the build backlog:** release prep (version bump to 0.2.0, GitHub release with the exe). Leave the release unpublished
until the hardware test plan has passed. After that, run the bundled hardware test plan with the user.
The user has not yet given their feedback on v0.1.0.

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
    Pdh.cs                performance counter (PDH) P/Invoke, English paths, wildcard arrays
    SystemMetrics.cs      CPU utility/effective MHz, GPU busiest engine + shared mem, RAM
  Features/
    Modes.cs              profiles: built-ins 0–2, user ids 3+; per-profile config accessors; ProfileList (pure helpers)
    ModeControl.cs        apply a profile; AC/DC memory; Ctrl+Shift+F5 cycle
    FanCurveLibrary.cs    named curves in one config string
    FanController.cs      pure per-tick fan decision: hysteresis, ramp, stall band, CPU/battery overrides (no I/O, unit-tested)
    FanControl.cs         1 Hz loop thread + watchdog thread, EC writes, hand-back, Status for the UI
    FanCurve.cs           8-point curve, parse/normalize/interpolate
    PowerLimitControl.cs  per-mode PL apply, PowerLimitKeeper (pure re-assert policy), restore on exit
    Telemetry.cs          1 Hz on-demand sampler (ref-counted Use()), 300-sample history, recording
    TelemetrySample.cs    one row + CSV format (the header is the format version)
    SessionRecorder.cs    sessions\session-*.csv writer/loader, prune to 20, 12 h auto-stop
    PowerNative.cs        Windows power overlay (powrprof)
    BatteryControl.cs     charge limit persistence/reapply
    ScreenControl.cs      internal panel refresh rate (from G-Helper)
  Helpers/                AppConfig (JSON), Logger, Startup (Task Scheduler), ProcessHelper, SelfTest, Guardian (--guard), HardwareTests (--hwtest)
  UI/                     SettingsForm (main), FansForm (Fans + Power, profile picker), MonitorForm + LineChart, FanCurveEditor, ProfileMenu, PromptForm,
                          RForm/RButton/Slider (G-Helper), ToastForm, TrayIcons
tests/
  FwHelper.Tests/         xunit; FanControllerTests (safety rules + random-walk invariants)
```

## Commands

```powershell
cd src
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\publish
..\publish\FwHelper.exe --selftest     # hardware test → %AppData%\FwHelper\selftest.txt (moves the fan!)
..\publish\FwHelper.exe --hwtest all   # targeted hardware experiments (see docs/hardware-test-plan.md); closes and restarts the tray app
cd ..; dotnet test tests\FwHelper.Tests   # unit tests (no hardware needed)
```

Any change to fan logic goes in `FanController` together with a test. `FanControl` should stay a thin I/O shell.

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

- **Two `FwHelper.exe` processes are normal.** The second is the `--guard <pid>` guardian (ADR 0009). It covers a hard kill of the
  tray app, but not "End process tree" or the guardian being killed first. `CloseOtherInstances` kills both the old app and the old
  guardian. The new instance takes the fan back straight away.
- **The fan duty is rewritten every tick on purpose.** Anything else that releases the fan (an old guardian, `framework_tool`) is overridden within 1 s.
- **The battery guard holds 100 % at ≥48 °C; it does not release to the EC like Linux does.** The EC curve follows CPU temperature and would stop the fan. See ADR 0009.
- **`--selftest` changes real state.** It sets the fan to 100 % and 20 %, and writes back the charge limit.
- **Charge-limit wire order is `[mode, max, min]`.** Swapping them sets a minimum instead.
- **The charge limit resets on reboot** (Linux finding). `BatteryControl.AutoLimit()` at startup is what keeps it set.
- **Power overlay changes are skipped while Battery Saver is on**, by design.
- **Firmware rewrites PL1 after a profile change.** Any PL write that comes before an overlay switch is likely to be lost.
- **PL1 ceiling is about 35 W** on this board, so the sliders stop there (PL1 8–35, PL2 15–80). The first PL write captures the raw MSR value, which is restored when PL is turned off or the app exits.
- **Stiction:** the fan does not spin below about 8–12 % duty. `FanController` never requests 1–11 %.
- Releasing to the EC is *quieter* than manual 100 %, not the same thing. The EC curve tops out at about 3.2k rpm.
- PECI reads in about 1 °C steps with ±1 °C jitter. Any threshold without hysteresis will flap.
- A process started from the autostart task has cwd = System32, which is how it decides to start hidden.
- **UI can't be checked by launching the app here.** Starting a second instance closes the user's running one. To look at a form,
  render it offscreen from a throwaway xunit test (STA thread, `Location = (-4000,-4000)`, `Show()`, `DrawToBitmap`) into the scratchpad, then delete the test.
- **Unit tests must never touch `AppConfig`.** It reads and writes the user's real `%AppData%\FwHelper\config.json`. Keep the logic
  pure (`ProfileList`, `FanController`, `PowerLimitKeeper`...) and test that.
- **A disabled `RButton` draws its text twice.** Hide buttons that don't apply rather than disabling them, or fix `RButton.OnPaint`.
- Profile ids are config keys. Never renumber them, and never reuse a deleted one (ADR 0012).
- `IntelPowerLimits.GetPackagePower()` keeps one shared last-energy state. With two callers (Monitor and Fans + Power), each one's
  interval gets shorter. The readings are still correct averages.

## Conventions

- The style follows G-Helper: static feature classes, `AppConfig` flat keys (`<name>_<mode>` for per-mode values),
  `Logger.WriteLine` for every hardware write together with its result.
- Nullable enabled, implicit usings, block-scoped `namespace FwHelper.<Folder> { }` (not file-scoped).
- Commit messages are imperative, and say *why* in the body.
