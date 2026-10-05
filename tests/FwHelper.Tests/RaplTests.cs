using FwHelper.Features;
using FwHelper.Hardware;
using Xunit;

namespace FwHelper.Tests
{
    public class RaplTests
    {
        private const double Unit = 1.0 / (1 << 14); // 61 µJ, the usual RAPL energy unit

        [Fact]
        public void Watts_from_counter_delta()
        {
            uint tenJoules = (uint)(10 / Unit);
            Assert.Equal(10.0, RaplEnergy.Watts(1000, 1000 + tenJoules, 1.0, Unit)!.Value, 3);
        }

        [Fact]
        public void One_wrap_is_handled()
        {
            uint fiveJoules = (uint)(5 / Unit);
            uint before = uint.MaxValue - fiveJoules / 2;
            uint after = unchecked(before + fiveJoules);
            Assert.True(after < before);
            Assert.Equal(5.0, RaplEnergy.Watts(before, after, 1.0, Unit)!.Value, 3);
        }

        [Theory]
        [InlineData(0.01)]   // too soon
        [InlineData(61)]     // suspend or several wraps
        public void Gaps_outside_the_window_give_nothing(double seconds)
        {
            Assert.Null(RaplEnergy.Watts(0, 1000, seconds, Unit));
        }

        [Fact]
        public void Implausible_power_is_discarded()
        {
            uint joules300 = (uint)(300 / Unit);
            Assert.Null(RaplEnergy.Watts(0, joules300, 1.0, Unit));
        }

        [Fact]
        public void Meter_needs_two_readings_and_keeps_its_baseline_when_called_too_soon()
        {
            var m = new RaplEnergy();
            uint step = (uint)(8 / Unit);
            Assert.Null(m.Next(0, Unit, 1000));
            Assert.Null(m.Next(step / 100, Unit, 1010));          // 10 ms later: ignored, baseline kept
            Assert.Equal(8.0, m.Next(step, Unit, 2000)!.Value, 3); // measured from the first reading
        }

        [Theory]
        [InlineData(0UL, "")]
        [InlineData(1UL << 8, "EDP")]
        [InlineData((1UL << 8) | (1UL << 10), "EDP + PL1")]
        [InlineData((1UL << 3) | (1UL << 24), "bit 3")]   // unknown status bit named; log bits (16+) ignored
        public void Limit_reasons_decode(ulong value, string expected)
        {
            Assert.Equal(expected, PerfLimitReasons.Decode(value));
        }

        [Fact]
        public void Sessions_from_the_21_column_version_still_load()
        {
            const string oldHeader = "time,mode,on_ac,cpu_pct,cpu_mhz,gpu_pct,gpu_engine,gpu_shared_gb,mem_used_gb,mem_total_gb," +
                                     "cpu_c,battery_c,ddr_c,board_c,fan_rpm,fan_duty,fan_status,battery_pct,battery_w,package_w,pl1_w";
            const string oldRow = "2026-10-05T12:34:56,Balanced,1,27.3,2636,3.1,3D,2.72,11.50,31.60,62,34,41,39,2650,40,curve,85,9.9,12.3,25";
            var layout = TelemetrySample.Layout(oldHeader)!;
            var s = TelemetrySample.FromCsv(oldRow, layout)!;
            Assert.Equal(62, s.CpuTemp);
            Assert.Equal(12.3, s.PackageW);
            Assert.Null(s.CpuW);
            Assert.Null(s.Throttle);
        }

        [Fact]
        public void New_columns_round_trip()
        {
            var s = new TelemetrySample(new DateTime(2026, 10, 5, 1, 2, 3), "Turbo", true, 50, 3000, 20, "3D", 1, 10, 32,
                80, 35, 45, 50, 4000, 60, "curve", 90, null, 30.5, 35, 22.1, 6.4, "core: EDP + PL1 · gpu: thermal");
            Assert.Equal(s, TelemetrySample.FromCsv(s.ToCsv()));
        }

        [Fact]
        public void Not_a_session_header()
        {
            Assert.Null(TelemetrySample.Layout("a,b,c"));
        }
    }
}
