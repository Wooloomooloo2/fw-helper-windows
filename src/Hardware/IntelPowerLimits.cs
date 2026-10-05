using FwHelper.Helpers;
using System.Reflection;

namespace FwHelper.Hardware
{
    /// <summary>
    /// Intel package power limits (PL1 / PL2) through MSR_PKG_POWER_LIMIT, using the PawnIO IntelMSR module.
    /// Optional / experimental: needs PawnIO installed and admin rights, and Intel DTT firmware may still
    /// apply its own (lower) limits on top of these.
    /// </summary>
    public static class IntelPowerLimits
    {
        private const uint MSR_RAPL_POWER_UNIT = 0x606;
        private const uint MSR_PKG_POWER_LIMIT = 0x610;
        public const uint MSR_PKG_ENERGY_STATUS = 0x611;

        private static readonly PawnIOWrapper _io = new();
        private static double _powerUnit;   // watts per LSB
        private static double _energyUnit;  // joules per LSB

        public static string Status { get; private set; } = "Not initialized";
        public static bool IsAvailable { get; private set; }

        public static bool Init()
        {
            if (IsAvailable) return true;
            if (!ProcessHelper.IsUserAdministrator()) { Status = "Requires admin"; return false; }

            var connect = _io.Connect();
            if (connect != PawnIOWrapper.ConnectResult.OK)
            {
                Status = connect == PawnIOWrapper.ConnectResult.NotInstalled ? "PawnIO not installed" : "PawnIO: " + connect;
                return false;
            }

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("FwHelper.IntelMSR.bin")!;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                if (!_io.LoadModule(ms.ToArray())) { Status = "Can't load MSR module"; return false; }
            }
            catch (Exception ex)
            {
                Status = "MSR module error: " + ex.Message;
                return false;
            }

            if (!ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unit)) { Status = "Can't read RAPL units"; return false; }
            _powerUnit = 1.0 / (1UL << (int)(unit & 0xF));
            _energyUnit = 1.0 / (1UL << (int)((unit >> 8) & 0x1F));

            IsAvailable = true;
            Status = IsLocked() ? "Locked by BIOS" : "OK";
            Logger.WriteLine("Intel power limits: " + Status);
            return true;
        }

        public static bool IsLocked() => ReadMsr(MSR_PKG_POWER_LIMIT, out ulong v) && (v >> 63) == 1;

        public static (int pl1, int pl2)? Get()
        {
            if (!IsAvailable || !ReadMsr(MSR_PKG_POWER_LIMIT, out ulong v)) return null;
            int pl1 = (int)Math.Round((v & 0x7FFF) * _powerUnit);
            int pl2 = (int)Math.Round(((v >> 32) & 0x7FFF) * _powerUnit);
            return (pl1, pl2);
        }

        /// <summary>The register as firmware left it before our first write; put back by <see cref="Restore"/>.</summary>
        private static ulong? _original;

        public static bool Set(int pl1, int pl2)
        {
            if (!IsAvailable || !ReadMsr(MSR_PKG_POWER_LIMIT, out ulong v)) return false;
            if ((v >> 63) == 1) { Logger.WriteLine("PL MSR locked"); return false; }
            if (_original is null)
            {
                _original = v;
                Logger.WriteLine($"PL MSR original: 0x{v:X16} ({Decode(v)})");
            }

            ulong raw1 = (ulong)Math.Clamp(pl1 / _powerUnit, 1, 0x7FFF);
            ulong raw2 = (ulong)Math.Clamp(pl2 / _powerUnit, 1, 0x7FFF);

            v &= ~0x7FFFUL; v |= raw1 | (1UL << 15);                 // PL1 + enable
            v &= ~(0x7FFFUL << 32); v |= (raw2 << 32) | (1UL << 47); // PL2 + enable

            bool ok = _io.Execute("ioctl_write_msr", new ulong[] { MSR_PKG_POWER_LIMIT, v }, null);
            Logger.WriteLine($"Set PL1={pl1}W PL2={pl2}W: {(ok ? "OK" : "failed")}");
            return ok;
        }

        /// <summary>Write back the register as it was before our first <see cref="Set"/>. No-op if we never wrote it.</summary>
        public static bool Restore()
        {
            if (!IsAvailable || _original is not ulong original) return true;
            bool ok = _io.Execute("ioctl_write_msr", new ulong[] { MSR_PKG_POWER_LIMIT, original }, null);
            Logger.WriteLine($"PL MSR restored ({Decode(original)}): {(ok ? "OK" : "failed")}");
            if (ok) _original = null;
            return ok;
        }

        private static string Decode(ulong v) =>
            $"PL1 {(v & 0x7FFF) * _powerUnit:0}W{((v >> 15) & 1) switch { 1 => "", _ => " off" }}, " +
            $"PL2 {((v >> 32) & 0x7FFF) * _powerUnit:0}W{((v >> 47) & 1) switch { 1 => "", _ => " off" }}";

        public const uint MSR_PP0_ENERGY_STATUS = 0x639; // cores
        public const uint MSR_PP1_ENERGY_STATUS = 0x641; // uncore / integrated graphics

        private static readonly RaplEnergy _packageMeter = new();

        /// <summary>
        /// CPU package power for the Fans + Power window and --hwtest (needs two calls to produce a value).
        /// Other consumers keep their own <see cref="RaplEnergy"/> per counter and use <see cref="ReadEnergy"/>.
        /// </summary>
        public static float? GetPackagePower()
        {
            lock (_packageMeter)
                return ReadEnergy(MSR_PKG_ENERGY_STATUS) is uint raw
                    && _packageMeter.Next(raw, _energyUnit, Environment.TickCount64) is double w ? (float)w : null;
        }

        public static double EnergyUnit => _energyUnit;

        /// <summary>Raw 32-bit energy counter (package 0x611, PP0 0x639, PP1 0x641), or null without PawnIO.</summary>
        public static uint? ReadEnergy(uint msr) => IsAvailable && ReadMsr(msr, out ulong raw) ? (uint)raw : null;

        /// <summary>Raw MSR read for diagnostics (perf limit reasons); null without PawnIO.</summary>
        public static ulong? Read(uint msr) => IsAvailable && ReadMsr(msr, out ulong v) ? v : null;

        private static bool ReadMsr(uint msr, out ulong value)
        {
            value = 0;
            var output = new ulong[1];
            if (!_io.Execute("ioctl_read_msr", new ulong[] { msr }, output)) return false;
            value = output[0];
            return true;
        }
    }
}
