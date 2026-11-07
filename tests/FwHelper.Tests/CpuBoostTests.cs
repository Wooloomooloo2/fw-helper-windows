using FwHelper.Features;
using FwHelper.Hardware;
using FwHelper.UI;
using Xunit;

namespace FwHelper.Tests
{
    public class CpuBoostTests
    {
        [Fact]
        public void Boost_off_writes_99_percent_everywhere()
        {
            var off = CpuBoost.OffValues(new PowerPlan.FrequencyCaps(100, 100, 100, 100));
            Assert.Equal(new PowerPlan.FrequencyCaps(99, 99, 99, 99), off);
        }

        [Fact]
        public void A_lower_value_the_user_set_is_kept() =>
            Assert.Equal(new PowerPlan.FrequencyCaps(99, 80, 99, 50), CpuBoost.OffValues(new PowerPlan.FrequencyCaps(100, 80, 100, 50)));

        [Fact]
        public void Old_recordings_read_as_boost_on()
        {
            var layout = TelemetrySample.Layout("time,mode,on_ac,cpu_c")!;
            var s = TelemetrySample.FromCsv("2026-11-04T18:31:53,Turbo,1,80", layout);
            Assert.False(s!.BoostOff);
        }

        private static TelemetrySample Sample(bool boostOff, int second = 0) =>
            new(new DateTime(2026, 11, 7, 15, 0, second), "Cyberpunk", true, 40, 1790, 98, "3D", 8, 20, 32, 80, 35, 45, 60,
                4400, -1, "EC auto", 90, null, 30, null, CpuW: 6, GpuW: 19, Fps: 55, FpsApp: "Cyberpunk2077", WinMode: "Performance", BoostOff: boostOff);

        [Fact]
        public void Overlay_shows_boost_off()
        {
            var power = new PowerModeStatus(2, 2, false, false, true);
            Assert.Contains(OverlayForm.Rows(Sample(true), null, power), r => r.Key == "PWR" && r.Value == "Cyberpunk · Windows Performance · boost off");
            Assert.Contains(OverlayForm.Rows(Sample(false), null, power), r => r.Key == "PWR" && r.Value == "Cyberpunk · Windows Performance");
        }

        [Fact]
        public void Summary_names_boost_off_and_warns_when_it_changed_mid_run()
        {
            var all = Enumerable.Range(0, 59).Select(i => Sample(true, i)).ToList();
            var s = RunSummary.From(all);
            Assert.Equal(1, s.BoostOffFraction);
            Assert.DoesNotContain(s.Warnings, w => w.Contains("boost"));
            Assert.Contains("CPU boost <b>off</b>", SummaryReport.Html(all, s, "x.csv", "t"));

            var mixed = all.Select((r, i) => i < 30 ? r with { BoostOff = false } : r).ToList();
            Assert.Contains(RunSummary.From(mixed).Warnings, w => w.Contains("CPU boost was off for"));
        }
    }
}
