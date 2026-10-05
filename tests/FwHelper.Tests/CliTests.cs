using FwHelper.Features;
using FwHelper.Helpers;
using FwHelper.UI;
using Xunit;

namespace FwHelper.Tests
{
    public class CliTests
    {
        [Theory]
        [InlineData(new[] { "--status" }, true)]
        [InlineData(new[] { "--MODE", "Turbo" }, true)]
        [InlineData(new[] { "-tray" }, false)]           // app option, not a command
        [InlineData(new[] { "--selftest" }, false)]
        [InlineData(new[] { "--guard", "123" }, false)]
        [InlineData(new string[0], false)]
        public void Recognises_commands_without_stealing_app_options(string[] args, bool expected)
        {
            Assert.Equal(expected, CommandProtocol.IsCommand(args));
        }

        [Theory]
        [InlineData("--mode")]
        [InlineData("--charge-limit 49")]
        [InlineData("--charge-limit 101")]
        [InlineData("--charge-limit eighty")]
        [InlineData("--fan-floor maybe")]
        public void Rejects_bad_arguments_before_sending(string line)
        {
            var (command, error) = CommandProtocol.Parse(line.Split(' '));
            Assert.Null(command);
            Assert.NotNull(error);
        }

        [Fact]
        public void Multi_word_profile_names_are_kept_together()
        {
            var (command, _) = CommandProtocol.Parse(new[] { "--mode", "Quiet", "desk" });
            Assert.Equal(new CliCommand("mode", "Quiet desk"), command);
        }

        [Theory]
        [InlineData("status", null)]
        [InlineData("mode", "Quiet desk")]
        [InlineData("charge-limit", "80")]
        public void Wire_format_round_trips(string verb, string? arg)
        {
            var c = new CliCommand(verb, arg);
            Assert.Equal(c, CommandProtocol.Decode(CommandProtocol.Encode(c)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("format c:")]
        [InlineData("rm\t-rf")]
        public void Server_rejects_unknown_verbs(string? line)
        {
            Assert.Null(CommandProtocol.Decode(line));
        }

        [Fact]
        public void Embedded_newlines_cannot_smuggle_a_second_command()
        {
            string wire = CommandProtocol.Encode(new CliCommand("mode", "x\nfan-floor\toff"));
            Assert.DoesNotContain('\n', wire);
        }

        private static readonly (int, string)[] Profiles = { (0, "Silent"), (1, "Balanced"), (2, "Turbo"), (3, "Gaming"), (4, "gaming") };

        [Theory]
        [InlineData("turbo", 2)]
        [InlineData("2", 2)]
        [InlineData(" Silent ", 0)]
        [InlineData("Gaming", null)]   // ambiguous (two profiles differ only in case)
        [InlineData("9", null)]
        [InlineData("Nope", null)]
        public void Resolves_profiles_by_id_or_name(string arg, int? expected)
        {
            Assert.Equal(expected, CommandProtocol.ResolveProfile(arg, Profiles));
        }

        private static string Row(IReadOnlyList<OverlayRow> rows, string key) => rows.Single(r => r.Key == key).Value;

        [Fact]
        public void Overlay_rows_handle_missing_values()
        {
            var s = new TelemetrySample(DateTime.Now, "Balanced", true, null, null, null, null, null, 8, 32,
                null, null, null, null, 0, -1, "EC auto", null, null, null, null);
            var rows = OverlayForm.Rows(s);
            Assert.Equal(new[] { "FPS", "CPU", "GPU", "PKG", "RAM", "BAT" }, rows.Select(r => r.Key));
            Assert.Equal("–", Row(rows, "FPS"));
            Assert.Equal("25%  (8.0 / 32 GB)", Row(rows, "RAM"));
            Assert.Contains("AC", Row(rows, "BAT"));
        }

        [Fact]
        public void Overlay_shows_fps_watts_and_percent_of_limit()
        {
            var s = new TelemetrySample(DateTime.Now, "Turbo", false, 50, 3200, 20, "3D", 2.7, 16, 32,
                80, 35, 40, 45, 4000, 60, "curve", 80, 12.0, 20.0, 25, 14.5, 4.2, "", 1.0, 2400, 60, "DarkSoulsIII", 130);
            var rows = OverlayForm.Rows(s);
            Assert.Equal("60  DarkSoulsIII", Row(rows, "FPS"));
            Assert.Contains("80°C", Row(rows, "CPU"));
            Assert.Contains("14.5 W", Row(rows, "CPU"));
            Assert.Contains("4.2 W", Row(rows, "GPU"));
            Assert.Contains("2.7 GB", Row(rows, "GPU"));
            Assert.Equal("20.0 W / 25 W (80%)  cap 2.4 GHz", Row(rows, "PKG"));
            Assert.Equal("80%  2h10  12.0 W", Row(rows, "BAT"));
        }

        [Fact]
        public void Overlay_power_limit_defaults_to_the_ec_pl1()
        {
            var s = new TelemetrySample(DateTime.Now, "Balanced", true, 10, 2000, 5, null, null, 8, 32,
                60, 30, 40, 40, 2000, -1, "EC auto", 85, null, 17.5, null);
            Assert.StartsWith("17.5 W / 35 W (50%)", Row(OverlayForm.Rows(s), "PKG"));
        }

        [Fact]
        public void Overlay_says_why_fps_is_missing()
        {
            var s = new TelemetrySample(DateTime.Now, "Balanced", true, 10, 2000, 5, null, null, 8, 32,
                60, 30, 40, 40, 2000, -1, "EC auto", 85, null, 17.5, null);
            Assert.Equal("n/a", Row(OverlayForm.Rows(s, "no rights"), "FPS"));
        }
    }
}
