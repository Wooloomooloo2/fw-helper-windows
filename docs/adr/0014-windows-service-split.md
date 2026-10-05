# 0014 — Split into a Windows service and a user tray app?

- **Status:** **Proposed.** Not decided, and nothing is built
- **Date:** 2026-10-05

## Context

FW-Helper is one unprivileged tray process (ADR 0002). Several gaps go back to that single choice:

| Gap | Today's mitigation |
|---|---|
| A hard kill of the app while it holds the fan | Guardian process (ADR 0009). It does not survive "End process tree", or the guardian being killed first |
| PL1/PL2 need admin | The user restarts elevated, or the autostart task runs at Highest (ADR 0006/0008) |
| A PL override persists after a hard kill | Accepted until reboot (ADR 0010) |
| Package/CPU/GPU watts and throttle reasons need admin | Shown only when elevated with PawnIO (ADR 0011) |
| No fan control before logon or after sign-out | Fan is back with the EC (safe) |

The Linux sibling avoids all of these with a root daemon, an unprivileged GUI, and D-Bus with polkit (Linux ADR 0003).

## Options

1. **Status quo:** single process, guardian, optional elevation. No installer. The gaps above remain.
2. **LocalSystem service + user tray app.**
   - The service owns the fan loop, the PL writes, MSR telemetry and the floor learning. The tray app becomes a UI over a named pipe,
     with an ACL that lets only the interactive user's session write.
   - *Gains:*
     - Service Control Manager recovery actions (restart, or run a restore command) cover crashes and kills of the service.
     - No UAC for PL.
     - The service survives UI crashes.
     - Fan control continues across sign-out.
   - *Costs:*
     - An installer (MSI/MSIX, or `sc create` from the exe).
     - An IPC protocol and its versioning.
     - A privileged attack surface: the pipe must validate every request (ranges, rate limits), the way the Linux daemon does with polkit.
     - Updating the app means replacing a running service.
3. **Hybrid:** keep the single process, but have an *optional* tiny service that does only the restore (fan auto, PL restore)
   when the tray app's process disappears. It is smaller than option 2, but it still needs an installer.

## Recommendation (for discussion)

Stay with **option 1** until the hardware test plan shows real problems. Specifically:
- the guardian is killed together with the app by "End task";
- PL writes turn out to bind (`--hwtest pl`), which makes elevation a recurring need.

If either happens, go to **option 2**. Its fan/PL code already sits behind `IFanHardware` and `PowerLimitControl`, so it can move
into a service with little change. Option 3 adds most of option 2's packaging cost for a fraction of the benefit.

## Decision

Pending: the user's call after the hardware test session.
