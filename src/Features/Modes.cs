using FwHelper.Helpers;

namespace FwHelper.Features
{
    public static class Modes
    {
        public const int Silent = 0;
        public const int Balanced = 1;
        public const int Turbo = 2;
        public const int Count = 3;

        public static readonly string[] Names = { "Silent", "Balanced", "Turbo" };

        public static readonly Color[] Colors =
        {
            Color.FromArgb(6, 180, 138),   // silent – green
            Color.FromArgb(58, 174, 239),  // balanced – blue
            Color.FromArgb(255, 32, 32),   // turbo – red
        };

        public static string Name(int mode) => Names[Math.Clamp(mode, 0, Count - 1)];

        // ---- Per-mode settings ----

        /// <summary>Index into <see cref="PowerNative.Overlays"/>.</summary>
        public static int GetOverlay(int mode) => AppConfig.Get(AppConfig.ModeKey("overlay", mode), mode);
        public static void SetOverlay(int mode, int overlay) => AppConfig.Set(AppConfig.ModeKey("overlay", mode), overlay);

        public static bool IsCustomFan(int mode) => AppConfig.Get(AppConfig.ModeKey("fan_custom", mode), 0) == 1;
        public static void SetCustomFan(int mode, bool on) => AppConfig.Set(AppConfig.ModeKey("fan_custom", mode), on ? 1 : 0);

        public static FanCurve GetCurve(int mode) =>
            FanCurve.Parse(AppConfig.GetString(AppConfig.ModeKey("fan_curve", mode))) ?? FanCurve.Default(mode);
        public static void SetCurve(int mode, FanCurve curve) => AppConfig.Set(AppConfig.ModeKey("fan_curve", mode), curve.ToString());

        public static bool IsPowerLimit(int mode) => AppConfig.Get(AppConfig.ModeKey("pl_custom", mode), 0) == 1;
        public static void SetPowerLimit(int mode, bool on) => AppConfig.Set(AppConfig.ModeKey("pl_custom", mode), on ? 1 : 0);

        // Defaults from the Linux measurements (docs/hardware-baseline.md): real PL1 ceiling ~35 W, stock PL2 60 W,
        // 25 W is the gaming sweet spot (same fps as 35 W, quieter)
        public static int GetPL1(int mode) => AppConfig.Get(AppConfig.ModeKey("pl1", mode), mode switch { Silent => 15, Turbo => 35, _ => 25 });
        public static int GetPL2(int mode) => AppConfig.Get(AppConfig.ModeKey("pl2", mode), mode switch { Silent => 30, Turbo => 64, _ => 60 });
        public static void SetPL(int mode, int pl1, int pl2)
        {
            AppConfig.Set(AppConfig.ModeKey("pl1", mode), pl1);
            AppConfig.Set(AppConfig.ModeKey("pl2", mode), pl2);
        }
    }
}
