using System.Text.Json;
using System.Text.Json.Serialization;

namespace FwHelper.Features
{
    /// <summary>
    /// Run notes (plan Phase 1): what a recording can't measure. Written next to the session as <c>session-….notes.json</c>.
    /// FW-Helper fills in the firmware, its own version and the fan curve when recording starts; the user adds the game
    /// settings and free notes when it stops (or later, from Compare). The summary shows them, and Compare lists the ones that
    /// differ, so a forgotten change (another preset, a BIOS update) shows up. Pure apart from <see cref="Load"/>/<see cref="Save"/>.
    /// </summary>
    public sealed class RunContext
    {
        public const string Suffix = ".notes.json";

        // Filled in by FW-Helper
        public string? Bios { get; set; }
        public string? Ec { get; set; }
        public string? App { get; set; }
        public string? FanCurve { get; set; }

        // Typed by the user
        public string? GameSettings { get; set; }
        public string? Notes { get; set; }

        public static string PathFor(string sessionCsv) => Path.ChangeExtension(sessionCsv, null) + Suffix;
        public static bool IsNotesFile(string path) => path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
        public static string SessionFor(string notesPath) => notesPath[..^Suffix.Length] + ".csv";

        /// <summary>The fan as a short name: "EC auto", a saved curve's name, or "custom curve" for one not in the library.</summary>
        public static string FanName(bool custom, string? curve, IEnumerable<(string name, string curve)> library) =>
            !custom ? "EC auto" : library.FirstOrDefault(l => l.curve == curve).name is { Length: > 0 } n ? n : "custom curve";

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        public static RunContext FromJson(string json) => JsonSerializer.Deserialize<RunContext>(json, Json) ?? new RunContext();

        /// <summary>A session's notes, or null if it has none (recordings before 8 Nov 2026) or they can't be read.</summary>
        public static RunContext? Load(string sessionCsv)
        {
            string path = PathFor(sessionCsv);
            if (!File.Exists(path)) return null;
            try { return FromJson(File.ReadAllText(path)); }
            catch (JsonException) { return null; }
        }

        public void Save(string sessionCsv) => File.WriteAllText(PathFor(sessionCsv), ToJson());

        /// <summary>Trimmed, or null when empty, so an emptied box removes the line.</summary>
        public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
