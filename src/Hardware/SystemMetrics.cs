using System.Runtime.InteropServices;

namespace FwHelper.Hardware
{
    public record SystemSnapshot(
        double? CpuPct, double? CpuMhz, double? GpuPct, string? GpuEngine, double? GpuSharedGb,
        double MemUsedGb, double MemTotalGb);

    /// <summary>
    /// OS-side load metrics (no admin): CPU utility and effective clock, GPU engine load, memory.
    /// CPU numbers match Task Manager (% Processor Utility / Performance); GPU load is the busiest engine type,
    /// summed over processes, which is also what Task Manager shows.
    /// </summary>
    public sealed class SystemMetrics : IDisposable
    {
        private readonly Pdh _pdh = new();
        private readonly IntPtr _cpuUtility, _cpuPerformance, _cpuFrequency, _gpuEngine, _gpuShared;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        public SystemMetrics()
        {
            _cpuUtility = _pdh.Add(@"\Processor Information(_Total)\% Processor Utility");
            _cpuPerformance = _pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
            _cpuFrequency = _pdh.Add(@"\Processor Information(_Total)\Processor Frequency");
            _gpuEngine = _pdh.Add(@"\GPU Engine(*)\Utilization Percentage");
            _gpuShared = _pdh.Add(@"\GPU Adapter Memory(*)\Shared Usage");
            _pdh.Collect(); // rate counters need a first sample
        }

        public SystemSnapshot Sample()
        {
            _pdh.Collect();

            double? cpu = _pdh.Value(_cpuUtility) is double u ? Math.Clamp(u, 0, 100) : null;
            double? mhz = _pdh.Value(_cpuPerformance) is double perf && _pdh.Value(_cpuFrequency) is double baseMhz
                ? baseMhz * perf / 100 : null;

            var (gpuPct, engine) = BusiestEngine(_pdh.Values(_gpuEngine));
            double? shared = _gpuShared == IntPtr.Zero ? null : _pdh.Values(_gpuShared).Sum(v => v.value) / 1e9;

            var mem = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref mem);

            return new SystemSnapshot(cpu, mhz, gpuPct, engine, shared,
                (mem.TotalPhys - mem.AvailPhys) / 1e9, mem.TotalPhys / 1e9);
        }

        /// <summary>
        /// Instance names look like "pid_1234_luid_0x00000000_0x0000D2C1_phys_0_eng_0_engtype_3D".
        /// Sum per engine type across processes, return the busiest type.
        /// </summary>
        public static (double? pct, string? engine) BusiestEngine(IEnumerable<(string name, double value)> instances)
        {
            var byType = new Dictionary<string, double>();
            foreach (var (name, value) in instances)
            {
                int i = name.IndexOf("engtype_", StringComparison.Ordinal);
                if (i < 0) continue;
                string type = name[(i + 8)..];
                byType[type] = byType.GetValueOrDefault(type) + value;
            }
            if (byType.Count == 0) return (null, null);
            var top = byType.MaxBy(kv => kv.Value);
            return (Math.Clamp(top.Value, 0, 100), top.Key);
        }

        public void Dispose() => _pdh.Dispose();
    }
}
