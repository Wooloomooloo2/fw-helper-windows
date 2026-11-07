using System.Globalization;
using System.Net;
using System.Text;

namespace FwHelper.Features
{
    /// <summary>One run as the compare report sees it: its name in the project, its summary and its CSV file name.</summary>
    public sealed record CompareEntry(string Name, RunSummary S, string File);

    /// <summary>
    /// The report for a compare project (plan Phase 3): several runs against a baseline. Self-contained HTML with the same look as
    /// the run summary; the charts are static SVG with hover titles, so it needs no script. Pure: summaries in, string out.
    /// </summary>
    public static class CompareReport
    {
        /// <summary>The meta tag that names the project file a report was made from.</summary>
        public const string ProjectMeta = "fwhelper-project";

        /// <summary>Steady lengths further apart than this are flagged: heat soak makes long runs slower.</summary>
        public const double LengthRatio = 2;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------- What each run was set to ----------

        public static readonly string[] SettingNames = { "Game", "Profile", "Windows mode", "Fan", "Power", "CPU boost", "Power target", "CPU cap" };

        /// <summary>The run's settings as recorded, in <see cref="SettingNames"/> order. Mixed values show their shares.</summary>
        public static string[] Settings(RunSummary s) => new[]
        {
            s.Game ?? "none detected",
            Mixed(s.Modes),
            s.WinModes.Count == 0 ? "not recorded" : Mixed(s.WinModes),
            Mixed(s.Fan),
            s.OnAcFraction >= 1 ? "AC" : s.OnAcFraction == 0 ? "battery" : $"AC {s.OnAcFraction * 100:0} %",
            s.BoostOffFraction >= 1 ? "off" : s.BoostOffFraction == 0 ? "on" : $"off {s.BoostOffFraction * 100:0} %",
            s.PowerTarget is int pt ? $"{pt} W" : "none",
            s.FreqCap is int fc ? $"down to {fc} MHz" : "none",
        };

        private static string Mixed(IReadOnlyList<Share> shares) => shares.Count switch
        {
            0 => "?",
            1 => shares[0].Value,
            _ => string.Join(", ", shares.Take(3).Select(x => $"{x.Value} {x.Fraction * 100:0} %")),
        };

        /// <summary>Indexes into <see cref="SettingNames"/> of the settings that aren't the same in every run.</summary>
        public static int[] Differences(IReadOnlyList<CompareEntry> runs)
        {
            var all = runs.Select(r => Settings(r.S)).ToList();
            return Enumerable.Range(0, SettingNames.Length).Where(i => all.Select(v => v[i]).Distinct().Count() > 1).ToArray();
        }

        // ---------- Can they be compared ----------

        public static List<string> Warnings(IReadOnlyList<CompareEntry> runs)
        {
            var w = new List<string>();
            if (runs.Count < 2) w.Add("A comparison needs at least two runs.");

            var games = runs.Select(r => r.S.Game).Where(g => g is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (games.Count > 1) w.Add($"The runs are of different games ({string.Join(", ", games)}): their FPS can't be compared, only their power.");
            var noGame = runs.Where(r => r.S.Game is null).Select(r => r.Name).ToList();
            if (noGame.Count > 0) w.Add($"No game was detected in {List(noGame)}, so {(noGame.Count == 1 ? "it has" : "they have")} no FPS.");

            string Source(RunSummary s) => s.OnAcFraction >= 1 ? "AC" : s.OnAcFraction == 0 ? "battery" : "mixed";
            if (runs.Select(r => Source(r.S)).Distinct().Count() > 1)
                w.Add("Some runs were on battery and some on AC: the power limits differ between them.");

            var lengths = runs.Where(r => r.S.Steady.Count > 0).Select(r => r.S.Steady.Count).ToList();
            if (lengths.Count > 1 && lengths.Max() > LengthRatio * lengths.Min())
                w.Add($"The runs differ a lot in length ({lengths.Min()} s to {lengths.Max()} s of steady play): longer runs include more heat soak.");
            var shortRuns = runs.Where(r => r.S.Steady.Count is > 0 and < RunSummary.ShortRunSeconds).Select(r => r.Name).ToList();
            if (shortRuns.Count > 0)
                w.Add($"{List(shortRuns)} {(shortRuns.Count == 1 ? "has" : "have")} under {RunSummary.ShortRunSeconds} s of steady play: compare medians only.");

            var days = runs.Where(r => r.S.Start.Year > 2000).Select(r => r.S.Start.Date).Distinct().OrderBy(d => d).ToList();
            if (days.Count > 1)
                w.Add($"The runs were recorded on different days ({string.Join(", ", days.Select(d => d.ToString("d MMM", Inv)))}). " +
                      "Firmware or drivers may have changed in between; FW-Helper doesn't record them yet.");

            // Each run's own warnings, except those already covered above
            // the same warning in several runs is said once
            var own = runs.SelectMany(r => r.S.Warnings
                    .Where(x => !x.StartsWith("No game") && !x.StartsWith("Only ") && !x.StartsWith("The run was on battery"))
                    .Select(x => (r.Name, Text: x)))
                .GroupBy(x => x.Text);
            foreach (var g in own)
            {
                var names = g.Select(x => x.Name).Distinct().ToList();
                w.Add(names.Count == runs.Count && runs.Count > 1 ? $"Every run: {g.Key}" : $"{List(names)}: {g.Key}");
            }
            return w;
        }

        private static string List(IReadOnlyList<string> names) =>
            names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

        // ---------- What changed ----------

        /// <summary>The run with the highest median FPS, or null if none has FPS.</summary>
        public static int? Fastest(IReadOnlyList<CompareEntry> runs) => Best(runs, r => r.S["fps"]?.Median);

        /// <summary>The run with the most FPS per watt, or null.</summary>
        public static int? MostEfficient(IReadOnlyList<CompareEntry> runs) => Best(runs, r => r.S.FpsPerWatt);

        private static int? Best(IReadOnlyList<CompareEntry> runs, Func<CompareEntry, double?> get)
        {
            int? best = null;
            for (int i = 0; i < runs.Count; i++)
                if (get(runs[i]) is double v && (best is null || v > get(runs[best.Value])!.Value)) best = i;
            return best;
        }

        /// <summary>Relative change from the baseline, or null if either is missing or the baseline is 0.</summary>
        public static double? Change(double? value, double? baseline) =>
            value is double v && baseline is double b && b != 0 ? (v - b) / b : null;

        /// <summary>"+11 %", "−8 %", or "same" within half a percent.</summary>
        public static string Signed(double? change) => change switch
        {
            null => "",
            double c when Math.Abs(c) < 0.005 => "same",
            double c => (c > 0 ? "+" : "−") + Math.Abs(c * 100).ToString("0", Inv) + " %",
        };

        private static string SignedW(double? delta) => delta switch
        {
            null => "",
            double d when Math.Abs(d) < 0.05 => "same power",
            double d => (d > 0 ? "+" : "−") + Math.Abs(d).ToString("0.0", Inv) + " W",
        };

        /// <summary>One line per run against the baseline, with the numbers in it, then which run is fastest and most efficient.</summary>
        public static List<string> Conclusions(IReadOnlyList<CompareEntry> runs, int baseline)
        {
            var lines = new List<string>();
            if (runs.Count == 0) return lines;
            var b = runs[baseline].S;
            for (int i = 0; i < runs.Count; i++)
            {
                if (i == baseline) continue;
                var s = runs[i].S;
                var parts = new List<string>();
                if (s["fps"] is { } f && b["fps"] is { } bf)
                    parts.Add($"{Signed(Change(f.Median, bf.Median))} FPS ({bf.Median:0} → {f.Median:0})".Replace("same FPS", "the same FPS"));
                if (s["package_w"] is { } p && b["package_w"] is { } bp)
                    parts.Add($"{SignedW(p.Avg - bp.Avg)} package ({bp.Avg.ToString("0.0", Inv)} → {p.Avg.ToString("0.0", Inv)} W)");
                if (Change(s.FpsPerWatt, b.FpsPerWatt) is double e)
                    parts.Add($"FPS per watt {Signed(e)}");
                if (parts.Count == 0) continue;
                string line = $"{runs[i].Name} against {runs[baseline].Name}: {string.Join(", ", parts)}.";
                string? limit = s.GpuThrottle.FirstOrDefault()?.Value, baseLimit = b.GpuThrottle.FirstOrDefault()?.Value;
                if (limit is not null && baseLimit is not null && limit != baseLimit)
                    line += $" The GPU clock was held mostly by {limit} instead of {baseLimit}.";
                lines.Add(line);
            }
            if (Fastest(runs) is int fast && MostEfficient(runs) is int eff && runs.Count > 1)
                lines.Add(fast == eff
                    ? $"{runs[fast].Name} is both the fastest ({runs[fast].S["fps"]!.Median:0} FPS) and the most efficient ({runs[fast].S.FpsPerWatt!.Value.ToString("0.00", Inv)} FPS/W)."
                    : $"Fastest: {runs[fast].Name} ({runs[fast].S["fps"]!.Median:0} FPS). Most efficient: {runs[eff].Name} ({runs[eff].S.FpsPerWatt!.Value.ToString("0.00", Inv)} FPS/W, {runs[eff].S["fps"]!.Median:0} FPS).");
            return lines;
        }

        // ---------- The page ----------

        public static string Html(string project, IReadOnlyList<CompareEntry> runs, int baseline, string projectFile, string version)
        {
            baseline = runs.Count == 0 ? 0 : Math.Clamp(baseline, 0, runs.Count - 1);
            var sb = new StringBuilder(32 * 1024);
            sb.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append($"<meta name=\"{ProjectMeta}\" content=\"{E(projectFile)}\">\n");
            sb.Append($"<title>{E(project)} · compare</title>\n<style>{Css}</style></head><body>\n<div class=\"wrap\">\n");

            sb.Append("<header>");
            sb.Append($"<div class=\"eyebrow\">FW-Helper comparison · {runs.Count} runs</div>");
            sb.Append($"<h1>{E(project)}</h1>");
            if (runs.Count > 0)
                sb.Append($"<p class=\"lede\">Baseline <b>{E(runs[baseline].Name)}</b>. Every figure covers each run's steady seconds only " +
                          $"(the game presenting frames, from {RunSummary.TrimSeconds} s in, menus left out), as in the run summaries.</p>");
            sb.Append("</header>\n");

            var warnings = Warnings(runs);
            if (warnings.Count > 0)
            {
                sb.Append("<div class=\"warn\"><h3>Read with care</h3><ul>");
                foreach (var w in warnings) sb.Append($"<li>{E(w)}</li>");
                sb.Append("</ul></div>\n");
            }
            if (runs.Count == 0) return End(sb, projectFile, version);
            var b = runs[baseline].S;

            // Tiles
            sb.Append("<div class=\"tiles\">");
            if (Fastest(runs) is int fast)
            {
                var f = runs[fast].S;
                Tile(sb, "Fastest", E(runs[fast].Name), fast == baseline ? $"{f["fps"]!.Median:0} FPS median: the baseline itself"
                    : $"{f["fps"]!.Median:0} FPS median, {Signed(Change(f["fps"]!.Median, b["fps"]?.Median))} on the baseline");
                string? limit = f.GpuThrottle.FirstOrDefault()?.Value, baseLimit = b.GpuThrottle.FirstOrDefault()?.Value;
                if (limit is not null)
                    Tile(sb, "GPU held by", fast == baseline || limit == baseLimit ? E(limit) : $"{E(baseLimit ?? "?")} → {E(limit)}",
                        fast == baseline || limit == baseLimit ? $"the same limit in the baseline and the fastest run: {E(SummaryReport.ThrottleMeaning(limit))}"
                        : $"what held the GPU clock most of the time, baseline → fastest. Now: {E(SummaryReport.ThrottleMeaning(limit))}");
            }
            if (MostEfficient(runs) is int eff)
                Tile(sb, "Most efficient", E(runs[eff].Name), $"{runs[eff].S.FpsPerWatt!.Value.ToString("0.00", Inv)} FPS per watt" +
                    (eff == baseline ? ": the baseline itself" : $", {Signed(Change(runs[eff].S.FpsPerWatt, b.FpsPerWatt))} on the baseline"));
            sb.Append("</div>\n");

            var conclusions = Conclusions(runs, baseline);
            if (conclusions.Count > 0)
            {
                sb.Append("<section><div class=\"eyebrow\">Conclusions</div><h2>What changed</h2><ul class=\"concl\">");
                foreach (var c in conclusions) sb.Append($"<li>{E(c)}</li>");
                sb.Append("</ul><p class=\"note\">Suggestions of what to try next come in a later version.</p></section>\n");
            }

            int section = 1;
            if (runs.Any(r => r.S["fps"] is not null))
            {
                sb.Append($"<section><div class=\"eyebrow\">{section++} · Frame rate</div><h2>FPS</h2><div class=\"chart\">");
                sb.Append("<div class=\"legend\"><span><i class=\"sw\" style=\"background:var(--fps)\"></i>Median</span>" +
                          "<span><i class=\"sw\" style=\"background:var(--fps-2)\"></i>5th percentile second (the slow seconds)</span></div>");
                FpsChart(sb, runs, baseline);
                sb.Append("</div><p class=\"note\">The 5th percentile is of one-second averages, not frame times: 1 % lows need per-frame recording, which comes later.</p></section>\n");
            }
            if (runs.Any(r => r.S["package_w"] is not null))
            {
                sb.Append($"<section><div class=\"eyebrow\">{section++} · Power</div><h2>Where the watts went</h2><div class=\"chart\">");
                sb.Append("<div class=\"legend\"><span><i class=\"sw\" style=\"background:var(--cpu)\"></i>CPU cores</span>" +
                          "<span><i class=\"sw\" style=\"background:var(--gpu)\"></i>GPU</span>" +
                          "<span><i class=\"sw\" style=\"background:var(--rest)\"></i>Rest of package</span>" +
                          "<span><i class=\"sw dash\"></i>PL1 for the run's Windows mode</span></div>");
                PowerChart(sb, runs);
                sb.Append("</div></section>\n");
            }
            if (runs.Any(r => r.S.FpsPerWatt is not null))
            {
                sb.Append($"<section><div class=\"eyebrow\">{section++} · Efficiency</div><h2>FPS per watt</h2><div class=\"chart\">");
                Bars(sb, runs, baseline, r => r.S.FpsPerWatt, v => v.ToString("0.00", Inv), "var(--batt)", "FPS per watt");
                sb.Append("</div><p class=\"note\">Median FPS ÷ average package watts. On battery this is what decides how long a game lasts.</p></section>\n");
            }

            // What differs
            sb.Append($"<section><div class=\"eyebrow\">{section++} · Settings</div><h2>What differs</h2>");
            var diff = Differences(runs);
            var settings = runs.Select(r => Settings(r.S)).ToList();
            if (diff.Length > 0)
            {
                sb.Append("<div class=\"tbl\"><table><thead><tr><th>Run</th>");
                foreach (int i in diff) sb.Append($"<th class=\"l\">{E(SettingNames[i])}</th>");
                sb.Append("</tr></thead><tbody>");
                for (int r = 0; r < runs.Count; r++)
                {
                    sb.Append($"<tr><td>{RunCell(runs, r, baseline)}</td>");
                    foreach (int i in diff)
                        sb.Append($"<td class=\"l{(r != baseline && settings[r][i] != settings[baseline][i] ? " chg" : "")}\">{E(settings[r][i])}</td>");
                    sb.Append("</tr>");
                }
                sb.Append("</tbody></table></div>");
            }
            else sb.Append("<p>Every recorded setting is the same in all runs.</p>");
            var same = Enumerable.Range(0, SettingNames.Length).Except(diff).ToList();
            if (same.Count > 0)
                sb.Append($"<p class=\"note\">The same in every run: {E(string.Join(" · ", same.Select(i => $"{SettingNames[i]} {settings[0][i]}")))}. " +
                          "Fan curves, firmware and in-game settings aren't recorded yet, so a change there won't show here.</p>");
            sb.Append("</section>\n");

            // Every figure, run by run
            sb.Append($"<section><div class=\"eyebrow\">{section} · Figures</div><h2>Run by run</h2><div class=\"tbl\"><table><thead><tr><th>Run</th>");
            var cols = new (string Head, Func<RunSummary, double?> Get, string Fmt, bool Delta)[]
            {
                ("Median FPS", s => s["fps"]?.Median, "0", true),
                ("5th pct FPS", s => s["fps"]?.P5, "0", true),
                ("Package W", s => s["package_w"]?.Avg, "0.0", true),
                ("CPU W", s => s["cpu_w"]?.Avg, "0.0", true),
                ("GPU W", s => s["gpu_w"]?.Avg, "0.0", true),
                ("FPS/W", s => s.FpsPerWatt, "0.00", true),
                ("CPU MHz", s => s["cpu_mhz"]?.Avg, "0", false),
                ("GPU MHz", s => s["gpu_mhz"]?.Avg, "0", false),
                ("CPU °C", s => s["cpu_c"]?.Avg, "0", false),
                ("Fan rpm", s => s["fan_rpm"]?.Avg, "0", false),
                ("Steady s", s => s.Steady.Count, "0", false),
            };
            foreach (var c in cols) sb.Append($"<th>{E(c.Head)}</th>");
            sb.Append("<th class=\"l\">GPU held by</th></tr></thead><tbody>");
            for (int r = 0; r < runs.Count; r++)
            {
                var s = runs[r].S;
                sb.Append($"<tr><td>{RunCell(runs, r, baseline)}</td>");
                foreach (var c in cols)
                {
                    string v = c.Get(s) is double d ? d.ToString(c.Fmt, Inv) : "–";
                    string delta = c.Delta && r != baseline && Signed(Change(c.Get(s), c.Get(b))) is { Length: > 0 } ch ? $"<br><span class=\"u\">{ch}</span>" : "";
                    sb.Append($"<td>{v}{delta}</td>");
                }
                sb.Append($"<td class=\"l\">{E(s.GpuThrottle.FirstOrDefault() is { } t ? $"{t.Value} {t.Fraction * 100:0} %" : "–")}</td></tr>");
            }
            sb.Append("</tbody></table></div><p class=\"note\">Each run's own summary has the second-by-second charts: open its recording in the Monitor and press Summary.</p></section>\n");

            return End(sb, projectFile, version);
        }

        private static string RunCell(IReadOnlyList<CompareEntry> runs, int r, int baseline) =>
            $"{E(runs[r].Name)}{(r == baseline ? " <span class=\"tag\">baseline</span>" : "")}<br><span class=\"u mono\">{E(runs[r].File)}</span>";

        private static string End(StringBuilder sb, string projectFile, string version)
        {
            sb.Append($"<footer class=\"note\">Made by FW-Helper {E(version)} from <span class=\"mono\">{E(projectFile)}</span>. " +
                      "Monitor → Compare… opens the project again, to add runs or make this report again.</footer>\n</div></body></html>\n");
            return sb.ToString();
        }

        // ---------- Charts (static SVG, one row per run) ----------

        private const int CW = 760, LabelW = 190, ValueW = 120, RowH = 34;

        private static string Svg(int rows, string label) =>
            $"<svg viewBox=\"0 0 {CW} {rows * RowH + 22}\" role=\"img\" aria-label=\"{E(label)}\">";

        private static double Nice(double max)
        {
            if (max <= 0) return 1;
            double p = Math.Pow(10, Math.Floor(Math.Log10(max))), m = max / p;
            return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10) * p;
        }

        private static void Axis(StringBuilder sb, double max, Func<double, double> x, int rows, string unit)
        {
            double step = Nice(max / 5);
            for (double t = 0; t <= max + 1e-9; t += step)
            {
                sb.Append($"<line x1=\"{F(x(t))}\" x2=\"{F(x(t))}\" y1=\"14\" y2=\"{rows * RowH + 16}\" style=\"stroke:var(--grid)\"/>");
                sb.Append($"<text x=\"{F(x(t))}\" y=\"10\" text-anchor=\"middle\" class=\"ax\">{t.ToString("0.##", Inv)}{(t + step > max + 1e-9 ? " " + unit : "")}</text>");
            }
        }

        private static void RowLabel(StringBuilder sb, IReadOnlyList<CompareEntry> runs, int r, int baseline)
        {
            double y = 18 + r * RowH + RowH / 2.0;
            sb.Append($"<text x=\"{LabelW - 10}\" y=\"{F(y + 4)}\" text-anchor=\"end\" class=\"rl{(r == baseline ? " base" : "")}\">{E(Clip(runs[r].Name, 26))}</text>");
        }

        private static string Clip(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

        private static void FpsChart(StringBuilder sb, IReadOnlyList<CompareEntry> runs, int baseline)
        {
            double max = Nice(runs.Max(r => r.S["fps"]?.Median ?? 0) * 1.05);
            double X(double v) => LabelW + v / max * (CW - LabelW - ValueW);
            sb.Append(Svg(runs.Count, "Median and 5th percentile FPS per run"));
            Axis(sb, max, X, runs.Count, "FPS");
            double? bm = runs[baseline].S["fps"]?.Median;
            for (int r = 0; r < runs.Count; r++)
            {
                RowLabel(sb, runs, r, baseline);
                if (runs[r].S["fps"] is not { } f) continue;
                double y = 18 + r * RowH + 6;
                sb.Append($"<g><title>{E(runs[r].Name)}: median {f.Median:0} FPS, 5th percentile {f.P5:0}, range {f.Min:0}–{f.Max:0}</title>");
                sb.Append($"<rect x=\"{LabelW}\" y=\"{F(y)}\" width=\"{F(X(f.Median) - LabelW)}\" height=\"14\" rx=\"2\" style=\"fill:var(--fps)\"/>");
                sb.Append($"<rect x=\"{LabelW}\" y=\"{F(y + 16)}\" width=\"{F(X(f.P5) - LabelW)}\" height=\"6\" rx=\"1.5\" style=\"fill:var(--fps-2)\"/>");
                string ch = r == baseline ? "" : Signed(Change(f.Median, bm));
                sb.Append($"<text x=\"{F(X(f.Median) + 6)}\" y=\"{F(y + 12)}\" class=\"val\">{f.Median:0}<tspan class=\"dl\"> {E(ch)}</tspan></text></g>");
            }
            sb.Append("</svg>");
        }

        private static void PowerChart(StringBuilder sb, IReadOnlyList<CompareEntry> runs)
        {
            double top = runs.Max(r => Math.Max(r.S["package_w"]?.Avg ?? 0, SummaryReport.Pl1For(r.S.WinModes.FirstOrDefault()?.Value) ?? 0));
            double max = Math.Max(35, Math.Ceiling(top / 5) * 5);
            double X(double v) => LabelW + v / max * (CW - LabelW - ValueW);
            sb.Append(Svg(runs.Count, "Average CPU, GPU and rest-of-package watts per run"));
            Axis(sb, max, X, runs.Count, "W");
            for (int r = 0; r < runs.Count; r++)
            {
                RowLabel(sb, runs, r, -1);
                var s = runs[r].S;
                if (s["package_w"] is not { } pkg) continue;
                double y = 18 + r * RowH + 6, x0 = 0;
                int? pl1 = SummaryReport.Pl1For(s.WinModes.FirstOrDefault()?.Value);
                sb.Append($"<g><title>{E(runs[r].Name)}: package {W(pkg.Avg)} W = CPU {W(s["cpu_w"]?.Avg)} + GPU {W(s["gpu_w"]?.Avg)} + rest {W(s["rest_w"]?.Avg)}" +
                          (pl1 is int p ? $"; PL1 {p} W ({E(s.WinModes[0].Value)})" : "") + "</title>");
                foreach (var (key, color) in new[] { ("cpu_w", "var(--cpu)"), ("gpu_w", "var(--gpu)"), ("rest_w", "var(--rest)") })
                {
                    if (s[key]?.Avg is not double v || v <= 0) continue;
                    sb.Append($"<rect x=\"{F(X(x0))}\" y=\"{F(y)}\" width=\"{F(Math.Max(0, X(x0 + v) - X(x0) - 1.5))}\" height=\"22\" rx=\"2\" style=\"fill:{color}\"/>");
                    x0 += v;
                }
                if (pl1 is int pl) sb.Append($"<line x1=\"{F(X(pl))}\" x2=\"{F(X(pl))}\" y1=\"{F(y - 3)}\" y2=\"{F(y + 25)}\" style=\"stroke:var(--ink-2)\" stroke-width=\"1.5\" stroke-dasharray=\"3 3\"/>");
                sb.Append($"<text x=\"{F(Math.Max(X(pkg.Avg), pl1 is int q ? X(q) : 0) + 6)}\" y=\"{F(y + 15)}\" class=\"val\">{W(pkg.Avg)} W</text></g>");
            }
            sb.Append("</svg>");
        }

        private static void Bars(StringBuilder sb, IReadOnlyList<CompareEntry> runs, int baseline, Func<CompareEntry, double?> get,
            Func<double, string> fmt, string color, string what)
        {
            double max = Nice(runs.Max(r => get(r) ?? 0) * 1.05);
            double X(double v) => LabelW + v / max * (CW - LabelW - ValueW);
            sb.Append(Svg(runs.Count, what + " per run"));
            Axis(sb, max, X, runs.Count, "");
            for (int r = 0; r < runs.Count; r++)
            {
                RowLabel(sb, runs, r, baseline);
                if (get(runs[r]) is not double v) continue;
                double y = 18 + r * RowH + 6;
                string ch = r == baseline ? "" : Signed(Change(v, get(runs[baseline])));
                sb.Append($"<g><title>{E(runs[r].Name)}: {fmt(v)} {E(what)}</title>");
                sb.Append($"<rect x=\"{LabelW}\" y=\"{F(y)}\" width=\"{F(X(v) - LabelW)}\" height=\"22\" rx=\"2\" style=\"fill:{color}\"/>");
                sb.Append($"<text x=\"{F(X(v) + 6)}\" y=\"{F(y + 15)}\" class=\"val\">{fmt(v)}<tspan class=\"dl\"> {E(ch)}</tspan></text></g>");
            }
            sb.Append("</svg>");
        }

        private static void Tile(StringBuilder sb, string key, string value, string detail) =>
            sb.Append($"<div class=\"tile\"><div class=\"eyebrow\">{E(key)}</div><div class=\"k\">{value}</div><div class=\"d\">{detail}</div></div>");

        private static string E(string s) => WebUtility.HtmlEncode(s);
        private static string F(double v) => v.ToString("0.#", Inv);
        private static string W(double? v) => v is double d ? d.ToString("0.0", Inv) : "–";

        private const string Css = """
:root {
  --bg: #f7f8f9; --panel: #ffffff; --ink: #15171a; --ink-2: #4d525a; --ink-3: #7a808a; --rule: #dfe2e6; --grid: #eceef1;
  --fps: #6250d6; --fps-2: #b3aaf0; --cpu: #2a78d6; --gpu: #eb6834; --rest: #c3c8cf; --batt: #12936b; --warn: #9a6a00; --warn-bg: #fff7e6;
  --chg: #fdf0d8;
  --f-display: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif;
  --f-body: "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif;
  --f-mono: "Cascadia Mono", Consolas, ui-monospace, monospace;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --bg: #131518; --panel: #1b1e22; --ink: #eef0f2; --ink-2: #b8bdc5; --ink-3: #8a909a; --rule: #2e3238; --grid: #24282d;
    --fps: #9085e9; --fps-2: #4f4888; --cpu: #3987e5; --gpu: #d95926; --rest: #4a5058; --batt: #3cbf92; --warn: #e0b04a; --warn-bg: #2a2416;
    --chg: #33291a; color-scheme: dark;
  }
}
:root[data-theme="dark"] {
  --bg: #131518; --panel: #1b1e22; --ink: #eef0f2; --ink-2: #b8bdc5; --ink-3: #8a909a; --rule: #2e3238; --grid: #24282d;
  --fps: #9085e9; --fps-2: #4f4888; --cpu: #3987e5; --gpu: #d95926; --rest: #4a5058; --batt: #3cbf92; --warn: #e0b04a; --warn-bg: #2a2416;
  --chg: #33291a; color-scheme: dark;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--ink); font: 15px/1.55 var(--f-body); }
.wrap { max-width: 860px; margin: 0 auto; padding: 36px 16px 56px; display: flex; flex-direction: column; gap: 30px; }
h1, h2 { font-family: var(--f-display); margin: 0; text-wrap: balance; }
h1 { font-size: 2rem; line-height: 1.15; font-weight: 600; }
h2 { font-size: 1.3rem; font-weight: 600; }
p { margin: 0; max-width: 72ch; }
header { display: flex; flex-direction: column; gap: 10px; }
.eyebrow { font: 500 0.72rem/1 var(--f-mono); text-transform: uppercase; letter-spacing: 0.08em; color: var(--ink-3); }
.lede { color: var(--ink-2); }
.lede b { color: var(--ink); font-weight: 600; }
.note { font-size: 0.84rem; color: var(--ink-3); }
.mono { font-family: var(--f-mono); }
.warn { background: var(--warn-bg); border: 1px solid var(--rule); border-left: 3px solid var(--warn); border-radius: 6px; padding: 12px 16px; }
.warn h3 { margin: 0 0 4px; font: 600 0.95rem var(--f-display); color: var(--warn); }
.warn ul { margin: 0; padding-left: 1.1em; font-size: 0.9rem; color: var(--ink-2); }
.tiles { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.tile { background: var(--panel); border: 1px solid var(--rule); border-radius: 6px; padding: 14px 16px; display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.tile .k { font: 600 1.3rem/1.2 var(--f-display); overflow-wrap: anywhere; }
.tile .d { font-size: 0.84rem; color: var(--ink-2); }
section { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
.wrap > * { min-width: 0; max-width: 100%; }
.concl { margin: 0; padding-left: 1.1em; display: flex; flex-direction: column; gap: 6px; max-width: 72ch; }
.chart { background: var(--panel); border: 1px solid var(--rule); border-radius: 6px; padding: 12px 12px 6px; }
.chart svg { display: block; width: 100%; height: auto; font-family: var(--f-body); }
.chart .ax { font: 10.5px var(--f-mono); fill: var(--ink-3); }
.chart .rl { font-size: 12.5px; fill: var(--ink-2); }
.chart .rl.base { fill: var(--ink); font-weight: 600; }
.chart .val { font: 600 12px var(--f-mono); fill: var(--ink); }
.chart .dl { font-weight: 400; fill: var(--ink-3); }
.chart g:hover rect { opacity: 0.85; }
.legend { display: flex; flex-wrap: wrap; gap: 4px 14px; font-size: 0.78rem; color: var(--ink-2); padding: 0 4px 6px; }
.legend span { display: inline-flex; align-items: center; gap: 6px; }
.sw { width: 11px; height: 11px; border-radius: 2px; display: inline-block; }
.sw.dash { width: 16px; height: 0; border-top: 2px dashed var(--ink-3); border-radius: 0; }
table { border-collapse: collapse; font-size: 0.86rem; width: 100%; }
th, td { text-align: right; padding: 6px 8px; border-bottom: 1px solid var(--rule); font-variant-numeric: tabular-nums; white-space: nowrap; vertical-align: top; }
th:first-child, td:first-child, .l { text-align: left; }
td.chg { background: var(--chg); }
th { font-weight: 600; color: var(--ink-2); font-size: 0.78rem; }
tr:last-child td { border-bottom: 0; }
.u { color: var(--ink-3); font-size: 0.8em; }
.tag { font: 500 0.68rem var(--f-mono); text-transform: uppercase; letter-spacing: 0.06em; color: var(--ink-3); border: 1px solid var(--rule); border-radius: 3px; padding: 0 4px; }
.tbl { overflow-x: auto; background: var(--panel); border: 1px solid var(--rule); border-radius: 6px; padding: 6px 8px; }
@media (max-width: 600px) { .tiles { grid-template-columns: 1fr; } h1 { font-size: 1.6rem; } }
@media print { .chart, .tile, .tbl { break-inside: avoid; } }
""";
    }
}
