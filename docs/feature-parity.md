# Feature parity with the Linux version, and roadmap

This compares Windows `v0.1.0` (2026-10-03) with Linux [fw-helper](https://github.com/Wooloomooloo2/fw-helper)
`0.6.4` (commit `1e56f95`, 2026-09-26).

✅ = done and verified on hardware · 🟡 = built but not verified, or partial · ❌ = missing · — = not applicable

## Side by side

| Area | Linux | Windows | Notes for Windows |
|---|---|---|---|
| **Fan: custom curve** | ✅ any number of points, 1 Hz, hysteresis, ramp up 12 / down 4 per tick | 🟡 8 points, 1 Hz, 2 °C hysteresis, ramp down 2 %/tick, unit-tested | Not yet soak-tested on hardware. ADR 0009 |
| Fan: firmware-floor clamp (learned) | ✅ | ❌ | Needs the EC auto duty to be readable on Windows. Check this first |
| Fan: battery guard | ✅ | 🟡 42→48 °C ramp to 100 %, unit-tested | Holds 100 % rather than releasing (ADR 0009) |
| Fan: watchdog + crash-path restore | ✅ (`kill -9` → 0.27 s) | 🟡 watchdog thread (untested); ✅ guardian process verified on hard kill | Not covered: End process tree (ADR 0009) |
| Fan: release across suspend | ✅ | 🟡 | Code exists. No suspend event seen in the log yet |
| Fan: stiction refusal (1–29/255) | ✅ | ✅ never 1–11 % (unit-tested) | |
| Fan: pinned fixed duty | ✅ | ❌ | Only via `--selftest` |
| **Named / saved profiles** | ✅ user profiles (`profiles.d`), save/delete, can override built-ins | ❌ three fixed modes with per-mode settings | Would need a model change: modes → named profiles |
| Built-in ladder | ✅ quiet / balanced / performance / turbo / max, plus game / retro | 🟡 Silent / Balanced / Turbo | |
| AC/battery auto-switch | ✅ (off by default) | ✅ (always on, remembered per source) | |
| OS power-profile delegation | ✅ PPD | ✅ Windows power overlay | |
| **Power limits (TDP)** | ✅ PL1 via kernel RAPL MMIO, **no extra software**, re-assert loop, 8–35 W | 🟡 PL1+PL2 via PawnIO MSR `0x610`, admin, never run | Likely wrong register (MMIO governs), ceiling about 35 W. See ADR 0006 |
| Core parking | ✅ | ❌ | On Windows this would be the power-plan parking policy or affinity, not offlining. Low value (+2 %) |
| GPU frequency cap | ✅ | ❌ | No obvious Windows route without the Intel driver API. Low value (+4.9 %) |
| **Charge limit** | ✅ verified that charging stops | ✅ set/readback; stop not checked | Reapply on every boot (volatile) |
| **Monitoring: temperatures** | ✅ all EC sensors + coretemp package, scaled to crit | ✅ 5 EC sensors | |
| Monitoring: fan rpm/duty/owner | ✅ | ✅ rpm + duty/"EC auto" | |
| Monitoring: package / CPU / GPU watts | ✅ RAPL deltas, 1 Hz, 0.1 W | 🟡 package W (PawnIO + admin) and system W (battery); no CPU/GPU split | ADR 0011 |
| Monitoring: CPU load %, busy MHz, throttle | ✅ | 🟡 load % and effective all-core MHz (PDH); no busy MHz or throttle reasons | ADR 0011 |
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
| Automated tests / CI | ✅ fixture tests + CI | 🟡 xunit for the fan controller; no CI | |

## Suggested roadmap

This is in priority order. Safety comes first, because fan control and PL writes are the parts that can do damage.

1. ~~**Fan safety to Linux parity**~~: done 2026-10-04 (ADR 0009) apart from the **hardware soak test** (see CLAUDE.md "Resume here").
   The learned firmware floor (Linux ADR 0011) is still open. It depends on open question 3 in CLAUDE.md.
2. **Settle the fan scale question.** `--hwtest fansweep` is built and waiting on the hardware run. Windows 100 % → 7.3k rpm vs Linux full duty → about 5.2k (see hardware baseline).
3. **Power limits: make them true or remove them.** Partly done 2026-10-05 (ADR 0010): defaults, re-assert, restore, and `--hwtest pl` built. Waiting on the hardware run. Install PawnIO, run elevated, and check whether `0x610` binds under a load
   longer than 32 s. If not, look into the MCHBAR copy. Re-base defaults on the 35 W ceiling, add re-assert after an overlay change,
   and decide what happens on exit.
4. ~~**Monitoring**~~: built 2026-10-05 (ADR 0011). Originally: without admin, CPU load, GPU load (PDH), memory, plus package watts when PawnIO is available.
   Then live charts on a "Monitor" view.
5. **Named profiles** in place of three fixed modes: named fan curves, save/delete, and keep Silent/Balanced/Turbo as built-ins.
6. Release engineering: CI done 2026-10-05. Still to do: GitHub release, unit tests for `FanCurve` and the curve controller (an EC interface behind a fake).
7. Possibly later: the architecture question (a privileged service plus a user UI, Linux ADR 0003). This would solve
   the hard-kill fan restore and the PawnIO admin requirement in one go, at the cost of an installer.
