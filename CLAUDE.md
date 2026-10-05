# CLAUDE.md

Guidance for Claude Code working in this repository. **This is the project's living context file.
Update "Current state" and "Resume here" at the end of every working session, and whenever a
fact in here stops being true.**

## What this is

`FW-Helper` for Windows is a G-Helper-style tray app for the **Framework Laptop 13 Pro**. It handles
performance modes, custom fan curves, battery charge limit, refresh rate, keyboard/power LED, and
a driverless power/temperature governor, monitoring, FPS overlay and a CLI. It is derived from [G-Helper](https://github.com/seerge/g-helper) and is GPL-3.0.

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
| `CHANGELOG.md` | Per-version changes; release notes link here |

## Current state (2026-10-05)

Version **0.2.0**, which is unreleased (see `CHANGELOG.md`). Builds with 0 warnings, 155 unit tests pass, and CI is green. `publish/` is gitignored.
The user is running **0.2.0** from `publish\` (republished 2026-10-05 13:19, with the pipe fixes). Before relaunching it, ask, or at
least say so: `--hwtest` and starting a second instance both close it. Read-only EC probes and CLI calls are always fine.

| Feature | Status | Evidence |
|---|---|---|
| EC connection (no admin) | ✅ verified | every log session, `admin=False` |
| Sensors: 5 temps, fan rpm, battery | ✅ verified | selftest 2026-10-03 |
| EC fan duty / ownership / thermal ramps (read-only) | ✅ verified | read-only probe 2026-10-05; hardware-baseline.md |
| Charge limit 50–100 % | ✅ set/readback; 🟡 actual charge stop not observed on Windows | 85 % held across app restarts |
| Keyboard backlight, power LED | ✅ verified | selftest |
| Modes → Windows power overlay, AC/DC memory | ✅ verified | many plug/unplug switches in the log |
| Refresh rate 60/120/auto | ✅ verified | log |
| Fixed fan duty + hand back to EC | ✅ verified | selftest: 100 % → 7281 rpm, 20 % → 2635 rpm, auto → 0 |
| **Custom fan curve loop** (1 Hz, hysteresis, stall band, battery guard, Tjmax release, PECI-only input) | 🟡 unit-tested against a fake EC; **not run on hardware** since the rewrite | `FanLoopTests`, `FanControllerTests` |
| Fan watchdog (5 s) | 🟡 unit-tested against a fake EC | `FanLoopTests` |
| Learned EC floor ("never quieter than the EC") | 🟡 unit-tested; learning not yet seen live | ADR 0013 |
| Guardian process (hard-kill fan restore) | ✅ verified with `Stop-Process -Force` on a dummy parent and on the real app (2026-10-05) | log 2026-10-04 21:57; about 28 MB working set |
| Release fan on suspend / reapply on resume | 🟡 code only | no Suspend/Resume entry in the log yet |
| **Power target / temperature cap governor** (frequency cap via power plan, Energy Meter; ADR 0016) | 🟡 unit-tested against a fake; reads and a no-op cap write verified; **binding not yet verified** | `--hwtest governor` |
| EC temperature backstop (`0x0050` PROCHOT threshold) | 🟡 code + pure tests; config read verified, write not yet run | ADR 0016 |
| CPU/GPU/DRAM watts (Energy Meter) + EC throttle status (`0x3E22`) | ✅ verified without admin | ADR 0016 |
| FPS (ETW present events) + overlay | ✅ FPS verified without admin (Dark Souls III, 60 FPS); overlay rendered offscreen | ADR 0017 |
| Hardware test mode `--hwtest fansweep\|watchdog\|pl\|all` | 🟡 built, not yet run | `docs/hardware-test-plan.md` |
| CI (GitHub Actions: build `-warnaserror`, test, publish artifact) | ✅ green | `.github/workflows/ci.yml` |
| Autostart (Task Scheduler) | 🟡 not confirmed | |
| Monitor window (6 live charts) + CSV session recording | 🟡 built. PDH counters checked on this machine; window rendered offscreen; not yet used live | ADR 0011 |
| User profiles (ids 3+) + named fan-curve library | 🟡 built and unit-tested (pure parts). Window rendered offscreen; not yet used live | ADR 0012 |
| CLI (`--status` etc. over a named pipe) + overlay window | ✅ CLI verified against the running app (8 back-to-back calls); 🟡 overlay not yet used live | ADR 0015 |
| Unit tests | ✅ 155: fan loop (fake EC), controller, floor, curve, governor (policy + loop on a fake), backstop, FPS, telemetry, profiles, CLI, overlay rows | `dotnet test tests/FwHelper.Tests` |

### Resume here

2026-10-04: docs were set up (ADRs 0001–0008), then fan safety parity was built (ADR 0009).

2026-10-05: **The user's direction is to build as much as possible and run the hardware tests together later.** Don't stop to
test on hardware. Add each new check to `docs/hardware-test-plan.md`. Built today:
- CI and the draft-release workflow;
- `--hwtest`;
- the power-limit rework (ADR 0010);
- monitoring and recording (ADR 0011);
- PawnIO removed; driverless power target + temperature cap governor and EC backstop (ADR 0016); FPS + new overlay (ADR 0017);
- profiles and named curves (ADR 0012);
- the learned EC floor, a PECI-only control input and a testable `FanLoop` (ADR 0013);
- the proposed service split (ADR 0014);
- the CLI over a per-user named pipe, and the overlay window (ADR 0015).

**No tag has been pushed. Tag `v0.2.0` only after the hardware test plan has passed.**

**Next:** the user runs `docs/hardware-test-plan.md` in one session. Go through the results with them, fix what fails, record the facts
([W] tags in hardware-baseline.md), settle open questions 1 and 2 and ADR 0014, then tag. Remaining gaps are listed in
`docs/feature-parity.md` → Roadmap. The user has not yet given their feedback on v0.1.0.

Open questions:
1. Fan scale: Windows 100 % → 7.3k rpm, but Linux full duty → about 5.2k. Which is right? `--hwtest fansweep`
2. ~~Does MSR `0x610` bind PL1?~~ Moot: PawnIO removed (ADR 0016); the EC owns PL1/PL2. New question: **does a power-plan frequency cap (`PROCFREQMAX`) bind?** `--hwtest governor`
3. ~~Can the EC's own auto-mode duty be read on Windows?~~ **Yes** (`0x0027`, 2026-10-05). The floor is built (ADR 0013).

## Layout

```
src/
  Program.cs              entry point, tray, power/session events, SafeShutdown
  Hardware/
    CrosEc.cs             raw IOCTL transport to \\.\GLOBALROOT\Device\CrosEC
    FrameworkEc.cs        EC features: sensors, fan, charge limit, battery, LEDs
    Pdh.cs                performance counter (PDH) P/Invoke, English paths, wildcard arrays
    SystemMetrics.cs      CPU utility/effective MHz, GPU busiest engine + shared mem, RAM
    EnergyMeter.cs        RAPL watts from Windows Energy Meter counters (no driver)
    PowerPlan.cs          CPU frequency caps in the active power plan (PROCFREQMAX/1, AC+DC)
    PresentMonitor.cs     real-time ETW session on present events → FpsCounter
  Features/
    Modes.cs              profiles: built-ins 0–2, user ids 3+; per-profile config accessors; ProfileList (pure helpers)
    ModeControl.cs        apply a profile; AC/DC memory; Ctrl+Shift+F5 cycle
    FanCurveLibrary.cs    named curves in one config string
    FanController.cs      pure per-tick fan decision: hysteresis, ramp, stall band, CPU/battery overrides (no I/O, unit-tested)
    FanLoop.cs            1 Hz loop + watchdog threads, floor learning; hardware only via IFanHardware (FanHardware.cs)
    FanControl.cs         static front: the app's FanLoop on the real EC, floor persistence
    FirmwareFloor.cs      learned "never quieter than the EC" floor (pure)
    FanCurve.cs           8-point curve, parse/normalize/interpolate
    GovernorPolicy.cs     pure power/temperature governor (frequency cap)
    Governor.cs           1 Hz loop over IGovernorHardware; GovernorControl.cs: settings, save/restore of plan caps
    EcBackstop.cs         EC PROCHOT threshold (read-modify-write, firmware config saved per EC version)
    FpsCounter.cs         pure FPS per process from present timestamps
    Telemetry.cs          1 Hz on-demand sampler (ref-counted Use()), 300-sample history, recording
    TelemetrySample.cs    one row + CSV format (readers map columns by header name; only append columns)
    SessionRecorder.cs    sessions\session-*.csv writer/loader, prune to 20, 12 h auto-stop
    PowerNative.cs        Windows power overlay (powrprof)
    BatteryControl.cs     charge limit persistence/reapply
    ScreenControl.cs      internal panel refresh rate (from G-Helper)
  Helpers/                AppConfig (JSON), Logger, Startup (Task Scheduler), ProcessHelper, SelfTest, Guardian (--guard), HardwareTests (--hwtest),
                          Cli + CommandProtocol (pure) + CommandServer (named pipe, runs commands on the UI thread)
  UI/                     SettingsForm (main), FansForm (Fans + Power, profile picker), MonitorForm + LineChart, OverlayForm, FanCurveEditor, ProfileMenu, PromptForm,
                          RForm/RButton/Slider (G-Helper), ToastForm, TrayIcons
tests/
  FwHelper.Tests/         xunit, no hardware: FakeFan (fake EC) drives FanLoop; never touches AppConfig
```

## Commands

```powershell
cd src
dotnet build -c Release
# publishing to ..\publish fails while FW-Helper is running from there (exe locked): quit it first
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\publish
..\publish\FwHelper.exe --selftest     # hardware test → %AppData%\FwHelper\selftest.txt (moves the fan!)
..\publish\FwHelper.exe --status | Out-String   # CLI (see README); never replaces the running app
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
7. **Nothing may need admin or a third-party driver** (ADR 0016, the user's requirement). No PawnIO, WinRing0 or similar.
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
- **The EC owns PL1/PL2** (35/60 W, PECI) and no host command changes them, so the power target slider stops at 35 W. The governor caps *frequency* and must always restore the plan values (`gov_saved_caps` in config) on every exit path, including from the guard.
- **The backstop rewrites the whole EC thermal config** for PECI. Always build it from the saved firmware config (`ec_backstop_orig`, tied to the EC version), never from scratch.
- **Stiction:** the fan does not spin below about 8–12 % duty. `FanController` never requests 1–11 %.
- Releasing to the EC is *quieter* than manual 100 %, not the same thing. The EC curve tops out at about 3.2k rpm.
- PECI reads in about 1 °C steps with ±1 °C jitter. Any threshold without hysteresis will flap.
- A process started from the autostart task has cwd = System32, which is how it decides to start hidden.
- **UI can't be checked by launching the app here.** Starting a second instance closes the user's running one. To look at a form,
  render it offscreen from a throwaway xunit test (STA thread, `Location = (-4000,-4000)`, `Show()`, `DrawToBitmap`) into the scratchpad, then delete the test.
- **Unit tests must never touch `AppConfig`.** It reads and writes the user's real `%AppData%\FwHelper\config.json`. Keep the logic
  pure (`ProfileList`, `FanController`, `PowerLimitKeeper`...) and test that.
- A disabled `RButton` used to draw its text twice (fixed 2026-10-05 in `OnPaint`). The profile buttons are hidden rather than disabled anyway.
- **Argument order in `Program.Main` matters:** `--guard`, then CLI verbs, then `--selftest`/`--hwtest`, then `CloseOtherInstances`.
  A new argument placed after `CloseOtherInstances` will kill the user's running app.
- The command pipe is an input: any new verb must be validated again in `CommandServer`, not only in `CommandProtocol.Parse`.
- Profile ids are config keys. Never renumber them, and never reuse a deleted one (ADR 0012).
- Each consumer of Energy Meter or PDH keeps its own query (the governor, telemetry and hwtest each have one).
- The FPS ETW session runs only while telemetry is in use. A crashed run's session (`FwHelper-Presents`) is taken over by name.
- **Always filter ETW providers by event id** (`Only(...)` in PresentMonitor). DxgKrnl unfiltered is about 4,900 events/s at idle: +1–1.5 W.
- **Idle cost matters, because the user watches CPU watts.** Measure FW-Helper's CPU (`TotalProcessorTime` over 10–20 s) after adding anything that runs periodically.
  Target: under 0.5 % of one core at idle, with the overlay open or closed.

## Conventions

- The style follows G-Helper: static feature classes, `AppConfig` flat keys (`<name>_<mode>` for per-mode values),
  `Logger.WriteLine` for every hardware write together with its result.
- Nullable enabled, implicit usings, block-scoped `namespace FwHelper.<Folder> { }` (not file-scoped).
- Commit messages are imperative, and say *why* in the body.
