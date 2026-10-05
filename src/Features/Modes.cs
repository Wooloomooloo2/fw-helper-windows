using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>Pure helpers for the profile list (ADR 0012), kept free of config I/O so they can be unit-tested.</summary>
    public static class ProfileList
    {
        public const int MaxNameLength = 24;

        /// <summary>"3,4,7" → [3, 4, 7]; ignores junk, duplicates and built-in ids.</summary>
        public static List<int> Parse(string? s, int firstUserId) =>
            (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => int.TryParse(p, out int id) ? id : -1)
                .Where(id => id >= firstUserId)
                .Distinct()
                .ToList();

        public static string Serialize(IEnumerable<int> ids) => string.Join(",", ids);

        /// <summary>Ids are never reused, so settings of a deleted profile can't leak into a new one.</summary>
        public static int NextId(IEnumerable<int> existing, int lastIssued, int firstUserId) =>
            Math.Max(Math.Max(lastIssued, firstUserId - 1), existing.DefaultIfEmpty(firstUserId - 1).Max()) + 1;

        /// <summary>Trimmed, without config separators, at most <see cref="MaxNameLength"/> characters; null if nothing is left.</summary>
        public static string? CleanName(string? name)
        {
            if (name is null) return null;
            var chars = name.Where(c => !char.IsControl(c) && c is not (',' or ';' or '=' or '|')).ToArray();
            string clean = new string(chars).Trim();
            if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength].TrimEnd();
            return clean.Length == 0 ? null : clean;
        }

        /// <summary>Hotkey order: Balanced → Turbo → Silent (G-Helper's Fn+F5), then user profiles; wraps around.</summary>
        public static int Next(int current, IReadOnlyList<int> userIds)
        {
            var order = new List<int> { Modes.Balanced, Modes.Turbo, Modes.Silent };
            order.AddRange(userIds);
            int i = order.IndexOf(current);
            return order[(i + 1) % order.Count];
        }
    }

    /// <summary>
    /// Performance profiles: the built-in Silent / Balanced / Turbo (ids 0–2) and user profiles (ids 3+, ADR 0012).
    /// Every per-profile setting is a config key "&lt;name&gt;_&lt;id&gt;", the same for built-in and user profiles.
    /// </summary>
    public static class Modes
    {
        public const int Silent = 0;
        public const int Balanced = 1;
        public const int Turbo = 2;
        public const int BuiltInCount = 3;

        private static readonly string[] BuiltInNames = { "Silent", "Balanced", "Turbo" };

        private static readonly Color[] BuiltInColors =
        {
            Color.FromArgb(6, 180, 138),   // silent – green
            Color.FromArgb(58, 174, 239),  // balanced – blue
            Color.FromArgb(255, 32, 32),   // turbo – red
        };

        /// <summary>Per-profile keys copied when a profile is duplicated and removed when it's deleted.</summary>
        private static readonly string[] ProfileKeys = { "overlay", "fan_custom", "fan_curve", "pl_custom", "pl1", "pl2", "name", "base" };

        public static bool IsBuiltIn(int mode) => mode is >= 0 and < BuiltInCount;

        public static List<int> UserIds() => ProfileList.Parse(AppConfig.GetString("profiles"), BuiltInCount);

        /// <summary>Built-ins first (Silent, Balanced, Turbo), then user profiles in creation order.</summary>
        public static List<int> All() => Enumerable.Range(0, BuiltInCount).Concat(UserIds()).ToList();

        public static bool Exists(int mode) => IsBuiltIn(mode) || UserIds().Contains(mode);

        public static string Name(int mode) =>
            IsBuiltIn(mode) ? BuiltInNames[mode] : AppConfig.GetString(AppConfig.ModeKey("name", mode)) ?? $"Profile {mode}";

        /// <summary>The built-in a user profile was made from: its colour, icon and fallback defaults.</summary>
        public static int Base(int mode) =>
            IsBuiltIn(mode) ? mode : Math.Clamp(AppConfig.Get(AppConfig.ModeKey("base", mode), Balanced), 0, BuiltInCount - 1);

        public static Color ColorOf(int mode) => BuiltInColors[Base(mode)];

        // ---- Profile management ----

        /// <summary>New user profile with a copy of <paramref name="copyFrom"/>'s settings. Returns its id.</summary>
        public static int Create(string name, int copyFrom)
        {
            var ids = UserIds();
            int id = ProfileList.NextId(ids, AppConfig.Get("profile_last_id", 0), BuiltInCount);
            AppConfig.Set("profile_last_id", id);

            // Copy effective values (defaults included), so the copy doesn't change if defaults change later
            SetOverlay(id, GetOverlay(copyFrom));
            SetCustomFan(id, IsCustomFan(copyFrom));
            SetCurve(id, GetCurve(copyFrom));
            SetPowerLimit(id, IsPowerLimit(copyFrom));
            SetPL(id, GetPL1(copyFrom), GetPL2(copyFrom));
            AppConfig.Set(AppConfig.ModeKey("name", id), ProfileList.CleanName(name) ?? $"Profile {id}");
            AppConfig.Set(AppConfig.ModeKey("base", id), Base(copyFrom));

            ids.Add(id);
            AppConfig.Set("profiles", ProfileList.Serialize(ids));
            Logger.WriteLine($"Profile created: {Name(id)} ({id}) from {Name(copyFrom)}");
            return id;
        }

        public static bool Rename(int mode, string name)
        {
            if (IsBuiltIn(mode) || ProfileList.CleanName(name) is not string clean) return false;
            AppConfig.Set(AppConfig.ModeKey("name", mode), clean);
            return true;
        }

        public static void Delete(int mode)
        {
            if (IsBuiltIn(mode)) return;
            string name = Name(mode);
            AppConfig.Set("profiles", ProfileList.Serialize(UserIds().Where(id => id != mode)));
            foreach (var key in ProfileKeys) AppConfig.Remove(AppConfig.ModeKey(key, mode));
            foreach (var key in new[] { "mode_ac", "mode_dc" })
                if (AppConfig.Get(key, -1) == mode) AppConfig.Set(key, Balanced);
            Logger.WriteLine($"Profile deleted: {name} ({mode})");
        }

        // ---- Per-profile settings (user profiles fall back to their base's defaults) ----

        /// <summary>Index into <see cref="PowerNative.Overlays"/>.</summary>
        public static int GetOverlay(int mode) => AppConfig.Get(AppConfig.ModeKey("overlay", mode), Base(mode));
        public static void SetOverlay(int mode, int overlay) => AppConfig.Set(AppConfig.ModeKey("overlay", mode), overlay);

        public static bool IsCustomFan(int mode) => AppConfig.Get(AppConfig.ModeKey("fan_custom", mode), 0) == 1;
        public static void SetCustomFan(int mode, bool on) => AppConfig.Set(AppConfig.ModeKey("fan_custom", mode), on ? 1 : 0);

        public static FanCurve GetCurve(int mode) =>
            FanCurve.Parse(AppConfig.GetString(AppConfig.ModeKey("fan_curve", mode))) ?? FanCurve.Default(Base(mode));
        public static void SetCurve(int mode, FanCurve curve) => AppConfig.Set(AppConfig.ModeKey("fan_curve", mode), curve.ToString());

        public static bool IsPowerLimit(int mode) => AppConfig.Get(AppConfig.ModeKey("pl_custom", mode), 0) == 1;
        public static void SetPowerLimit(int mode, bool on) => AppConfig.Set(AppConfig.ModeKey("pl_custom", mode), on ? 1 : 0);

        // Defaults from the Linux measurements (docs/hardware-baseline.md): real PL1 ceiling ~35 W, stock PL2 60 W,
        // 25 W is the gaming sweet spot (same fps as 35 W, quieter)
        public static int GetPL1(int mode) => AppConfig.Get(AppConfig.ModeKey("pl1", mode), Base(mode) switch { Silent => 15, Turbo => 35, _ => 25 });
        public static int GetPL2(int mode) => AppConfig.Get(AppConfig.ModeKey("pl2", mode), Base(mode) switch { Silent => 30, Turbo => 64, _ => 60 });
        public static void SetPL(int mode, int pl1, int pl2)
        {
            AppConfig.Set(AppConfig.ModeKey("pl1", mode), pl1);
            AppConfig.Set(AppConfig.ModeKey("pl2", mode), pl2);
        }
    }
}
