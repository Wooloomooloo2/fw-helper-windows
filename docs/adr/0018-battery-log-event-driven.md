# 0018 — Battery log: event-driven, EcoQoS, rolled up into months and years

- **Status:** Accepted
- **Date:** 2026-10-10

## Context

The user wants a battery monitor: time on battery, average draw, what the battery was last charged to, and history. History is
kept as **30 days of detail, then 12 months, then years**, each older tier an aggregate of the one before. The user's hard
requirement: **it must cost almost nothing**. A battery monitor that drains the battery would defeat its own purpose.

Telemetry (ADR 0011) samples at 1 Hz, and only while the Monitor, the overlay or a recording is using it. That is the wrong tool for this.

This laptop uses **Modern Standby** (`powercfg /a`: S0 Low Power Idle). Classic suspend/resume events never arrive, and the log has
no `Suspend` entry ever. Splitting "awake" from "asleep" therefore has to rely on the display state.

## Decision

- **No polling loop.** `PowerSettingRegisterNotification` (powrprof, `DEVICE_NOTIFY_CALLBACK`, no window needed) for:
  - `GUID_BATTERY_PERCENTAGE_REMAINING`: every 1 % step (minutes apart);
  - `GUID_ACDC_POWER_SOURCE`: plug in and unplug;
  - `GUID_CONSOLE_DISPLAY_STATE`: screen on/off, where dimmed counts as on.

  The callback only records what changed and signals an event. Verified on this laptop on 2026-10-10: all three register, and
  each delivers its current value when it is registered.
- **One logger thread** at `ThreadPriority.Lowest` with **EcoQoS** (`SetThreadInformation(ThreadPowerThrottling,
  EXECUTION_SPEED)`). Windows then schedules it on the efficient cores at a low clock. The thread is blocked in `WaitOne` almost
  all of the time. For each event it does one EC memmap read (`GetBattery`) and appends one CSV line.
- **Heartbeat:** every 10 min, but only while on battery with the screen on. It limits what a missed notification can cost. There
  is no timer on AC or with the screen off.
- **What each line holds:** time (UTC), kind, AC, screen, charging, %, mAh, full mAh, mV and cycles. Energy is mAh × mV, because
  the EC reports mAh and the voltage moves with the charge level. Each line's state applies to the interval up to the next line.
  Lines that repeat the previous state within a minute are skipped.
- **Gaps:** a `Stop` line is written on every exit (`SafeShutdown`, including logoff and shutdown). The interval after a `Stop`, or
  before a `Start` (which covers a crash), counts as unknown. Long quiet intervals, such as overnight on AC or in standby, are real
  and are counted. If the app was off and the charge went up, the discharge session starts again.
- **Storage** (`%AppData%\FwHelper\battery\`):
  - `events.csv` holds the last 30 local days and is append-only;
  - `history.json` holds the month and year totals and `RolledUntil`.

  Once a day, events older than 30 days are added into their months, and months older than 12 into their years. Every total in
  `BatteryPeriod` merges by sum, min or last value, so no tier ever needs the raw data again. Intervals are split proportionally at
  local day, month and year boundaries. Both files are written to a temp file first and then swapped in.
- **Pure core** (`Features/BatteryHistory.cs`): intervals, period totals, the roll-up and the current session, all unit-tested.
  `BatteryLog` is the I/O shell.
- **UI:** its own **Battery window**, opened in one click from the main window (Battery button) and the tray menu. The Monitor
  window also has a Battery button. The window shows the current or last discharge, the last charge, health and cycles. It has a
  24 h / 7 day charge chart, shaded for AC and screen-off. The Days / Months / Years table has grouped headings: time on battery
  (total, screen on, screen off), energy used (Wh), average draw (W, screen on and off), and charging (unplugs, average charged-to %).
  While the window is open it refreshes every 5 s (one EC read) and shows "updated hh:mm:ss". Nothing runs while it is closed.
  (2026-10-11: a once-a-minute refresh inside the Monitor window looked frozen next to the 1 Hz charts.)
- **Charger bounce:** a battery reading between two AC readings less than 1 min apart is not an unplug. One was seen on 2026-10-10
  (2 s).
- The tray tooltip gains "On battery 2 h 14 min · 6.1 W avg", built from a cached session on each mouse move.
- **CLI:** `--battery`. It also works when FW-Helper isn't running, from the files on disk.

## Consequences

- "Average draw" is energy over time with the screen on, at 1 % resolution. That's about 0.6 Wh per step. It is accurate over
  tens of minutes, not seconds. For instant watts, use the Monitor charts.
- The screen-off figures include Modern Standby drain. That is useful in itself, because it is the "battery lost overnight" number.
- History only starts when this version is installed. Windows' own `powercfg /batteryreport` is not imported.
- Cost to verify on hardware: the FW-Helper CPU time over a long idle, with and without this build. See the hardware test plan.
- **Measured cycles:** all energy the log has seen drawn from the battery, divided by the design energy. This is shown next to
  "battery says N", because the gauge's own count stayed at 1 while about 4.5 cycles were drained (hardware-baseline.md). The
  measured count only covers the time since the log started.

## Amendment 2026-10-11: standby from Windows' own records, and "Sleep after"

**Finding.** Windows writes a summary of every Modern Standby session in the System log: Kernel-Power **event 507**, written when
standby ends, with named fields (`DurationInUs`, `ScreenOffDurationInUs`, `ScreenOff/SleepEnergyCapacityAtStart/End` in mWh,
`SleepDurationInUs`, `DripsResidencyInUs`, `Reason`, `AudioPlaying`, `PowerStateAc`, and more). Event 506 marks the entry.
Reading the log needs no admin. On this laptop, 60 sessions since 2 Oct showed:
- **the sleep phase** draws about **0.33 W**, with about 99 % deep-idle (DRIPS) residency, whether the lid was closed or austerity forced it;
- **the screen-off phase** (the system is still awake) draws **1.0–1.7 W**. Three times it lasted almost exactly **60 min**, which is
  the on-battery "Sleep after" value (3600 s). The timeout runs from the screen turning off. Closing the lid goes straight to the sleep phase;
- "Austerity Battery Drain Budget Exceeded" (reason 55) is a reason a session ends, not a cheaper state.

**Decision.**
- `StandbySession` (pure, unit-tested against the real field values) holds one session on battery: the awake and asleep phases,
  each with seconds and mWh, plus deep-idle %, exit reason and audio. Sessions on AC and ones under 1 min are skipped.
- `BatteryLog` catches up from the System log at start (`EventLogReader`, everything after the last stored session), then
  subscribes (`EventLogWatcher`). The event log service pushes each new session, so nothing polls. Sessions are stored in
  `battery\standby.csv`, rolled up with the events after 30 days, and added to `BatteryPeriod` as `AwakeOff*` and `Asleep*`.
  Sessions from before the battery log started are not counted, so a day never shows standby time without its time on battery.
- Back-to-back sessions (a policy change ends one and starts the next at once) are joined into one "last standby".
- **Setting:** the Battery window has **"Sleep after the screen turns off"** (on battery): 5, 10, 15 or 30 min, 1 h, 2 h, or never.
  It reads and writes Windows' own `STANDBYIDLE` DC value in the active plan (`PowerWriteDCValueIndex`, then re-activate the plan,
  with no admin needed; a no-op write was checked on 2026-10-11). It is the same setting as Windows Settings → Power, so the two
  stay in sync. It is a user preference, so it is **not** restored on exit (unlike the governor's frequency caps).
- Not exposed: the standby budget settings (`STANDBYBUDGETPERCENT` 5 %, grace period 900 s, and others), which are undocumented,
  and network connectivity in standby. The sleep phase is already at about 99 % deep idle, so these have little left to save.
