using FwHelper.Helpers;
using System.Globalization;

namespace FwHelper.Features
{
    /// <summary>
    /// Writes telemetry to Documents\FW Benchmark\session-yyyyMMdd-HHmmss.csv (same idea as the Linux recorder):
    /// flushed every 10 rows, auto-stops after 12 h. Never deletes sessions.
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        public const int FlushEvery = 10;
        public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);

        /// <summary>Documents\FW Benchmark (the user's request, 2026-11-04). Before that: %AppData%\FwHelper\sessions.</summary>
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FW Benchmark");

        private readonly StreamWriter _writer;
        private int _rows;

        public string FilePath { get; }
        public DateTime Started { get; } = DateTime.Now;
        public int Rows => _rows;
        public bool Expired => DateTime.Now - Started >= MaxDuration;

        public SessionRecorder()
        {
            // No pruning: the folder is in the user's Documents and benchmark sessions are kept for comparison
            Directory.CreateDirectory(Folder);

            FilePath = Path.Combine(Folder, $"session-{Started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.csv");
            _writer = new StreamWriter(FilePath, append: false);
            _writer.WriteLine(TelemetrySample.CsvHeader);
            _writer.Flush();
            Logger.WriteLine("Recording to " + FilePath);
        }

        public void Write(TelemetrySample sample)
        {
            _writer.WriteLine(sample.ToCsv());
            if (++_rows % FlushEvery == 0) _writer.Flush();
        }

        public void Dispose()
        {
            _writer.Flush();
            _writer.Dispose();
            Logger.WriteLine($"Recording stopped: {_rows} rows");
        }

        public static List<TelemetrySample> Load(string path)
        {
            var lines = File.ReadAllLines(path);
            // Columns are matched by name, so sessions from older versions (fewer columns) still open
            var layout = lines.Length > 0 ? TelemetrySample.Layout(lines[0]) : null;
            if (layout is null) throw new InvalidDataException("Not a FW-Helper session file");
            return lines.Skip(1).Select(l => TelemetrySample.FromCsv(l, layout)).OfType<TelemetrySample>().ToList();
        }
    }
}
