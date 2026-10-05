using FwHelper.Helpers;
using System.Text;

namespace FwHelper.Hardware
{
    public record TempSensor(int Index, string Name, int? Celsius);

    public record BatteryInfo(
        bool Present, bool AcPresent, bool Charging, bool Discharging,
        int VoltageMv, int RateMa, int RemainingMah, int FullMah, int DesignMah, int DesignVoltageMv, int Cycles,
        string Manufacturer, string Model, string Chemistry)
    {
        public int Percent => FullMah > 0 ? Math.Clamp((int)Math.Round(100.0 * RemainingMah / FullMah), 0, 100) : 0;
        public int HealthPercent => DesignMah > 0 ? (int)Math.Round(100.0 * FullMah / DesignMah) : 0;
        /// <summary>Positive while charging, negative while discharging (watts).</summary>
        public double Watts => (Discharging ? -1 : 1) * VoltageMv * (double)RateMa / 1_000_000;
    }

    /// <summary>
    /// Framework Laptop EC features on top of <see cref="CrosEc"/>.
    /// Command ids come from the ChromeOS EC host command set plus Framework's 0x3Exx extensions.
    /// </summary>
    public static class FrameworkEc
    {
        // Standard ChromeOS EC host commands
        private const ushort EC_CMD_HELLO = 0x0001;
        private const ushort EC_CMD_GET_VERSION = 0x0002;
        private const ushort EC_CMD_PWM_GET_FAN_TARGET_RPM = 0x0020;
        private const ushort EC_CMD_PWM_GET_KEYBOARD_BACKLIGHT = 0x0022;
        private const ushort EC_CMD_PWM_SET_KEYBOARD_BACKLIGHT = 0x0023;
        private const ushort EC_CMD_PWM_SET_FAN_DUTY = 0x0024;
        private const ushort EC_CMD_THERMAL_AUTO_FAN_CTRL = 0x0052;
        private const ushort EC_CMD_TEMP_SENSOR_GET_INFO = 0x0070;

        // Framework specific
        private const ushort EC_CMD_CHARGE_LIMIT_CONTROL = 0x3E03;
        private const ushort EC_CMD_FP_LED_LEVEL_CONTROL = 0x3E0E;

        private const byte CHG_LIMIT_SET = 0x02;
        private const byte CHG_LIMIT_GET = 0x08;

        // Memory map layout
        private const int EC_MEMMAP_TEMP_SENSOR = 0x00;
        private const int EC_MEMMAP_FAN = 0x10;
        private const int EC_MEMMAP_TEMP_SENSOR_B = 0x18;
        private const int EC_MEMMAP_THERMAL_VERSION = 0x23;
        private const int EC_MEMMAP_BATT_VOLT = 0x40;
        private const int EC_TEMP_SENSOR_OFFSET = 200;

        public static readonly CrosEc Ec = new();

        private static Dictionary<int, string>? _sensorNames;

        public static bool Connect()
        {
            if (!Ec.Connect()) return false;
            var status = Ec.Command(EC_CMD_HELLO, 0, BitConverter.GetBytes(0xA0B0C0D0u), 4, out var resp);
            bool ok = status == EcStatus.Success && BitConverter.ToUInt32(resp) == 0xA0B0C0D0u + 0x01020304u;
            Logger.WriteLine($"EC hello: {status} {(ok ? "OK" : "bad response")}");
            return ok;
        }

        public static string? GetVersion()
        {
            if (Ec.Command(EC_CMD_GET_VERSION, 0, ReadOnlySpan<byte>.Empty, 100, out var resp) != EcStatus.Success) return null;
            uint image = BitConverter.ToUInt32(resp, 96);
            int offset = image == 1 ? 0 : 32; // 1 = RO, otherwise RW
            return Encoding.ASCII.GetString(resp, offset, 32).TrimEnd('\0');
        }

        // ---------- Sensors ----------

        private static Dictionary<int, string> SensorNames()
        {
            if (_sensorNames is not null) return _sensorNames;
            var names = new Dictionary<int, string>();
            for (int i = 0; i < 16; i++)
            {
                if (Ec.Command(EC_CMD_TEMP_SENSOR_GET_INFO, 0, new[] { (byte)i }, 33, out var resp) != EcStatus.Success) break;
                names[i] = Encoding.ASCII.GetString(resp, 0, 32).TrimEnd('\0');
            }
            if (names.Count > 0) _sensorNames = names;
            return names;
        }

        public static List<TempSensor> GetTemperatures()
        {
            var result = new List<TempSensor>();
            var mem = Ec.ReadMemory(0, 0x24);
            if (mem is null) return result;

            var names = SensorNames();
            int count = mem[EC_MEMMAP_THERMAL_VERSION] >= 2 ? 16 : 8;
            for (int i = 0; i < count; i++)
            {
                byte raw = i < 8 ? mem[EC_MEMMAP_TEMP_SENSOR + i] : mem[EC_MEMMAP_TEMP_SENSOR_B + i - 8];
                if (raw == 0xFF) continue; // not present
                int? celsius = raw >= 0xFC ? null : raw + EC_TEMP_SENSOR_OFFSET - 273;
                result.Add(new TempSensor(i, names.GetValueOrDefault(i, $"sensor{i}"), celsius));
            }
            return result;
        }

        /// <summary>Best guess of the CPU temperature: PECI (package), then the CPU-area sensor, then the hottest sensor.</summary>
        public static int? GetCpuTemp(List<TempSensor>? temps = null)
        {
            temps ??= GetTemperatures();
            var valid = temps.Where(t => t.Celsius.HasValue).ToList();
            return valid.FirstOrDefault(t => t.Name.Contains("peci", StringComparison.OrdinalIgnoreCase))?.Celsius
                ?? valid.FirstOrDefault(t => t.Name.StartsWith("cpu", StringComparison.OrdinalIgnoreCase))?.Celsius
                ?? valid.Where(t => !t.Name.Contains("batt", StringComparison.OrdinalIgnoreCase)).Max(t => t.Celsius);
        }

        /// <summary>
        /// The fan control input: PECI (CPU package) only, never a fallback. A board sensor reads ~45 °C while the CPU can be at 90,
        /// so without PECI the fan must go back to the EC rather than follow the wrong sensor (ADR 0013).
        /// </summary>
        public static int? GetControlTemp(List<TempSensor> temps) =>
            temps.FirstOrDefault(t => t.Name.Contains("peci", StringComparison.OrdinalIgnoreCase))?.Celsius;

        public static List<int> GetFanRpms()
        {
            var result = new List<int>();
            var mem = Ec.ReadMemory(EC_MEMMAP_FAN, 8);
            if (mem is null) return result;
            for (int i = 0; i < 4; i++)
            {
                ushort rpm = BitConverter.ToUInt16(mem, i * 2);
                if (rpm == 0xFFFF) continue;     // not present
                result.Add(rpm == 0xFFFE ? 0 : rpm); // 0xFFFE = stalled
            }
            return result;
        }

        public static int? GetFanTargetRpm()
        {
            if (Ec.Command(EC_CMD_PWM_GET_FAN_TARGET_RPM, 0, ReadOnlySpan<byte>.Empty, 4, out var resp) != EcStatus.Success) return null;
            return (int)BitConverter.ToUInt32(resp);
        }

        // ---------- Fan control ----------

        /// <summary>Fixed duty for all fans. Disables EC automatic fan control until <see cref="SetFanAuto"/> is called.</summary>
        public static bool SetFanDuty(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            return Ec.Command(EC_CMD_PWM_SET_FAN_DUTY, 0, BitConverter.GetBytes((uint)percent)) == EcStatus.Success;
        }

        // Read-only fan state (layouts from Linux drivers/hwmon/cros_ec_hwmon.c + cros_ec_commands.h)
        private const ushort EC_CMD_PWM_GET_FAN_DUTY = 0x0027;
        private const ushort EC_CMD_THERMAL_GET_THRESHOLD = 0x0051;
        private const byte EC_AUTO_FAN_CONTROL_CMD_GET = 1;

        /// <summary>Duty the fan is running at right now (%), whoever set it. Works in EC auto mode too: that's the EC's own choice.</summary>
        public static int? GetFanDuty(int fan = 0)
        {
            if (Ec.Command(EC_CMD_PWM_GET_FAN_DUTY, 0, new[] { (byte)fan }, 4, out var resp) != EcStatus.Success) return null;
            return (int)BitConverter.ToUInt32(resp);
        }

        /// <summary>Hardware truth for "who owns the fan": true = EC thermal control, false = fixed duty from the host.</summary>
        public static bool? IsFanAuto(int fan = 0)
        {
            // v2: { u8 fan_idx; u8 cmd; u8 set_auto; } padded to 4
            if (Ec.Command(EC_CMD_THERMAL_AUTO_FAN_CTRL, 2, new byte[] { (byte)fan, EC_AUTO_FAN_CONTROL_CMD_GET, 0, 0 }, 1, out var resp) != EcStatus.Success) return null;
            return resp[0] != 0;
        }

        /// <summary>EC thermal config for one sensor, °C (null = not set): host warn/high/halt thresholds and the EC fan ramp.</summary>
        public record ThermalConfig(int? Warn, int? High, int? Halt, int? FanOff, int? FanMax);

        public static ThermalConfig? GetThermalConfig(int sensor)
        {
            // v1: u32 sensor_num → struct ec_thermal_config { u32 temp_host[3]; u32 temp_host_release[3]; u32 temp_fan_off; u32 temp_fan_max; } in K
            if (Ec.Command(EC_CMD_THERMAL_GET_THRESHOLD, 1, BitConverter.GetBytes((uint)sensor), 32, out var c) != EcStatus.Success) return null;
            int? C(int offset) => BitConverter.ToUInt32(c, offset) is uint k and > 0 ? (int)k - 273 : null;
            return new ThermalConfig(C(0), C(4), C(8), C(24), C(28));
        }

        /// <summary>Hand all fans back to the EC thermal control loop.</summary>
        public static bool SetFanAuto()
        {
            var status = Ec.Command(EC_CMD_THERMAL_AUTO_FAN_CTRL, 0, ReadOnlySpan<byte>.Empty);
            if (status != EcStatus.Success) Logger.WriteLine("EC auto fan failed: " + status);
            return status == EcStatus.Success;
        }

        // ---------- Battery ----------

        public static (int max, int min)? GetChargeLimit()
        {
            var status = Ec.Command(EC_CMD_CHARGE_LIMIT_CONTROL, 0, new byte[] { CHG_LIMIT_GET, 0, 0 }, 2, out var resp);
            if (status != EcStatus.Success) return null;
            return (resp[0], resp[1]);
        }

        public static bool SetChargeLimit(int max)
        {
            max = Math.Clamp(max, 50, 100);
            var status = Ec.Command(EC_CMD_CHARGE_LIMIT_CONTROL, 0, new byte[] { CHG_LIMIT_SET, (byte)max, 0 });
            Logger.WriteLine($"Charge limit {max}%: {status}");
            return status == EcStatus.Success;
        }

        public static BatteryInfo? GetBattery()
        {
            var m = Ec.ReadMemory(EC_MEMMAP_BATT_VOLT, 0x40);
            if (m is null) return null;
            int U32(int o) => (int)BitConverter.ToUInt32(m, o - EC_MEMMAP_BATT_VOLT);
            string Str(int o) => Encoding.ASCII.GetString(m, o - EC_MEMMAP_BATT_VOLT, 8).TrimEnd('\0', ' ');

            byte flags = m[0x4C - EC_MEMMAP_BATT_VOLT];
            return new BatteryInfo(
                Present: (flags & 0x02) != 0,
                AcPresent: (flags & 0x01) != 0,
                Charging: (flags & 0x08) != 0,
                Discharging: (flags & 0x04) != 0,
                VoltageMv: U32(0x40), RateMa: U32(0x44), RemainingMah: U32(0x48),
                FullMah: U32(0x58), DesignMah: U32(0x50), DesignVoltageMv: U32(0x54), Cycles: U32(0x5C),
                Manufacturer: Str(0x60), Model: Str(0x68), Chemistry: Str(0x78));
        }

        // ---------- Lighting ----------

        public static int? GetKeyboardBacklight()
        {
            if (Ec.Command(EC_CMD_PWM_GET_KEYBOARD_BACKLIGHT, 0, ReadOnlySpan<byte>.Empty, 2, out var resp) != EcStatus.Success) return null;
            return resp[0];
        }

        public static bool SetKeyboardBacklight(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            return Ec.Command(EC_CMD_PWM_SET_KEYBOARD_BACKLIGHT, 0, new[] { (byte)percent }) == EcStatus.Success;
        }

        public enum PowerLedLevel : byte { High = 0, Medium = 1, Low = 2, UltraLow = 3 }

        /// <summary>Power button / fingerprint LED brightness in percent (v1 response: percentage, level).</summary>
        public static int? GetPowerLedPercent()
        {
            if (Ec.Command(EC_CMD_FP_LED_LEVEL_CONTROL, 1, new byte[] { 0xFF, 1 }, 2, out var resp) != EcStatus.Success) return null;
            return resp[0];
        }

        public static bool SetPowerLedLevel(PowerLedLevel level)
        {
            var status = Ec.Command(EC_CMD_FP_LED_LEVEL_CONTROL, 0, new byte[] { (byte)level, 0 });
            Logger.WriteLine($"Power LED {level}: {status}");
            return status == EcStatus.Success;
        }
    }
}
