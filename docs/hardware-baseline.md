# Hardware baseline

These are facts about the target machine. Each one is marked by where it came from:

- **[W]** Measured on Windows by this app (`--selftest` / `log.txt`).
- **[L]** Measured by the Linux sibling. It does not depend on the OS, unless noted. Source:
  [fw-helper `docs/hardware-baseline.md`](https://github.com/Wooloomooloo2/fw-helper/blob/main/docs/hardware-baseline.md)
  and `CLAUDE.md` "Reference numbers", at commit `1e56f95` (2026-09-26).
- **[?]** Assumed or inferred. It has not been measured.

When you measure something on Windows that confirms or contradicts an [L] fact, record it here.

## Machine

| | |
|---|---|
| Model | Framework Laptop 13 Pro, board `FRANMJCP07`, BIOS 03.02 [L] |
| CPU | Intel Core Ultra X7 358H: 16 CPUs, no SMT. P-cores 0–3, E-cores 4–11, LP-E cores 12–15 [L] |
| GPU | Intel Arc B390 (integrated). rp0 2500 / rpe 900 / rpn 100 MHz [L] |
| EC | `sakura-3.0.2-cf48815` (Nuvoton npcx9m3f) [W][L] |
| Windows driver | Framework `CrosEcBus` → `\\.\GLOBALROOT\Device\CrosEC`, opens without admin rights [W] |

## CrosEC driver interface [W]

| | |
|---|---|
| `IOCTL_CROSEC_XCMD` | `0x80ECE004`. The buffer must be **exactly 248 bytes**: a 20-byte header `{u32 version, command, outsize, insize, result}` then 228 bytes of payload |
| `IOCTL_CROSEC_RDMEM` | `0x80EC6008`. Buffer `{u32 offset, u32 bytes, u8[256]}` |

## EC commands used

| Cmd | Name | Notes |
|---|---|---|
| `0x0001` | HELLO | `0xA0B0C0D0` → `+0x01020304`. Startup gate [W] |
| `0x0002` | GET_VERSION | 100-byte response. RO/RW string chosen by the image field at offset 96 [W] |
| `0x0020` | PWM_GET_FAN_TARGET_RPM | Reads 0 under manual control [L] |
| `0x0022/0x0023` | Keyboard backlight get/set | Percent [W] |
| `0x0024` | PWM_SET_FAN_DUTY | Percent, all fans. **Turns off EC auto.** In auto mode the EC stores whole percent [W][L] |
| `0x0052` | THERMAL_AUTO_FAN_CTRL | Gives the fan back to the EC. The EC takes control again within about 4 s [W][L] |
| `0x0070` | TEMP_SENSOR_GET_INFO | Sensor names [W] |
| `0x3E03` | CHARGE_LIMIT_CONTROL | `[mode, max, min]`. Modes: Disable 0x01, Set 0x02, Get 0x08, Override 0x80. **max comes before min.** Get returns `[max, min]`. (Not `0x3E07`) [W][L] |
| `0x3E0E` | FP_LED_LEVEL_CONTROL | v0 set level (High 0 … UltraLow 3); v1 get `{percent, level}` [W] |
| `0x3E22` | PROCHOT status | Read `0000` under load [L]. Not used yet |

Memory map offsets used: temperatures at `0x00`/`0x18` (K − 200 offset; `0xFF` = absent, `0xFC+` = error),
fans at `0x10` (`0xFFFF` = absent, `0xFFFE` = stalled), thermal version at `0x23`, battery block at `0x40–0x7F`.

## Temperature sensors

| Index | Name | crit [L] | Notes |
|---|---|---|---|
| 0 | `local_f75397@4c` | 87.85 °C | |
| 1 | `cpu_f75303@4d` | 87.85 °C | |
| 2 | `battery_temp@b` | **49.85 °C** | Lowest crit on the board, and nothing protects it. Reaches about 42 °C while charging with the CPU idle [L] |
| 3 | `ddr_f75303@4d` | 86.85 °C | |
| 4 | `peci-temp` | 119.85 °C | Higher than Tjmax, so useless as a limit. **Fan control input** |

- Tjmax is 100 °C. Readings are whole Kelvin, so values move in steps of about 1 °C and jitter by ±1 °C. PECI can rise about 4 °C/s [L]
- Peak PECI in normal use: 92.8 °C [L]

## Fan

- Single fan [W].
- Windows self-test [W]: duty 100 % → **7281 rpm** after 4 s; 20 % → 2635 rpm; auto at idle → 0 rpm.
- Linux duty→rpm (duty out of 255) [L]: 0/20 → 0 · 30 → 1107 · 50 → 1879 · 77 → 2693 · 100 → 3355 · 150 → 4551 · 180 → 5201.
  The fan sticks (won't start) somewhere between duty 20 and 30/255, which is about **8–12 %**.
- **Open question:** the two sets of numbers disagree. Windows reports 20 % ≈ 2635 rpm, but Linux predicts about 1900 rpm for duty 51/255.
  Windows reports 100 % ≈ 7281 rpm, against Linux's 5201 at 180/255 (where the table stops). Either the duty scales differ, or 7.3k
  is a real full-speed number that Linux never measured. Cross-check one duty on both systems.
- The EC's own curve is **strongly hysteretic** [L]. When heating, the fan starts somewhere between 66.8 and 72.8 °C. When cooling, it holds duty 50–90/255
  until the temperature drops below about 45 °C. It tops out near 3100–3300 rpm, so giving control back to the EC gives *less* airflow than manual full speed.

## Power [L unless marked]

- **The governing PL1 is the MMIO/MCHBAR copy.** The MSR package limit `0x610` reads 200 W and governs nothing on Linux.
  The Windows app writes `0x610` through PawnIO, so expect this to matter. **[?] untested on Windows**
- The board accepts a PL1 of 35 W, even though it declares 25 W. A 40 W setpoint draws 35.07 W, so **about 35 W is the real ceiling**.
- Stock PL2 is 60 W. PL4 is 75 W on a 79 W supply and 80 W on a 96 W adapter. The PL1 time window is about 32 s.
- Firmware **re-derives PL1 a few seconds after a platform-profile change**. Under Windows, that is a power-overlay change [?].
- PL1 survives s2idle. After a reboot it comes back at about 35 W.
- Throughput compared with 25 W: 30 W gives +8.9 %, 35 W gives +15.9 %. Each 10 W of PL1 adds about 12 °C. Idle draw is about 1.8 W.
- **Games:** PL1 only binds up to about 21 W. 25 W gives the same fps as 35 W, and is quieter.
- Undervolting is impossible: MSR `0x150` is locked.
- Throttle reasons: `0x64F` (core), `0x6B0` (graphics), `0x6B1` (ring). Under load only bit 8 (EDP) shows up.
- RAPL energy counters can be read for package/core/uncore, so CPU and GPU watts can be separated. The core and uncore domains *limit* nothing.

## GPU [L]

- 2500 MHz is the **requested** clock. The sustained **achieved** clock is about 1950 MHz on AC, and 1300–1500 MHz on battery under heavy load.
- The GPU and CPU trade clock speed under the electrical limit. None of the CPU-clock levers help in games.

## Battery

- [W] ATC `FRANEDA` Li-ion. Design 4640 mAh at 15.64 V, full 4685 mAh, 1 cycle, health 101 % (2026-10-03).
- [W] The charge limit reads back correctly. 85 % has held across app restarts.
- [L] Charging **stops** at the limit (80 % → current 0). The limit **resets to 100 % on reboot** and survives suspend.
  The UEFI limit setting uses the same mechanism.
- [L] The USB-C adapter negotiates 20 V × 5 A.

## Display [W]

- `\\.\DISPLAY1` internal panel, 60 / 120 Hz.
