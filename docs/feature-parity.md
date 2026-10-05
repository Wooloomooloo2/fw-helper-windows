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
| **Power limits (TDP)** | ✅ PL1 via kernel RAPL MMIO, **no extra software**, re-assert loop, 8–35 W | 🟡 PL1+PL2 via PawnIO MSR `0x610`, admin, never run | Likely wrong register (MMIO governs), ceiling about 35 W. See ADR 0006 |
| Core parking | ✅ | ❌ | On Windows this would be the power-plan parking policy or affinity, not offlining. Low value (+2 %) |
| GPU frequency cap | ✅ | ❌ | No obvious Windows route without the Intel driver API. Low value (+4.9 %) |
| **Charge limit** | ✅ verified that charging stops | ✅ set/readback; stop not checked | Reapply on every boot (volatile) |
| **Monitoring: temperatures** | ✅ all EC sensors + coretemp package, scaled to crit | ✅ 5 EC sensors | |
| Monitoring: fan rpm/duty/owner | ✅ | ✅ rpm + duty/"EC auto" | |
| Monitoring: package / CPU / GPU watts | ✅ RAPL deltas, 1 Hz, 0.1 W | 🟡 package, CPU (PP0) and GPU (PP1) watts via PawnIO + admin, plus system W (battery). Code only | ADR 0011 amendment |
| Monitoring: CPU load %, busy MHz, throttle | ✅ | 🟡 load % and effective MHz (PDH); throttle reasons via PawnIO (code only); no busy MHz | ADR 0011 amendment |
| Monitoring: GPU load, achieved vs requested clock | ✅ | 🟡 load (busiest engine) and shared memory; no clock | No Windows counter for the Intel achieved clock |
| Monitoring: memory / swap | ✅ | ✅ RAM (no swap) | |
| Monitoring: battery W, time left | ✅ | ✅ watts, % and health | |
| Live charts | ✅ 6 cards, 300-sample window | ✅ 6 cards, 300 samples, reopens saved sessions | Rendered offscreen; not yet seen live |
| Session recording (CSV) | ✅ | ✅ 21 columns, 12 h auto-stop, keeps 20 | |
| Overlay / in-game HUD | ✅ overlay window + MangoHud | ❌ | |
| **Keyboard backlight / power LED** | ❌ | ✅ | Windows-only extra |
| **Refresh-rate switching** | ❌ | ✅ 60/max/auto | Windows-only extra |
| Tray icon + global hotkey | ❌ | ✅ `Ctrl+Shift+F5` | |
| CLI | ✅ `fw-helperctl` | 🟡 `--selftest` only | |
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
- **Does MSR `0x610` bind PL1?** Linux says the MMIO/MCHBAR copy governs. `--hwtest pl`. If it doesn't bind, look for a PawnIO route to MCHBAR, or drop the feature.
- **Service split** (ADR 0014, proposed). Decide after the test session.

### Remaining gaps compared with Linux
- Achieved GPU clock. Windows has no counter for it, and it would need the Intel GPU driver API or MMIO.
- Busy-weighted CPU MHz. Possible from the APERF/MPERF MSRs via PawnIO.
- Overlay window / in-game HUD.
- CLI.
- Core parking and a GPU frequency cap. Linux measured little benefit (+2 %, +4.9 %), so this is low priority.
