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
                var (samples, summary) = Summarise(csvPath, RunSummary.LabelFromFileName(csvPath));
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

        /// <summary>A recording and its summary, with its frame times when it has a frames file.</summary>
        public static (List<TelemetrySample> Samples, RunSummary Summary) Summarise(string csvPath, string? label)
        {
            var samples = SessionRecorder.Load(csvPath);
            List<(DateTime, double)>? frames = null;
            if (samples.Count > 0)
            {
                try { frames = FrameLog.Load(FrameLog.PathFor(csvPath), samples[0].Time); }
                catch (Exception ex) { Logger.WriteLine($"Frame times not read ({Path.GetFileName(csvPath)}): {ex.Message}"); }
            }
            var summary = RunSummary.From(samples, label, frames);
            summary.Context = RunContext.Load(csvPath);
            return (samples, summary);
        }

        /// <summary>The session CSV for a file picked in the Monitor: the CSV itself, its frames file's, or the one a report names (same folder).</summary>
        public static string CsvFor(string path)
        {
            if (FrameLog.IsFrameFile(path)) return FrameLog.SessionFor(path);
            if (RunContext.IsNotesFile(path)) return RunContext.SessionFor(path);
            if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return path;
            string name = SummaryReport.SessionOf(File.ReadAllText(path))
                          ?? throw new InvalidDataException("This isn't a FW-Helper run summary.");
            string csv = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(name));
            if (!File.Exists(csv)) throw new FileNotFoundException($"The report's recording ({name}) isn't next to it any more.");
            return csv;
        }
    }
}
