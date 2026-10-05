using System.Globalization;

namespace FwHelper.Features
{
    /// <summary>One 1 Hz telemetry row. Null = not available (no sensor, no PawnIO, on AC...).</summary>
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
        int? PL1)
    {
        public static readonly string[] Columns =
        {
            "time", "mode", "on_ac", "cpu_pct", "cpu_mhz", "gpu_pct", "gpu_engine", "gpu_shared_gb", "mem_used_gb", "mem_total_gb",
            "cpu_c", "battery_c", "ddr_c", "board_c", "fan_rpm", "fan_duty", "fan_status", "battery_pct", "battery_w", "package_w", "pl1_w",
        };

        public static string CsvHeader => string.Join(",", Columns);

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string ToCsv() => string.Join(",",
            Time.ToString("yyyy-MM-ddTHH:mm:ss", Inv), Text(Mode), OnAC ? "1" : "0",
            Num(CpuPct, "0.0"), Num(CpuMhz, "0"), Num(GpuPct, "0.0"), Text(GpuEngine), Num(GpuSharedGb, "0.00"),
            MemUsedGb.ToString("0.00", Inv), MemTotalGb.ToString("0.00", Inv),
            Num(CpuTemp), Num(BatteryTemp), Num(DdrTemp), Num(BoardTemp),
            FanRpm.ToString(Inv), FanDuty.ToString(Inv), Text(FanStatus),
            Num(BatteryPct), Num(BatteryW, "0.0"), Num(PackageW, "0.0"), Num(PL1));

        /// <summary>Parse a row written by <see cref="ToCsv"/>; null if it doesn't match the header layout.</summary>
        public static TelemetrySample? FromCsv(string line)
        {
            var f = line.Split(',');
            if (f.Length != Columns.Length || !DateTime.TryParse(f[0], Inv, DateTimeStyles.None, out var time)) return null;
            try
            {
                return new TelemetrySample(time, f[1], f[2] == "1",
                    D(f[3]), D(f[4]), D(f[5]), S(f[6]), D(f[7]), D(f[8]) ?? 0, D(f[9]) ?? 0,
                    I(f[10]), I(f[11]), I(f[12]), I(f[13]),
                    I(f[14]) ?? 0, I(f[15]) ?? -1, f[16], I(f[17]), D(f[18]), D(f[19]), I(f[20]));
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
