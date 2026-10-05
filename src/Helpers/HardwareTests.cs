using FwHelper.Features;
using FwHelper.Hardware;
using System.Globalization;
using System.Text;

namespace FwHelper.Helpers
{
    /// <summary>
    /// "FwHelper.exe --hwtest &lt;name&gt;": targeted hardware experiments that answer open questions in docs/hardware-test-plan.md.
    /// Each writes %AppData%\FwHelper\hwtest-&lt;name&gt;.txt and always leaves the fan with the EC and PL MSR as it found it.
    /// </summary>
    public static class HardwareTests
    {
        public const string Arg = "--hwtest";
        public static readonly string[] Names = { "fansweep", "watchdog", "pl" };

        public static void Run(string[] args)
        {
            int index = Array.IndexOf(args, Arg);
            string name = index + 1 < args.Length ? args[index + 1].ToLowerInvariant() : "";
            var tests = name == "all" ? Names : Names.Contains(name) ? new[] { name } : Array.Empty<string>();
            if (tests.Length == 0)
            {
                MessageBox.Show($"Usage: FwHelper.exe {Arg} <{string.Join("|", Names)}|all>", "FW-Helper hardware test");
                return;
            }

            if (!FrameworkEc.Connect())
            {
                MessageBox.Show("Can't reach the Framework EC.", "FW-Helper hardware test");
                return;
            }

            var summary = new StringBuilder();
            foreach (var test in tests)
            {
                var report = new Report(test);
                try
                {
                    switch (test)
                    {
                        case "fansweep": FanSweep(report); break;
                        case "watchdog": Watchdog(report); break;
                        case "pl": PowerLimits(report); break;
                    }
                }
                catch (Exception ex)
                {
                    report.Line("ERROR: " + ex);
                }
                finally
                {
                    FrameworkEc.SetFanAuto();
                    if (IntelPowerLimits.IsAvailable) IntelPowerLimits.Restore();
                }
                summary.AppendLine($"{test}: {report.Verdict}");
                report.Save();
            }

            MessageBox.Show(summary + $"\nReports: {Logger.AppDataDir}\\hwtest-*.txt", "FW-Helper hardware test");
        }

        // ---------------- fansweep ----------------

        // Linux duty(/255) → rpm, docs/hardware-baseline.md
        private static readonly (int duty255, int rpm)[] LinuxTable =
        {
            (0, 0), (20, 0), (30, 1107), (40, 1512), (50, 1879), (65, 2296), (77, 2693),
            (90, 3052), (100, 3355), (120, 3840), (150, 4551), (180, 5201),
        };

        /// <summary>Duty % → rpm table, to settle whether Windows' percent and Linux's /255 are the same scale.</summary>
        private static void FanSweep(Report r)
        {
            int[] duties = { 0, 5, 8, 10, 12, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90, 100 };
            r.Line("duty_pct,rpm,linux_rpm_same_duty,cpu_c");
            foreach (int duty in duties)
            {
                if (FrameworkEc.GetCpuTemp() >= 90) { r.Line("aborted: CPU ≥ 90°C"); r.Verdict = "aborted (hot)"; return; }
                FrameworkEc.SetFanDuty(duty);
                var samples = new List<int>();
                for (int s = 0; s < 8; s++)
                {
                    Thread.Sleep(1000);
                    samples.Add(FrameworkEc.GetFanRpms().FirstOrDefault());
                }
                int rpm = (int)samples.TakeLast(3).Average();
                string linux = LinuxRpm(duty * 255 / 100) is int lr ? lr.ToString() : "n/a";
                r.Line($"{duty},{rpm},{linux},{FrameworkEc.GetCpuTemp()}");
            }
            r.Verdict = "done, compare rpm with linux_rpm_same_duty";
        }

        private static int? LinuxRpm(int duty255)
        {
            for (int i = 1; i < LinuxTable.Length; i++)
            {
                var (d0, r0) = LinuxTable[i - 1];
                var (d1, r1) = LinuxTable[i];
                if (duty255 <= d1) return r0 + (r1 - r0) * (duty255 - d0) / (d1 - d0);
            }
            return null;
        }

        // ---------------- watchdog ----------------

        /// <summary>Stall the fan loop for 9 s; the watchdog must hand the fan to the EC, and the loop must take it back after.</summary>
        private static void Watchdog(Report r)
        {
            FanControl.Start(FanCurve.Parse("40:30,50:30,60:40,70:60,80:80,85:90,90:95,95:100")!);
            Thread.Sleep(3000);
            r.Line($"before stall: status '{FanControl.Status}', duty {FanControl.LastDuty}%");
            bool sawCurve = FanControl.LastDuty >= 30;

            var stall = Task.Run(() => FanControl.StallLoopForTest(TimeSpan.FromSeconds(9)));
            string? during = null;
            for (int i = 0; i < 9 && during is null; i++)
            {
                Thread.Sleep(1000);
                if (FanControl.Status == "EC: watchdog") during = $"watchdog fired after ~{i + 1} s";
            }
            stall.Wait();
            r.Line("during stall: " + (during ?? "watchdog did NOT fire"));

            Thread.Sleep(3000);
            r.Line($"after stall: status '{FanControl.Status}', duty {FanControl.LastDuty}%");
            bool recovered = FanControl.LastDuty >= 30 && FanControl.Status != "EC: watchdog";
            FanControl.Stop();

            r.Verdict = sawCurve && during is not null && recovered ? "PASS" : "FAIL";
        }

        // ---------------- pl ----------------

        /// <summary>Does writing MSR 0x610 through PawnIO actually bind package power? Sustained all-core load, > PL1 window (32 s).</summary>
        private static void PowerLimits(Report r)
        {
            if (!ProcessHelper.IsUserAdministrator()) { r.Line("needs an elevated prompt"); r.Verdict = "skipped (not admin)"; return; }
            if (!IntelPowerLimits.Init()) { r.Line("init: " + IntelPowerLimits.Status); r.Verdict = "skipped (" + IntelPowerLimits.Status + ")"; return; }

            r.Line($"status {IntelPowerLimits.Status}, MSR now {IntelPowerLimits.Get()}, overlay {PowerNative.GetOverlayIndex()}");
            r.Line("phase,setpoint_pl1,msr_after,measured_pkg_w,max_cpu_c,core_limit_reasons");

            var baseline = Measure();
            r.Line($"stock,-,{IntelPowerLimits.Get()},{baseline.watts:0.0},{baseline.maxTemp},{baseline.limits}");
            Cool();

            var results = new List<(int target, double watts)>();
            foreach (var (pl1, pl2) in new[] { (15, 30), (25, 60) })
            {
                IntelPowerLimits.Set(pl1, pl2);
                var m = Measure();
                r.Line($"set,{pl1},{IntelPowerLimits.Get()},{m.watts:0.0},{m.maxTemp},{m.limits}");
                results.Add((pl1, m.watts));
                Cool();
            }
            IntelPowerLimits.Restore();

            bool binds = results.All(x => Math.Abs(x.watts - x.target) <= 2);
            r.Verdict = binds ? "PASS: MSR 0x610 binds package power"
                : $"FAIL: measured {string.Join(" / ", results.Select(x => $"{x.watts:0.0}W for {x.target}W"))}; 0x610 likely not governing (see ADR 0006)";
        }

        /// <summary>45 s all-core load; mean package power over the last 12 s.</summary>
        private static (double watts, int? maxTemp, string limits) Measure()
        {
            using var cts = new CancellationTokenSource();
            var workers = Enumerable.Range(0, Environment.ProcessorCount).Select(_ => Task.Factory.StartNew(() =>
            {
                double x = 1.0001;
                while (!cts.IsCancellationRequested) for (int i = 0; i < 100_000; i++) x = Math.Sqrt(x * 1.0000001 + i);
                return x;
            }, TaskCreationOptions.LongRunning)).ToArray();

            var watts = new List<float>();
            int? maxTemp = null;
            IntelPowerLimits.GetPackagePower(); // prime the energy counter
            for (int s = 0; s < 45; s++)
            {
                Thread.Sleep(1000);
                int? t = FrameworkEc.GetCpuTemp();
                if (t > maxTemp || maxTemp is null) maxTemp = t;
                if (IntelPowerLimits.GetPackagePower() is float w && s >= 33) watts.Add(w);
                if (t >= 95) break;
            }
            // Still under load: what is limiting the cores (EDP is Linux's only live reason; PL1 means our limit binds)
            string limits = IntelPowerLimits.Read(PerfLimitReasons.MSR_CORE_PERF_LIMIT_REASONS) is ulong v ? PerfLimitReasons.Decode(v) : "n/a";
            cts.Cancel();
            Task.WaitAll(workers);
            return (watts.Count > 0 ? watts.Average() : double.NaN, maxTemp, limits);
        }

        private static void Cool() => Thread.Sleep(15000);

        // ---------------- report ----------------

        private sealed class Report(string name)
        {
            private readonly StringBuilder _sb = new();
            public string Verdict { get; set; } = "no verdict";

            public void Line(string s)
            {
                _sb.AppendLine(s);
                Logger.WriteLine($"hwtest {name}: {s}");
            }

            public void Save()
            {
                _sb.AppendLine($"verdict: {Verdict}");
                _sb.Insert(0, $"FW-Helper {Application.ProductVersion} hwtest {name} {DateTime.Now.ToString("s", CultureInfo.InvariantCulture)}{Environment.NewLine}");
                File.WriteAllText(Path.Combine(Logger.AppDataDir, $"hwtest-{name}.txt"), _sb.ToString());
                Logger.WriteLine($"hwtest {name}: {Verdict}");
            }
        }
    }
}
