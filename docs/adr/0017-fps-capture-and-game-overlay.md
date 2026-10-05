# 0017 — FPS from present events (ETW), and a G-Helper-style overlay

- **Status:** Accepted. Extends [0015](0015-cli-over-named-pipe-and-overlay.md)'s overlay
- **Date:** 2026-10-05

## Context

The user wants an overlay showing **FPS**, package temperature, CPU/GPU load, CPU/GPU watts and "% used" (RAM, GPU memory, power
against the limit, battery). Windows has no FPS counter. Tools such as PresentMon, RTSS and the Xbox Game Bar count frames from the
present events that graphics runtimes emit through ETW, or hook the game. Hooking (injecting into games) is out of scope:
it trips anti-cheat and it is fragile.

## Decision

- **A real-time ETW session** (`Hardware/PresentMonitor.cs`) using Microsoft's TraceEvent library. It listens to the events PresentMon
  uses, with GUIDs and ids taken from `GameTechDev/PresentMon` `PresentData/ETW/*.h`:
  - `Microsoft-Windows-DXGI` `{CA11C036-…}`: Present_Start (0x2A) and PresentMultiplaneOverlay_Start (0x37). This covers D3D10/11/12.
  - `Microsoft-Windows-D3D9` `{783ACA0A-…}`: Present_Start (0x01).
  - `Microsoft-Windows-DxgKrnl` `{802EC45A-…}`: Present_Info (0xB8). This is kernel level and also covers Vulkan/OpenGL.
- **Counting** (`Features/FpsCounter.cs`, pure and unit-tested):
  - For each process, count API-level presents if there are any, otherwise kernel ones, so a frame is never counted twice.
  - The window is the last 1 s before the newest event, because real-time ETW delivers events in batches.
  - The app shown is the foreground window's process. If that isn't drawing frames, the busiest presenter is shown instead.
- **Permissions:** real-time ETW sessions need admin *or* membership of **Performance Log Users**. The user is a member, which was checked.
  Verified 2026-10-05: no admin needed, and Dark Souls III read a steady 60 FPS. Without the right, the overlay shows "n/a" and the
  Monitor shows the reason.
- **The session only runs while telemetry is in use** (the overlay, the Monitor or a recording). It is stopped when they close.
- **Overlay** (`UI/OverlayForm.cs`): owner-drawn, monospace, a key column in accent colour, with FPS larger. Rows:

  | Row | Content |
  |---|---|
  | FPS | frames/s and the app's name |
  | CPU | load %, effective GHz, package (PECI) °C, cores W |
  | GPU | load %, shared GPU memory GB, GPU W |
  | PKG | package W / limit W (%), where the limit is the profile's power target or else the EC's PL1 35 W; plus the governor's frequency cap when it is active |
  | RAM | used % (used / total GB) |
  | BAT | %, time left and draw on battery, or "AC" |

  It is topmost, never takes focus, and has no taskbar or Alt+Tab entry. Drag it to move it (the position is remembered); right-click to close it.
- **Monitor:** new FPS and Battery cards, DRAM watts, and the governor's cap on the clock card.

## Consequences

- **New dependency:** `Microsoft.Diagnostics.Tracing.TraceEvent` (MIT). It was chosen over hand-written ETW P/Invoke, whose
  `EVENT_TRACE_LOGFILE` and `EVENT_RECORD` layouts are easy to get subtly wrong.
- **Not shown over exclusive full-screen.** That needs injection. Borderless and windowed games (most modern games) are fine.
- Frame timing (1 % lows, frame-time graph) is not done yet. The events carry timestamps, so it can be added later.
- A crashed run can leave the `FwHelper-Presents` ETW session behind. The next start takes it over by name.
- **Filter by event id in the kernel** (`TraceEventProviderOptions.EventIDsToEnable`). Unfiltered, DxgKrnl delivered about 4,900 events/s
  at idle. That cost about 3.8 % of a core and +1–1.5 W of package power at idle while the overlay was open (user report, measured
  2026-10-05). Filtered, it costs 0.16 %.
