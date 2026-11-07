using System.Text.Json;
using System.Text.Json.Serialization;

namespace FwHelper.Features
{
    /// <summary>One run in a compare project: its recording and a short name for the charts ("A: Balanced, EC fan").</summary>
    public sealed record CompareRun(string File, string Name);

    /// <summary>
    /// A named set of recordings compared against one of them, the baseline (plan Phase 3). Stored as
    /// <c>Documents\FW Benchmark\projects\&lt;name&gt;.json</c>; the report is <c>&lt;name&gt;.html</c> next to it. Pure: (de)serialises
    /// and resolves paths, the caller does the file I/O.
    /// </summary>
    public sealed class CompareProject
    {
        public const string Extension = ".json";
        public const int MaxRuns = 12;

        public string Name { get; set; } = "";
        public int Baseline { get; set; }
        public List<CompareRun> Runs { get; set; } = new();

        public static string FolderIn(string sessionsFolder) => Path.Combine(sessionsFolder, "projects");
        public static string PathFor(string projectsFolder, string name) => Path.Combine(projectsFolder, SessionRecorder.CleanName(name) + Extension);
        public static string ReportPathFor(string projectPath) => Path.ChangeExtension(projectPath, ".html");

        /// <summary>A recording in the sessions folder is stored by file name, so the folder can move; anything else by full path.</summary>
        public static string Store(string sessionsFolder, string csvPath)
        {
            string full = Path.GetFullPath(csvPath);
            return string.Equals(Path.GetDirectoryName(full)?.TrimEnd('\\', '/'), Path.GetFullPath(sessionsFolder).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(full) : full;
        }

        public static string Resolve(string sessionsFolder, string stored) =>
            Path.IsPathRooted(stored) ? stored : Path.Combine(sessionsFolder, stored);

        /// <summary>The baseline index, kept inside the list.</summary>
        public int BaselineIndex => Runs.Count == 0 ? 0 : Math.Clamp(Baseline, 0, Runs.Count - 1);

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        public static CompareProject FromJson(string json)
        {
            var p = JsonSerializer.Deserialize<CompareProject>(json, Json) ?? throw new InvalidDataException("Not a compare project.");
            p.Runs = (p.Runs ?? new()).Where(r => !string.IsNullOrWhiteSpace(r?.File)).Select(r => r with { Name = r.Name ?? "" }).ToList();
            p.Baseline = p.BaselineIndex;
            return p;
        }

        /// <summary>The name a run gets when it's added: the recording's name if it has one, else game and start time.</summary>
        public static string DefaultName(string csvPath, RunSummary? s)
        {
            if (RunSummary.LabelFromFileName(csvPath) is { Length: > 0 } label) return label;
            string when = s is { Start.Year: > 2000 } ? s.Start.ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture) : Path.GetFileNameWithoutExtension(csvPath);
            string? game = s?.Game is { } g ? Path.GetFileNameWithoutExtension(g) : null;
            string? mode = s?.Modes.FirstOrDefault()?.Value;
            return string.Join(" ", new[] { game, mode, when }.Where(x => !string.IsNullOrEmpty(x)));
        }
    }
}
