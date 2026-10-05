using FwHelper.Features;
using FwHelper.Hardware;
using Xunit;

namespace FwHelper.Tests
{
    public class FirmwareFloorTests
    {
        // Ramps read from the real EC (sakura-3.0.2), 2026-10-05
        private static readonly Dictionary<int, (int off, int max)> Ramps = new()
        {
            [0] = (40, 75), [1] = (40, 78), [2] = (40, 50), [3] = (40, 50), [4] = (103, 105),
        };

        private static List<TempSensor> Temps(int local, int cpu, int battery, int ddr, int? peci) => new()
        {
            new(0, "local_f75397@4c", local), new(1, "cpu_f75303@4d", cpu), new(2, "battery_temp@b", battery),
            new(3, "ddr_f75303@4d", ddr), new(4, "peci-temp", peci),
        };

        [Fact]
        public void Model_is_the_highest_ramp_position_and_ignores_peci_below_its_ramp()
        {
            // Measured row: local 46 → 17 %, cpu 47 → 18 %; PECI at 64 is far below its 103 °C ramp
            Assert.Equal(18.4, FirmwareFloor.Model(Temps(46, 47, 31, 39, 64), Ramps), 1);
        }

        [Fact]
        public void Battery_drives_the_model_on_its_steep_ramp()
        {
            Assert.Equal(50, FirmwareFloor.Model(Temps(30, 30, 45, 30, 50), Ramps));
        }

        [Fact]
        public void Missing_readings_and_unknown_sensors_are_skipped()
        {
            var temps = new List<TempSensor> { new(4, "peci-temp", null), new(9, "unknown", 99) };
            Assert.Equal(0, FirmwareFloor.Model(temps, Ramps));
        }

        [Fact]
        public void Unlearned_floor_falls_back_to_the_model()
        {
            var f = new FirmwareFloor();
            Assert.Equal(0, f.Floor(0));
            Assert.Equal(17, f.Floor(17.4));
            Assert.Equal(100, f.Floor(100));
        }

        [Fact]
        public void Keeps_the_lowest_duty_per_bucket()
        {
            var f = new FirmwareFloor();
            f.Observe(17, 27);
            f.Observe(18, 25);
            f.Observe(19, 40);   // falling-branch hysteresis or an outlier: ignored, lower already seen
            Assert.Equal(25, f.Floor(17));
        }

        [Fact]
        public void Floor_never_decreases_as_the_model_rises()
        {
            var f = new FirmwareFloor();
            f.Observe(15, 25);
            f.Observe(30, 20);   // odd low reading in a hotter bucket
            Assert.True(f.Floor(30) >= f.Floor(15));
            int last = 0;
            for (double m = 0; m <= 100; m += 0.5)
            {
                int v = f.Floor(m);
                Assert.True(v >= last, $"floor dropped at {m}");
                last = v;
            }
        }

        [Fact]
        public void Never_learns_impossible_duties()
        {
            var f = new FirmwareFloor();
            f.Observe(20, -1);
            f.Observe(20, 101);
            Assert.Equal(0, f.LearnedBuckets);
        }

        [Fact]
        public void Round_trips_and_rejects_junk()
        {
            var f = new FirmwareFloor();
            f.Observe(15, 25);
            f.Observe(40, 52);
            var parsed = FirmwareFloor.Parse(f.Serialize() + ",7:20,x:1,45:300");
            Assert.Equal(2, parsed.LearnedBuckets);
            Assert.Equal(52, parsed.Floor(42));
        }

        [Fact]
        public void Changed_flag_is_cleared_by_serialize()
        {
            var f = new FirmwareFloor();
            f.Observe(15, 25);
            Assert.True(f.Changed);
            f.Serialize();
            Assert.False(f.Changed);
            f.Observe(15, 30);   // not lower: no change
            Assert.False(f.Changed);
        }

        [Fact]
        public void Controller_applies_the_floor_above_the_curve()
        {
            var c = new FanController(FanCurve.Parse("40:0,50:20,60:40,70:60,80:80,85:90,90:95,95:100")!);
            var d = c.Next(45, 30, floor: 30);
            Assert.Equal(30, d.Duty);
            Assert.Equal("EC floor", d.Reason);
        }
    }
}
