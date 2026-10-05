using FwHelper.Features;
using FwHelper.Hardware;
using System.Collections.Concurrent;
using Xunit;

namespace FwHelper.Tests
{
    /// <summary>Fake EC: settable temperatures, records every write, can hang to simulate a stuck EC call.</summary>
    internal sealed class FakeFan : IFanHardware
    {
        public volatile int Cpu = 60, Board = 45, Battery = 30;
        public volatile bool CpuMissing;
        public volatile int Duty = 25;            // what the fan runs at
        public volatile bool Auto = true;         // who owns it
        public volatile bool FailWrites;
        public readonly ConcurrentQueue<string> Writes = new();

        public List<TempSensor> GetTemperatures() => new()
        {
            new(0, "local_f75397@4c", Board),
            new(2, "battery_temp@b", Battery),
            new(4, "peci-temp", CpuMissing ? null : Cpu),
        };

        public bool SetFanDuty(int percent)
        {
            if (FailWrites) return false;
            Writes.Enqueue("duty " + percent);
            Duty = percent;
            Auto = false;
            return true;
        }

        public bool SetFanAuto()
        {
            Writes.Enqueue("auto");
            Auto = true;
            return true;
        }

        public int? GetFanDuty() => Duty;
        public bool? IsFanAuto() => Auto;

        public IReadOnlyDictionary<int, (int off, int max)> GetFanRamps() => new Dictionary<int, (int, int)>
        {
            [0] = (40, 75),
            [2] = (40, 50),
            [4] = (103, 105),
        };
    }

    public class FanLoopTests
    {
        private static readonly FanCurve Curve = FanCurve.Parse("40:0,50:20,60:40,70:60,80:80,85:90,90:95,95:100")!;

        private static FanLoop Make(FakeFan hw, bool floor = false, FirmwareFloor? learned = null) => new(hw, new FanLoopOptions
        {
            IntervalMs = 20,
            WatchdogTimeoutMs = 200,
            LearnEveryTicks = 1,
            FloorEnabled = () => floor,
            Floor = learned ?? new FirmwareFloor(),
            Log = _ => { },
        });

        private static void WaitFor(Func<bool> condition, int timeoutMs = 3000)
        {
            var until = Environment.TickCount64 + timeoutMs;
            while (!condition())
            {
                if (Environment.TickCount64 > until) throw new TimeoutException("condition not met");
                Thread.Sleep(5);
            }
        }

        [Fact]
        public void Start_takes_the_fan_at_the_curve_duty()
        {
            var hw = new FakeFan { Cpu = 60 };
            using var loop = Make(hw);
            loop.Start(Curve);
            WaitFor(() => loop.LastDuty == 40);
            Assert.False(hw.Auto);
            Assert.Equal("curve", loop.Status);
        }

        [Fact]
        public void Stop_hands_the_fan_back()
        {
            var hw = new FakeFan();
            using var loop = Make(hw);
            loop.Start(Curve);
            WaitFor(() => !hw.Auto);
            loop.Stop();
            Assert.True(hw.Auto);
            Assert.Equal(-1, loop.LastDuty);
        }

        [Fact]
        public void Stop_sends_auto_even_if_never_started()
        {
            var hw = new FakeFan { Auto = false }; // e.g. left behind by a crash
            using var loop = Make(hw);
            loop.Stop();
            Assert.True(hw.Auto);
        }

        [Fact]
        public void Duty_is_rewritten_when_something_else_releases_the_fan()
        {
            var hw = new FakeFan();
            using var loop = Make(hw);
            loop.Start(Curve);
            WaitFor(() => !hw.Auto);
            hw.Auto = true; // an old guardian or framework_tool
            WaitFor(() => !hw.Auto);
        }

        [Fact]
        public void Losing_the_cpu_sensor_releases_the_fan()
        {
            var hw = new FakeFan();
            using var loop = Make(hw);
            loop.Start(Curve);
            WaitFor(() => !hw.Auto);
            hw.CpuMissing = true;
            WaitFor(() => hw.Auto && loop.Status == "EC: no CPU temperature");
        }

        [Fact]
        public void Watchdog_releases_a_stalled_loop_and_the_loop_retakes_after()
        {
            var hw = new FakeFan();
            using var loop = Make(hw);
            loop.Start(Curve);
            WaitFor(() => !hw.Auto);

            var stall = Task.Run(() => loop.StallLoopForTest(TimeSpan.FromMilliseconds(800)));
            WaitFor(() => hw.Auto && loop.Status == "EC: watchdog", 2000);
            stall.Wait();
            WaitFor(() => !hw.Auto && loop.Status == "curve");
        }

        [Fact]
        public void Failed_writes_are_reported_not_hidden()
        {
            var hw = new FakeFan { FailWrites = true };
            using var loop = Make(hw);
            loop.Start(Curve);
            WaitFor(() => loop.Status == "EC: duty write failed");
            Assert.Equal(-1, loop.LastDuty);
        }

        [Fact]
        public void Learns_the_floor_only_while_the_ec_owns_the_fan()
        {
            var hw = new FakeFan { Board = 54, Duty = 52 }; // model 40 %
            var floor = new FirmwareFloor();
            using var loop = Make(hw, learned: floor);
            loop.Run();
            WaitFor(() => floor.LearnedBuckets == 1);
            Assert.Equal(52, floor.Floor(40));

            hw.Auto = false;  // someone else holds a fixed duty: not the EC's choice
            hw.Duty = 5;
            Thread.Sleep(200);
            Assert.Equal(52, floor.Floor(40));
        }

        [Fact]
        public void Floor_raises_a_quiet_curve_when_enabled()
        {
            var hw = new FakeFan { Cpu = 45, Board = 54 };         // curve says 10 %, model 40 %
            var learned = FirmwareFloor.Parse("40:52");
            using var loop = Make(hw, floor: true, learned: learned);
            loop.Start(Curve);
            WaitFor(() => loop.LastDuty == 52);
            Assert.Equal("EC floor", loop.Status);
        }

        [Fact]
        public void Floor_is_ignored_when_disabled()
        {
            var hw = new FakeFan { Cpu = 45, Board = 54 };
            using var loop = Make(hw, floor: false, learned: FirmwareFloor.Parse("40:52"));
            loop.Start(Curve);
            WaitFor(() => loop.LastDuty == FanController.MinSpinDuty);
            Assert.Equal("curve", loop.Status);
        }
    }
}
