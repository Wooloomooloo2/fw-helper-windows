using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// Hard temperature backstop (ADR 0016): the EC asserts PROCHOT# when PECI reaches HIGH, and lets go at the release value.
    /// Read-modify-write of the peci-temp sensor's EC thermal config: only HIGH and its release change, the fan ramp is kept.
    /// The firmware's own config is saved to app config (tagged with the EC version) before the first change, so it can be put
    /// back even after a crash. The EC keeps thresholds in RAM only: an EC reset returns to firmware defaults by itself.
    /// </summary>
    public static class EcBackstop
    {
        public const int AboveCapC = 5;
        private const int HighOffset = 4, HighReleaseOffset = 16;   // temp_host[HIGH], temp_host_release[HIGH]
        private const string SavedKey = "ec_backstop_orig", SavedFirmwareKey = "ec_backstop_firmware";

        public static int? ActiveAtC { get; private set; }

        // ---- Pure helpers ----

        /// <summary>New config with HIGH = <paramref name="highC"/> and its release = <paramref name="releaseC"/>; everything else kept.</summary>
        public static byte[] WithHigh(byte[] config, int highC, int releaseC)
        {
            var c = (byte[])config.Clone();
            BitConverter.TryWriteBytes(c.AsSpan(HighOffset), (uint)(highC + 273));
            BitConverter.TryWriteBytes(c.AsSpan(HighReleaseOffset), (uint)(releaseC + 273));
            return c;
        }

        public static int? HighC(byte[] config) => BitConverter.ToUInt32(config, HighOffset) is uint k and > 0 ? (int)k - 273 : null;

        /// <summary>HIGH to program for a cap, or null if it wouldn't be below the firmware's own (we only ever make it stricter).</summary>
        public static int? HighFor(int capC, byte[] firmware) =>
            capC + AboveCapC is int high && (HighC(firmware) is not int fw || high < fw) ? high : null;

        // ---- EC ----

        private static int? Sensor() =>
            FrameworkEc.GetTemperatures().FirstOrDefault(t => t.Name.Contains("peci", StringComparison.OrdinalIgnoreCase))?.Index;

        private static byte[]? Firmware(byte[] current)
        {
            string ec = FrameworkEc.GetVersion() ?? "";
            if (AppConfig.GetString(SavedFirmwareKey) == ec && AppConfig.GetString(SavedKey) is string hex)
            {
                try { return Convert.FromHexString(hex); } catch (FormatException) { }
            }
            // Nothing saved for this EC firmware: what's there now is the firmware's own config
            AppConfig.Set(SavedKey, Convert.ToHexString(current));
            AppConfig.Set(SavedFirmwareKey, ec);
            return current;
        }

        /// <summary>EC throttles at cap + 5 °C and releases at the cap.</summary>
        public static void Apply(int capC)
        {
            if (Sensor() is not int sensor || FrameworkEc.GetThermalConfigRaw(sensor) is not byte[] current) return;
            if (Firmware(current) is not byte[] firmware) return;

            if (HighFor(capC, firmware) is not int high) { Restore(); return; }
            var wanted = WithHigh(firmware, high, capC);
            if (current.AsSpan().SequenceEqual(wanted)) { ActiveAtC = high; return; }
            if (FrameworkEc.SetThermalConfigRaw(sensor, wanted))
            {
                ActiveAtC = high;
                Logger.WriteLine($"EC backstop: PROCHOT at {high}°C, release at {capC}°C");
            }
        }

        /// <summary>Put the firmware's config back if ours is in place. Safe to call any time (also at startup after a crash).</summary>
        public static void Restore()
        {
            if (AppConfig.GetString(SavedKey) is not string hex || AppConfig.GetString(SavedFirmwareKey) != FrameworkEc.GetVersion()) return;
            if (Sensor() is not int sensor || FrameworkEc.GetThermalConfigRaw(sensor) is not byte[] current) return;
            byte[] firmware;
            try { firmware = Convert.FromHexString(hex); } catch (FormatException) { return; }

            if (!current.AsSpan().SequenceEqual(firmware) && FrameworkEc.SetThermalConfigRaw(sensor, firmware))
                Logger.WriteLine($"EC backstop removed (HIGH back to {HighC(firmware)}°C)");
            ActiveAtC = null;
        }
    }
}
