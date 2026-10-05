# 0012 — User profiles with stable ids, and a named fan-curve library

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

v0.1.0 had exactly three modes (Silent/Balanced/Turbo), and code across the app indexed arrays by mode number. The Linux sibling
has user profiles (save/delete, can replace a built-in) where every profile carries its own curve. The user also asked for
**named fan curves**. Two things should stay as they are: the G-Helper feel (three big buttons, `Ctrl+Shift+F5`) and existing users' settings.

## Decision

**Profiles** (`Features/Modes.cs`):

- Built-ins keep ids **0, 1, 2**. User profiles get ids from **3 up**. The list lives in config as `profiles = "3,4,7"`.
  **Ids are never reused** (`profile_last_id`), so a new profile can never inherit a deleted profile's leftover keys.
- Every per-profile setting keeps the existing key form `<name>_<id>` (`overlay_`, `fan_custom_`, `fan_curve_`, `pl_custom_`,
  `pl1_`, `pl2_`), plus `name_<id>` and `base_<id>` for user profiles. **No migration**: v0.1.0 configs work as they are.
- A user profile is always **created as a copy** of the profile being edited. It stores the effective values, so it doesn't drift if
  defaults change. It also records that profile's **base** built-in, which sets its colour, icon and fallback defaults.
- Built-ins can be edited but not renamed or deleted. Deleting the active profile, or one that `mode_ac`/`mode_dc` points to,
  falls back to Balanced.
- `Ctrl+Shift+F5` cycles Balanced → Turbo → Silent (unchanged) and then the user profiles in creation order.

**UI:**

- The main window keeps the three built-in buttons. The "Performance mode ▾" header opens a menu with every profile.
  While a user profile is active, the header shows its name in the profile's colour.
- The tray menu lists all profiles, rebuilt every time it opens.
- Fans + Power: the three mode tabs are replaced by a profile picker with **Use / New… / Rename / Delete**.
  Rename and Delete are hidden for built-ins. Use is hidden for the active profile.

**Named curves** (`Features/FanCurveLibrary.cs`): one config string, `fan_curves = "Name=t:d,…;Name2=…"`. Fans + Power →
**Curves ▾** has "Save this curve as…", the saved curves (click to load into the profile being edited) and "Delete saved curve".
Names go through `ProfileList.CleanName`, which removes `, ; = |`, trims, and caps at 24 characters.

The pure parts (`ProfileList`, `FanCurveLibrary.Parse/Serialize`) are unit-tested. `Create/Rename/Delete` write the real
`%AppData%` config, so they are on the hardware test plan instead.

## Consequences

- Loading a named curve does **not** turn the profile's custom-curve switch on. That remains its own deliberate choice, because
  it hands the fan to software control.
- Profiles don't carry the charge limit. That matches Linux, where built-ins never touch it. A per-profile charge limit could be added later.
- There's no limit on the number of profiles. The tray menu grows with them.
- `Modes.Count`, `Modes.Names` and `Modes.Colors` are gone. Use `Modes.All()`, `Modes.Name(id)` and `Modes.ColorOf(id)`.
