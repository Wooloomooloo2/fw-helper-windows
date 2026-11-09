using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class RunContextTests
    {
        private static readonly DateTime T0 = new(2026, 11, 8, 20, 0, 0);

        [Fact]
        public void Notes_round_trip_and_leave_out_what_is_empty()
        {
            var c = new RunContext { Bios = "03.04", Ec = "sakura-3.0.4", App = "0.3.0", FanCurve = "EC auto", GameSettings = "1080p Medium" };
            string json = c.ToJson();
            Assert.DoesNotContain("notes", json);
            var back = RunContext.FromJson(json);
            Assert.Equal("03.04", back.Bios);
            Assert.Equal("1080p Medium", back.GameSettings);
            Assert.Null(back.Notes);

            Assert.Null(RunContext.Clean("   "));
            Assert.Equal("a  b", RunContext.Clean("  a  b \n"));

            string csv = @"C:\x\session-20261108-200000 Cyberpunk2077 Balanced.csv";
            Assert.Equal(@"C:\x\session-20261108-200000 Cyberpunk2077 Balanced.notes.json", RunContext.PathFor(csv));
            Assert.Equal(csv, RunContext.SessionFor(RunContext.PathFor(csv)));
            Assert.True(RunContext.IsNotesFile(RunContext.PathFor(csv)));
        }

        [Fact]
        public void Fan_is_named_after_the_saved_curve_it_matches()
        {
            var library = new[] { ("Quiet", "a"), ("Gaming", "b") };
            Assert.Equal("EC auto", RunContext.FanName(false, "b", library));
            Assert.Equal("Gaming", RunContext.FanName(true, "b", library));
            Assert.Equal("custom curve", RunContext.FanName(true, "c", library));
        }

        private static List<TelemetrySample> Rows() =>
            Enumerable.Range(0, 130).Select(i => new TelemetrySample(T0.AddSeconds(i), "Balanced", true, 60, 3000, 95, "3D", 8, 20, 32, 85,
                35, 45, 60, 4400, -1, "EC auto", 90, null, 24, null, CpuW: 8, GpuW: 11, DramW: 1.2, Fps: 55, FpsApp: "Cyberpunk2077.exe",
                WinMode: "Balanced")).ToList();

        private static RunSummary Run(string? settings, string bios = "03.04")
        {
            var s = RunSummary.From(Rows(), "x");
            s.Context = new RunContext { Bios = bios, Ec = "sakura-3.0.4", FanCurve = "EC auto", GameSettings = settings, Notes = "Dogtown <market>\nrain" };
            return s;
        }

        [Fact]
        public void Compare_lists_game_settings_and_firmware_that_differ()
        {
            var runs = new[] { new CompareEntry("A", Run("1080p Medium"), "a.csv"), new CompareEntry("B", Run("1080p High", "03.05"), "b.csv") };
            var diff = CompareReport.Differences(runs).Select(i => CompareReport.SettingNames[i]).ToList();
            Assert.Equal(new[] { "Game settings", "BIOS" }, diff);
        }

        [Fact]
        public void Summary_shows_the_notes_escaped()
        {
            var s = Run("1080p Medium");
            string html = SummaryReport.Html(Rows(), s, "a.csv", "0");
            Assert.Contains("game settings <b>1080p Medium</b>", html);
            Assert.Contains("Dogtown &lt;market&gt;<br>rain", html);
            Assert.Contains("Recorded on BIOS 03.04, EC sakura-3.0.4.", html);
        }
    }
}
