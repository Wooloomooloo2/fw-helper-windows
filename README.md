# FW-Helper

A lightweight tray app for the **Framework Laptop 13** (built and tested on the Framework Laptop 13 Pro, Intel Core Ultra Series 3),
modelled on [G-Helper](https://github.com/seerge/g-helper) for Asus laptops. It talks directly to the Framework embedded
controller through Framework's own CrosEC Windows driver, the same interface `framework_tool` uses, with no extra services
and no admin rights needed for the core features.

## Features

| Area | What it does | How |
|---|---|---|
| Performance modes | Silent / Balanced / Turbo plus your own named profiles, remembered separately for AC and battery, `Ctrl+Shift+F5` to cycle | Windows power mode (Intel DTT/IPF follows it), per-mode fan curve, optional PL1/PL2 |
| Fans | Custom 8-point fan curve per profile, or Framework EC automatic; save and reuse named curves | EC `PWM_SET_FAN_DUTY` / `THERMAL_AUTO_FAN_CTRL` |
| Sensors | CPU (PECI), board, DDR, battery temps, fan RPM, battery W | EC memory map |
| Battery | Charge limit 50–100%, health, cycles | Framework EC `CHARGE_LIMIT_CONTROL` (0x3E03) |
| Display | 60Hz / max Hz / Auto (max on AC, 60Hz on battery) | `ChangeDisplaySettingsEx` |
| Lighting | Keyboard backlight, power button LED level | EC `PWM_SET_KEYBOARD_BACKLIGHT`, Framework `FP_LED_LEVEL_CONTROL` |
| Power & temperature limits | Per-profile package power target and a CPU temperature cap, plus an optional EC hard-throttle backstop. No driver, no admin | Windows Energy Meter + power-plan max CPU frequency; EC `THERMAL_SET_THRESHOLD` |
| Overlay | Always-on-top: FPS, package °C, CPU/GPU load and watts, power vs limit, RAM, GPU memory, battery | ETW present events (like PresentMon), Energy Meter, EC |
| Monitoring | Live charts: CPU/GPU load, CPU clock and cap, package/CPU/GPU/DRAM watts, temperatures, fan, memory, FPS, battery; CSV session recording | Performance counters (PDH), Windows Energy Meter, ETW, EC |
| Startup | Run at logon via Task Scheduler | |

### Fan safety
While a custom curve is active, the EC's own fan control is turned off, so FW-Helper treats the fan as borrowed:
* The fan is handed back to the EC when you quit, on a crash, at sign-out and before sleep.
* If FW-Helper is killed forcibly (Task Manager → End task), a small guard process hands the fan back. You will see a second
  `FwHelper.exe`; that is the guard.
* A watchdog hands the fan back if the control loop stops responding for 5 seconds.
* If the CPU temperature can't be read, the EC takes the fan back.
* The CPU at 95°C or above forces 100%. At 100°C the EC takes over.
* A warm battery (42°C and above, common while charging) raises the fan whatever the curve says. At 48°C it runs at 100%.
* The fan is never asked to run below about 12%, where it would stall.

The fan status line shows when one of these overrides is active.

## Requirements
* Framework Laptop 13 with the Framework driver bundle installed (provides the **Framework EC** / `CrosEcBus` driver)
* .NET 8 (or newer) Desktop Runtime

## Build

```powershell
cd src
dotnet build -c Release
# single file exe in publish\
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\publish
```

## Command line

```powershell
FwHelper.exe --status | Out-String        # mode, temperatures, fan (and who owns it), battery
FwHelper.exe --profiles | Out-String
FwHelper.exe --mode Turbo                 # by name or id; also your own profiles
FwHelper.exe --charge-limit 80
FwHelper.exe --fan-floor on               # never quieter than the EC's own fan control
```

Changes go to the running FW-Helper over a named pipe that only your own account can open. `--status` also works when it isn't running.

`FwHelper.exe --selftest` writes a hardware report to `%AppData%\FwHelper\selftest.txt`.
Settings live in `%AppData%\FwHelper\config.json` and the log is in `%AppData%\FwHelper\log.txt`.

## EC notes (verified on EC `sakura-3.0.2`)
* Driver device: `\\.\GLOBALROOT\Device\CrosEC`, accessible without elevation
* `IOCTL_CROSEC_XCMD` = `0x80ECE004`, buffer must be exactly 248 bytes (20 byte header + 228 payload)
* `IOCTL_CROSEC_RDMEM` = `0x80EC6008`, buffer 8 + 256 bytes
* Temp sensors: `local_f75397`, `cpu_f75303`, `battery_temp`, `ddr_f75303`, `peci-temp`; single fan

## Documentation
* [Architecture decisions](docs/adr/README.md)
* [Hardware baseline](docs/hardware-baseline.md): measured EC, sensor, fan and power facts
* [Feature parity with the Linux version](docs/feature-parity.md) ([fw-helper](https://github.com/Wooloomooloo2/fw-helper)) and the roadmap
* [External references](docs/references.md)

## License
GPL-3.0. Portions (UI controls, display helpers) are adapted from G-Helper, which is GPL-3.0.

Nothing needs admin rights or a third-party driver. Undervolting is not supported: Intel locks it on this CPU.
