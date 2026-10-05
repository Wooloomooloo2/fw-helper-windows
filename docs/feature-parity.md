# Feature parity with the Linux version, and roadmap

This compares Windows `v0.1.0` (2026-10-03) with Linux [fw-helper](https://github.com/Wooloomooloo2/fw-helper)
`0.6.4` (commit `1e56f95`, 2026-09-26).

✅ = done and verified on hardware · 🟡 = built but not verified, or partial · ❌ = missing · — = not applicable

## Side by side

| Area | Linux | Windows | Notes for Windows |
|---|---|---|---|
| **Fan: custom curve** | ✅ any number of points, 1 Hz, hysteresis, ramp up 12 / down 4 per tick | 🟡 8 points, 1 Hz, 2 °C hysteresis, ramp down 2 %/tick, unit-tested | Not yet soak-tested on hardware. ADR 0009 |
| Fan: firmware-floor clamp (learned) | ✅ | 🟡 learned from the EC duty (`0x0027`), lowest per model bucket, on by default, unit-tested | Keyed on the EC's own sensor ramps, not PECI. ADR 0013 |
| Fan: battery guard | ✅ | 🟡 42→48 °C ramp to 100 %, unit-tested | Holds 100 % rather than releasing (ADR 0009) |
| Fan: watchdog + crash-path restore | ✅ (`kill -9` → 0.27 s) | 🟡 watchdog unit-tested against a fake EC; ✅ guardian process verified on hard kill | Not covered: End process tree (ADR 0009) |
| Fan: release across suspend | ✅ | 🟡 | Code exists. No suspend event seen in the log yet |
| Fan: stiction refusal (1–29/255) | ✅ | ✅ never 1–11 % (unit-tested) | |
| Fan: pinned fixed duty | ✅ | ❌ | Only via `--selftest` |
| **Named / saved profiles** | ✅ user profiles (`profiles.d`), save/delete, can override built-ins | ✅ user profiles (copy, rename, delete) + named fan-curve library | Built-ins can be edited but not deleted. ADR 0012 |
| Built-in ladder | ✅ quiet / balanced / performance / turbo / max, plus game / retro | 🟡 Silent / Balanced / Turbo | |
| AC/battery auto-switch | ✅ (off by default) | ✅ (always on, remembered per source) | |
| OS power-profile delegation | ✅ PPD | ✅ Windows power overlay | |
| **Power limits (TDP)** | ✅ PL1 via kernel RAPL MMIO, **no extra software**, re-assert loop, 8–35 W | 🟡 **No driver:** a per-profile power target held by a frequency-cap governor (Energy Meter + `PROCFREQMAX`); CPU cores only. EC owns PL1/PL2 and can't be overridden | ADR 0016. Whether the cap binds is still to test (`--hwtest governor`) |
| Temperature cap | ❌ | 🟡 governor on PECI, plus EC PROCHOT backstop (`0x0050`) | Windows-only extra. ADR 0016 |
| Undervolting | ❌ (Linux ADR 0007: locked) | ❌ locked (MSR `0x150`), not offered | ADR 0016 |
| Core parking | ✅ | ❌ | On Windows this would be the power-plan parking policy or affinity, not offlining. Low value (+2 %) |
| GPU frequency cap | ✅ | ❌ | No obvious Windows route without the Intel driver API. Low value (+4.9 %) |
| **Charge limit** | ✅ verified that charging stops | ✅ set/readback; stop not checked | Reapply on every boot (volatile) |
| **Monitoring: temperatures** | ✅ all EC sensors + coretemp package, scaled to crit | ✅ 5 EC sensors | |
| Monitoring: fan rpm/duty/owner | ✅ | ✅ rpm + duty/"EC auto" | |
| Monitoring: package / CPU / GPU watts | ✅ RAPL deltas, 1 Hz, 0.1 W | ✅ package, CPU (PP0), GPU (PP1) and DRAM watts from Windows Energy Meter (no driver, verified) | ADR 0016 |
| Monitoring: CPU load %, busy MHz, throttle | ✅ | 🟡 load % and effective MHz (PDH); throttling = EC soft/hard (`0x3E22`) + governor cap; no busy MHz or per-domain reasons | ADR 0016 |
| Monitoring: GPU load, achieved vs requested clock | ✅ | 🟡 load (busiest engine) and shared memory; no clock | No Windows counter for the Intel achieved clock |
| Monitoring: memory / swap | ✅ | ✅ RAM (no swap) | |
| Monitoring: battery W, time left | ✅ | ✅ watts, % and health | |
| Live charts | ✅ 6 cards, 300-sample window | ✅ 6 cards, 300 samples, reopens saved sessions | Rendered offscreen; not yet seen live |
| Session recording (CSV) | ✅ | ✅ 21 columns, 12 h auto-stop, keeps 20 | |
| Overlay / in-game HUD | ✅ overlay window + MangoHud | 🟡 overlay with **FPS** (ETW, no admin, verified), temps, load, watts, % used; not over exclusive full-screen | ADR 0017 |
| FPS | ✅ via MangoHud | ✅ ETW present events (DXGI/D3D9/DxgKrnl), verified on a game | ADR 0017 |
| **Keyboard backlight / power LED** | ❌ | ✅ | Windows-only extra |
| **Refresh-rate switching** | ❌ | ✅ 60/max/auto | Windows-only extra |
| Tray icon + global hotkey | ❌ | ✅ `Ctrl+Shift+F5` | |
| CLI | ✅ `fw-helperctl` | 🟡 `--status`, `--profiles`, `--mode`, `--charge-limit`, `--fan-floor` (named pipe); no fan duty or PL commands | ADR 0015 |
| Packaging | ✅ `.deb`, verified install/remove | 🟡 single-file exe + zip, not released | |
| Automated tests / CI | ✅ fixture tests + CI | ✅ xunit (fan loop against a fake EC, controller, floor, profiles, telemetry) + GitHub Actions | |

## Roadmap

Safety first: fan control and PL writes are the parts that can do damage.

### Done (waiting only on `docs/hardware-test-plan.md`)
1. ~~Fan safety to Linux parity~~: done 2026-10-04 (ADR 0009). The fan loop has been testable against a fake EC since 2026-10-05 (ADR 0013).
2. ~~Learned firmware floor~~: done 2026-10-05 (ADR 0013).
3. ~~Power limits: measured defaults, re-assert, restore~~: done 2026-10-05 (ADR 0010).
4. ~~Monitoring, recording, CPU/GPU watts, throttle reasons~~: done 2026-10-05 (ADR 0011 and its amendment).
5. ~~Named profiles and curves~~: done 2026-10-05 (ADR 0012).
6. ~~CI, unit tests, draft-release workflow~~: done 2026-10-05.

### Waiting on hardware answers
- **Fan scale:** Windows 100 % → 7.3k rpm, against Linux full duty → about 5.2k. `--hwtest fansweep`.
- **Does a power-plan frequency cap bind on this CPU?** `--hwtest governor`. If not, ADR 0016 falls back to the "maximum processor state" settings.
- **Service split** (ADR 0014, proposed). Decide after the test session.

### Remaining gaps compared with Linux
- Achieved GPU clock. Windows has no counter for it, and it would need the Intel GPU driver API or MMIO.
- Busy-weighted CPU MHz and per-domain throttle reasons: both need MSRs, which means a driver (dropped in ADR 0016).
- In-game HUD over exclusive full-screen (the overlay window covers windowed and borderless games).
- CLI commands for fan duty and PL (left out on purpose: GUI-only for now).
- Core parking and a GPU frequency cap. Linux measured little benefit (+2 %, +4.9 %), so this is low priority.
