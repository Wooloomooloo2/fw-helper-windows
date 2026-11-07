using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// Per-profile "CPU boost off" (ADR 0016, 3rd amendment): "Maximum processor state" 99 % in the active power plan, which turns
    /// turbo off (about 1.8 GHz). It is the one CPU limit Windows obeys under the Best Performance overlay, so in GPU-bound games
    /// the watts the CPU gives up go to the GPU. Like the governor's caps, the user's own values are saved before the first
    /// change and restored on quit, after a crash (next start) and after a hard kill (the guard).
    /// </summary>
    public static class CpuBoost
    {
        public const uint OffPercent = 99;
        private const string SavedKey = "boost_saved_state";

        public static bool IsOff(int mode) => AppConfig.Get(AppConfig.ModeKey("cpu_boost_off", mode), 0) == 1;
        public static void SetOff(int mode, bool off) => AppConfig.Set(AppConfig.ModeKey("cpu_boost_off", mode), off ? 1 : 0);

        /// <summary>True while FW-Helper holds boost off in the power plan.</summary>
        public static bool Active { get => _active; private set { if (_active == value) return; _active = value; Changed?.Invoke(); } }
        private static bool _active;
        /// <summary>Boost was turned off or back on (the caller's thread).</summary>
        public static event Action? Changed;

        /// <summary>What to write for boost off: 99 %, but never above a lower value the user set themselves.</summary>
        public static PowerPlan.FrequencyCaps OffValues(PowerPlan.FrequencyCaps original) => new(
            Math.Min(original.Ac0, OffPercent), Math.Min(original.Dc0, OffPercent),
            Math.Min(original.Ac1, OffPercent), Math.Min(original.Dc1, OffPercent));

        /// <summary>Apply the current profile's setting (startup, profile switch, settings change).</summary>
        public static void Apply()
        {
            if (!IsOff(ModeControl.CurrentMode))
            {
                Restore(null);
                return;
            }
            if (Active) return;

            var saved = PowerPlan.FrequencyCaps.Parse(AppConfig.GetString(SavedKey));
            if (saved is null)
            {
                saved = PowerPlan.ReadMaxState();
                if (saved is null) { Logger.WriteLine("CPU boost off: can't read the power plan"); return; }
                AppConfig.Set(SavedKey, saved.ToString());
            }
            Active = PowerPlan.WriteMaxState(OffValues(saved));
            Logger.WriteLine($"CPU boost off (max processor state {OffPercent} %, was {saved}): {(Active ? "OK" : "failed")}");
        }

        /// <summary>Put back the values saved before boost was turned off. Every exit path, the next start and the guard.</summary>
        public static void Restore(string? why)
        {
            if (PowerPlan.FrequencyCaps.Parse(AppConfig.GetString(SavedKey)) is not PowerPlan.FrequencyCaps saved) { Active = false; return; }
            if (PowerPlan.WriteMaxState(saved))
            {
                AppConfig.Remove(SavedKey);
                Active = false;
                Logger.WriteLine($"CPU boost back on (max processor state {saved}){(why is null ? "" : " — " + why)}");
            }
        }
    }
}
