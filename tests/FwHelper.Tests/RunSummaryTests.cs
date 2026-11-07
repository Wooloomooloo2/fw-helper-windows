using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class RunSummaryTests
    {
        private static readonly DateTime T0 = new(2026, 11, 4, 18, 31, 0);

        private static TelemetrySample Row(int second, double? fps, string? app = "Game.exe", double pkg = 30, string? gpuThrottle = "PL1",
            string mode = "Turbo", string? winMode = "Performance", bool onAc = true) =>
            new(T0.AddSeconds(second), mode, onAc, 60, 3000, 95, "3D", 8, 20, 32, 85, 35, 45, 60, 4400, -1, "EC auto", 90, null,
                pkg, null, CpuW: 8, GpuW: 16, DramW: 1.2, Fps: fps, FpsApp: fps is null ? null : app,
                GpuReqMhz: 4250, GpuMhz: 2000, GpuThrottle: gpuThrottle, WinMode: winMode);

        /// <summary>5 s of menu, then the game at 50 FPS for 100 s, then the desktop.</summary>
        private static List<TelemetrySample> Run()
        {
            var rows = new List<TelemetrySample>();
            for (int i = 0; i < 5; i++) rows.Add(Row(i, 200, "Launcher.exe", pkg: 12));
            for (int i = 5; i < 105; i++) rows.Add(Row(i, i < 15 ? 300 : 50));
            for (int i = 105; i < 110; i++) rows.Add(Row(i, null, pkg: 5));
            return rows;
        }

        [Fact]
        public void Steady_state_is_the_game_presenting_after_the_trim()
        {
            var s = RunSummary.From(Run());
            Assert.Equal("Game.exe", s.Game);
            Assert.True(s.Trimmed);
            Assert.Equal(15, s.SteadyFrom);   // game starts at 5 s, +10 s trim: the 300 FPS loading burst is gone
            Assert.Equal(104, s.SteadyTo);
            Assert.Equal(90, s.Steady.Count);
            Assert.Equal(50, s["fps"]!.Median);
            Assert.Equal(50, s["fps"]!.Max);
            Assert.Equal(30, s["package_w"]!.Avg, 3);
            Assert.Equal(6, s["rest_w"]!.Avg, 3);  // 30 − 8 − 16
            Assert.Equal(50 / 30.0, s.FpsPerWatt!.Value, 3);
            Assert.Equal(TimeSpan.FromSeconds(109), s.Length);
        }

        [Fact]
        public void Shares_of_throttle_reasons_and_modes()
        {
            var rows = Run();
            for (int i = 15; i < 45; i++) rows[i] = rows[i] with { GpuThrottle = "PL4" };
            var s = RunSummary.From(rows);
            Assert.Equal("PL1", s.GpuThrottle[0].Value);
            Assert.Equal(60 / 90.0, s.GpuThrottle[0].Fraction, 3);
            Assert.Equal(30 / 90.0, s.GpuThrottle.Single(t => t.Value == "PL4").Fraction, 3);
            Assert.Single(s.Modes);
            Assert.Equal("Performance", s.WinModes.Single().Value);
            Assert.Empty(s.Warnings);
        }

        [Fact]
        public void Menu_bursts_are_left_out_but_dips_stay()
        {
            var rows = Run();
            rows[103] = rows[103] with { Fps = 390 };   // the game's menu after quitting
            rows[104] = rows[104] with { Fps = 101 };
            rows[50] = rows[50] with { Fps = 12 };      // a real stutter
            var s = RunSummary.From(rows);
            Assert.Equal(2, s.MenuSeconds);
            Assert.Equal(88, s.Steady.Count);
            Assert.Equal(50, s["fps"]!.Max);
            Assert.Equal(12, s["fps"]!.Min);
        }

        [Fact]
        public void A_run_too_short_to_trim_keeps_every_game_second()
        {
            var rows = Enumerable.Range(0, 25).Select(i => Row(i, 40)).ToList();
            var s = RunSummary.From(rows);
            Assert.False(s.Trimmed);
            Assert.Equal(25, s.Steady.Count);
            Assert.Contains(s.Warnings, w => w.Contains("5th percentile"));
        }

        [Fact]
        public void Without_a_game_every_second_counts()
        {
            var rows = Enumerable.Range(0, 80).Select(i => Row(i, null)).ToList();
            var s = RunSummary.From(rows);
            Assert.Null(s.Game);
            Assert.Equal(70, s.Steady.Count); // still trimmed
            Assert.Null(s["fps"]);
            Assert.Null(s.FpsPerWatt);
            Assert.Contains(s.Warnings, w => w.Contains("No game"));
        }

        [Fact]
        public void Setup_problems_are_warned_about()
        {
            var rows = Run();
            for (int i = 60; i < 105; i++) rows[i] = rows[i] with { OnAC = false, WinMode = "Battery Saver", Mode = "Silent" };
            var s = RunSummary.From(rows);
            Assert.Contains(s.Warnings, w => w.Contains("unplugged"));
            Assert.Contains(s.Warnings, w => w.Contains("profile changed"));
            Assert.Contains(s.Warnings, w => w.Contains("Windows power mode changed"));
            Assert.Contains(s.Warnings, w => w.Contains("Battery Saver"));
        }

        [Fact]
        public void Old_recordings_without_the_windows_mode_say_so()
        {
            var rows = Run().Select(r => r with { WinMode = null }).ToList();
            Assert.Contains(RunSummary.From(rows).Warnings, w => w.Contains("before FW-Helper recorded the Windows power mode"));
        }

        [Fact]
        public void Empty_recording()
        {
            var s = RunSummary.From(new List<TelemetrySample>());
            Assert.Single(s.Warnings);
            Assert.Empty(s.Steady);
        }

        [Theory]
        [InlineData(@"C:\x\session-20261104-172058cyber_punk_bal.csv", "cyber punk bal")]
        [InlineData("session-20261104-172058 Tomb Raider.csv", "Tomb Raider")]
        [InlineData("session-20261104-172058.csv", null)]
        [InlineData("other.csv", null)]
        public void Label_from_a_renamed_file(string path, string? label) => Assert.Equal(label, RunSummary.LabelFromFileName(path));

        [Theory]
        [InlineData("Cyberpunk 1080p medium", "session-20261107-140512 Cyberpunk 1080p medium.csv", "Cyberpunk 1080p medium")]
        [InlineData("  HZD: FSR/off?  ", "session-20261107-140512 HZD FSR off.csv", "HZD FSR off")]
        [InlineData("run 2...", "session-20261107-140512 run 2.csv", "run 2")]
        [InlineData("", "session-20261107-140512.csv", null)]
        [InlineData(null, "session-20261107-140512.csv", null)]
        [InlineData("<>|", "session-20261107-140512.csv", null)]
        public void A_recording_name_goes_into_the_file_and_back_out_as_the_label(string? name, string file, string? label)
        {
            string f = SessionRecorder.FileName(new DateTime(2026, 11, 7, 14, 5, 12), name);
            Assert.Equal(file, f);
            Assert.Equal(label, RunSummary.LabelFromFileName(f));
        }

        [Fact]
        public void Long_recording_names_are_cut() =>
            Assert.Equal(SessionRecorder.MaxNameLength, SessionRecorder.CleanName(new string('a', 200)).Length);

        [Fact]
        public void Percentiles_interpolate()
        {
            double[] v = { 10, 20, 30, 40, 50 };
            Assert.Equal(30, RunSummary.Percentile(v, 50));
            Assert.Equal(12, RunSummary.Percentile(v, 5), 6);
            Assert.Equal(10, RunSummary.Percentile(v, 0));
            Assert.Equal(50, RunSummary.Percentile(v, 100));
        }

        [Fact]
        public void Report_is_self_contained_and_names_its_session()
        {
            var rows = Run();
            rows[20] = rows[20] with { FpsApp = "</script><b>x" };
            var s = RunSummary.From(rows, "1080p <medium>");
            string html = SummaryReport.Html(rows, s, "session-20261104-183100 a&b.csv", "0.3.0");
            Assert.StartsWith("<!doctype html>", html);
            Assert.DoesNotContain("http://", html.Replace("http://www.w3.org/2000/svg", ""));
            Assert.DoesNotContain("https://", html);
            Assert.DoesNotContain("</script><b>", html);           // JSON can't close the script element
            Assert.Contains("1080p &lt;medium&gt;", html);
            Assert.Equal("session-20261104-183100 a&b.csv", SummaryReport.SessionOf(html));
            Assert.Contains("Median FPS", html);
            Assert.Contains("PL1", html);
            Assert.Null(SummaryReport.SessionOf("<html></html>"));
        }

        [Fact]
        public void Report_of_an_empty_or_gameless_run_still_renders()
        {
            var none = new List<TelemetrySample>();
            Assert.Contains("<!doctype html>", SummaryReport.Html(none, RunSummary.From(none), "s.csv", "x"));
            var idle = Enumerable.Range(0, 30).Select(i => Row(i, null)).ToList();
            Assert.DoesNotContain("Median FPS", SummaryReport.Html(idle, RunSummary.From(idle), "s.csv", "x"));
        }

        [Fact]
        public void Report_path_is_next_to_the_csv() =>
            Assert.Equal(@"C:\b\session-1.html", SummaryReport.PathFor(@"C:\b\session-1.csv"));
    }
}
