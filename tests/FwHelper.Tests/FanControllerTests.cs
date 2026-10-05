using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class FanControllerTests
    {
        // Linear 2 %/°C from 40 °C, so expected duties are easy to read
        private static FanController Make() =>
            new(FanCurve.Parse("40:0,50:20,60:40,70:60,80:80,85:90,90:95,95:100")!);

        private static int Duty(FanDecision d)
        {
            Assert.Equal(FanAction.Duty, d.Action);
            return d.Duty;
        }

        [Fact]
        public void No_cpu_temperature_at_start_never_takes_the_fan()
        {
            Assert.Equal(FanAction.Release, Make().Next(null, 30).Action);
        }

        [Fact]
        public void Losing_cpu_temperature_holds_then_releases()
        {
            var c = Make();
            Assert.Equal(40, Duty(c.Next(60, 30)));
            Assert.Equal(40, Duty(c.Next(null, 30)));
            Assert.Equal(40, Duty(c.Next(null, 30)));
            Assert.Equal(FanAction.Release, c.Next(null, 30).Action);
        }

        [Fact]
        public void Rising_temperature_is_followed_immediately()
        {
            var c = Make();
            Assert.Equal(0, Duty(c.Next(40, 30)));
            Assert.Equal(80, Duty(c.Next(80, 30)));
        }

        [Fact]
        public void Falling_temperature_within_hysteresis_holds_duty()
        {
            var c = Make();
            Assert.Equal(40, Duty(c.Next(60, 30)));
            Assert.Equal(40, Duty(c.Next(59, 30)));
            Assert.Equal(40, Duty(c.Next(58, 30)));
            Assert.Equal(38, Duty(c.Next(57, 30)));   // held 59 → 38
        }

        [Fact]
        public void Dithering_around_a_point_does_not_move_the_fan()
        {
            var c = Make();
            int first = Duty(c.Next(61, 30));
            foreach (int t in new[] { 60, 61, 60, 61, 60, 61 })
                Assert.Equal(first, Duty(c.Next(t, 30)));
        }

        [Fact]
        public void Ramp_down_is_limited_per_tick()
        {
            var c = Make();
            Assert.Equal(80, Duty(c.Next(80, 30)));
            Assert.Equal(80 - FanController.RampDownPerTick, Duty(c.Next(40, 30)));
            Assert.Equal(80 - 2 * FanController.RampDownPerTick, Duty(c.Next(40, 30)));
        }

        [Fact]
        public void Small_requests_are_raised_to_the_minimum_spinning_duty()
        {
            Assert.Equal(FanController.MinSpinDuty, Duty(Make().Next(42, 30)));   // curve says 4 %
        }

        [Fact]
        public void Ramping_down_to_zero_skips_the_stall_band()
        {
            var c = Make();
            Duty(c.Next(50, 30));
            var duties = Enumerable.Range(0, 20).Select(_ => Duty(c.Next(30, 30))).ToList();
            Assert.Equal(0, duties[^1]);
            Assert.DoesNotContain(duties, d => d > 0 && d < FanController.MinSpinDuty);
        }

        [Fact]
        public void Cpu_at_full_duty_line_forces_100()
        {
            var d = Make().Next(FanController.CpuFullDutyC, 30);
            Assert.Equal(100, Duty(d));
            Assert.Contains("95", d.Reason);
        }

        [Fact]
        public void Cpu_at_tjmax_releases_and_retakes_only_below_full_duty_line()
        {
            var c = Make();
            Assert.Equal(FanAction.Release, c.Next(100, 30).Action);
            Assert.Equal(FanAction.Release, c.Next(97, 30).Action);
            Assert.Equal(FanAction.Release, c.Next(95, 30).Action);
            Assert.Equal(FanAction.Duty, c.Next(94, 30).Action);
        }

        [Theory]
        [InlineData(null, 0)]
        [InlineData(41, 0)]
        [InlineData(42, FanController.MinSpinDuty)]
        [InlineData(45, 56)]
        [InlineData(48, 100)]
        [InlineData(55, 100)]
        public void Battery_guard_duty(int? battery, int expected)
        {
            Assert.Equal(expected, FanController.BatteryGuardDuty(battery));
        }

        [Fact]
        public void Hot_battery_spins_the_fan_with_an_idle_cpu()
        {
            var d = Make().Next(40, 45);
            Assert.Equal(56, Duty(d));
            Assert.Equal("battery guard", d.Reason);
        }

        [Fact]
        public void Missing_battery_sensor_does_not_stop_the_curve()
        {
            Assert.Equal(40, Duty(Make().Next(60, null)));
        }

        [Fact]
        public void Reset_forgets_the_ramp()
        {
            var c = Make();
            Duty(c.Next(80, 30));
            c.Reset();
            Assert.Equal(0, Duty(c.Next(40, 30)));
        }

        [Fact]
        public void Random_walk_never_produces_an_unsafe_duty()
        {
            var rng = new Random(1234);
            var c = Make();
            int cpu = 50, batt = 35;
            for (int i = 0; i < 20_000; i++)
            {
                cpu = Math.Clamp(cpu + rng.Next(-4, 5), 30, 105);
                batt = Math.Clamp(batt + rng.Next(-1, 2), 25, 52);
                int? cpuReading = rng.Next(50) == 0 ? null : cpu;
                var d = c.Next(cpuReading, batt);

                if (d.Action == FanAction.Release) continue;
                Assert.InRange(d.Duty, 0, 100);
                Assert.False(d.Duty > 0 && d.Duty < FanController.MinSpinDuty, $"stall-band duty {d.Duty}");
                if (cpuReading >= FanController.CpuFullDutyC) Assert.Equal(100, d.Duty);
                if (cpuReading is not null) Assert.True(d.Duty >= FanController.BatteryGuardDuty(batt), $"below battery guard at {batt}°C");
            }
        }
    }
}
