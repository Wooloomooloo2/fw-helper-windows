# 0011 — Monitoring: PDH counters + EC, sampled on demand, recorded as CSV

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

The Linux sibling has a Monitor page: CPU/GPU load and clocks, CPU/GPU/system watts, temperatures, fan and memory, as live
charts plus recorded sessions (its M8 milestone). It reads sysfs, `/proc` fdinfo and RAPL. Windows needs its own sources,
and it should not need admin rights. The core app runs unprivileged (ADR 0002).

## Decision

**Sources** (`Hardware/SystemMetrics.cs`, `Hardware/Pdh.cs`):

| Metric | Source | Admin? |
|---|---|---|
| CPU load | PDH `\Processor Information(_Total)\% Processor Utility`, the same number Task Manager shows | no |
| CPU clock | `Processor Frequency × % Processor Performance / 100`. This is the *effective* clock averaged over all cores, not busy-only MHz | no |
| GPU load | PDH `\GPU Engine(*)\Utilization Percentage`, summed per engine type across processes, then the busiest type. This matches Task Manager | no |
| GPU shared memory | PDH `\GPU Adapter Memory(*)\Shared Usage` | no |
| RAM | `GlobalMemoryStatusEx` | no |
| Temperatures, fan, battery | EC (ADR 0003) | no |
| System watts | Battery discharge rate (only on battery) | no |
| Package watts | RAPL energy MSR via PawnIO (ADR 0006) | **yes**. Shown when available |

PDH is used through P/Invoke with **English** counter paths (`PdhAddEnglishCounter`), so there is no NuGet dependency and it works whatever the display language.
Wildcard counters are re-expanded on every collect. On the 358H there were 896 GPU engine instances.

**Sampling** (`Features/Telemetry.cs`): 1 Hz, **only while something uses it**: the Monitor window, or a recording
(reference-counted `Telemetry.Use()`). The last 300 samples (5 min) are kept for live charts.

**Recording** (`SessionRecorder`): CSV in `%AppData%\FwHelper\sessions\session-yyyyMMdd-HHmmss.csv`. It has a fixed 21-column header
(`TelemetrySample.Columns`), invariant culture, empty field = not available. It flushes every 10 rows, stops by itself after 12 h,
and keeps the newest 20 sessions. These numbers match the Linux recorder. A recording carries on after the Monitor window closes. The tray menu has "Stop recording".
Saved sessions open back into the same charts.

**UI** (`UI/MonitorForm.cs`, `UI/LineChart.cs`): six cards (Load, CPU clock, Power, Temperature, Fan, Memory), with the latest values in the
header and reference lines for PL1, the 95 °C full-fan line and the battery guard.

## Consequences

- Nothing runs when the Monitor window is closed and nothing is recording. The tray tooltip still uses its own lightweight EC reads.
- **Not at parity with Linux:**
  - No achieved GPU clock. Windows has no counter for the Intel GPU's achieved frequency without the Intel driver API.
  - No separate CPU/GPU watts. Core/uncore RAPL MSRs could add that once PawnIO works.
  - No busy-weighted MHz.
  - No throttle reasons. MSR `0x64F` via PawnIO is a candidate.
- The CSV header is the format version. A changed column list means old sessions won't open (`Load` checks the header). If columns
  change, add a version-tolerant reader rather than silently misreading.
- The CPU clock is an all-core average, so it reads lower than the busiest core under a single-threaded load.
  Linux found the same trap (flat mean vs `Bzy_MHz`).
