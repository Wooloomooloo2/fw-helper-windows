using FwHelper.Features;
using FwHelper.Helpers;

namespace FwHelper.UI
{
    /// <summary>Writes a session's summary report next to its CSV and opens it in the default browser (plan Phase 2).</summary>
    public static class RunReport
    {
        /// <summary>Make (or remake) the report for a session CSV and open it. Returns the error text, or null.</summary>
        public static string? Open(string csvPath)
        {
            try
            {
                var samples = SessionRecorder.Load(csvPath);
                var summary = RunSummary.From(samples, RunSummary.LabelFromFileName(csvPath));
                string html = SummaryReport.Html(samples, summary, Path.GetFileName(csvPath), Application.ProductVersion.Split('+')[0]);
                string path = SummaryReport.PathFor(csvPath);
                File.WriteAllText(path, html);
                Logger.WriteLine($"Run summary: {path} ({summary.Steady.Count} steady s of {samples.Count})");
                ProcessHelper.OpenUrl(path);
                return null;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Run summary failed: " + ex.Message);
                return ex.Message;
            }
        }

        /// <summary>The session CSV for a file picked in the Monitor: the CSV itself, or the one a report names (same folder).</summary>
        public static string CsvFor(string path)
        {
            if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return path;
            string name = SummaryReport.SessionOf(File.ReadAllText(path))
                          ?? throw new InvalidDataException("This isn't a FW-Helper run summary.");
            string csv = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(name));
            if (!File.Exists(csv)) throw new FileNotFoundException($"The report's recording ({name}) isn't next to it any more.");
            return csv;
        }
    }
}
