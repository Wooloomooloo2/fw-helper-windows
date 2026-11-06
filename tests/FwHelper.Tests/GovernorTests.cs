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

        [Fact]
        public void Fixed_ceiling_alone_is_applied_as_is()
        {
            var p = new GovernorPolicy(Max);
            var d = p.Next(30, 90, null, null, maxMHz: 2200);
            Assert.Equal(2200, d.CapMHz);
            Assert.StartsWith("max", d.Reason);
            Assert.Equal(2200, p.Next(null, null, null, null, maxMHz: 2250).CapMHz);   // rounded down to a 100 MHz step
            Assert.False(p.Next(30, 90, null, null, maxMHz: Max).Capped);             // at max turbo = no cap
        }

        [Fact]
        public void Cpu_power_target_cuts_on_cores_watts_not_package()
        {
            var p = new GovernorPolicy(Max);
            // Package at 30 W is fine (no package target); the cores at 14 W are over a 6 W cap
            var d = p.Next(30, 70, null, null, cpuW: 14, cpuTargetW: 6);
            Assert.True(d.Capped);
            Assert.StartsWith("cpu", d.Reason);
            // Cores under their cap: no cut even with a high package draw
            var q = new GovernorPolicy(Max);
            for (int i = 0; i < 10; i++) Assert.False(q.Next(30, 70, null, null, cpuW: 4, cpuTargetW: 6).Capped);
        }

        [Fact]
        public void Dynamic_cap_never_exceeds_the_fixed_ceiling()
        {
            var p = new GovernorPolicy(Max);
            for (int i = 0; i < 50; i++)
            {
                var d = p.Next(10, 60, null, null, cpuW: 2, cpuTargetW: 10, maxMHz: 2000);   // far under the watt cap
                Assert.Equal(2000, d.CapMHz);
            }
            // Going over the watt cap cuts from the ceiling, not from max turbo: the first cut lands just under 2000
            GovernorDecision cut = default;
            for (int i = 0; i < 20 && cut.CapMHz is 0 or 2000; i++) cut = p.Next(10, 60, null, null, cpuW: 20, cpuTargetW: 10, maxMHz: 2000);
            Assert.InRange(cut.CapMHz, 1500, 1900);
            Assert.StartsWith("cpu", cut.Reason);
        }

        [Fact]
        public void A_cap_the_cpu_ignores_is_released_and_held_off()
        {
            var p = new GovernorPolicy(Max);
            var d = p.Next(30, 70, null, null, cpuW: 25, cpuTargetW: 10, fastestCoreMHz: 4500);
            Assert.True(d.Capped);
            // The cores stay at 4.5 GHz whatever the cap: after 3 ticks the cap is released, not wound down to the floor
            for (int i = 0; i < GovernorPolicy.NotBindingTicks; i++) d = p.Next(30, 70, null, null, cpuW: 25, cpuTargetW: 10, fastestCoreMHz: 4500);
            Assert.False(d.Capped);
            Assert.Equal(GovernorPolicy.NotBinding, d.Reason);
            // ...and stays released for the hold period, even though the pressure is still there
            for (int i = 1; i < GovernorPolicy.NotBindingHoldTicks; i++)
                Assert.False(p.Next(30, 70, null, null, cpuW: 25, cpuTargetW: 10, fastestCoreMHz: 4500).Capped);
            // Then it tries again
            Assert.True(p.Next(30, 70, null, null, cpuW: 25, cpuTargetW: 10, fastestCoreMHz: 4500).Capped);
        }

        [Fact]
        public void A_cap_the_cpu_follows_is_not_flagged()
        {
            var p = new GovernorPolicy(Max);
            int cap = 0;
            for (int i = 0; i < 60; i++)
            {
                var d = p.Next(30, 70, null, null, cpuW: 25, cpuTargetW: 10, fastestCoreMHz: cap == 0 ? 4500 : cap * 0.97);
                Assert.NotEqual(GovernorPolicy.NotBinding, d.Reason);
                cap = d.CapMHz;
            }
            Assert.True(cap > 0);
        }

        [Fact]
        public void Raising_after_a_cut_says_easing_off()
        {
            var p = new GovernorPolicy(Max);
            for (int i = 0; i < 10; i++) p.Next(20, 99, null, 85);      // well over the temperature cap
            var d = p.Next(20, 70, null, 85);                           // now far under it
            Assert.True(d.Capped);
            Assert.Equal("easing off", d.Reason);
        }

        [Fact]
        public void Reset_clears_a_not_binding_hold()
        {
            var p = new GovernorPolicy(Max);
            for (int i = 0; i < 5; i++) p.Next(30, 70, null, null, cpuW: 25, cpuTargetW: 10, fastestCoreMHz: 4500);
            p.Reset();
            Assert.True(p.Next(30, 70, null, null, cpuW: 25, cpuTargetW: 10, fastestCoreMHz: 4500).Capped);
        }

        [Fact]
        public void Blind_dynamic_target_keeps_the_fixed_ceiling()
        {
            var p = new GovernorPolicy(Max);
            var d = p.Next(null, null, 15, null, cpuW: null, cpuTargetW: 6, maxMHz: 1800);
            Assert.Equal(1800, d.CapMHz);
        }
    }

    /// <summary>Fake hardware with a crude CPU model: package watts track the frequency cap.</summary>
    internal sealed class FakeGovernorHardware : IGovernorHardware
    {
        public int Cap;           // 0 = none
        public int Writes;
        public double IdleW = 5, WattsPerGHz = 7;
        public int? Temp = 60;

        /// <summary>Like Windows under the Best Performance overlay: the cap is written but has no effect.</summary>
        public bool IgnoresCap;

        private int EffectiveMHz => Cap == 0 || IgnoresCap ? 4500 : Math.Min(Cap, 4500);

        public PowerReading ReadPower()
        {
            double ghz = EffectiveMHz / 1000.0;
            return new PowerReading(IdleW + WattsPerGHz * ghz, WattsPerGHz * ghz, 0.5, 1);
        }

        public int? CpuTemp() => Temp;

        public double? FastestCoreMHz() => EffectiveMHz;

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
            using var g = new Governor(hw, () => new GovernorTargets(20, null), 4800, intervalMs: 1_000_000, log: _ => { });
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
            using var g = new Governor(hw, () => new GovernorTargets(25, null), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 20; i++) g.Tick();
            Assert.True(hw.Writes <= 1);
            Assert.Equal(0, hw.Cap);
        }

        [Fact]
        public void Stop_always_removes_the_cap()
        {
            var hw = new FakeGovernorHardware();
            using var g = new Governor(hw, () => new GovernorTargets(10, null), 4800, intervalMs: 1_000_000, log: _ => { });
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
            using var g = new Governor(hw, () => new GovernorTargets(target, null), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 10; i++) g.Tick();
            target = null;
            g.Tick();
            Assert.Equal(0, hw.Cap);
        }

        [Fact]
        public void Converges_on_a_cpu_power_target()
        {
            var hw = new FakeGovernorHardware();            // cores: 7 W/GHz, uncapped 33.6 W
            using var g = new Governor(hw, () => new GovernorTargets(null, null, CpuW: 14), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 120; i++) g.Tick();
            Assert.InRange(hw.ReadPower().CoresW!.Value, 12.5, 15);
            Assert.InRange(hw.Cap, 1800, 2200);
        }

        [Fact]
        public void Never_winds_down_to_the_floor_when_the_cap_is_ignored()
        {
            var hw = new FakeGovernorHardware { IgnoresCap = true };     // cores always at 4.5 GHz, 31.5 W
            using var g = new Governor(hw, () => new GovernorTargets(null, null, CpuW: 10), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            int lowest = int.MaxValue;
            for (int i = 0; i < 120; i++)
            {
                g.Tick();
                if (hw.Cap > 0) lowest = Math.Min(lowest, hw.Cap);
            }
            Assert.True(lowest >= 2 * GovernorPolicy.MinMHz, $"cap went down to {lowest} MHz");   // 3 cuts at most, then released
            Assert.Contains("not binding", g.Status);
        }

        [Fact]
        public void Reset_policy_drops_the_previous_profiles_cap()
        {
            var hw = new FakeGovernorHardware();
            var targets = new GovernorTargets(null, null, CpuW: 8);
            using var g = new Governor(hw, () => targets, 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 20; i++) g.Tick();
            Assert.True(hw.Cap > 0);
            targets = new GovernorTargets(null, 94);                    // e.g. Balanced: only the global temp cap, far away
            g.ResetPolicy();
            g.Tick();
            Assert.Equal(0, hw.Cap);
        }

        [Fact]
        public void Fixed_ceiling_is_written_once_and_released_on_stop()
        {
            var hw = new FakeGovernorHardware();
            using var g = new Governor(hw, () => new GovernorTargets(null, null, MaxMHz: 2400), 4800, intervalMs: 1_000_000, log: _ => { });
            g.Start();
            for (int i = 0; i < 10; i++) g.Tick();
            Assert.Equal(2400, hw.Cap);
            Assert.Equal(1, hw.Writes);
            g.Stop();
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

        private static byte[] DxgiPayload(int pointerSize, uint flags)
        {
            var b = new byte[pointerSize + 8];
            BitConverter.GetBytes(flags).CopyTo(b, pointerSize);
            return b;
        }

        [Theory]
        [InlineData(8, 0x1u, true)]      // DXGI_PRESENT_TEST: Horizon Zero Dawn Remastered sends one per frame (2026-11-04)
        [InlineData(8, 0x201u, true)]
        [InlineData(8, 0x200u, false)]   // ALLOW_TEARING: the real frame
        [InlineData(8, 0x0u, false)]
        [InlineData(4, 0x1u, true)]      // 32-bit process
        public void Dxgi_test_presents_are_recognised(int pointerSize, uint flags, bool test) =>
            Assert.Equal(test, FpsCounter.IsDxgiTestPresent(DxgiPayload(pointerSize, flags), pointerSize));

        [Fact]
        public void A_truncated_dxgi_payload_counts_as_a_frame() =>
            Assert.False(FpsCounter.IsDxgiTestPresent(new byte[8], 8));

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

    public class LimitsNoteTests
    {
        [Fact]
        public void Performance_warns_that_the_limits_do_nothing()
        {
            Assert.Contains("No effect in Performance", FwHelper.UI.FansForm.LimitsNote(2));
            Assert.DoesNotContain("No effect", FwHelper.UI.FansForm.LimitsNote(1));
            Assert.DoesNotContain("No effect", FwHelper.UI.FansForm.LimitsNote(0));
        }
    }
}
