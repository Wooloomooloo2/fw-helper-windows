# External references

These are the repositories and documents this project is based on, adapts code from, or compares itself against.
When code is adapted from one of them, the source file's header comment should name it.

## Sibling and parent projects

| Project | Relationship | What to look at |
|---|---|---|
| [Wooloomooloo2/fw-helper](https://github.com/Wooloomooloo2/fw-helper) | **The Linux version of this app** (Rust, GTK4, privileged daemon). Same laptop. It is ahead on features and on hardware verification | `CLAUDE.md` ("Traps", "Reference numbers"), `docs/hardware-baseline.md`, `docs/adr/`, `docs/framework_gaming_profile.md`. See [feature-parity.md](feature-parity.md) |
| [seerge/g-helper](https://github.com/seerge/g-helper) | **Model and code source** (ASUS, C# WinForms, GPL-3.0) | UI controls (`RForm`, `RButton`, `Slider`), display refresh-rate code, `AppConfig`, Task Scheduler startup, mode/fan-curve UX |

## Framework hardware and firmware

| Project | Why |
|---|---|
| [FrameworkComputer/framework-system](https://github.com/FrameworkComputer/framework-system) | `framework_tool`. The reference for the Windows CrosEC driver interface (device path, IOCTL codes, buffer layout) and the Framework `0x3Exx` EC commands |
| [FrameworkComputer/EmbeddedController](https://github.com/FrameworkComputer/EmbeddedController) | Framework's EC firmware fork. Look here for the exact behaviour of fan control, charge limit and the LED commands |
| [ChromiumOS EC `ec_commands.h`](https://chromium.googlesource.com/chromiumos/platform/ec/+/HEAD/include/ec_commands.h) | The host command set and memory map layout (`EC_MEMMAP_*`, `EC_CMD_*`) |
| [Framework knowledge base](https://knowledgebase.frame.work/) | The driver bundle, which provides the CrosEC / `CrosEcBus` driver this app needs |

## Power, thermal and FPS

| Source | Why |
|---|---|
| FrameworkComputer/EmbeddedController, branch `fwk-sakura-20260429`: `zephyr/program/framework/sakura/src/cpu_power.c`, `src/cpu_power/intel_cpu_power_interface.c`, `common/thermal.c` | How the EC sets PL1/PL2/PL4/PsysPL2 itself (PECI) and why the host can't change them; PROCHOT on the HIGH threshold (ADR 0016) |
| Linux `drivers/hwmon/cros_ec_hwmon.c`, `include/linux/platform_data/cros_ec_commands.h` | Layouts of `PWM_GET_FAN_DUTY`, `THERMAL_AUTO_FAN_CTRL` v2, and `THERMAL_GET/SET_THRESHOLD` (ADR 0013/0016) |
| Windows Energy Metering Interface (`\Energy Meter(*)\Power` counters) | Driverless RAPL power: package, cores, GPU, DRAM |
| Windows power settings `PROCFREQMAX` / `PROCFREQMAX1` | Driverless CPU frequency cap, the governor's actuator |
| [GameTechDev/PresentMon](https://github.com/GameTechDev/PresentMon) `PresentData/ETW/*.h` | Provider GUIDs and present event ids used for FPS (ADR 0017) |
| [PawnIO](https://pawnio.eu) | *Historical:* used for MSR access until ADR 0016 removed it |

## Libraries

| Library | Use |
|---|---|
| [dahall/TaskScheduler](https://github.com/dahall/TaskScheduler) (NuGet `TaskScheduler` 2.12.2) | Autostart task |
| [microsoft/perfview TraceEvent](https://github.com/microsoft/perfview) (NuGet `Microsoft.Diagnostics.Tracing.TraceEvent` 3.2.8, MIT) | Real-time ETW session for FPS |
