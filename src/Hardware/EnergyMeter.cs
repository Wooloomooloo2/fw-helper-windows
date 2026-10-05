namespace FwHelper.Hardware
{
    public record PowerReading(double? PackageW, double? CoresW, double? GpuW, double? DramW);

    /// <summary>
    /// RAPL power through Windows' Energy Metering Interface (ADR 0016): "\Energy Meter(RAPL_Package0_*)\Power" in mW.
    /// No driver, no admin. Each instance has its own PDH query, so separate consumers don't interfere.
    /// </summary>
    public sealed class EnergyMeter : IDisposable
    {
        private readonly Pdh _pdh = new();
        private readonly IntPtr _power;

        public EnergyMeter()
        {
            _power = _pdh.Add(@"\Energy Meter(*)\Power");
            _pdh.Collect();
        }

        public bool Available => _power != IntPtr.Zero;

        public PowerReading Read()
        {
            if (!Available || !_pdh.Collect()) return new PowerReading(null, null, null, null);
            return Parse(_pdh.Values(_power));
        }

        /// <summary>Instance names → domains; values mW → W.</summary>
        public static PowerReading Parse(IEnumerable<(string name, double value)> instances)
        {
            double? pkg = null, cores = null, gpu = null, dram = null;
            foreach (var (name, mw) in instances)
            {
                double w = mw / 1000.0;
                if (!double.IsFinite(w) || w < 0 || w > 500) continue;
                string n = name.ToUpperInvariant();
                if (n.EndsWith("_PKG")) pkg = w;
                else if (n.EndsWith("_PP0")) cores = w;
                else if (n.EndsWith("_PP1")) gpu = w;
                else if (n.EndsWith("_DRAM")) dram = w;
            }
            return new PowerReading(pkg, cores, gpu, dram);
        }

        public void Dispose() => _pdh.Dispose();
    }
}
