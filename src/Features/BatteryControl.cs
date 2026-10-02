using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    public static class BatteryControl
    {
        public const int MinLimit = 50;

        /// <summary>Configured limit, or the EC's current value if the app never set one.</summary>
        public static int GetLimit()
        {
            int saved = AppConfig.Get("charge_limit");
            if (saved >= MinLimit && saved <= 100) return saved;
            return FrameworkEc.GetChargeLimit()?.max ?? 100;
        }

        public static void SetLimit(int limit)
        {
            limit = Math.Clamp(limit, MinLimit, 100);
            AppConfig.Set("charge_limit", limit);
            FrameworkEc.SetChargeLimit(limit);
        }

        /// <summary>Re-apply on startup / resume in case the EC was reset.</summary>
        public static void AutoLimit()
        {
            int saved = AppConfig.Get("charge_limit");
            if (saved < MinLimit || saved > 100) return;
            var current = FrameworkEc.GetChargeLimit();
            if (current?.max != saved) FrameworkEc.SetChargeLimit(saved);
        }
    }
}
