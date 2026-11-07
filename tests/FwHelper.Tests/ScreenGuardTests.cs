using System.Drawing;
using FwHelper.UI;
using Xunit;

namespace FwHelper.Tests
{
    public class ScreenGuardTests
    {
        // This laptop: the 2880×1920 panel sits left of the 4K primary, at negative x
        private static readonly Rectangle External = new(0, 0, 3840, 2100);
        private static readonly Rectangle Laptop = new(-2880, 0, 2880, 1824);

        [Fact]
        public void A_window_on_a_screen_that_is_still_there_stays()
        {
            var onLaptop = new Rectangle(-2000, 300, 900, 1200);
            Assert.Equal(onLaptop, ScreenGuard.Fit(onLaptop, new[] { Laptop }, Laptop));
        }

        [Fact]
        public void A_window_left_on_the_unplugged_screen_comes_to_the_remaining_one()
        {
            var onExternal = new Rectangle(2800, 900, 952, 1914);
            var fitted = ScreenGuard.Fit(onExternal, new[] { Laptop }, Laptop);
            Assert.True(Laptop.Contains(fitted));
            Assert.Equal(952, fitted.Width);
            Assert.Equal(1824, fitted.Height); // shrunk to fit
        }

        [Fact]
        public void A_title_bar_mostly_off_screen_is_unreachable()
        {
            // The external screen is gone: only 40 px of this title bar are still on the laptop panel
            Assert.False(ScreenGuard.Reachable(new Rectangle(-40, 100, 800, 600), new[] { Laptop }));
            Assert.True(ScreenGuard.Reachable(new Rectangle(-200, 100, 800, 600), new[] { Laptop }));
        }

        [Fact]
        public void A_title_bar_above_the_top_is_unreachable() =>
            Assert.False(ScreenGuard.Reachable(new Rectangle(100, -500, 800, 400), new[] { External }));
    }
}
