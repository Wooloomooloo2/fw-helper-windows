using System.Globalization;

namespace FwHelper.Features
{
    /// <summary>One 1 Hz telemetry row. Null = not available (no sensor, no reading, on AC...). PL1 is the power target (ADR 0016).</summary>
    public record TelemetrySample(
        DateTime Time,
        string Mode,
        bool OnAC,
        double? CpuPct,
        double? CpuMhz,
        double? GpuPct,
        string? GpuEngine,
        double? GpuSharedGb,
        double MemUsedGb,
        double MemTotalGb,
        int? CpuTemp,
        int? BatteryTemp,
        int? DdrTemp,
        int? BoardTemp,
        int FanRpm,
        int FanDuty,
        string FanStatus,
        int? BatteryPct,
        double? BatteryW,
        double? PackageW,
        int? PL1,
        double? CpuW = null,
        double? GpuW = null,
        string? Throttle = null,
        double? DramW = null,
        int? FreqCapMHz = null,
        double? Fps = null,
        string? FpsApp = null,
        int? BatteryMinutes = null)
    {
        /// <summary>CSV columns. Readers map by name, so columns can be added at the end without breaking old sessions.</summary>
        public static readonly string[] Columns =
        {
            "time", "mode", "on_ac", "cpu_pct", "cpu_mhz", "gpu_pct", "gpu_engine", "gpu_shared_gb", "mem_used_gb", "mem_total_gb",
            "cpu_c", "battery_c", "ddr_c", "board_c", "fan_rpm", "fan_duty", "fan_status", "battery_pct", "battery_w", "package_w", "pl1_w",
            "cpu_w", "gpu_w", "throttle",
            "dram_w", "freq_cap_mhz", "fps", "fps_app", "battery_min",
        };

        public static string CsvHeader => string.Join(",", Columns);

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly IReadOnlyDictionary<string, int> CurrentLayout = Layout(CsvHeader)!;

        public string ToCsv() => string.Join(",",
            Time.ToString("yyyy-MM-ddTHH:mm:ss", Inv), Text(Mode), OnAC ? "1" : "0",
            Num(CpuPct, "0.0"), Num(CpuMhz, "0"), Num(GpuPct, "0.0"), Text(GpuEngine), Num(GpuSharedGb, "0.00"),
            MemUsedGb.ToString("0.00", Inv), MemTotalGb.ToString("0.00", Inv),
            Num(CpuTemp), Num(BatteryTemp), Num(DdrTemp), Num(BoardTemp),
            FanRpm.ToString(Inv), FanDuty.ToString(Inv), Text(FanStatus),
            Num(BatteryPct), Num(BatteryW, "0.0"), Num(PackageW, "0.0"), Num(PL1),
            Num(CpuW, "0.0"), Num(GpuW, "0.0"), Text(Throttle),
            Num(DramW, "0.0"), Num(FreqCapMHz), Num(Fps, "0"), Text(FpsApp), Num(BatteryMinutes));

        /// <summary>Column name → index for a header line; null if it isn't a FW-Helper session header.</summary>
        public static IReadOnlyDictionary<string, int>? Layout(string header)
        {
            var names = header.Split(',');
            if (names.Length == 0 || names[0] != "time" || !names.Contains("cpu_c")) return null;
            var map = new Dictionary<string, int>();
            for (int i = 0; i < names.Length; i++) map.TryAdd(names[i], i);
            return map;
        }

        /// <summary>Parse a row; <paramref name="layout"/> comes from the file's header (default: this version's columns).</summary>
        public static TelemetrySample? FromCsv(string line, IReadOnlyDictionary<string, int>? layout = null)
        {
            layout ??= CurrentLayout;
            var f = line.Split(',');
            if (f.Length != layout.Count) return null;
            string F(string name) => layout.TryGetValue(name, out int i) ? f[i] : "";
            if (!DateTime.TryParse(F("time"), Inv, DateTimeStyles.None, out var time)) return null;
            try
            {
                return new TelemetrySample(time, F("mode"), F("on_ac") == "1",
                    D(F("cpu_pct")), D(F("cpu_mhz")), D(F("gpu_pct")), S(F("gpu_engine")), D(F("gpu_shared_gb")),
                    D(F("mem_used_gb")) ?? 0, D(F("mem_total_gb")) ?? 0,
                    I(F("cpu_c")), I(F("battery_c")), I(F("ddr_c")), I(F("board_c")),
                    I(F("fan_rpm")) ?? 0, I(F("fan_duty")) ?? -1, F("fan_status"), I(F("battery_pct")),
                    D(F("battery_w")), D(F("package_w")), I(F("pl1_w")),
                    D(F("cpu_w")), D(F("gpu_w")), S(F("throttle")),
                    D(F("dram_w")), I(F("freq_cap_mhz")), D(F("fps")), S(F("fps_app")), I(F("battery_min")));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static string Num(double? v, string format) => v is double d && double.IsFinite(d) ? d.ToString(format, Inv) : "";
        private static string Num(int? v) => v?.ToString(Inv) ?? "";
        private static string Text(string? s) => (s ?? "").Replace(',', ';');
        private static double? D(string s) => s.Length == 0 ? null : double.Parse(s, Inv);
        private static int? I(string s) => s.Length == 0 ? null : int.Parse(s, Inv);
        private static string? S(string s) => s.Length == 0 ? null : s;
    }
}
