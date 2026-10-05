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

        [Fact]
        public void Overlay_text_handles_missing_values()
        {
            var s = new TelemetrySample(DateTime.Now, "Balanced", false, null, null, null, null, null, 8, 32,
                null, null, null, null, 0, -1, "EC auto", null, null, null, null);
            string text = OverlayForm.Format(s);
            Assert.Contains("CPU", text);
            Assert.Contains("rpm EC", text);
            Assert.Equal(4, text.Split('\n').Length);
        }

        [Fact]
        public void Overlay_prefers_package_power_over_system_draw()
        {
            var s = new TelemetrySample(DateTime.Now, "Turbo", false, 50, 3000, 20, "3D", 1, 8, 32,
                80, 35, 40, 45, 4000, 60, "curve", 80, 25.0, 30.0, 35, 22.0, 6.0);
            Assert.Contains("30.0W pkg", OverlayForm.Format(s));
            Assert.Contains("4000 rpm 60%", OverlayForm.Format(s));
        }
    }
}
