# 0003 — Talk to the EC through Framework's CrosEC Windows driver

- **Status:** Accepted
- **Date:** 2026-10-03 (recorded retrospectively 2026-10-04)

## Context

Fans, charge limit, keyboard backlight, power LED, temperatures and battery data all live in
the Framework embedded controller (a ChromeOS EC derivative, `sakura-3.0.2` on this board). On
Windows there are three ways to reach it:

1. Raw port I/O to the EC's LPC/eSPI host interface. This needs a kernel driver (WinRing0/PawnIO),
   needs admin, and races the firmware and OS for the same ports.
2. ACPI/WMI methods. Framework exposes nothing useful for these features this way.
3. **Framework's own `CrosEcBus` driver**, which is part of the Framework driver bundle and is what
   [`framework_tool`](https://github.com/FrameworkComputer/framework-system) uses on Windows.

## Decision

Use (3). `Hardware/CrosEc.cs` opens `\\.\GLOBALROOT\Device\CrosEC` and uses two IOCTLs:

| IOCTL | Code | Buffer |
|---|---|---|
| `IOCTL_CROSEC_XCMD` (host command) | `0x80ECE004` | **exactly 248 bytes**: 20-byte header `{version, command, outsize, insize, result}` + 228 payload |
| `IOCTL_CROSEC_RDMEM` (memory map read) | `0x80EC6008` | 8 + 256 bytes: `{offset, bytes, data[256]}` |

`Hardware/FrameworkEc.cs` builds the features on top of this. It uses standard ChromeOS host commands
(`HELLO`, `GET_VERSION`, `PWM_*`, `THERMAL_AUTO_FAN_CTRL`, `TEMP_SENSOR_GET_INFO`) plus Framework's
`0x3Exx` extensions (`CHARGE_LIMIT_CONTROL 0x3E03`, `FP_LED_LEVEL_CONTROL 0x3E0E`).
`docs/hardware-baseline.md` has the full table.

On startup, `EC_CMD_HELLO` is the gate. If it fails, the app tells the user to install the
Framework driver bundle and exits.

## Consequences

- **No elevation** for any EC feature (verified: `admin=False` in every log session).
- The driver serialises access, so the app does not race `framework_tool` or firmware for the EC.
- Depends on Framework continuing to ship this driver. A Windows install without the bundle
  cannot run the app at all.
- The 248-byte buffer size is a hard driver requirement, found on hardware. The obvious
  "header + exact payload" size is rejected. Keep `CommandBufferSize` as it is.
- The EC exposes no RAPL/MSR access, so CPU power limits need a separate route (ADR 0006).
