using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// Applies a performance mode: Windows power mode, fan behaviour and (optionally) CPU power limits.
    /// Like G-Helper, the selected mode is remembered separately for AC and battery.
    /// </summary>
    public static class ModeControl
    {
        public static int CurrentMode { get; private set; } = Modes.Balanced;

        public static event Action? ModeChanged;

        private static string PowerKey => PowerNative.IsOnAC() ? "mode_ac" : "mode_dc";

        public static int SavedMode() => Math.Clamp(AppConfig.Get(PowerKey, Modes.Balanced), 0, Modes.Count - 1);

        public static void SetMode(int mode, bool save = true)
        {
            mode = Math.Clamp(mode, 0, Modes.Count - 1);
            CurrentMode = mode;
            if (save) AppConfig.Set(PowerKey, mode);

            Logger.WriteLine($"Mode: {Modes.Name(mode)} ({(PowerNative.IsOnAC() ? "AC" : "battery")})");

            PowerNative.SetOverlay(Modes.GetOverlay(mode));
            ApplyFan();
            ApplyPowerLimits();

            ModeChanged?.Invoke();
        }

        public static void CycleMode()
        {
            // Same order as G-Helper's Fn+F5: Balanced → Turbo → Silent
            int next = CurrentMode switch
            {
                Modes.Balanced => Modes.Turbo,
                Modes.Turbo => Modes.Silent,
                _ => Modes.Balanced,
            };
            SetMode(next);
        }

        /// <summary>Re-apply saved mode for the current power source (startup, AC plug/unplug, resume).</summary>
        public static void AutoMode() => SetMode(SavedMode(), save: false);

        public static void ApplyFan()
        {
            if (Modes.IsCustomFan(CurrentMode))
                FanControl.Start(Modes.GetCurve(CurrentMode));
            else
                FanControl.Stop();
        }

        public static void ApplyPowerLimits() => PowerLimitControl.Apply(CurrentMode);
    }
}
