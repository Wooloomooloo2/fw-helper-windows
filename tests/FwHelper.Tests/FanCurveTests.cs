using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class FanCurveTests
    {
        private const string Sample = "40:0,50:20,60:40,70:60,80:80,85:90,90:95,95:100";

        [Fact]
        public void Round_trips_through_string()
        {
            Assert.Equal(Sample, FanCurve.Parse(Sample)!.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("40:0,50:20")]                                     // too few points
        [InlineData("40:0,50:20,60:40,70:60,80:80,85:90,90:95,x:100")] // not a number
        [InlineData("40-0,50:20,60:40,70:60,80:80,85:90,90:95,95:100")]
        public void Rejects_malformed_input(string? s)
        {
            Assert.Null(FanCurve.Parse(s));
        }

        [Fact]
        public void Normalizes_to_strictly_rising_temps_and_non_decreasing_duty()
        {
            var c = FanCurve.Parse("50:30,40:10,40:50,60:20,70:60,80:80,85:90,90:95")!;
            for (int i = 1; i < FanCurve.Points; i++)
            {
                Assert.True(c.Temps[i] > c.Temps[i - 1]);
                Assert.True(c.Duties[i] >= c.Duties[i - 1]);
            }
        }

        [Fact]
        public void Clamps_values_to_valid_ranges()
        {
            var c = FanCurve.Parse("0:-5,50:20,60:40,70:60,80:80,85:90,90:95,150:300")!;
            Assert.Equal(FanCurve.MinTemp, c.Temps[0]);
            Assert.Equal(0, c.Duties[0]);
            Assert.Equal(FanCurve.MaxTemp, c.Temps[^1]);
            Assert.Equal(100, c.Duties[^1]);
        }

        [Theory]
        [InlineData(20, 0)]    // below first point
        [InlineData(40, 0)]
        [InlineData(45, 10)]   // interpolated
        [InlineData(55, 30)]
        [InlineData(95, 100)]
        [InlineData(110, 100)] // above last point
        public void Interpolates_linearly(int temp, int duty)
        {
            Assert.Equal(duty, FanCurve.Parse(Sample)!.DutyAt(temp));
        }

        [Fact]
        public void Defaults_exist_for_every_mode_and_end_at_full_duty()
        {
            for (int mode = 0; mode < Modes.Count; mode++)
                Assert.Equal(100, FanCurve.Default(mode).Duties[^1]);
        }

        [Fact]
        public void Clone_is_independent()
        {
            var a = FanCurve.Parse(Sample)!;
            var b = a.Clone();
            b.Duties[0] = 50;
            Assert.Equal(0, a.Duties[0]);
        }
    }
}
