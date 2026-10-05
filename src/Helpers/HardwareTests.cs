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
        public static readonly string[] Names = { "fansweep", "watchdog", "governor" };

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
                        case "governor": GovernorTest(report); break;
                    }
                }
                catch (Exception ex)
                {
                    report.Line("ERROR: " + ex);
                }
                finally
                {
                    FrameworkEc.SetFanAuto();
                    GovernorControl.RestoreSavedCaps("after hwtest");
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

        // ---------------- governor ----------------

        /// <summary>
        /// ADR 0016's open question first: does a power-plan frequency cap bind on this CPU at all? Then: does the governor hold a
        /// 15 W target? Sustained all-core load, measured with Energy Meter (no driver). Restores the user's caps afterwards.
        /// </summary>
        private static void GovernorTest(Report r)
        {
            var original = PowerPlan.Read();
            r.Line($"plan caps before: {original}");
            try
            {
                r.Line("phase,package_w,cores_w,effective_mhz,max_cpu_c,ec_throttle");
                PowerPlan.SetCap(0);
                var stock = Measure(null);
                r.Line($"no cap,{stock}");
                Cool();

                PowerPlan.SetCap(2000);
                var capped = Measure(null);
                r.Line($"cap 2000 MHz,{capped}");
                Cool();
                PowerPlan.SetCap(0);

                var governed = Measure(15);
                r.Line($"governor 15 W,{governed}");

                bool binds = capped.Watts < stock.Watts * 0.8 && capped.Mhz < 2400;
                bool holds = Math.Abs(governed.Watts - 15) <= 2;
                r.Verdict = (binds ? "PASS: frequency cap binds" : "FAIL: frequency cap does not bind (see ADR 0016 fallback)") + "; " +
                            (holds ? "PASS: governor holds 15 W" : $"FAIL: governor gave {governed.Watts:0.0} W for 15 W");
            }
            finally
            {
                if (original is not null) PowerPlan.Write(original);
                r.Line($"plan caps after: {PowerPlan.Read()}");
            }
        }

        private readonly record struct Measurement(double Watts, double Cores, double Mhz, int? MaxTemp, string Throttle)
        {
            public override string ToString() => $"{Watts:0.0},{Cores:0.0},{Mhz:0},{MaxTemp},{Throttle}";
        }

        /// <summary>45 s all-core load (optionally under a governor power target); means over the last 15 s.</summary>
        private static Measurement Measure(int? powerTarget)
        {
            using var cts = new CancellationTokenSource();
            var workers = Enumerable.Range(0, Environment.ProcessorCount).Select(_ => Task.Factory.StartNew(() =>
            {
                double x = 1.0001;
                while (!cts.IsCancellationRequested) for (int i = 0; i < 100_000; i++) x = Math.Sqrt(x * 1.0000001 + i);
                return x;
            }, TaskCreationOptions.LongRunning)).ToArray();

            using var hw = new EcGovernorHardware();
            using var governor = powerTarget is null ? null : new Governor(hw, () => (powerTarget, null), GovernorControl.CpuMaxMHz, log: s => Logger.WriteLine("hwtest governor: " + s));
            governor?.Start();
            using var meter = new EnergyMeter();
            using var metrics = new SystemMetrics();

            var watts = new List<double>(); var cores = new List<double>(); var mhz = new List<double>();
            int? maxTemp = null;
            bool hard = false;
            for (int s = 0; s < 45; s++)
            {
                Thread.Sleep(1000);
                var p = meter.Read();
                var sys = metrics.Sample();
                int? t = FrameworkEc.GetCpuTemp();
                if (t > maxTemp || maxTemp is null) maxTemp = t;
                if (FrameworkEc.GetApThrottleStatus() is (_, true)) hard = true;
                if (s >= 30)
                {
                    if (p.PackageW is double w) watts.Add(w);
                    if (p.CoresW is double c) cores.Add(c);
                    if (sys.CpuMhz is double m) mhz.Add(m);
                }
                if (t >= 95) break;
            }
            governor?.Stop();
            cts.Cancel();
            Task.WaitAll(workers);
            static double Avg(List<double> l) => l.Count > 0 ? l.Average() : double.NaN;
            return new Measurement(Avg(watts), Avg(cores), Avg(mhz), maxTemp, hard ? "EC PROCHOT seen" : "");
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
