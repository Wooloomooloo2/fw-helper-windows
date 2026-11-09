using System.Globalization;
using System.Text.RegularExpressions;

namespace FwHelper.Features
{
    /// <summary>Min / average / max / median / 5th percentile of one column over the steady seconds.</summary>
    public record Stat(double Min, double Avg, double Max, double Median, double P5, int Count);

    /// <summary>One value of a text column and its share of the steady seconds (0–1).</summary>
    public record Share(string Value, double Fraction);

    /// <summary>
    /// Frame times over the steady seconds (plan Phase 1). The lows are the FPS of the 99th and 99.9th percentile frame time, the
    /// usual definition (PresentMon, CapFrameX "percentile" lows): they show stutter that one-second averages hide.
    /// </summary>
    public record FrameStat(int Count, double AvgFps, double? Low1, double? Low01, double P99Ms, double MaxMs)
    {
        /// <summary>Fewer frames than this and a percentile is a handful of frames: not shown.</summary>
        public const int MinFor1 = 100, MinFor01 = 1000;

        public static FrameStat? From(IEnumerable<double> frameMs)
        {
            var v = frameMs.Where(x => x > 0 && double.IsFinite(x)).OrderBy(x => x).ToArray();
            if (v.Length == 0) return null;
            double p99 = RunSummary.Percentile(v, 99), p999 = RunSummary.Percentile(v, 99.9);
            return new FrameStat(v.Length, 1000 / v.Average(),
                v.Length >= MinFor1 ? 1000 / p99 : null, v.Length >= MinFor01 ? 1000 / p999 : null, p99, v[^1]);
        }
    }

    /// <summary>
    /// What one recorded run did (plan Phase 2), from its 1 Hz samples. Pure, so it is unit-tested against fixed rows.
    /// Steady state: the seconds where the run's game was presenting frames, minus the first <see cref="TrimSeconds"/>
    /// (loading, shader compile). Without a game, every second counts.
    /// </summary>
    public sealed class RunSummary
    {
        public const int TrimSeconds = 10;
        /// <summary>Below this many steady seconds the 5th percentile is too noisy to compare runs on.</summary>
        public const int ShortRunSeconds = 60;
        /// <summary>Seconds above this multiple of the median FPS are taken as menus or loading screens and left out.</summary>
        public const double MenuFactor = 2;

        public string? Label { get; init; }
        public string? Game { get; private init; }
        public DateTime Start { get; private init; }
        public DateTime End { get; private init; }
        public TimeSpan Length => End - Start;
        /// <summary>Indices into the samples: the steady window (inclusive) and which samples in it count.</summary>
        public int SteadyFrom { get; private init; }
        public int SteadyTo { get; private init; }
        public IReadOnlyList<int> Steady { get; private init; } = Array.Empty<int>();
        public bool Trimmed { get; private init; }
        /// <summary>Seconds left out as menus or loading screens (FPS above <see cref="MenuFactor"/> × median).</summary>
        public int MenuSeconds { get; private init; }

        public IReadOnlyDictionary<string, Stat> Stats { get; private init; } = new Dictionary<string, Stat>();
        public IReadOnlyList<Share> Modes { get; private init; } = Array.Empty<Share>();
        public IReadOnlyList<Share> WinModes { get; private init; } = Array.Empty<Share>();
        public IReadOnlyList<Share> GpuThrottle { get; private init; } = Array.Empty<Share>();
        public IReadOnlyList<Share> EcThrottle { get; private init; } = Array.Empty<Share>();
        public IReadOnlyList<Share> Fan { get; private init; } = Array.Empty<Share>();
        public double OnAcFraction { get; private init; }
        /// <summary>Share of the steady seconds with CPU boost off (0–1).</summary>
        public double BoostOffFraction { get; private init; }
        public int? PowerTarget { get; private init; }
        public int? FreqCap { get; private init; }
        public IReadOnlyList<string> Warnings { get; private init; } = Array.Empty<string>();

        public Stat? this[string key] => Stats.TryGetValue(key, out var s) ? s : null;
        /// <summary>Median FPS per average package watt; null without both.</summary>
        /// <summary>Frame-time figures, when the recording has a frames file (recordings from 2026-11-08 on).</summary>
        public FrameStat? Frames { get; private init; }

        /// <summary>The run notes (firmware, fan curve, game settings, notes), when the recording has them. Set by the loader.</summary>
        public RunContext? Context { get; set; }

        public double? FpsPerWatt => this["fps"] is { } f && this["package_w"] is { Avg: > 0 } p ? f.Median / p.Avg : null;

        /// <summary>The numeric columns summarised, with their CSV name, label and unit.</summary>
        public static readonly (string Key, string Label, string Unit, Func<TelemetrySample, double?> Get)[] Columns =
        {
            ("fps", "FPS", "", s => s.Fps),
            ("package_w", "Package", "W", s => s.PackageW),
            ("cpu_w", "CPU cores", "W", s => s.CpuW),
            ("gpu_w", "GPU", "W", s => s.GpuW),
            ("rest_w", "Rest of package", "W", s => s.PackageW is double p && s.CpuW is double c && s.GpuW is double g ? Math.Max(0, p - c - g) : null),
            ("dram_w", "DRAM", "W", s => s.DramW),
            ("battery_w", "System (battery)", "W", s => s.BatteryW),
            ("cpu_pct", "CPU load", "%", s => s.CpuPct),
            ("gpu_pct", "GPU load", "%", s => s.GpuPct),
            ("cpu_mhz", "CPU clock", "MHz", s => s.CpuMhz),
            ("gpu_mhz", "GPU clock", "MHz", s => s.GpuMhz),
            ("gpu_req_mhz", "GPU clock asked", "MHz", s => s.GpuReqMhz),
            ("cpu_c", "CPU temp", "°C", s => s.CpuTemp),
            ("ddr_c", "DDR temp", "°C", s => s.DdrTemp),
            ("battery_c", "Battery temp", "°C", s => s.BatteryTemp),
            ("board_c", "Board temp", "°C", s => s.BoardTemp),
            ("fan_rpm", "Fan", "rpm", s => s.FanRpm),
            ("mem_used_gb", "RAM used", "GB", s => s.MemUsedGb > 0 ? s.MemUsedGb : null),
            ("gpu_shared_gb", "GPU shared memory", "GB", s => s.GpuSharedGb),
        };

        public static RunSummary From(IReadOnlyList<TelemetrySample> samples, string? label = null,
            IReadOnlyList<(DateTime Time, double Ms)>? frames = null)
        {
            if (samples.Count == 0) return new RunSummary { Label = label, Warnings = new[] { "The recording is empty." } };

            // The game: the process that presented frames for the most seconds
            string? game = samples.Where(s => s.Fps > 0 && s.FpsApp is { Length: > 0 })
                .GroupBy(s => s.FpsApp!).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
            bool Playing(TelemetrySample s) => game is null || (s.FpsApp == game && s.Fps > 0);

            int first = 0, last = samples.Count - 1;
            if (game is not null)
            {
                first = IndexOf(samples, Playing, 0, 1);
                last = IndexOf(samples, Playing, samples.Count - 1, -1);
            }

            // Trim the start (loading, shaders), as long as at least 20 s are left
            int from = first;
            bool trimmed = false;
            while (from < last && (samples[from].Time - samples[first].Time).TotalSeconds < TrimSeconds) from++;
            if ((samples[last].Time - samples[from].Time).TotalSeconds >= 20) trimmed = from > first;
            else from = first;

            var steady = Enumerable.Range(from, last - from + 1).Where(i => Playing(samples[i])).ToList();
            // Menus and loading screens run uncapped: drop seconds above twice the median FPS (real dips stay in)
            int menus = 0;
            if (game is not null && steady.Count > 0)
            {
                double median = Percentile(steady.Select(i => samples[i].Fps!.Value).OrderBy(v => v).ToArray(), 50);
                menus = steady.RemoveAll(i => samples[i].Fps > MenuFactor * median);
            }
            var rows = steady.Select(i => samples[i]).ToList();

            var stats = new Dictionary<string, Stat>();
            foreach (var (key, _, _, get) in Columns)
                if (Describe(rows.Select(get)) is { } st) stats[key] = st;

            var warnings = new List<string>();
            var modes = Shares(rows, s => s.Mode);
            var winModes = Shares(rows.Where(s => s.WinMode is not null).ToList(), s => s.WinMode);
            double onAc = rows.Count == 0 ? 0 : rows.Count(s => s.OnAC) / (double)rows.Count;

            if (game is null) warnings.Add("No game was presenting frames, so every second is counted and there's no FPS.");
            if (steady.Count > 0 && steady.Count < ShortRunSeconds)
                warnings.Add($"Only {steady.Count} s of steady play: the 5th percentile isn't reliable below {ShortRunSeconds} s, so compare medians only.");
            if (onAc < 1 && rows.Count > 0)
                warnings.Add(onAc == 0 ? "The run was on battery: power limits are lower than on AC." : $"The charger was unplugged for {(1 - onAc) * 100:0} % of the run.");
            if (modes.Count > 1) warnings.Add("The FW-Helper profile changed during the run: " + string.Join(", ", modes.Select(m => m.Value)) + ".");
            if (winModes.Count > 1) warnings.Add("The Windows power mode changed during the run: " + string.Join(", ", winModes.Select(m => m.Value)) + ".");
            double boostOff = rows.Count == 0 ? 0 : rows.Count(r => r.BoostOff) / (double)rows.Count;
            if (boostOff is > 0 and < 1) warnings.Add($"CPU boost was off for {boostOff * 100:0} % of the run and on for the rest.");
            if (winModes.Any(m => m.Value == "Battery Saver")) warnings.Add("Battery Saver was on.");
            if (rows.Count > 0 && rows.All(s => s.WinMode is null))
                warnings.Add("This recording is from before FW-Helper recorded the Windows power mode, so it isn't known.");

            return new RunSummary
            {
                Label = label,
                Game = game,
                Start = samples[0].Time,
                End = samples[^1].Time,
                SteadyFrom = from,
                SteadyTo = last,
                Steady = steady,
                Trimmed = trimmed,
                MenuSeconds = menus,
                Stats = stats,
                Modes = modes,
                WinModes = winModes,
                GpuThrottle = rows.Any(s => s.GpuMhz is not null) ? Shares(rows.Where(s => s.GpuMhz is not null).ToList(), s => s.GpuThrottle ?? "none") : Array.Empty<Share>(),
                EcThrottle = Shares(rows, s => s.Throttle ?? "none"),
                Fan = Shares(rows, s => s.FanStatus is { Length: > 0 } f ? Regex.Replace(f, @"\s*\d+%.*$", "") : "?"),
                OnAcFraction = onAc,
                BoostOffFraction = boostOff,
                PowerTarget = rows.Select(s => s.PL1).FirstOrDefault(p => p is not null),
                FreqCap = rows.Select(s => s.FreqCapMHz).Where(c => c is not null).Min(),
                Warnings = warnings,
                Frames = frames is { Count: > 0 } ? FrameStat.From(SteadyFrames(samples, steady, frames)) : null,
            };
        }

        /// <summary>
        /// The frame times that fall in steady seconds. A sample covers the second before it was taken, so a frame belongs to the
        /// first sample at or after it (if that's within 1.5 s); frames in loading, menus and the trimmed start are left out.
        /// </summary>
        public static IEnumerable<double> SteadyFrames(IReadOnlyList<TelemetrySample> samples, IReadOnlyCollection<int> steady,
            IReadOnlyList<(DateTime Time, double Ms)> frames)
        {
            var isSteady = new HashSet<int>(steady);
            int i = 0;
            foreach (var (time, ms) in frames.OrderBy(f => f.Time))
            {
                while (i < samples.Count && samples[i].Time < time) i++;
                if (i == samples.Count) yield break;
                if (isSteady.Contains(i) && (samples[i].Time - time).TotalSeconds <= 1.5) yield return ms;
            }
        }

        /// <summary>The label a user gave by renaming the file: "session-20261104-172058cyber_punk_bal" → "cyber punk bal".</summary>
        public static string? LabelFromFileName(string path)
        {
            var m = Regex.Match(Path.GetFileNameWithoutExtension(path), @"^session-\d{8}-\d{6}[\s_-]*(.+)$");
            return m.Success ? m.Groups[1].Value.Replace('_', ' ').Trim() : null;
        }

        public static Stat? Describe(IEnumerable<double?> values)
        {
            var v = values.OfType<double>().Where(double.IsFinite).OrderBy(x => x).ToArray();
            if (v.Length == 0) return null;
            return new Stat(v[0], v.Average(), v[^1], Percentile(v, 50), Percentile(v, 5), v.Length);
        }

        /// <summary>Linear-interpolated percentile of sorted values.</summary>
        public static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 1) return sorted[0];
            double rank = p / 100 * (sorted.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = Math.Min(lo + 1, sorted.Length - 1);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
        }

        private static List<Share> Shares(IReadOnlyList<TelemetrySample> rows, Func<TelemetrySample, string?> get)
        {
            if (rows.Count == 0) return new List<Share>();
            return rows.GroupBy(r => get(r) is { Length: > 0 } v ? v : "none")
                .Select(g => new Share(g.Key, g.Count() / (double)rows.Count))
                .OrderByDescending(s => s.Fraction).ToList();
        }

        private static int IndexOf(IReadOnlyList<TelemetrySample> s, Func<TelemetrySample, bool> match, int start, int step)
        {
            for (int i = start; i >= 0 && i < s.Count; i += step)
                if (match(s[i])) return i;
            return start;
        }

        public static string Format(double v, string unit) => unit switch
        {
            "W" => v.ToString("0.0", CultureInfo.InvariantCulture),
            "GB" => v.ToString("0.0", CultureInfo.InvariantCulture),
            _ => v.ToString("0", CultureInfo.InvariantCulture),
        };
    }
}
