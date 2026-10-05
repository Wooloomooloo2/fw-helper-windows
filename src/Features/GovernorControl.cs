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
        public const int MinPowerW = 8, MaxPowerW = 35;      // EC PL1 is 35 W: a target above it can't bind
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

        public static bool IsBackstopEnabled() => AppConfig.Get("temp_backstop", 1) == 1;
        public static void SetBackstopEnabled(bool on) => AppConfig.Set("temp_backstop", on ? 1 : 0);

        private static (int? powerW, int? tempC) Targets() => (PowerTarget(ModeControl.CurrentMode), TempCap());

        // ---- Lifecycle ----

        /// <summary>App start: undo anything a crashed previous run left behind, before applying the current profile.</summary>
        public static void Init()
        {
            RestoreSavedCaps("left over from a previous run");
            if (TempCap() is null || !IsBackstopEnabled()) EcBackstop.Restore();
        }

        /// <summary>Apply the current profile's targets and the global cap (startup, mode change, resume, settings change).</summary>
        public static void Apply()
        {
            var (power, temp) = Targets();

            if (temp is int cap && IsBackstopEnabled()) EcBackstop.Apply(cap);
            else EcBackstop.Restore();

            if (power is null && temp is null)
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
            _governor.Start();
            _governor.Tick();
        }

        /// <summary>Every exit path: no cap left in the user's power plan, firmware thermal config back.</summary>
        public static void Shutdown()
        {
            try { _governor?.Stop(); } catch { }
            RestoreSavedCaps(null);
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
