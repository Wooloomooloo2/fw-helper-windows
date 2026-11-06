using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class KeyboardBacklightTests
    {
        [Theory]
        [InlineData(0, 5)]
        [InlineData(5, 5)]      // at the threshold: still "dark"
        [InlineData(6, 0)]
        [InlineData(324, 0)]
        public void Auto_level_follows_the_5_lux_threshold(int lux, int level) =>
            Assert.Equal(level, KeyboardBacklight.AutoLevel(lux));

        [Fact]
        public void A_write_reverted_to_the_auto_level_is_auto()
        {
            Assert.True(KeyboardBacklight.WriteOverridden(50, 0, 324));    // bright: Auto holds it off
            Assert.True(KeyboardBacklight.WriteOverridden(50, 5, 2));      // dark: Auto holds it at 5 %
        }

        [Fact]
        public void A_write_that_holds_is_not_auto()
        {
            Assert.False(KeyboardBacklight.WriteOverridden(50, 50, 324));
            Assert.False(KeyboardBacklight.WriteOverridden(0, 0, 2));      // off in the dark: Auto would have picked 5 %
        }

        [Fact]
        public void Ambiguous_readings_are_unknown()
        {
            Assert.Null(KeyboardBacklight.WriteOverridden(0, 0, 324));     // off in bright light: Auto would do the same
            Assert.Null(KeyboardBacklight.WriteOverridden(5, 5, 2));
            Assert.Null(KeyboardBacklight.WriteOverridden(50, 75, 324));   // changed some other way (Fn+Space)
            Assert.Null(KeyboardBacklight.WriteOverridden(50, null, 324));
            Assert.Null(KeyboardBacklight.WriteOverridden(50, 0, null));   // no light reading
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(5, false)]
        [InlineData(20, true)]
        [InlineData(75, true)]
        public void Only_levels_auto_never_sets_rule_it_out(int actual, bool rulesOut) =>
            Assert.Equal(rulesOut, KeyboardBacklight.RulesOutAuto(actual));

        [Fact]
        public void Describe_says_what_the_ec_is_doing()
        {
            Assert.Equal("On · 75% · 324 lux", KeyboardBacklight.Describe(75, 324, false));
            Assert.Equal("Off · 324 lux", KeyboardBacklight.Describe(0, 324, false));
            Assert.StartsWith("EC Auto: off at 324 lux", KeyboardBacklight.Describe(0, 324, true));
            Assert.Contains("Fn+Space", KeyboardBacklight.Describe(0, 324, true));
            Assert.Equal("Backlight: no reading", KeyboardBacklight.Describe(null, 324, false));
        }
    }
}
