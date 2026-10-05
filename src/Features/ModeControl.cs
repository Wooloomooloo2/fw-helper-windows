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

        public static int SavedMode()
        {
            int mode = AppConfig.Get(PowerKey, Modes.Balanced);
            return Modes.Exists(mode) ? mode : Modes.Balanced;
        }

        public static void SetMode(int mode, bool save = true)
        {
            if (!Modes.Exists(mode)) mode = Modes.Balanced;
            CurrentMode = mode;
            if (save) AppConfig.Set(PowerKey, mode);

            Logger.WriteLine($"Mode: {Modes.Name(mode)} ({(PowerNative.IsOnAC() ? "AC" : "battery")})");

            PowerNative.SetOverlay(Modes.GetOverlay(mode));
            ApplyFan();
            ApplyPowerLimits();

            ModeChanged?.Invoke();
        }

        /// <summary>Same order as G-Helper's Fn+F5 (Balanced → Turbo → Silent), then user profiles.</summary>
        public static void CycleMode() => SetMode(ProfileList.Next(CurrentMode, Modes.UserIds()));

        /// <summary>After a profile is deleted: if it was active, fall back to what's saved for this power source.</summary>
        public static void OnProfileDeleted(int mode)
        {
            if (CurrentMode == mode) AutoMode();
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

        public static void ApplyPowerLimits() => GovernorControl.Apply();
    }
}
