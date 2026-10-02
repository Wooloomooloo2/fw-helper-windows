# FW-Helper

A lightweight tray app for the **Framework Laptop 13** (built and tested on the Framework Laptop 13 Pro, Intel Core Ultra Series 3),
modelled on [G-Helper](https://github.com/seerge/g-helper) for Asus laptops. It talks directly to the Framework embedded
controller through Framework's own CrosEC Windows driver, the same interface `framework_tool` uses, with no extra services
and no admin rights needed for the core features.

## Features

| Area | What it does | How |
|---|---|---|
| Performance modes | Silent / Balanced / Turbo, remembered separately for AC and battery, `Ctrl+Shift+F5` to cycle | Windows power mode (Intel DTT/IPF follows it), per-mode fan curve, optional PL1/PL2 |
| Fans | Custom 8-point fan curve per mode, or Framework EC automatic | EC `PWM_SET_FAN_DUTY` / `THERMAL_AUTO_FAN_CTRL` |
| Sensors | CPU (PECI), board, DDR, battery temps, fan RPM, battery W | EC memory map |
| Battery | Charge limit 50–100%, health, cycles | Framework EC `CHARGE_LIMIT_CONTROL` (0x3E03) |
| Display | 60Hz / max Hz / Auto (max on AC, 60Hz on battery) | `ChangeDisplaySettingsEx` |
| Lighting | Keyboard backlight, power button LED level | EC `PWM_SET_KEYBOARD_BACKLIGHT`, Framework `FP_LED_LEVEL_CONTROL` |
| CPU power limits | Experimental per-mode PL1/PL2 override | `MSR_PKG_POWER_LIMIT` via [PawnIO](https://pawnio.eu) (admin + PawnIO required) |
| Startup | Run at logon via Task Scheduler | |

### Fan safety
While a custom curve is active, the EC's own fan control is disabled. FW-Helper hands the fans back to the EC on quit,
crash, sign-out and sleep. Above 95°C the fan is forced to 100%. If temperatures can't be read, control goes back to the EC.
If the process is ever killed forcibly (Task Manager → End task) while a curve is active, the fan stays at its last duty
until FW-Helper starts again, a mode without a custom curve is selected, or the machine reboots.

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

`FwHelper.exe --selftest` writes a hardware report to `%AppData%\FwHelper\selftest.txt`.
Settings live in `%AppData%\FwHelper\config.json` and the log is in `%AppData%\FwHelper\log.txt`.

## EC notes (verified on EC `sakura-3.0.2`)
* Driver device: `\\.\GLOBALROOT\Device\CrosEC`, accessible without elevation
* `IOCTL_CROSEC_XCMD` = `0x80ECE004`, buffer must be exactly 248 bytes (20 byte header + 228 payload)
* `IOCTL_CROSEC_RDMEM` = `0x80EC6008`, buffer 8 + 256 bytes
* Temp sensors: `local_f75397`, `cpu_f75303`, `battery_temp`, `ddr_f75303`, `peci-temp`; single fan

## License
GPL-3.0. Portions (UI controls, PawnIO wrapper, display helpers) are adapted from G-Helper, which is GPL-3.0.
