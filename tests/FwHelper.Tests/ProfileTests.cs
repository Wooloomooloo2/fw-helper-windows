using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    // Only the pure helpers: Modes.Create/Delete write the real %AppData% config, so they're covered by the hardware test plan
    public class ProfileTests
    {
        [Theory]
        [InlineData(null, new int[0])]
        [InlineData("", new int[0])]
        [InlineData("3,4,7", new[] { 3, 4, 7 })]
        [InlineData(" 3 , x, 1, 4,4,-2 ", new[] { 3, 4 })]   // junk, built-in ids and duplicates dropped
        public void Parses_profile_ids(string? s, int[] expected)
        {
            Assert.Equal(expected, ProfileList.Parse(s, Modes.BuiltInCount));
        }

        [Fact]
        public void Ids_round_trip()
        {
            Assert.Equal(new[] { 3, 5, 9 }, ProfileList.Parse(ProfileList.Serialize(new[] { 3, 5, 9 }), Modes.BuiltInCount));
        }

        [Theory]
        [InlineData(new int[0], 0, 3)]          // first user profile
        [InlineData(new[] { 3, 4 }, 4, 5)]
        [InlineData(new[] { 3 }, 7, 8)]         // 4..7 were deleted: never reuse
        [InlineData(new[] { 9 }, 4, 10)]        // config edited by hand
        public void Next_id_is_never_reused(int[] existing, int lastIssued, int expected)
        {
            Assert.Equal(expected, ProfileList.NextId(existing, lastIssued, Modes.BuiltInCount));
        }

        [Theory]
        [InlineData("  Gaming  ", "Gaming")]
        [InlineData("a,b;c=d|e", "abcde")]
        [InlineData("tab\there", "tabhere")]
        [InlineData("   ", null)]
        [InlineData(null, null)]
        [InlineData("This name is far too long to fit", "This name is far too lon")]
        public void Cleans_names(string? input, string? expected)
        {
            Assert.Equal(expected, ProfileList.CleanName(input));
        }

        [Fact]
        public void Hotkey_cycles_builtins_like_g_helper_then_user_profiles()
        {
            var user = new[] { 3, 5 };
            Assert.Equal(Modes.Turbo, ProfileList.Next(Modes.Balanced, user));
            Assert.Equal(Modes.Silent, ProfileList.Next(Modes.Turbo, user));
            Assert.Equal(3, ProfileList.Next(Modes.Silent, user));
            Assert.Equal(5, ProfileList.Next(3, user));
            Assert.Equal(Modes.Balanced, ProfileList.Next(5, user));
        }

        [Fact]
        public void Hotkey_without_user_profiles_is_the_classic_three_way_cycle()
        {
            Assert.Equal(Modes.Balanced, ProfileList.Next(Modes.Silent, Array.Empty<int>()));
        }

        [Fact]
        public void Hotkey_from_an_unknown_profile_goes_to_balanced()
        {
            Assert.Equal(Modes.Balanced, ProfileList.Next(42, new[] { 3 }));
        }

        [Fact]
        public void Curve_library_round_trips()
        {
            var a = FanCurve.Default(Modes.Silent);
            var b = FanCurve.Default(Modes.Turbo);
            var parsed = FanCurveLibrary.Parse(FanCurveLibrary.Serialize(new[] { ("Quiet desk", a), ("Gaming", b) }));
            Assert.Equal(2, parsed.Count);
            Assert.Equal(("Quiet desk", a.ToString()), (parsed[0].name, parsed[0].curve.ToString()));
            Assert.Equal(("Gaming", b.ToString()), (parsed[1].name, parsed[1].curve.ToString()));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("=40:0")]
        [InlineData("NoCurve=")]
        [InlineData("Bad=1:2,3:4")]
        public void Curve_library_skips_bad_entries(string? s)
        {
            Assert.Empty(FanCurveLibrary.Parse(s));
        }

        [Fact]
        public void Curve_library_keeps_good_entries_next_to_bad_ones()
        {
            string good = "Good=" + FanCurve.Default(Modes.Balanced);
            Assert.Single(FanCurveLibrary.Parse($"Bad=1:2;{good};;junk"));
        }
    }
}
