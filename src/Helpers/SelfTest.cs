using FwHelper.Features;
using FwHelper.Hardware;
using System.Text;

namespace FwHelper.Helpers
{
    /// <summary>
    /// "FwHelper.exe --selftest": exercises the EC features and writes a report to %AppData%\FwHelper\selftest.txt.
    /// Writes are reverted (fan back to auto, same charge limit / keyboard level written back).
    /// </summary>
    public static class SelfTest
    {
        public static string Run()
        {
            var sb = new StringBuilder();
            void Log(string s) { sb.AppendLine(s); Logger.WriteLine("selftest: " + s); }

            Log($"connect: {FrameworkEc.Connect()}");
            Log($"ec version: {FrameworkEc.GetVersion()}");

            var temps = FrameworkEc.GetTemperatures();
            foreach (var t in temps) Log($"temp {t.Index} {t.Name}: {t.Celsius}");
            Log($"cpu temp: {FrameworkEc.GetCpuTemp(temps)}");
            Log($"fans: {string.Join(",", FrameworkEc.GetFanRpms())} target {FrameworkEc.GetFanTargetRpm()}");

            var b = FrameworkEc.GetBattery();
            Log($"battery: {b}");
            Log($"battery %: {b?.Percent} health {b?.HealthPercent} watts {b?.Watts:0.00}");

            var limit = FrameworkEc.GetChargeLimit();
            Log($"charge limit: {limit}");
            if (limit is { } l) Log($"charge limit write-back {l.max}: {FrameworkEc.SetChargeLimit(l.max)} -> {FrameworkEc.GetChargeLimit()}");

            int? kb = FrameworkEc.GetKeyboardBacklight();
            Log($"keyboard: {kb}");
            if (kb is int k) Log($"keyboard write-back {k}: {FrameworkEc.SetKeyboardBacklight(k)} -> {FrameworkEc.GetKeyboardBacklight()}");

            Log($"power led %: {FrameworkEc.GetPowerLedPercent()}");

            Log($"fan duty 100%: {FrameworkEc.SetFanDuty(100)}");
            Thread.Sleep(4000);
            Log($"  rpm after 4s: {string.Join(",", FrameworkEc.GetFanRpms())}");
            Log($"fan duty 20%: {FrameworkEc.SetFanDuty(20)}");
            Thread.Sleep(5000);
            Log($"  rpm after 5s: {string.Join(",", FrameworkEc.GetFanRpms())}");
            Log($"fan auto: {FrameworkEc.SetFanAuto()}");
            Thread.Sleep(3000);
            Log($"  rpm after 3s: {string.Join(",", FrameworkEc.GetFanRpms())} target {FrameworkEc.GetFanTargetRpm()}");

            Log($"power overlay: {PowerNative.GetOverlayIndex()}");
            string? screen = ScreenControl.FindLaptopScreen();
            Log($"screen: {screen} now {ScreenControl.GetRefreshRate(screen)} low {ScreenControl.GetLowRefreshRate(screen)} max {ScreenControl.GetMaxRefreshRate(screen)}");
            Log($"admin: {ProcessHelper.IsUserAdministrator()} power limits init: {IntelPowerLimits.Init()} ({IntelPowerLimits.Status}) {IntelPowerLimits.Get()}");

            var curve = FanCurve.Default(Modes.Balanced);
            Log($"curve {curve}: 30->{curve.DutyAt(30)} 55->{curve.DutyAt(55)} 82->{curve.DutyAt(82)} 99->{curve.DutyAt(99)}");

            string report = sb.ToString();
            File.WriteAllText(Path.Combine(Logger.AppDataDir, "selftest.txt"), report);
            return report;
        }
    }
}
