using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class PowerLimitKeeperTests
    {
        [Fact]
        public void Nothing_to_do_without_a_target_or_a_reading()
        {
            var k = new PowerLimitKeeper();
            Assert.Equal(PowerLimitCheck.Ok, k.Check((33, 60)));
            k.SetTarget(25, 60);
            Assert.Equal(PowerLimitCheck.Ok, k.Check(null));
        }

        [Fact]
        public void Rounding_within_tolerance_is_ok()
        {
            var k = new PowerLimitKeeper();
            k.SetTarget(25, 60);
            Assert.Equal(PowerLimitCheck.Ok, k.Check((26, 59)));
        }

        [Fact]
        public void Firmware_drift_is_rewritten_a_bounded_number_of_times()
        {
            var k = new PowerLimitKeeper();
            k.SetTarget(25, 60);
            for (int i = 0; i < PowerLimitKeeper.MaxCorrections; i++)
                Assert.Equal(PowerLimitCheck.Rewrite, k.Check((33, 60)));
            Assert.Equal(PowerLimitCheck.GiveUp, k.Check((33, 60)));
            Assert.Equal(PowerLimitCheck.GiveUp, k.Check((33, 60)));
        }

        [Fact]
        public void New_target_resets_the_correction_budget()
        {
            var k = new PowerLimitKeeper();
            k.SetTarget(25, 60);
            for (int i = 0; i <= PowerLimitKeeper.MaxCorrections; i++) k.Check((33, 60));
            k.SetTarget(15, 30);
            Assert.Equal(PowerLimitCheck.Rewrite, k.Check((33, 60)));
            Assert.Equal(1, k.Corrections);
        }

        [Fact]
        public void Pl2_drift_alone_triggers_a_rewrite()
        {
            var k = new PowerLimitKeeper();
            k.SetTarget(25, 60);
            Assert.Equal(PowerLimitCheck.Rewrite, k.Check((25, 45)));
        }

        [Fact]
        public void Clear_stops_checking()
        {
            var k = new PowerLimitKeeper();
            k.SetTarget(25, 60);
            k.Clear();
            Assert.Equal(PowerLimitCheck.Ok, k.Check((33, 60)));
        }
    }
}
