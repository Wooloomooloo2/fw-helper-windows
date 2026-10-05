using System.Globalization;

namespace FwHelper.Helpers
{
    public record CliCommand(string Verb, string? Arg);

    /// <summary>
    /// The CLI's command set and its one-line pipe protocol (ADR 0015). Pure: parsing and validation only, so it can be unit-tested.
    /// </summary>
    public static class CommandProtocol
    {
        public static readonly string[] Verbs = { "status", "profiles", "mode", "charge-limit", "fan-floor", "help" };

        public const string Usage =
            "FW-Helper command line\n" +
            "  FwHelper.exe --status                 current mode, temperatures, fan, battery\n" +
            "  FwHelper.exe --profiles               list profiles (id, name)\n" +
            "  FwHelper.exe --mode <name|id>         switch profile (needs FW-Helper running)\n" +
            "  FwHelper.exe --charge-limit <50-100>  set the battery charge limit (needs FW-Helper running)\n" +
            "  FwHelper.exe --fan-floor on|off       never quieter than the EC (needs FW-Helper running)\n" +
            "Tip: in PowerShell pipe the output (| Out-String) so the prompt waits for it.";

        /// <summary>True if the arguments are a CLI command rather than app options (-tray, --selftest, ...).</summary>
        public static bool IsCommand(string[] args) =>
            args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal) && Verbs.Contains(args[0][2..].ToLowerInvariant());

        /// <summary>Parse "--verb [arg]"; returns an error message instead of a command when the input is invalid.</summary>
        public static (CliCommand? command, string? error) Parse(string[] args)
        {
            if (!IsCommand(args)) return (null, Usage);
            string verb = args[0][2..].ToLowerInvariant();
            string? arg = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

            switch (verb)
            {
                case "mode" when string.IsNullOrWhiteSpace(arg):
                    return (null, "--mode needs a profile name or id (see --profiles)");
                case "charge-limit" when !int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) || limit is < 50 or > 100:
                    return (null, "--charge-limit needs a number from 50 to 100");
                case "fan-floor" when arg?.ToLowerInvariant() is not ("on" or "off"):
                    return (null, "--fan-floor needs on or off");
                case "help":
                    return (null, Usage);
            }
            return (new CliCommand(verb, arg?.Trim()), null);
        }

        /// <summary>Wire format: "verb\targ" on one line.</summary>
        public static string Encode(CliCommand c) => c.Arg is null ? c.Verb : $"{c.Verb}\t{c.Arg.Replace('\n', ' ').Replace('\t', ' ')}";

        public static CliCommand? Decode(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            int tab = line.IndexOf('\t');
            string verb = (tab < 0 ? line : line[..tab]).Trim().ToLowerInvariant();
            if (!Verbs.Contains(verb)) return null;
            return new CliCommand(verb, tab < 0 ? null : line[(tab + 1)..]);
        }

        /// <summary>Profile by id or case-insensitive name; null if unknown or ambiguous.</summary>
        public static int? ResolveProfile(string arg, IEnumerable<(int id, string name)> profiles)
        {
            var list = profiles.ToList();
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && list.Any(p => p.id == id)) return id;
            var matches = list.Where(p => p.name.Equals(arg.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? matches[0].id : null;
        }

        /// <summary>The pipe is per user; CurrentUserOnly on both ends stops other accounts from connecting.</summary>
        public static string PipeName => "FwHelper-" + Environment.UserName;
    }
}
