using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FwHelper.Features
{
    /// <summary>
    /// A self-contained HTML report of one run (plan Phase 2), opened in the browser and written next to the CSV. System fonts
    /// only and the data embedded as JSON, so it works offline and can be kept or shared as one file. Pure: string in, string out.
    /// </summary>
    public static class SummaryReport
    {
        /// <summary>The meta tag that ties a report to its CSV, so the Monitor can open a report's session.</summary>
        public const string SessionMeta = "fwhelper-session";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The report's path for a session CSV: same name, .html.</summary>
        public static string PathFor(string csvPath) => Path.ChangeExtension(csvPath, ".html");

        /// <summary>The CSV a report was made from, read from its meta tag; null if it isn't a FW-Helper report.</summary>
        public static string? SessionOf(string html)
        {
            var m = System.Text.RegularExpressions.Regex.Match(html, $"<meta name=\"{SessionMeta}\" content=\"([^\"]+)\"");
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        /// <summary>The PL1 Intel DTT sets for each Windows power mode on BIOS 03.04 (hardware-baseline.md), for the power chart.</summary>
        public static int? Pl1For(string? winMode) => winMode switch
        {
            "Efficiency" => 15,
            "Balanced" => 25,
            "Performance" => 33,
            _ => null,
        };

        /// <summary>What an Intel GPU throttle reason means, in words.</summary>
        public static string ThrottleMeaning(string reason) => reason switch
        {
            "none" => "not limited: the GPU ran at the clock it asked for",
            "PL1" => "the sustained package power budget (PL1), which Intel DTT sets from the Windows power mode",
            "PL2" => "the short-term power limit (PL2)",
            "PL4" => "the current limit (PL4)",
            "thermal" => "temperature",
            "PSU" => "the power supply",
            "SW range" => "the driver's frequency range",
            "HW range" => "the hardware frequency range",
            _ => reason.Contains('+') ? "several limits at once" : reason,
        };

        public static string Html(IReadOnlyList<TelemetrySample> samples, RunSummary s, string csvName, string version)
        {
            var sb = new StringBuilder(64 * 1024);
            string title = s.Game ?? "Run";
            string winMode = s.WinModes.FirstOrDefault()?.Value ?? "not recorded";
            string profile = string.Join(" → ", s.Modes.Select(m => m.Value));

            sb.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append($"<meta name=\"{SessionMeta}\" content=\"{E(csvName)}\">\n");
            sb.Append($"<title>{E(title)} · {s.Start.ToString("d MMM HH:mm", Inv)}</title>\n<style>{Css}</style></head><body>\n<div class=\"wrap\">\n");

            // Header
            sb.Append("<header>");
            sb.Append($"<div class=\"eyebrow\">FW-Helper run summary · {E(s.Start.ToString("dddd d MMMM yyyy, HH:mm", Inv))}</div>");
            sb.Append($"<h1>{E(title)}{(s.Label is { } l ? $" <span class=\"lbl\">{E(l)}</span>" : "")}</h1>");
            var ctx = new List<string>
            {
                $"Profile <b>{E(profile.Length > 0 ? profile : "?")}</b>",
                $"Windows <b>{E(winMode)}</b>",
                $"Fan <b>{E(string.Join(", ", s.Fan.Take(2).Select(f => f.Value)))}</b>",
                s.OnAcFraction >= 1 ? "on AC" : s.OnAcFraction == 0 ? "on battery" : "partly on battery",
            };
            if (s.BoostOffFraction >= 0.5) ctx.Add("CPU boost <b>off</b>");
            if (s.PowerTarget is int pt) ctx.Add($"power target <b>{pt} W</b>");
            if (s.FreqCap is int fc) ctx.Add($"CPU capped down to <b>{fc} MHz</b>");
            if (s.Context?.FanCurve is { } curve && curve != "EC auto") ctx.Add($"fan curve <b>{E(curve)}</b>");
            if (s.Context?.GameSettings is { } gs) ctx.Add($"game settings <b>{E(gs)}</b>");
            sb.Append($"<p class=\"lede\">{string.Join(" · ", ctx)}</p>");
            if (s.Context?.Notes is { } notes) sb.Append($"<p class=\"usernote\">{E(notes).Replace("\n", "<br>")}</p>");
            sb.Append($"<p class=\"note\">{Duration(s.Length)} recorded. ");
            sb.Append(s.Game is null
                ? "No game was detected, so the figures cover every second."
                : $"The figures cover the <b>{s.Steady.Count} s</b> in which {E(s.Game)} was presenting frames" +
                  (s.Trimmed ? $", from {RunSummary.TrimSeconds} s after it started (loading and shader compiles distort the start)" : "") +
                  (s.MenuSeconds > 0 ? $". {s.MenuSeconds} s at more than {RunSummary.MenuFactor:0}× the median FPS were left out as menus or loading screens" : "") +
                  ". Only the unshaded seconds count.");
            sb.Append("</p></header>\n");

            if (s.Warnings.Count > 0)
            {
                sb.Append("<div class=\"warn\"><h3>Read with care</h3><ul>");
                foreach (var w in s.Warnings) sb.Append($"<li>{E(w)}</li>");
                sb.Append("</ul></div>\n");
            }

            // Tiles
            sb.Append("<div class=\"tiles\">");
            if (s["fps"] is { } fps)
                Tile(sb, "Median FPS", N(fps.Median), s.Frames is { Low1: double low1 } fr
                    ? $"1 % low <b>{N(low1)}</b>{(fr.Low01 is double low01 ? $" · 0.1 % low {N(low01)}" : "")} · range {N(fps.Min)}–{N(fps.Max)}"
                    : $"5th percentile {N(fps.P5)} · range {N(fps.Min)}–{N(fps.Max)}");
            if (s["package_w"] is { } pkg)
                Tile(sb, "Package power", $"{W(pkg.Avg)}<small> W</small>",
                    $"CPU {W(s["cpu_w"]?.Avg)} W · GPU {W(s["gpu_w"]?.Avg)} W · rest {W(s["rest_w"]?.Avg)} W");
            if (s.FpsPerWatt is double fpw)
                Tile(sb, "FPS per watt", fpw.ToString("0.00", Inv), "Median FPS ÷ average package watts. Higher is more efficient.");
            if (s.GpuThrottle.FirstOrDefault() is { } top)
                Tile(sb, "GPU clock held by", E(top.Value == "none" ? "nothing" : top.Value), $"{Pct(top.Fraction)} of the time: {E(ThrottleMeaning(top.Value))}");
            if (s["gpu_mhz"] is { } gm)
                Tile(sb, "GPU clock", $"{N(gm.Avg)}<small> MHz</small>", s["gpu_req_mhz"] is { } gr ? $"asked for {N(gr.Avg)} MHz · GPU load {N(s["gpu_pct"]?.Avg)} %" : $"GPU load {N(s["gpu_pct"]?.Avg)} %");
            if (s["cpu_c"] is { } ct)
                Tile(sb, "CPU temperature", $"{N(ct.Avg)}<small> °C avg</small>", $"peak {N(ct.Max)} °C · fan {N(s["fan_rpm"]?.Avg)} rpm avg");
            sb.Append("</div>\n");

            // Over time
            sb.Append("<section><div class=\"eyebrow\">1 · Over time</div><h2>Second by second</h2>");
            sb.Append("<p class=\"note\">Hover over (or tap) any chart for every reading at that second.</p>");
            sb.Append("<div id=\"charts\"></div></section>\n");

            // Power split
            if (s["package_w"] is not null)
            {
                sb.Append("<section><div class=\"eyebrow\">2 · Power</div><h2>Where the watts went</h2>");
                sb.Append("<div class=\"chart\" id=\"c-split\"><div class=\"legend\">" +
                          "<span><i class=\"sw\" style=\"background:var(--cpu)\"></i>CPU cores</span>" +
                          "<span><i class=\"sw\" style=\"background:var(--gpu)\"></i>GPU</span>" +
                          "<span><i class=\"sw\" style=\"background:var(--rest)\"></i>Rest of package (uncore, memory, I/O)</span>" +
                          (Pl1For(winMode) is not null ? "<span><i class=\"sw dash\"></i>PL1 for this Windows mode</span>" : "") +
                          "</div></div>");
                sb.Append("<p class=\"note\">Averages over the steady seconds. The CPU and GPU share one package budget: when it's full, watts one takes, the other loses.</p></section>\n");
            }

            // Limits
            if (s.GpuThrottle.Count > 0 || s.EcThrottle.Any(e => e.Value != "none"))
            {
                sb.Append("<section><div class=\"eyebrow\">3 · Limits</div><h2>What held the clocks back</h2>");
                if (s.GpuThrottle.Count > 0)
                {
                    sb.Append("<div class=\"tbl\"><table><thead><tr><th>GPU clock limited by</th><th>Share of time</th><th></th></tr></thead><tbody>");
                    foreach (var t in s.GpuThrottle)
                        sb.Append($"<tr><td>{E(t.Value)}</td><td>{Pct(t.Fraction)}</td><td class=\"why\">{E(ThrottleMeaning(t.Value))}</td></tr>");
                    sb.Append("</tbody></table></div>");
                }
                var ec = s.EcThrottle.Where(e => e.Value != "none").ToList();
                if (ec.Count > 0)
                    sb.Append($"<p>The EC reported CPU throttling ({E(string.Join(", ", ec.Select(e => $"{e.Value} {Pct(e.Fraction)}")))}).</p>");
                else
                    sb.Append("<p class=\"note\">The EC reported no CPU throttling.</p>");
                sb.Append("<p class=\"note\">A diagnosis of the main bottleneck, with suggestions to try, comes in a later version.</p></section>\n");
            }

            // Table of all stats
            sb.Append("<section><div class=\"eyebrow\">4 · Figures</div><h2>Every reading</h2><div class=\"tbl\"><table>");
            sb.Append("<thead><tr><th>Reading</th><th>Average</th><th>Median</th><th>5th pct</th><th>Min</th><th>Max</th></tr></thead><tbody>");
            foreach (var (key, label, unit, _) in RunSummary.Columns)
            {
                if (s[key] is not { } st) continue;
                string F(double v) => RunSummary.Format(v, unit);
                string u = unit.Length > 0 ? $" <span class=\"u\">{E(unit)}</span>" : "";
                sb.Append($"<tr><td>{E(label)}</td><td>{F(st.Avg)}{u}</td><td>{F(st.Median)}</td><td>{F(st.P5)}</td><td>{F(st.Min)}</td><td>{F(st.Max)}</td></tr>");
            }
            sb.Append("</tbody></table></div>");
            if (s.Frames is { } f)
            {
                string Ms(double v) => v.ToString("0.0", Inv);
                sb.Append($"<p class=\"note\">Frame times: {f.Count.ToString("N0", Inv)} frames in the steady seconds, averaging {N(f.AvgFps)} FPS. " +
                          (f.Low1 is double l1 ? $"1 % low {N(l1)} FPS (99 % of frames took under {Ms(f.P99Ms)} ms)" : $"Too few frames for a 1 % low (under {FrameStat.MinFor1})") +
                          (f.Low01 is double l01 ? $", 0.1 % low {N(l01)} FPS" : "") +
                          $"; the longest frame took {Ms(f.MaxMs)} ms. A low far below the median means stutter, even when the average looks fine.</p>");
            }
            else if (s["fps"] is not null)
                sb.Append("<p class=\"note\">This recording has no frame times (they're recorded from 8 Nov 2026 on), so there are no 1 % lows: the 5th percentile is of one-second averages.</p>");
            sb.Append("</section>\n");

            string firmware = s.Context is { } c && (c.Bios ?? c.Ec) is not null
                ? $"Recorded on BIOS {E(c.Bios ?? "?")}, EC {E(c.Ec ?? "?")}{(c.App is { } app ? $", FW-Helper {E(app)}" : "")}. " : "";
            sb.Append($"<footer class=\"note\">{firmware}Made by FW-Helper {E(version)} from <span class=\"mono\">{E(csvName)}</span>. " +
                      "Open the CSV in FW-Helper's Monitor (Open session…) to see it in the live charts, or to make this report again.</footer>\n");
            sb.Append("</div><div id=\"tip\" hidden></div>\n");

            // Data: one array per column over every sample, plus the steady flags
            var steady = new bool[samples.Count];
            foreach (int i in s.Steady) steady[i] = true;
            var data = new Dictionary<string, object?>
            {
                ["t"] = samples.Select(x => (int)Math.Round((x.Time - s.Start).TotalSeconds)).ToArray(),
                ["clock"] = samples.Select(x => x.Time.ToString("HH:mm:ss", Inv)).ToArray(),
                ["steady"] = steady,
                ["gpu_throttle"] = samples.Select(x => x.GpuThrottle).ToArray(),
                ["throttle"] = samples.Select(x => x.Throttle).ToArray(),
                ["fps_app"] = samples.Select(x => x.FpsApp).ToArray(),
                ["pl1"] = Pl1For(winMode),
                ["avg"] = new Dictionary<string, double?>
                {
                    ["cpu_w"] = s["cpu_w"]?.Avg, ["gpu_w"] = s["gpu_w"]?.Avg, ["rest_w"] = s["rest_w"]?.Avg, ["package_w"] = s["package_w"]?.Avg,
                },
            };
            foreach (var (key, _, _, get) in RunSummary.Columns)
                data[key] = samples.Select(x => get(x) is double v && double.IsFinite(v) ? Math.Round(v, 1) : (double?)null).ToArray();

            // The default encoder escapes <, > and &, so the JSON can't close the script element
            sb.Append("<script>const D = ").Append(JsonSerializer.Serialize(data)).Append(";\n").Append(Js).Append("</script>\n</body></html>\n");
            return sb.ToString();
        }

        private static void Tile(StringBuilder sb, string key, string value, string detail) =>
            sb.Append($"<div class=\"tile\"><div class=\"eyebrow\">{E(key)}</div><div class=\"k\">{value}</div><div class=\"d\">{detail}</div></div>");

        private static string E(string s) => WebUtility.HtmlEncode(s);
        private static string N(double? v) => v is double d ? d.ToString("0", Inv) : "–";
        private static string W(double? v) => v is double d ? d.ToString("0.0", Inv) : "–";
        private static string Pct(double f) => (f * 100).ToString("0", Inv) + " %";
        private static string Duration(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{t.Seconds} s";

        private const string Css = """
:root {
  --bg: #f7f8f9; --panel: #ffffff; --ink: #15171a; --ink-2: #4d525a; --ink-3: #7a808a; --rule: #dfe2e6; --grid: #eceef1;
  --shade: rgba(120,128,140,0.10); --fps: #6250d6; --cpu: #2a78d6; --gpu: #eb6834; --rest: #c3c8cf; --pkg: #c8384a;
  --mem: #8a5cd6; --batt: #12936b; --warn: #9a6a00; --warn-bg: #fff7e6;
  --f-display: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif;
  --f-body: "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif;
  --f-mono: "Cascadia Mono", Consolas, ui-monospace, monospace;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --bg: #131518; --panel: #1b1e22; --ink: #eef0f2; --ink-2: #b8bdc5; --ink-3: #8a909a; --rule: #2e3238; --grid: #24282d;
    --shade: rgba(160,170,185,0.09); --fps: #9085e9; --cpu: #3987e5; --gpu: #d95926; --rest: #4a5058; --pkg: #e35d6c;
    --mem: #a585e6; --batt: #3cbf92; --warn: #e0b04a; --warn-bg: #2a2416; color-scheme: dark;
  }
}
:root[data-theme="dark"] {
  --bg: #131518; --panel: #1b1e22; --ink: #eef0f2; --ink-2: #b8bdc5; --ink-3: #8a909a; --rule: #2e3238; --grid: #24282d;
  --shade: rgba(160,170,185,0.09); --fps: #9085e9; --cpu: #3987e5; --gpu: #d95926; --rest: #4a5058; --pkg: #e35d6c;
  --mem: #a585e6; --batt: #3cbf92; --warn: #e0b04a; --warn-bg: #2a2416; color-scheme: dark;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--ink); font: 15px/1.55 var(--f-body); }
.wrap { max-width: 860px; margin: 0 auto; padding: 36px 16px 56px; display: flex; flex-direction: column; gap: 30px; position: relative; }
h1, h2 { font-family: var(--f-display); margin: 0; text-wrap: balance; }
h1 { font-size: 2rem; line-height: 1.15; font-weight: 600; }
h1 .lbl { font-weight: 400; color: var(--ink-2); font-size: 1.3rem; }
h2 { font-size: 1.3rem; font-weight: 600; }
p { margin: 0; max-width: 72ch; }
header { display: flex; flex-direction: column; gap: 10px; }
.eyebrow { font: 500 0.72rem/1 var(--f-mono); text-transform: uppercase; letter-spacing: 0.08em; color: var(--ink-3); }
.lede { color: var(--ink-2); }
.lede b { color: var(--ink); font-weight: 600; }
.usernote { border-left: 3px solid var(--rule); padding: 2px 0 2px 12px; color: var(--ink-2); white-space: normal; }
.note { font-size: 0.84rem; color: var(--ink-3); }
.mono { font-family: var(--f-mono); }
.warn { background: var(--warn-bg); border: 1px solid var(--rule); border-left: 3px solid var(--warn); border-radius: 6px; padding: 12px 16px; }
.warn h3 { margin: 0 0 4px; font: 600 0.95rem var(--f-display); color: var(--warn); }
.warn ul { margin: 0; padding-left: 1.1em; font-size: 0.9rem; color: var(--ink-2); }
.tiles { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.tile { background: var(--panel); border: 1px solid var(--rule); border-radius: 6px; padding: 14px 16px; display: flex; flex-direction: column; gap: 4px; }
.tile .k { font: 600 1.55rem/1.1 var(--f-display); font-variant-numeric: tabular-nums; }
.tile .k small { font-size: 0.9rem; color: var(--ink-3); font-weight: 500; }
.tile .d { font-size: 0.84rem; color: var(--ink-2); }
section { display: flex; flex-direction: column; gap: 12px; }
.chart { background: var(--panel); border: 1px solid var(--rule); border-radius: 6px; padding: 12px 12px 6px; position: relative; }
.chart + .chart { margin-top: 10px; }
.chart h4 { margin: 0 4px 4px; font: 600 0.88rem var(--f-display); }
.chart svg { display: block; width: 100%; height: auto; font-family: var(--f-body); touch-action: pan-y; }
.legend { display: flex; flex-wrap: wrap; gap: 4px 14px; font-size: 0.78rem; color: var(--ink-2); padding: 0 4px 6px; }
.legend span { display: inline-flex; align-items: center; gap: 6px; }
.sw { width: 11px; height: 11px; border-radius: 2px; display: inline-block; }
.sw.dash { width: 16px; height: 0; border-top: 2px dashed var(--ink-3); border-radius: 0; }
table { border-collapse: collapse; font-size: 0.86rem; width: 100%; }
th, td { text-align: right; padding: 6px 8px; border-bottom: 1px solid var(--rule); font-variant-numeric: tabular-nums; white-space: nowrap; }
th:first-child, td:first-child { text-align: left; }
td.why { text-align: left; white-space: normal; color: var(--ink-2); }
th { font-weight: 600; color: var(--ink-2); font-size: 0.78rem; }
tr:last-child td { border-bottom: 0; }
.u { color: var(--ink-3); font-size: 0.8em; }
.tbl { overflow-x: auto; background: var(--panel); border: 1px solid var(--rule); border-radius: 6px; padding: 6px 8px; }
#tip { position: absolute; pointer-events: none; background: var(--ink); color: var(--bg); font: 0.76rem/1.5 var(--f-mono); padding: 7px 10px; border-radius: 4px; white-space: nowrap; z-index: 5; }
#tip b { font-weight: 600; }
#tip .dim { opacity: 0.65; }
@media (max-width: 600px) { .tiles { grid-template-columns: 1fr 1fr; } h1 { font-size: 1.6rem; } }
@media print { #tip { display: none; } .chart, .tile, .tbl { break-inside: avoid; } }
""";

        private const string Js = """
const NS = "http://www.w3.org/2000/svg";
const n = D.t.length;
function el(tag, attrs, parent, text) {
  const e = document.createElementNS(NS, tag);
  for (const k in attrs) e.setAttribute(k, attrs[k]);
  if (text != null) e.textContent = text;
  if (parent) parent.appendChild(e);
  return e;
}
const fmt = (v, d) => v == null ? "–" : v.toFixed(d);
const wrap = document.querySelector(".wrap"), tip = document.getElementById("tip");
wrap.appendChild(tip);

// Every reading at sample i, for the tooltip
function readout(i) {
  const r = [`<b>${D.clock[i]}</b> <span class="dim">+${D.t[i]} s${D.steady[i] ? "" : " · not counted"}</span>`];
  const add = (label, parts) => { const p = parts.filter(x => x); if (p.length) r.push(`<span class="dim">${label}</span> ${p.join(" · ")}`); };
  add("FPS  ", [D.fps[i] != null ? `<b>${fmt(D.fps[i], 0)}</b>` + (D.fps_app[i] ? ` ${D.fps_app[i]}` : "") : null]);
  add("Power", [D.package_w[i] != null ? `pkg <b>${fmt(D.package_w[i], 1)} W</b>` : null, D.cpu_w[i] != null ? `CPU ${fmt(D.cpu_w[i], 1)}` : null,
                D.gpu_w[i] != null ? `GPU ${fmt(D.gpu_w[i], 1)}` : null, D.battery_w[i] != null ? `system ${fmt(D.battery_w[i], 1)}` : null]);
  add("Clock", [D.cpu_mhz[i] != null ? `CPU ${fmt(D.cpu_mhz[i], 0)}` : null,
                D.gpu_mhz[i] != null ? `GPU ${fmt(D.gpu_mhz[i], 0)}` + (D.gpu_req_mhz[i] != null ? `/${fmt(D.gpu_req_mhz[i], 0)}` : "") + " MHz" : null,
                D.gpu_throttle[i] ? `held by ${D.gpu_throttle[i]}` : null]);
  add("Load ", [D.cpu_pct[i] != null ? `CPU ${fmt(D.cpu_pct[i], 0)} %` : null, D.gpu_pct[i] != null ? `GPU ${fmt(D.gpu_pct[i], 0)} %` : null]);
  add("Temp ", [D.cpu_c[i] != null ? `CPU ${D.cpu_c[i]}` : null, D.ddr_c[i] != null ? `DDR ${D.ddr_c[i]}` : null,
                D.battery_c[i] != null ? `batt ${D.battery_c[i]} °C` : null, D.fan_rpm[i] != null ? `fan ${D.fan_rpm[i]} rpm` : null]);
  add("Mem  ", [D.mem_used_gb[i] != null ? `RAM ${fmt(D.mem_used_gb[i], 1)} GB` : null, D.gpu_shared_gb[i] != null ? `GPU ${fmt(D.gpu_shared_gb[i], 1)} GB` : null]);
  if (D.throttle[i]) r.push(`<span class="dim">EC   </span> ${D.throttle[i]}`);
  return r.join("<br>");
}

const charts = [];
const W = 760, L = 46, R = 12, T = 8, H = 150;
function chart(title, series, opts) {
  opts = opts || {};
  if (!series.some(s => D[s.key].some(v => v != null))) return;
  const host = document.createElement("div");
  host.className = "chart";
  host.innerHTML = `<h4>${title}</h4><div class="legend">` +
    series.map(s => `<span><i class="sw" style="background:${s.color}"></i>${s.label}</span>`).join("") + "</div>";
  document.getElementById("charts").appendChild(host);
  const svg = el("svg", {viewBox: `0 0 ${W} ${H + 18}`, role: "img", "aria-label": title + " over time"}, host);
  let lo = opts.min ?? Infinity, hi = opts.max ?? -Infinity;
  if (opts.min == null || opts.max == null)
    series.forEach(s => D[s.key].forEach((v, i) => { if (v != null && (!opts.steadyScale || D.steady[i])) { if (opts.min == null) lo = Math.min(lo, v); if (opts.max == null) hi = Math.max(hi, v); } }));
  (opts.lines || []).forEach(l => hi = Math.max(hi, l.v));
  if (opts.min == null) lo = Math.max(0, Math.floor(lo * 0.9));
  if (!(hi > lo)) hi = lo + 1;
  const step = niceStep((hi - lo) / 4);
  if (opts.max == null) hi = Math.ceil(hi / step) * step;
  lo = Math.floor(lo / step) * step;
  const tMax = Math.max(1, D.t[n - 1]);
  const x = t => L + t / tMax * (W - L - R), y = v => T + (1 - (v - lo) / (hi - lo)) * (H - T);
  // Shade what isn't counted
  let start = null;
  for (let i = 0; i <= n; i++) {
    const out = i < n && !D.steady[i];
    if (out && start == null) start = i;
    if (!out && start != null) {
      const x0 = x(D.t[Math.max(0, start)]) , x1 = x(D.t[Math.min(n - 1, i)]);
      el("rect", {x: x0, y: T, width: Math.max(1, x1 - x0), height: H - T, fill: "var(--shade)"}, svg);
      start = null;
    }
  }
  for (let v = lo; v <= hi + 1e-9; v += step) {
    el("line", {x1: L, x2: W - R, y1: y(v), y2: y(v), stroke: "var(--grid)", "stroke-width": 1}, svg);
    el("text", {x: L - 6, y: y(v) + 4, "text-anchor": "end", "font-size": 10.5, fill: "var(--ink-3)", "font-family": "var(--f-mono)"}, svg, +v.toFixed(1));
  }
  const tStep = niceStep(tMax / 6);
  for (let t = 0; t <= tMax; t += tStep)
    el("text", {x: x(t), y: H + 14, "text-anchor": "middle", "font-size": 10.5, fill: "var(--ink-3)", "font-family": "var(--f-mono)"}, svg, t >= 60 ? `${Math.floor(t / 60)}:${String(t % 60).padStart(2, "0")}` : `${t}s`);
  (opts.lines || []).forEach(l => {
    el("line", {x1: L, x2: W - R, y1: y(l.v), y2: y(l.v), stroke: "var(--ink-3)", "stroke-width": 1.2, "stroke-dasharray": "4 3"}, svg);
    el("text", {x: W - R - 4, y: y(l.v) - 4, "text-anchor": "end", "font-size": 10.5, fill: "var(--ink-3)"}, svg, l.label);
  });
  series.forEach(s => {
    let d = "", pen = false;
    D[s.key].forEach((v, i) => {
      if (v == null) { pen = false; return; }
      d += (pen ? "L" : "M") + x(D.t[i]).toFixed(1) + " " + y(Math.min(Math.max(v, lo), hi)).toFixed(1);
      pen = true;
    });
    el("path", {d, fill: "none", stroke: s.color, "stroke-width": s.width || 1.6, "stroke-linejoin": "round"}, svg);
  });
  const cross = el("line", {y1: T, y2: H, stroke: "var(--ink-2)", "stroke-width": 1, visibility: "hidden"}, svg);
  const dots = series.map(s => el("circle", {r: 3.5, fill: s.color, stroke: "var(--panel)", "stroke-width": 1.5, visibility: "hidden"}, svg));
  const hit = el("rect", {x: L, y: 0, width: W - L - R, height: H, fill: "transparent", tabindex: 0}, svg);
  const c = {host, svg, x, y, cross, dots, series, lo, hi};
  charts.push(c);
  const pick = clientX => {
    const r = svg.getBoundingClientRect();
    const t = ((clientX - r.left) / r.width * W - L) / (W - L - R) * tMax;
    let best = 0;
    for (let i = 1; i < n; i++) if (Math.abs(D.t[i] - t) < Math.abs(D.t[best] - t)) best = i;
    return best;
  };
  const move = (clientX, clientY) => show(pick(clientX), c, clientX, clientY);
  hit.addEventListener("mousemove", e => move(e.clientX, e.clientY));
  hit.addEventListener("touchstart", e => move(e.touches[0].clientX, e.touches[0].clientY), {passive: true});
  hit.addEventListener("touchmove", e => move(e.touches[0].clientX, e.touches[0].clientY), {passive: true});
  hit.addEventListener("mouseleave", hide);
  hit.addEventListener("blur", hide);
  hit.addEventListener("keydown", e => {
    if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
    e.preventDefault();
    current = Math.min(n - 1, Math.max(0, (current ?? 0) + (e.key === "ArrowRight" ? 1 : -1)));
    const r = svg.getBoundingClientRect();
    show(current, c, r.left + x(D.t[current]) / W * r.width, r.top + 20);
  });
}
function niceStep(raw) {
  const p = Math.pow(10, Math.floor(Math.log10(raw || 1))), m = raw / p;
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10) * p;
}
let current = null;
function show(i, from, clientX, clientY) {
  current = i;
  charts.forEach(c => {
    const xv = c.x(D.t[i]);
    c.cross.setAttribute("x1", xv); c.cross.setAttribute("x2", xv); c.cross.setAttribute("visibility", "visible");
    c.series.forEach((s, k) => {
      const v = D[s.key][i];
      if (v == null) { c.dots[k].setAttribute("visibility", "hidden"); return; }
      c.dots[k].setAttribute("cx", xv); c.dots[k].setAttribute("cy", c.y(Math.min(Math.max(v, c.lo), c.hi)));
      c.dots[k].setAttribute("visibility", "visible");
    });
  });
  tip.innerHTML = readout(i); tip.hidden = false;
  const wr = wrap.getBoundingClientRect(), tw = tip.offsetWidth, th = tip.offsetHeight;
  let left = clientX - wr.left + 14;
  if (left + tw > wr.width - 8) left = clientX - wr.left - tw - 14;
  let top = clientY - wr.top + 14;
  const hr = from.host.getBoundingClientRect();
  if (top + th > hr.bottom - wr.top + 40) top = clientY - wr.top - th - 14;
  tip.style.left = Math.max(4, left) + "px"; tip.style.top = Math.max(4, top) + "px";
}
function hide() {
  tip.hidden = true;
  charts.forEach(c => { c.cross.setAttribute("visibility", "hidden"); c.dots.forEach(d => d.setAttribute("visibility", "hidden")); });
}

chart("Frame rate", [{key: "fps", label: "FPS", color: "var(--fps)", width: 1.8}], {min: 0, steadyScale: true});
chart("Power", [{key: "package_w", label: "Package", color: "var(--pkg)", width: 1.8}, {key: "cpu_w", label: "CPU cores", color: "var(--cpu)"},
  {key: "gpu_w", label: "GPU", color: "var(--gpu)"}, {key: "battery_w", label: "System (battery)", color: "var(--batt)"}],
  {min: 0, lines: D.pl1 ? [{v: D.pl1, label: `PL1 ${D.pl1} W`}] : []});
chart("CPU clock", [{key: "cpu_mhz", label: "CPU effective", color: "var(--cpu)"}], {min: 0});
chart("GPU clock", [{key: "gpu_req_mhz", label: "Asked", color: "var(--rest)"}, {key: "gpu_mhz", label: "Actual", color: "var(--gpu)", width: 1.8}], {min: 0});
chart("Load", [{key: "cpu_pct", label: "CPU", color: "var(--cpu)"}, {key: "gpu_pct", label: "GPU", color: "var(--gpu)"}], {min: 0, max: 100});
chart("Temperature", [{key: "cpu_c", label: "CPU", color: "var(--cpu)"}, {key: "ddr_c", label: "DDR", color: "var(--mem)"},
  {key: "battery_c", label: "Battery", color: "var(--batt)"}], {min: 20});
chart("Fan", [{key: "fan_rpm", label: "Fan rpm", color: "var(--ink-2)"}], {min: 0});
chart("Memory", [{key: "mem_used_gb", label: "RAM used", color: "var(--mem)"}, {key: "gpu_shared_gb", label: "GPU shared", color: "var(--gpu)"}], {min: 0});

// Power split: one stacked bar of the steady averages, against PL1
(function () {
  const host = document.getElementById("c-split");
  if (!host || D.avg.package_w == null) return;
  const parts = [["CPU cores", D.avg.cpu_w, "var(--cpu)"], ["GPU", D.avg.gpu_w, "var(--gpu)"], ["Rest", D.avg.rest_w, "var(--rest)"]];
  const max = Math.max(35, Math.ceil((D.pl1 || 0) / 5) * 5, Math.ceil(D.avg.package_w / 5) * 5);
  const SW = 760, SL = 10, SR = 60, bh = 26;
  const svg = el("svg", {viewBox: `0 0 ${SW} 64`, role: "img", "aria-label": "Average CPU, GPU and rest-of-package watts"}, host);
  const x = v => SL + v / max * (SW - SL - SR);
  for (let t = 0; t <= max; t += 5) {
    el("line", {x1: x(t), x2: x(t), y1: 14, y2: 18 + bh, stroke: "var(--grid)"}, svg);
    el("text", {x: x(t), y: 10, "text-anchor": "middle", "font-size": 10.5, fill: "var(--ink-3)", "font-family": "var(--f-mono)"}, svg, t + (t === max ? " W" : ""));
  }
  let x0 = 0;
  parts.forEach(([label, v, col]) => {
    if (v == null) return;
    const w = x(x0 + v) - x(x0);
    el("rect", {x: x(x0), y: 16, width: Math.max(0, w - 1.5), height: bh, fill: col, rx: 2}, svg);
    if (w > 70) el("text", {x: x(x0) + 6, y: 16 + bh / 2 + 4, "font-size": 11.5, fill: label === "Rest" ? "var(--ink)" : "#fff"}, svg, `${label} ${v.toFixed(1)} W`);
    x0 += v;
  });
  el("text", {x: x(D.avg.package_w) + 6, y: 16 + bh / 2 + 4, "font-size": 11.5, fill: "var(--ink)", "font-family": "var(--f-mono)"}, svg, D.avg.package_w.toFixed(1) + " W");
  if (D.pl1) el("line", {x1: x(D.pl1), x2: x(D.pl1), y1: 12, y2: 20 + bh, stroke: "var(--ink-2)", "stroke-width": 1.5, "stroke-dasharray": "3 3"}, svg);
})();
""";
    }
}
