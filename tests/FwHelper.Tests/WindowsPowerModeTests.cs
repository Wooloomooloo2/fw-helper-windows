using FwHelper.Features;
using FwHelper.UI;
using Xunit;

namespace FwHelper.Tests
{
    public class WindowsPowerModeTests
    {
        private const int Efficiency = 0, Balanced = 1, Performance = 2;

        [Fact]
        public void In_step_shows_the_budget_on_AC()
        {
            var s = new PowerModeStatus(Performance, Performance, false, false, true);
            Assert.False(s.Mismatch);
            Assert.False(s.CanFix);
            Assert.Equal("Windows: Best Performance · CPU + GPU 30–33 W sustained", s.Line);
            Assert.Equal("Performance", s.ShortLabel);
        }

        [Fact]
        public void On_battery_no_budget_is_claimed()
        {
            var s = new PowerModeStatus(Balanced, Balanced, false, false, false);
            Assert.Equal("Windows: Balanced · on battery", s.Line);
        }

        [Fact]
        public void Changed_outside_is_a_fixable_mismatch()
        {
            var s = new PowerModeStatus(Performance, Balanced, false, false, true);
            Assert.True(s.Mismatch);
            Assert.True(s.CanFix);
            Assert.StartsWith("⚠ Windows: Balanced, changed outside FW-Helper", s.Line);
            Assert.Contains("wants Best Performance", s.Line);
            Assert.Equal("⚠ Balanced", s.ShortLabel);
        }

        [Fact]
        public void Battery_saver_is_a_mismatch_FW_Helper_cannot_fix()
        {
            // The overlay can still read Best Performance underneath Battery Saver
            var s = new PowerModeStatus(Performance, Performance, true, false, false);
            Assert.True(s.Mismatch);
            Assert.False(s.CanFix);
            Assert.StartsWith("⚠ Windows: Battery Saver is on", s.Line);
            Assert.Equal("⚠ Battery Saver", s.ShortLabel);
        }

        [Fact]
        public void Unknown_overlay_is_not_a_mismatch()
        {
            var s = new PowerModeStatus(Balanced, -1, false, false, true);
            Assert.False(s.Mismatch);
            Assert.Equal("?", s.ShortLabel);
        }

        [Fact]
        public void Game_mode_is_shown()
        {
            var s = new PowerModeStatus(Performance, Performance, false, true, true);
            Assert.EndsWith(" · Game Mode", s.Line);
        }

        [Theory]
        [InlineData(true, Performance, Balanced, false, true)]
        [InlineData(false, Performance, Balanced, false, false)]    // keep-in-step off: Windows' choice wins
        [InlineData(true, Performance, Balanced, true, false)]      // never fight Battery Saver
        [InlineData(true, Performance, Performance, false, false)]  // already in step
        [InlineData(true, Performance, -1, false, false)]           // overlay unreadable
        [InlineData(true, Efficiency, Performance, false, true)]
        public void Resync_only_when_on_and_out_of_step(bool sync, int wanted, int actual, bool saver, bool expected) =>
            Assert.Equal(expected, WindowsPowerMode.ShouldResync(sync, wanted, actual, saver));

        [Fact]
        public void Every_mode_has_a_short_name_budget_and_explanation()
        {
            for (int i = 0; i < 3; i++)
            {
                Assert.False(string.IsNullOrEmpty(WindowsPowerMode.Short[i]));
                Assert.False(string.IsNullOrEmpty(WindowsPowerMode.Budget[i]));
                Assert.Contains(WindowsPowerMode.Budget[i].Replace("about ", ""), WindowsPowerMode.Explain[i]);
            }
            Assert.Contains("ignores", WindowsPowerMode.Explain[Performance]);
        }

        [Fact]
        public void Overlay_shows_profile_and_windows_mode()
        {
            var sample = new TelemetrySample(DateTime.Now, "Turbo", true, 10, 3000, 50, "3D", 1, 8, 32,
                60, 30, 40, 40, 2000, -1, "EC auto", 85, null, 20, null);
            var rows = OverlayForm.Rows(sample, null, new PowerModeStatus(Performance, Balanced, false, false, true));
            Assert.Contains(rows, r => r.Key == "PWR" && r.Value == "Turbo · Windows ⚠ Balanced");
            Assert.DoesNotContain(OverlayForm.Rows(sample), r => r.Key == "PWR");
        }
    }
}
