using FwHelper.Features;
using FwHelper.Hardware;
using Xunit;

namespace FwHelper.Tests
{
    public class GovernorPolicyTests
    {
        private const int Max = 4800;

        [Fact]
        public void No_targets_means_no_cap()
        {
            var p = new GovernorPolicy(Max);
            var d = p.Next(30, 90, null, null);
            Assert.Equal(0, d.CapMHz);
            Assert.Equal("off", d.Reason);
        }

        [Fact]
        public void Under_the_power_target_never_caps()
        {
            var p = new GovernorPolicy(Max);
            for (int i = 0; i < 30; i++) Assert.False(p.Next(10, 60, 25, null).Capped);
        }

        [Fact]
        public void Over_the_power_target_lowers_the_cap_and_keeps_lowering()
        {
            var p = new GovernorPolicy(Max);
            int last = Max;
            for (int i = 0; i < 10; i++)
            {
                var d = p.Next(35, 60, 15, null);
                Assert.True(d.Capped);
                Assert.True(d.CapMHz <= last);
                last = d.CapMHz;
            }
            Assert.True(last < 2500);
            Assert.StartsWith("power", p.Next(35, 60, 15, null).Reason);
        }

        [Fact]
        public void Power_is_smoothed_so_a_short_spike_barely_moves_the_cap()
        {
            var p = new GovernorPolicy(Max);
            for (int i = 0; i < 20; i++) p.Next(12, 60, 15, null);
            var d = p.Next(60, 60, 15, null);    // one 60 W second
            Assert.True(!d.Capped || d.CapMHz > 3500);
        }

        [Fact]
        public void Temperature_cap_uses_a_deadband()
        {
            var p = new GovernorPolicy(Max);
            var hot = p.Next(20, 90, null, 85);
            Assert.True(hot.Capped);
            int cap = hot.CapMHz;
            Assert.Equal(cap, p.Next(20, 85, null, 85).CapMHz);   // at the cap: hold
            Assert.True(p.Next(20, 80, null, 85).CapMHz > cap);   // comfortably under: raise
            Assert.StartsWith("temp", hot.Reason);
        }

        [Fact]
        public void The_tighter_constraint_wins()
        {
            var p = new GovernorPolicy(Max);
            var d = p.Next(16, 95, 15, 85);       // 7 % over on power, 2 "units" over on temp
            Assert.StartsWith("temp", d.Reason);
        }

        [Fact]
        public void Cap_is_released_completely_once_it_climbs_back_to_max()
        {
            var p = new GovernorPolicy(Max);
            for (int i = 0; i < 5; i++) p.Next(40, 60, 15, null);
            GovernorDecision d = default;
            for (int i = 0; i < 200 && (i == 0 || d.Capped); i++) d = p.Next(5, 60, 15, null);
            Assert.False(d.Capped);
            Assert.Equal(0, d.CapMHz);
        }

        [Fact]
        public void Cap_never_goes_below_the_floor_and_is_in_100_MHz_steps()
        {
            var p = new GovernorPolicy(Max);
            for (int i = 0; i < 100; i++)
            {
                var d = p.Next(80, 99, 8, 70);
                Assert.True(d.CapMHz >= GovernorPolicy.MinMHz);
                Assert.Equal(0, d.CapMHz % GovernorPolicy.StepMHz);
            }
        }

        [Fact]
        public void Without_readings_it_does_not_hold_a_cap()
        {
            var p = new GovernorPolicy(Max);
            for (int i = 0; i < 5; i++) p.Next(40, 90, 15, 85);
            var d = p.Next(null, null, 15, 85);
            Assert.False(d.Capped);
            Assert.Equal("no reading", d.Reason);
        }
    }

    /// <summary>Fake hardware with a crude CPU model: package watts track the frequency cap.</summary>
    internal sealed class FakeGovernorHardware : IGovernorHardware
    {
        public int Cap;           // 0 = none
        public int Writes;
        public double IdleW = 5, WattsPerGHz = 7;
        public int? Temp = 60;

        public PowerReading ReadPower()
        {
            double ghz = (Cap == 0 ? 4800 : Cap) / 1000.0;
            return new PowerReading(IdleW + WattsPerGHz * ghz, WattsPerGHz * ghz, 0.5, 1);
        }

        public int? CpuTemp() => Temp;

        public bool SetCap(int mhz)
        {
            Cap = mhz;
            Writes++;
            return true;
        }
    }

    public class GovernorLoopTests
    {
        [Fact]
        public void Converges_on_a_power_target()
        {
            var hw = new FakeGovernorHardware();            // uncapped: 5 + 7 * 4.8 = 38.6 W
            using var g = new Governor(hw, () => (20, null), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 120; i++) g.Tick();
            double w = hw.ReadPower().PackageW!.Value;
            Assert.InRange(w, 17.5, 21.5);
            Assert.True(hw.Cap > 0);
        }

        [Fact]
        public void Writes_only_when_the_cap_changes()
        {
            var hw = new FakeGovernorHardware { WattsPerGHz = 1 };   // always far under target
            using var g = new Governor(hw, () => (25, null), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 20; i++) g.Tick();
            Assert.True(hw.Writes <= 1);
            Assert.Equal(0, hw.Cap);
        }

        [Fact]
        public void Stop_always_removes_the_cap()
        {
            var hw = new FakeGovernorHardware();
            using var g = new Governor(hw, () => (10, null), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 10; i++) g.Tick();
            Assert.True(hw.Cap > 0);
            g.Stop();
            Assert.Equal(0, hw.Cap);
            Assert.Equal("off", g.Status);
        }

        [Fact]
        public void Turning_targets_off_releases_on_the_next_tick()
        {
            var hw = new FakeGovernorHardware();
            int? target = 10;
            using var g = new Governor(hw, () => (target, null), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 10; i++) g.Tick();
            target = null;
            g.Tick();
            Assert.Equal(0, hw.Cap);
        }
    }

    public class PowerReadingTests
    {
        [Fact]
        public void Energy_meter_instances_map_to_domains_in_watts()
        {
            var r = EnergyMeter.Parse(new[]
            {
                ("rapl_package0_pkg", 13400.0), ("RAPL_Package0_PP0", 9610.0), ("RAPL_Package0_PP1", 180.0),
                ("RAPL_Package0_DRAM", 930.0), ("_Total", 0.0),
            });
            Assert.Equal(new PowerReading(13.4, 9.61, 0.18, 0.93), r);
        }

        [Fact]
        public void Garbage_values_are_ignored()
        {
            Assert.Null(EnergyMeter.Parse(new[] { ("RAPL_Package0_PKG", double.NaN) }).PackageW);
            Assert.Null(EnergyMeter.Parse(new[] { ("RAPL_Package0_PKG", 9_000_000.0) }).PackageW);
        }

        [Fact]
        public void Frequency_caps_round_trip_through_config()
        {
            var caps = new PowerPlan.FrequencyCaps(0, 2000, 3000, 0);
            Assert.Equal(caps, PowerPlan.FrequencyCaps.Parse(caps.ToString()));
            Assert.Null(PowerPlan.FrequencyCaps.Parse("1,2,3"));
            Assert.Null(PowerPlan.FrequencyCaps.Parse(null));
        }
    }

    public class EcBackstopTests
    {
        // peci-temp config as read from the EC on 2026-10-05: high 120, halt 127, fan 103 → 105 °C
        private static readonly byte[] Peci = Convert.FromHexString(
            "000000008901000090010000000000000000000000000000780100007A010000");

        [Fact]
        public void Reads_the_firmware_high_threshold()
        {
            Assert.Equal(120, EcBackstop.HighC(Peci));
        }

        [Fact]
        public void Only_high_and_its_release_change()
        {
            var c = EcBackstop.WithHigh(Peci, 90, 85);
            Assert.Equal(90, EcBackstop.HighC(c));
            Assert.Equal(85 + 273u, BitConverter.ToUInt32(c, 16));     // release HIGH
            Assert.Equal(Peci[8..12], c[8..12]);                        // halt untouched
            Assert.Equal(Peci[24..32], c[24..32]);                      // fan ramp untouched
            Assert.Equal(Peci[0..4], c[0..4]);                          // warn untouched
        }

        [Theory]
        [InlineData(85, 90)]
        [InlineData(95, 100)]
        public void High_is_cap_plus_five(int cap, int high)
        {
            Assert.Equal(high, EcBackstop.HighFor(cap, Peci));
        }

        [Fact]
        public void Never_makes_the_firmware_threshold_less_strict()
        {
            var strict = EcBackstop.WithHigh(Peci, 88, 80);   // pretend firmware already throttles at 88
            Assert.Null(EcBackstop.HighFor(85, strict));        // 90 would be looser
        }
    }

    public class FpsCounterTests
    {
        private static void Frames(FpsCounter c, int pid, PresentSource s, int fps, double startMs, double seconds)
        {
            for (int i = 0; i < fps * seconds; i++) c.Record(pid, s, startMs + i * 1000.0 / fps);
        }

        [Fact]
        public void Counts_frames_in_the_last_second()
        {
            var c = new FpsCounter();
            Frames(c, 42, PresentSource.Api, 60, 0, 3);
            Assert.InRange(c.Fps(42)!.Value, 59, 61);
        }

        [Fact]
        public void Api_events_win_over_kernel_events_so_frames_are_not_counted_twice()
        {
            var c = new FpsCounter();
            Frames(c, 42, PresentSource.Api, 60, 0, 2);
            Frames(c, 42, PresentSource.Kernel, 60, 0, 2);
            Assert.InRange(c.Fps(42)!.Value, 59, 61);
        }

        [Fact]
        public void Kernel_events_count_for_apps_without_api_events()
        {
            var c = new FpsCounter();
            Frames(c, 7, PresentSource.Kernel, 144, 0, 2);   // e.g. a Vulkan game
            Assert.InRange(c.Fps(7)!.Value, 142, 146);
        }

        [Fact]
        public void Busiest_presenter_is_the_fallback()
        {
            var c = new FpsCounter();
            Frames(c, 1, PresentSource.Api, 30, 0, 2);
            Frames(c, 2, PresentSource.Api, 120, 0, 2);
            Assert.Equal(2, c.Busiest()!.Value.pid);
            Assert.Null(c.Fps(99));
        }

        [Fact]
        public void A_process_that_stopped_presenting_drops_out()
        {
            var c = new FpsCounter();
            Frames(c, 1, PresentSource.Api, 60, 0, 1);
            Frames(c, 2, PresentSource.Api, 60, 5000, 1);   // time moves on
            c.Prune();
            Assert.Null(c.Fps(1));
        }
    }

    public class TelemetryCompatTests
    {
        [Fact]
        public void Sessions_from_the_21_column_version_still_load()
        {
            const string oldHeader = "time,mode,on_ac,cpu_pct,cpu_mhz,gpu_pct,gpu_engine,gpu_shared_gb,mem_used_gb,mem_total_gb," +
                                     "cpu_c,battery_c,ddr_c,board_c,fan_rpm,fan_duty,fan_status,battery_pct,battery_w,package_w,pl1_w";
            const string oldRow = "2026-10-05T12:34:56,Balanced,1,27.3,2636,3.1,3D,2.72,11.50,31.60,62,34,41,39,2650,40,curve,85,9.9,12.3,25";
            var s = TelemetrySample.FromCsv(oldRow, TelemetrySample.Layout(oldHeader)!)!;
            Assert.Equal(62, s.CpuTemp);
            Assert.Equal(12.3, s.PackageW);
            Assert.Null(s.Fps);
        }

        [Fact]
        public void New_columns_round_trip()
        {
            var s = new TelemetrySample(new DateTime(2026, 10, 5, 1, 2, 3), "Turbo", false, 50, 3000, 20, "3D", 1, 10, 32,
                80, 35, 45, 50, 4000, 60, "curve", 90, 21.5, 30.5, 25, 22.1, 6.4, "governor: 2400 MHz cap · EC: soft",
                1.2, 2400, 144, "game", 95);
            Assert.Equal(s, TelemetrySample.FromCsv(s.ToCsv()));
        }
    }
}
