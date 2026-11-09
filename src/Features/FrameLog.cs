using System.Globalization;

namespace FwHelper.Features
{
    /// <summary>
    /// Every frame's time while recording (plan Phase 1), for 1 % and 0.1 % lows: the one-second FPS averages in the session CSV
    /// hide stutter. Written next to the session as <c>session-….frames.csv</c>, one line per frame of the app the FPS counter is
    /// following: <c>time_s</c> (seconds since local midnight of the recording's first day, so renaming the files breaks nothing)
    /// and <c>frame_ms</c> (time since that app's previous present). About 1 MB per 15 minutes at 60 FPS.
    /// </summary>
    public sealed class FrameLog : IDisposable
    {
        public const string Suffix = ".frames.csv";
        public const string Header = "time_s,frame_ms";
        public const double MaxFrameMs = 1000;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly StreamWriter _writer;
        private readonly DateTime _day;
        private int _pid = -1;
        private double? _previous;

        public string FilePath { get; }
        public int Frames { get; private set; }
        /// <summary>The process being followed, and the newest timestamp written (ms on the ETW clock).</summary>
        public int Pid => _pid;
        public double LastMs { get; private set; }

        public FrameLog(string path, DateTime started)
        {
            FilePath = path;
            _day = started.Date;
            _writer = new StreamWriter(path, append: false);
            _writer.WriteLine(Header);
        }

        public static string PathFor(string sessionCsv) => Path.ChangeExtension(sessionCsv, null) + Suffix;
        public static bool IsFrameFile(string path) => path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
        public static string SessionFor(string framesPath) => framesPath[..^Suffix.Length] + ".csv";

        /// <summary>
        /// Append presents of <paramref name="pid"/> (ms on the ETW clock, ascending, all newer than <see cref="LastMs"/> for the
        /// same process). <paramref name="origin"/> is the wall time of ETW time 0. A new process starts without a frame time.
        /// </summary>
        public void Write(int pid, IReadOnlyList<double> presents, DateTime origin)
        {
            if (pid != _pid) { _pid = pid; _previous = null; }
            foreach (double t in presents)
            {
                // Over a second since the last present: the app wasn't presenting (alt-tab, loading), so that isn't a frame
                if (_previous is double p && t > p && t - p <= MaxFrameMs)
                {
                    double seconds = (origin.AddMilliseconds(t) - _day).TotalSeconds;
                    _writer.Write(seconds.ToString("0.0000", Inv));
                    _writer.Write(',');
                    _writer.WriteLine((t - p).ToString("0.00", Inv));
                    Frames++;
                }
                _previous = t;
                LastMs = t;
            }
        }

        public void Dispose()
        {
            _writer.Dispose();
            // A recording without a game has no frames: don't leave an empty file
            if (Frames == 0) try { File.Delete(FilePath); } catch (IOException) { }
        }

        /// <summary>The frames of a recording whose first sample is on <paramref name="day"/>; empty if there's no frames file.</summary>
        public static List<(DateTime Time, double Ms)> Load(string path, DateTime day)
        {
            var frames = new List<(DateTime, double)>();
            if (!File.Exists(path)) return frames;
            using var reader = new StreamReader(path);
            if (reader.ReadLine() != Header) throw new InvalidDataException("Not a FW-Helper frames file");
            DateTime midnight = day.Date;
            while (reader.ReadLine() is { } line)
            {
                int comma = line.IndexOf(',');
                if (comma > 0 && double.TryParse(line.AsSpan(0, comma), NumberStyles.Float, Inv, out double s)
                    && double.TryParse(line.AsSpan(comma + 1), NumberStyles.Float, Inv, out double ms) && ms > 0)
                    frames.Add((midnight.AddSeconds(s), ms));
            }
            return frames;
        }
    }
}
