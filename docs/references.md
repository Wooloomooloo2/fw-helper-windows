# External references

These are the repositories and documents this project is based on, adapts code from, or compares itself against.
When code is adapted from one of them, the source file's header comment should name it.

## Sibling and parent projects

| Project | Relationship | What to look at |
|---|---|---|
| [Wooloomooloo2/fw-helper](https://github.com/Wooloomooloo2/fw-helper) | **The Linux version of this app** (Rust, GTK4, privileged daemon). Same laptop. It is ahead on features and on hardware verification | `CLAUDE.md` ("Traps", "Reference numbers"), `docs/hardware-baseline.md`, `docs/adr/`, `docs/framework_gaming_profile.md`. See [feature-parity.md](feature-parity.md) |
| [seerge/g-helper](https://github.com/seerge/g-helper) | **Model and code source** (ASUS, C# WinForms, GPL-3.0) | UI controls (`RForm`, `RButton`, `Slider`), PawnIO wrapper, display refresh-rate code, `AppConfig`, Task Scheduler startup, mode/fan-curve UX |

## Framework hardware and firmware

| Project | Why |
|---|---|
| [FrameworkComputer/framework-system](https://github.com/FrameworkComputer/framework-system) | `framework_tool`. The reference for the Windows CrosEC driver interface (device path, IOCTL codes, buffer layout) and the Framework `0x3Exx` EC commands |
| [FrameworkComputer/EmbeddedController](https://github.com/FrameworkComputer/EmbeddedController) | Framework's EC firmware fork. Look here for the exact behaviour of fan control, charge limit and the LED commands |
| [ChromiumOS EC `ec_commands.h`](https://chromium.googlesource.com/chromiumos/platform/ec/+/HEAD/include/ec_commands.h) | The host command set and memory map layout (`EC_MEMMAP_*`, `EC_CMD_*`) |
| [Framework knowledge base](https://knowledgebase.frame.work/) | The driver bundle, which provides the CrosEC / `CrosEcBus` driver this app needs |

## Power limits

| Project | Why |
|---|---|
| [PawnIO](https://pawnio.eu) · [namazso/PawnIO](https://github.com/namazso/PawnIO) | The signed kernel driver used for MSR access (ADR 0006) |
| [namazso/PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) | Source of the `IntelMSR` module. Our `src/Hardware/IntelMSR.bin` is that module's compiled binary, as shipped by G-Helper |
| Intel SDM Vol. 4 (MSRs) | `MSR_RAPL_POWER_UNIT 0x606`, `MSR_PKG_POWER_LIMIT 0x610`, `MSR_PKG_ENERGY_STATUS 0x611` |

## Libraries

| Library | Use |
|---|---|
| [dahall/TaskScheduler](https://github.com/dahall/TaskScheduler) (NuGet `TaskScheduler` 2.12.2) | Autostart task |
