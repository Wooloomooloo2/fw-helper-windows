using FwHelper.Helpers;
using System.Globalization;

namespace FwHelper.Features
{
    /// <summary>
    /// Writes telemetry to %AppData%\FwHelper\sessions\session-yyyyMMdd-HHmmss.csv (same idea as the Linux recorder):
    /// flushed every 10 rows, auto-stops after 12 h, keeps the newest 20 sessions.
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        public const int KeepSessions = 20;
        public const int FlushEvery = 10;
        public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);

        public static readonly string Folder = Path.Combine(Logger.AppDataDir, "sessions");

        private readonly StreamWriter _writer;
        private int _rows;

        public string FilePath { get; }
        public DateTime Started { get; } = DateTime.Now;
        public int Rows => _rows;
        public bool Expired => DateTime.Now - Started >= MaxDuration;

        public SessionRecorder()
        {
            Directory.CreateDirectory(Folder);
            foreach (var old in ToPrune(Directory.GetFiles(Folder, "session-*.csv"), KeepSessions - 1))
            {
                try { File.Delete(old); } catch { }
            }

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

        /// <summary>Session files to delete so that at most <paramref name="keep"/> remain (names sort by start time).</summary>
        public static IEnumerable<string> ToPrune(IEnumerable<string> files, int keep) =>
            files.OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(Math.Max(0, keep));

        public static List<TelemetrySample> Load(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0] != TelemetrySample.CsvHeader)
                throw new InvalidDataException("Not a FW-Helper session file (header doesn't match this version)");
            return lines.Skip(1).Select(TelemetrySample.FromCsv).OfType<TelemetrySample>().ToList();
        }
    }
}
