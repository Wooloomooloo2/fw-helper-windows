using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// App wiring for the power/temperature governor and the EC backstop (ADR 0016): settings, the original power-plan values
    /// (saved before the first change, restored on every exit path and after a crash), and per-profile targets.
    /// </summary>
    public static class GovernorControl
    {
        public const int MinPowerW = 8, MaxPowerW = 35;      // PL1 tops out at 35 W (DTT holds 33 W in Performance on BIOS 03.04): a target above it can't bind
        public const int MinTempC = 70, MaxTempC = 95;
        /// <summary>Highest P-core turbo on the 358H (Linux topology: 4700/4800 MHz); a cap at or above this is "no cap".</summary>
        public const int CpuMaxMHz = 4800;

        private const string SavedCapsKey = "gov_saved_caps";

        private static Governor? _governor;
        private static EcGovernorHardware? _hw;

        public static Governor? Instance => _governor;

        // ---- Settings ----

        public static int? PowerTarget(int mode) => AppConfig.Get(AppConfig.ModeKey("power_target", mode), 0) is int w and > 0 ? w : null;
        public static void SetPowerTarget(int mode, int? watts) =>
            AppConfig.Set(AppConfig.ModeKey("power_target", mode), watts is int w ? Math.Clamp(w, MinPowerW, MaxPowerW) : 0);

        public static int? TempCap() => AppConfig.Get("temp_cap", 0) is int c and > 0 ? c : null;
        public static void SetTempCap(int? celsius) => AppConfig.Set("temp_cap", celsius is int c ? Math.Clamp(c, MinTempC, MaxTempC) : 0);

        public const int MinCpuW = 3, MaxCpuW = 30;
        /// <summary>Lowest fixed ceiling offered in the UI: below this the desktop itself stutters.</summary>
        public const int MinCpuMHz = 1000;

        /// <summary>Per-profile CPU-cores watt cap: the rest of the package budget is left to the GPU.</summary>
        public static int? CpuPowerTarget(int mode) => AppConfig.Get(AppConfig.ModeKey("cpu_power_target", mode), 0) is int w and > 0 ? w : null;
        public static void SetCpuPowerTarget(int mode, int? watts) =>
            AppConfig.Set(AppConfig.ModeKey("cpu_power_target", mode), watts is int w ? Math.Clamp(w, MinCpuW, MaxCpuW) : 0);

        /// <summary>Per-profile fixed CPU frequency ceiling (MHz).</summary>
        public static int? CpuFreqCap(int mode) => AppConfig.Get(AppConfig.ModeKey("cpu_max_mhz", mode), 0) is int f and > 0 ? f : null;
        public static void SetCpuFreqCap(int mode, int? mhz) =>
            AppConfig.Set(AppConfig.ModeKey("cpu_max_mhz", mode), mhz is int f ? Math.Clamp(f / 100 * 100, MinCpuMHz, CpuMaxMHz) : 0);

        public static bool IsBackstopEnabled() => AppConfig.Get("temp_backstop", 1) == 1;
        public static void SetBackstopEnabled(bool on) => AppConfig.Set("temp_backstop", on ? 1 : 0);

        private static GovernorTargets Targets()
        {
            int mode = ModeControl.CurrentMode;
            return new GovernorTargets(PowerTarget(mode), TempCap(), CpuPowerTarget(mode), CpuFreqCap(mode));
        }

        // ---- Lifecycle ----

        /// <summary>App start: undo anything a crashed previous run left behind, before applying the current profile.</summary>
        public static void Init()
        {
            RestoreSavedCaps("left over from a previous run");
            CpuBoost.Restore("left over from a previous run");
            if (TempCap() is null || !IsBackstopEnabled()) EcBackstop.Restore();
        }

        /// <summary>Apply the current profile's targets and the global cap (startup, mode change, resume, settings change).</summary>
        public static void Apply()
        {
            CpuBoost.Apply();
            var targets = Targets();
            var temp = targets.TempC;

            if (temp is int cap && IsBackstopEnabled()) EcBackstop.Apply(cap);
            else EcBackstop.Restore();

            if (!targets.Any)
            {
                if (_governor is not null) _governor.Stop();
                RestoreSavedCaps(null);
                return;
            }

            // Save the user's own plan values once, before we change anything
            if (AppConfig.GetString(SavedCapsKey) is null && PowerPlan.Read() is PowerPlan.FrequencyCaps original)
                AppConfig.Set(SavedCapsKey, original.ToString());

            _hw ??= new EcGovernorHardware();
            _governor ??= new Governor(_hw, Targets, CpuMaxMHz);
            // Profile switch or settings change: decide afresh, never carry the last profile's cap or averages over
            _governor.ResetPolicy();
            _governor.Start();
            _governor.Tick();
        }

        /// <summary>Every exit path: no cap left in the user's power plan, firmware thermal config back.</summary>
        public static void Shutdown()
        {
            try { _governor?.Stop(); } catch { }
            RestoreSavedCaps(null);
            try { CpuBoost.Restore(null); } catch { }
            try { EcBackstop.Restore(); } catch { }
        }

        /// <summary>Put back the power-plan values saved before our first change. Also used by the guardian after a hard kill.</summary>
        public static void RestoreSavedCaps(string? why)
        {
            if (PowerPlan.FrequencyCaps.Parse(AppConfig.GetString(SavedCapsKey)) is not PowerPlan.FrequencyCaps saved) return;
            if (PowerPlan.Write(saved))
            {
                AppConfig.Remove(SavedCapsKey);
                Logger.WriteLine($"CPU frequency caps restored ({saved}){(why is null ? "" : " — " + why)}");
            }
        }
    }
}
