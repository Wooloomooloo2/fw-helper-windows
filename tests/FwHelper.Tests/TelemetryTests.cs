using FwHelper.Features;
using FwHelper.Hardware;
using FwHelper.UI;
using Xunit;

namespace FwHelper.Tests
{
    public class TelemetryTests
    {
        private static TelemetrySample Full() => new(
            new DateTime(2026, 10, 5, 12, 34, 56), "Balanced", true,
            27.25, 2636.4, 3.1, "3D", 2.72, 11.5, 31.6,
            62, 34, 41, 39, 2650, 40, "battery guard", 85, 9.9, 12.3, 25);

        [Fact]
        public void Csv_has_one_field_per_column()
        {
            Assert.Equal(TelemetrySample.Columns.Length, Full().ToCsv().Split(',').Length);
        }

        [Fact]
        public void Csv_round_trips()
        {
            var parsed = TelemetrySample.FromCsv(Full().ToCsv())!;
            Assert.Equal(Full() with { CpuPct = 27.3, CpuMhz = 2636 }, parsed); // written rounded
        }

        [Fact]
        public void Csv_round_trips_missing_values()
        {
            var sparse = new TelemetrySample(new DateTime(2026, 10, 5, 1, 2, 3), "Silent", false,
                null, null, null, null, null, 8, 32, null, null, null, null, 0, -1, "EC auto", null, null, null, null);
            Assert.Equal(sparse, TelemetrySample.FromCsv(sparse.ToCsv()));
        }

        [Fact]
        public void Commas_in_text_fields_dont_break_the_row()
        {
            var s = Full() with { FanStatus = "EC: a, b" };
            Assert.Equal(TelemetrySample.Columns.Length, s.ToCsv().Split(',').Length);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not,a,row")]
        [InlineData("2026-10-05T12:34:56,Balanced,1,x,,,,,1,2,,,,,0,-1,EC auto,,,,")]
        public void Bad_rows_are_rejected(string line)
        {
            Assert.Null(TelemetrySample.FromCsv(line));
        }

        [Theory]
        [InlineData(0u, "")]
        [InlineData(0x4u, "PL4")]                 // seen live in Cyberpunk 2077, 2026-11-04
        [InlineData(0x9u, "PL1+thermal")]
        [InlineData(0x100u, "0x100")]             // unknown bits are shown, not dropped
        public void Gpu_throttle_reasons_are_named(uint flags, string text) =>
            Assert.Equal(text, FwHelper.Hardware.IntelGpuClock.Describe(flags));

        [Fact]
        public void Gpu_clock_columns_round_trip_and_old_sessions_still_load()
        {
            var s = new TelemetrySample(new DateTime(2026, 11, 4, 17, 0, 0), "Turbo", true, 50, 3000, 95, "3D", 1.5, 12, 32,
                76, 34, 44, 55, 4500, -1, "EC auto", 86, null, 25.5, null, 8.2, 12.2, null, 1.3, null, 30, "Cyberpunk2077", null,
                2900, 1350, "PL4");
            Assert.Equal(s, TelemetrySample.FromCsv(s.ToCsv()));

            // A 0.3.0 session has no gpu_* columns: they load as null
            var oldHeader = string.Join(",", TelemetrySample.Columns.Take(TelemetrySample.Columns.Length - 3));
            var oldRow = string.Join(",", s.ToCsv().Split(',').Take(TelemetrySample.Columns.Length - 3));
            var loaded = TelemetrySample.FromCsv(oldRow, TelemetrySample.Layout(oldHeader));
            Assert.NotNull(loaded);
            Assert.Null(loaded!.GpuMhz);
            Assert.Equal(30, loaded.Fps);
        }

        [Fact]
        public void Gpu_load_is_the_busiest_engine_type_summed_over_processes()
        {
            var (pct, engine) = SystemMetrics.BusiestEngine(new[]
            {
                ("pid_1_luid_0x0_0xD2C1_phys_0_eng_0_engtype_3D", 20.0),
                ("pid_2_luid_0x0_0xD2C1_phys_0_eng_0_engtype_3D", 15.0),
                ("pid_3_luid_0x0_0xD2C1_phys_0_eng_3_engtype_VideoDecode", 30.0),
                ("garbage", 99.0),
            });
            Assert.Equal(35.0, pct);
            Assert.Equal("3D", engine);
        }

        [Fact]
        public void Gpu_load_is_capped_and_null_without_engines()
        {
            Assert.Equal(100.0, SystemMetrics.BusiestEngine(new[] { ("a_engtype_3D", 80.0), ("b_engtype_3D", 70.0) }).pct);
            Assert.Null(SystemMetrics.BusiestEngine(Array.Empty<(string, double)>()).pct);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(0.7, 1)]
        [InlineData(13, 20)]
        [InlineData(24, 25)]
        [InlineData(26, 50)]
        [InlineData(4551, 5000)]
        public void Chart_grid_step_rounds_up_to_nice_numbers(double v, double expected)
        {
            Assert.Equal(expected, LineChart.NiceStep(v));
        }
    }
}
