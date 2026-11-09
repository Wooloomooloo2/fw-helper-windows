using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class FrameTimeTests
    {
        private static readonly DateTime T0 = new(2026, 11, 8, 20, 0, 0);

        [Fact]
        public void Frames_file_round_trips_skips_gaps_and_restarts_on_a_new_app()
        {
            string dir = Path.Combine(Path.GetTempPath(), "fwh-frames-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string csv = Path.Combine(dir, "session-20261108-200000 Cyberpunk2077 Balanced.csv");
                string path = FrameLog.PathFor(csv);
                Assert.EndsWith("Balanced.frames.csv", path);
                Assert.Equal(csv, FrameLog.SessionFor(path));
                Assert.True(FrameLog.IsFrameFile(path));
                Assert.False(FrameLog.IsFrameFile(csv));

                var origin = T0.AddSeconds(-100); // ETW time 0
                using (var log = new FrameLog(path, T0))
                {
                    log.Write(7, new[] { 100_000.0, 100_016.5, 100_033.0 }, origin);  // 2 frames
                    log.Write(7, new[] { 100_050.0, 102_000.0, 102_020.0 }, origin);  // 17 ms, then a 1.95 s gap (not a frame), then 20 ms
                    log.Write(9, new[] { 102_030.0, 102_040.0 }, origin);             // new app: the first present has no frame time
                    Assert.Equal(5, log.Frames);
                    Assert.Equal(9, log.Pid);
                    Assert.Equal(102_040.0, log.LastMs);
                }
                var frames = FrameLog.Load(path, T0);
                Assert.Equal(new[] { 16.5, 16.5, 17.0, 20.0, 10.0 }, frames.Select(f => f.Ms).ToArray());
                Assert.Equal(T0.AddMilliseconds(16.5), frames[0].Time, TimeSpan.FromMilliseconds(0.1));
                Assert.Equal(T0.AddMilliseconds(2040), frames[^1].Time, TimeSpan.FromMilliseconds(0.1));

                // No game, no frames: no file left behind
                string empty = FrameLog.PathFor(Path.Combine(dir, "session-x.csv"));
                new FrameLog(empty, T0).Dispose();
                Assert.False(File.Exists(empty));
                Assert.Empty(FrameLog.Load(empty, T0));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Lows_are_the_fps_of_the_slow_percentile_frames()
        {
            // 990 frames at 16 ms and 10 at 50 ms: the slowest 1 % are the 50 ms ones
            var ms = Enumerable.Repeat(16.0, 990).Concat(Enumerable.Repeat(50.0, 10));
            var f = FrameStat.From(ms)!;
            Assert.Equal(1000, f.Count);
            Assert.Equal(1000 / 16.34, f.AvgFps, 1);
            Assert.InRange(f.Low1!.Value, 20, 62.5);      // p99 lies between the 16 ms and 50 ms frames
            Assert.Equal(20, f.Low01!.Value, 1);
            Assert.Equal(50, f.MaxMs);

            var few = FrameStat.From(Enumerable.Repeat(16.0, 99))!;
            Assert.Null(few.Low1);
            Assert.Null(few.Low01);
            Assert.Null(FrameStat.From(Array.Empty<double>()));
        }

        private static TelemetrySample Row(int second, double? fps) =>
            new(T0.AddSeconds(second), "Balanced", true, 60, 3000, 95, "3D", 8, 20, 32, 85, 35, 45, 60, 4400, -1, "EC auto", 90, null,
                24, null, CpuW: 8, GpuW: 11, DramW: 1.2, Fps: fps, FpsApp: fps is null ? null : "Game.exe");

        [Fact]
        public void Only_frames_in_steady_seconds_count()
        {
            // 0–4 s menu (no game), 5–124 s the game at 60 FPS, with a 100 ms stutter in the trimmed start (8 s) and one at 60 s
            var samples = Enumerable.Range(0, 125).Select(i => Row(i, i < 5 ? null : 60)).ToList();
            var frames = new List<(DateTime, double)>();
            for (int s = 5; s < 125; s++)
                for (int k = 0; k < 60; k++)
                    frames.Add((T0.AddSeconds(s - 1).AddMilliseconds(k * 16.6 + 1), s is 8 or 60 && k == 0 ? 100.0 : 16.6));

            var summary = RunSummary.From(samples, null, frames);
            Assert.NotNull(summary.Frames);
            Assert.Equal(summary.Steady.Count * 60, summary.Frames!.Count); // the trimmed start's frames are left out
            Assert.Equal(100, summary.Frames.MaxMs);                         // the 60 s stutter is in
            Assert.Null(RunSummary.From(samples).Frames);                    // no frames file: no frame figures
        }

        [Fact]
        public void Reports_show_lows_when_there_are_frame_times()
        {
            var samples = Enumerable.Range(0, 130).Select(i => Row(i, 60)).ToList();
            var frames = samples.SelectMany(s => Enumerable.Range(0, 60).Select(k => (s.Time.AddMilliseconds(-1000 + k * 16.6 + 1), k == 0 ? 40.0 : 16.6))).ToList();
            var withFrames = RunSummary.From(samples, "A", frames);
            var without = RunSummary.From(samples, "B");

            string html = SummaryReport.Html(samples, withFrames, "a.csv", "0");
            Assert.Contains("1 % low <b>", html);
            Assert.Contains("frames in the steady seconds", html);
            Assert.Contains("has no frame times", SummaryReport.Html(samples, without, "b.csv", "0"));

            var runs = new[] { new CompareEntry("A", withFrames, "a.csv"), new CompareEntry("B", without, "b.csv") };
            string compare = CompareReport.Html("p", runs, 0, "p.json", "0");
            Assert.Contains("1 % low, or the 5th percentile second for older recordings", compare);
            Assert.Contains("<th>1 % low</th>", compare);
            Assert.DoesNotContain("<th>1 % low</th>", CompareReport.Html("p", new[] { runs[1], runs[1] with { Name = "C" } }, 0, "p.json", "0"));
        }
    }
}
