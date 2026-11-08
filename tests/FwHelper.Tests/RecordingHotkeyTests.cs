using FwHelper.Features;
using FwHelper.UI;
using Xunit;

namespace FwHelper.Tests
{
    public class RecordingHotkeyTests
    {
        private static readonly TelemetrySample S =
            new(new DateTime(2026, 11, 8, 20, 0, 0), "Balanced", true, 60, 3000, 95, "3D", 8, 20, 32, 85, 35, 45, 60, 4400, -1, "EC auto", 90, null,
                24, null, CpuW: 8, GpuW: 11, DramW: 1.2, Fps: 55, FpsApp: "Cyberpunk2077.exe");

        [Fact]
        public void Overlay_shows_REC_with_the_elapsed_time_on_top_while_recording()
        {
            var rows = OverlayForm.Rows(S, recording: TimeSpan.FromSeconds(192));
            Assert.Equal("REC", rows[0].Key);
            Assert.Equal("● 3:12", rows[0].Value);
            Assert.Equal("● 0:00", OverlayForm.Rows(S, recording: TimeSpan.FromSeconds(-1))[0].Value); // sample from just before the start
            Assert.DoesNotContain(OverlayForm.Rows(S), r => r.Key == "REC");
        }

        [Fact]
        public void Compare_project_file_has_no_computed_fields()
        {
            var json = new CompareProject { Name = "x", Runs = { new("a.csv", "A") } }.ToJson();
            Assert.DoesNotContain("baselineIndex", json);
        }
    }
}
