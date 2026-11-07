using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class CompareTests
    {
        private static readonly DateTime T0 = new(2026, 11, 4, 18, 31, 0);

        private static TelemetrySample Row(DateTime start, int second, double fps, double pkg, double gpuW, string mode, string? winMode,
            string throttle, bool onAc, string app) =>
            new(start.AddSeconds(second), mode, onAc, 60, 3000, 95, "3D", 8, 20, 32, 85, 35, 45, 60, 4400, -1, "EC auto", 90, null,
                pkg, null, CpuW: 8, GpuW: gpuW, DramW: 1.2, Fps: fps, FpsApp: app,
                GpuReqMhz: 4250, GpuMhz: 2000, GpuThrottle: throttle, WinMode: winMode);

        private static CompareEntry Run(string name, double fps, double pkg, string mode = "Turbo", string? winMode = "Performance",
            string throttle = "PL1", int seconds = 130, bool onAc = true, string app = "Cyberpunk2077.exe", DateTime? start = null)
        {
            var t = start ?? T0;
            var rows = Enumerable.Range(0, seconds).Select(i => Row(t, i, fps, pkg, pkg - 14, mode, winMode, throttle, onAc, app)).ToList();
            return new CompareEntry(name, RunSummary.From(rows, name), $"session-{name}.csv");
        }

        [Fact]
        public void Project_round_trips_and_keeps_the_baseline_inside_the_list()
        {
            var p = new CompareProject { Name = "Cyberpunk A/B", Baseline = 1, Runs = { new("a.csv", "A: Balanced"), new(@"D:\x\b.csv", "B: Perf") } };
            var back = CompareProject.FromJson(p.ToJson());
            Assert.Equal("Cyberpunk A/B", back.Name);
            Assert.Equal(1, back.Baseline);
            Assert.Equal(p.Runs, back.Runs);

            var bad = CompareProject.FromJson("""{"name":"x","baseline":7,"runs":[{"file":"a.csv","name":null},{"file":"","name":"gone"}]}""");
            Assert.Single(bad.Runs);
            Assert.Equal("", bad.Runs[0].Name);
            Assert.Equal(0, bad.Baseline);
        }

        [Fact]
        public void Recordings_in_the_sessions_folder_are_stored_by_name()
        {
            string folder = Path.Combine(Path.GetTempPath(), "FW Benchmark");
            Assert.Equal("session-1.csv", CompareProject.Store(folder, Path.Combine(folder, "session-1.csv")));
            Assert.Equal(Path.Combine(folder, "session-1.csv"), CompareProject.Resolve(folder, "session-1.csv"));
            string elsewhere = Path.Combine(Path.GetTempPath(), "other", "session-2.csv");
            Assert.Equal(elsewhere, CompareProject.Store(folder, elsewhere));
            Assert.Equal(elsewhere, CompareProject.Resolve(folder, elsewhere));
            Assert.EndsWith(Path.Combine("projects", "A B.json"), CompareProject.PathFor(CompareProject.FolderIn(folder), "A:B"));
        }

        [Fact]
        public void A_run_is_named_after_its_recording_or_its_game_and_time()
        {
            Assert.Equal("cyber punk bal", CompareProject.DefaultName("session-20261104-172058cyber_punk_bal.csv", null));
            var s = Run("x", 50, 30).S;
            Assert.Equal("Cyberpunk2077 Turbo 4 Nov 18:31", CompareProject.DefaultName("session-20261104-183100.csv", s));
        }

        [Fact]
        public void Only_settings_that_differ_are_listed()
        {
            var runs = new[] { Run("A", 43, 24, "Balanced", "Balanced", "PL4"), Run("B", 51, 30, "Turbo", "Performance", "PL1") };
            var diff = CompareReport.Differences(runs).Select(i => CompareReport.SettingNames[i]).ToList();
            Assert.Equal(new[] { "Profile", "Windows mode" }, diff);
        }

        [Fact]
        public void Conclusions_give_the_numbers_against_the_baseline()
        {
            var runs = new[] { Run("A", 40, 24, "Balanced", "Balanced", "PL4"), Run("B", 50, 32, "Turbo", "Performance", "PL1") };
            var lines = CompareReport.Conclusions(runs, 0);
            Assert.Equal(2, lines.Count);
            Assert.Contains("B against A: +25 % FPS (40 → 50)", lines[0]);
            Assert.Contains("+8.0 W package (24.0 → 32.0 W)", lines[0]);
            Assert.Contains("held mostly by PL1 instead of PL4", lines[0]);
            Assert.Contains("Fastest: B (50 FPS). Most efficient: A", lines[1]); // 40/24 = 1.67 FPS/W beats 50/32 = 1.56
        }

        [Fact]
        public void Fastest_and_most_efficient()
        {
            var runs = new[] { Run("A", 40, 20), Run("B", 50, 30), Run("C", 45, 22) };
            Assert.Equal(1, CompareReport.Fastest(runs));
            Assert.Equal(2, CompareReport.MostEfficient(runs)); // 45/22 = 2.05 beats 40/20 = 2.0
            Assert.Equal("+25 %", CompareReport.Signed(CompareReport.Change(50, 40)));
            Assert.Equal("−20 %", CompareReport.Signed(CompareReport.Change(40, 50)));
            Assert.Equal("same", CompareReport.Signed(CompareReport.Change(40.1, 40)));
            Assert.Null(CompareReport.Change(1, 0));
        }

        [Fact]
        public void Warnings_flag_runs_that_dont_compare()
        {
            var runs = new[]
            {
                Run("A", 50, 30),
                Run("B", 40, 20, app: "HorizonZeroDawn.exe", onAc: false, seconds: 40, start: T0.AddDays(1)),
            };
            var w = CompareReport.Warnings(runs);
            Assert.Contains(w, x => x.Contains("different games"));
            Assert.Contains(w, x => x.Contains("battery and some on AC"));
            Assert.Contains(w, x => x.Contains("differ a lot in length"));
            Assert.Contains(w, x => x.StartsWith("B has under 60 s"));
            Assert.Contains(w, x => x.Contains("different days"));
            Assert.DoesNotContain(w, x => x.StartsWith("B: The run was on battery"));

            // A warning every run has is said once
            var old = CompareReport.Warnings(new[] { Run("A", 50, 30, winMode: null), Run("B", 52, 31, winMode: null) });
            Assert.Equal("Every run: This recording is from before FW-Helper recorded the Windows power mode, so it isn't known.", Assert.Single(old));

            Assert.Empty(CompareReport.Warnings(new[] { Run("A", 50, 30), Run("B", 52, 31) }));
        }

        [Fact]
        public void Report_is_self_contained_escaped_and_names_its_project()
        {
            var runs = new[] { Run("A <b>", 40, 24, "Balanced", "Balanced"), Run("B & co", 50, 30) };
            string html = CompareReport.Html("Cyber <script>", runs, 0, "cyber.json", "0.3.0");
            Assert.Contains("<meta name=\"fwhelper-project\" content=\"cyber.json\">", html);
            Assert.DoesNotContain("<script", html);
            Assert.DoesNotContain("A <b>", html);
            Assert.Contains("A &lt;b&gt;", html);
            Assert.DoesNotContain("http", html.Replace("http://www.w3.org", ""));
            Assert.Contains("What differs", html);
            Assert.Contains("+25 %", html);

            Assert.Contains("A comparison needs at least two runs", CompareReport.Html("x", Array.Empty<CompareEntry>(), 0, "x.json", "0"));
        }
    }
}
