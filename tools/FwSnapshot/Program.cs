// FwSnapshot: read-only dump of everything firmware-side that could govern the CPU/GPU power split, so two BIOS/EC versions
// can be diffed. Writes to %AppData%\FwHelper\fw-snapshots\<time>\. Needs no admin. Never sends an EC write command:
// only GET_* host commands (opcodes from Chromium EC include/ec_commands.h) and the read-only memory map.
using FwHelper.Hardware;
using System.Runtime.InteropServices;
using System.Text;

string dir = args.Length > 0 ? args[0] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "FwHelper", "fw-snapshots", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(dir);
Console.WriteLine("Snapshot → " + dir);

DumpFirmwareTables(dir);
DumpEc(dir);
DumpWindows(dir);
return 0;

static void DumpWindows(string dir)
{
    string script = Path.Combine(AppContext.BaseDirectory, "windows.ps1");
    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
        $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Dir \"{dir}\"") { UseShellExecute = false });
    p?.WaitForExit();
    Console.WriteLine("Windows: power plan, drivers, services, Intel registry written");
}

static void DumpFirmwareTables(string dir)
{
    var acpiDir = Directory.CreateDirectory(Path.Combine(dir, "acpi")).FullName;
    var list = new StringBuilder();
    const uint ACPI = 0x41435049, RSMB = 0x52534D42; // 'ACPI', 'RSMB' (big-endian provider signatures)
    uint size = Native.EnumSystemFirmwareTables(ACPI, null, 0);
    var ids = new byte[size];
    Native.EnumSystemFirmwareTables(ACPI, ids, size);
    var seen = new Dictionary<string, int>();
    var hashes = new HashSet<string>();
    for (int i = 0; i + 4 <= ids.Length; i += 4)
    {
        uint id = BitConverter.ToUInt32(ids, i);
        string sig = Encoding.ASCII.GetString(ids, i, 4);
        var data = Get(ACPI, id);
        if (data is null) { list.AppendLine($"{sig}: read failed"); continue; }
        // Duplicate signatures (several SSDTs) may all return the first table: note it rather than save copies
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))[..16];
        int n = seen[sig] = seen.GetValueOrDefault(sig) + 1;
        string oem = data.Length >= 36 ? Encoding.ASCII.GetString(data, 16, 8).TrimEnd('\0', ' ') : "";
        uint oemRev = data.Length >= 36 ? BitConverter.ToUInt32(data, 24) : 0;
        string name = n == 1 ? sig : $"{sig}{n}";
        if (hashes.Add(hash)) File.WriteAllBytes(Path.Combine(acpiDir, name + ".bin"), data);
        list.AppendLine($"{name,-6} len={data.Length,7} oemTable='{oem}' oemRev=0x{oemRev:X8} sha={hash}{(hashes.Count == 0 ? "" : "")}");
    }
    // The API above returns only the first of several tables with one signature (all 29 SSDTs) and omits the DSDT.
    // Windows keeps a copy of each under HKLM\HARDWARE\ACPI\<sig>\<OEM id>\<table id>\<revision>: save those too.
    list.AppendLine("\n# registry copies (HKLM\\HARDWARE\\ACPI)");
    using (var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI"))
        if (root is not null) Walk(root, "");
    File.WriteAllText(Path.Combine(dir, "acpi.txt"), list.ToString());
    Console.Write(list);

    void Walk(Microsoft.Win32.RegistryKey key, string path)
    {
        foreach (var valueName in key.GetValueNames())
            if (key.GetValue(valueName) is byte[] data)
            {
                string file = (path.TrimStart('\\').Replace('\\', '_') + (valueName.Length > 0 ? "_" + valueName : "")).Replace(' ', '-');
                foreach (char c in Path.GetInvalidFileNameChars()) file = file.Replace(c, '-');
                File.WriteAllBytes(Path.Combine(acpiDir, "reg_" + file + ".bin"), data);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))[..16];
                list.AppendLine($"{path.TrimStart('\\'),-40} len={data.Length,7} sha={hash}");
            }
        foreach (var sub in key.GetSubKeyNames())
            using (var k = key.OpenSubKey(sub)) if (k is not null) Walk(k, path + "\\" + sub);
    }
    var smbios = Get(RSMB, 0);
    if (smbios is not null) File.WriteAllBytes(Path.Combine(dir, "smbios.bin"), smbios);

    static byte[]? Get(uint provider, uint id)
    {
        uint len = Native.GetSystemFirmwareTable(provider, id, null, 0);
        if (len == 0) return null;
        var buf = new byte[len];
        return Native.GetSystemFirmwareTable(provider, id, buf, len) == len ? buf : null;
    }
}

static void DumpEc(string dir)
{
    var sb = new StringBuilder();
    if (!FrameworkEc.Connect()) { sb.AppendLine("EC: not connected"); File.WriteAllText(Path.Combine(dir, "ec.txt"), sb.ToString()); return; }
    var ec = FrameworkEc.Ec;
    string Str(byte[] b, int off, int len) => Encoding.ASCII.GetString(b, off, len).Split('\0')[0];

    if (ec.Command(0x0002, 0, ReadOnlySpan<byte>.Empty, 100, out var v) == EcStatus.Success) // GET_VERSION
        sb.AppendLine($"version RO='{Str(v, 0, 32)}' RW='{Str(v, 32, 32)}' image={BitConverter.ToUInt32(v, 96)}");
    if (ec.Command(0x0004, 0, ReadOnlySpan<byte>.Empty, 0xF0 - 20, out var bi) == EcStatus.Success) // GET_BUILD_INFO
        sb.AppendLine($"build '{Str(bi, 0, bi.Length)}'");
    if (ec.Command(0x0003, 0, ReadOnlySpan<byte>.Empty, 96, out var ci) == EcStatus.Success) // GET_CHIP_INFO
        sb.AppendLine($"chip vendor='{Str(ci, 0, 32)}' name='{Str(ci, 32, 32)}' rev='{Str(ci, 64, 32)}'");
    if (ec.Command(0x000D, 0, ReadOnlySpan<byte>.Empty, 8, out var f) == EcStatus.Success) // GET_FEATURES
        sb.AppendLine($"features 0x{BitConverter.ToUInt32(f, 4):X8}{BitConverter.ToUInt32(f, 0):X8}");
    if (ec.Command(0x000B, 0, ReadOnlySpan<byte>.Empty, 12, out var pi) == EcStatus.Success) // GET_PROTOCOL_INFO
        sb.AppendLine($"protocol versions=0x{BitConverter.ToUInt32(pi, 0):X} maxReq={BitConverter.ToUInt16(pi, 4)} maxResp={BitConverter.ToUInt16(pi, 6)} flags=0x{BitConverter.ToUInt32(pi, 8):X}");

    // GET_CMD_VERSIONS v1 (u16 cmd → u32 version mask): which host commands exist, without running any of them
    sb.AppendLine("\n# host commands (cmd: version mask)");
    int found = 0;
    foreach (var range in new[] { (0x0000, 0x03FF), (0x3E00, 0x3FFF) })
        for (int cmd = range.Item1; cmd <= range.Item2; cmd++)
            if (ec.Command(0x0008, 1, BitConverter.GetBytes((ushort)cmd), 4, out var m) == EcStatus.Success && BitConverter.ToUInt32(m) != 0)
            {
                sb.AppendLine($"0x{cmd:X4}: 0x{BitConverter.ToUInt32(m):X}");
                found++;
            }
    sb.AppendLine($"({found} commands)");

    sb.AppendLine("\n# temperature sensors + thermal config (raw ec_thermal_config, Kelvin)");
    for (int s = 0; s < 16; s++)
    {
        if (ec.Command(0x0070, 0, new[] { (byte)s }, 33, out var info) != EcStatus.Success) break; // TEMP_SENSOR_GET_INFO
        var raw = FrameworkEc.GetThermalConfigRaw(s);
        sb.AppendLine($"{s,2} {Str(info, 0, 32),-16} type={info[32]} cfg={(raw is null ? "n/a" : Convert.ToHexString(raw))}");
    }

    sb.AppendLine("\n# memory map (0x00-0xFF)");
    var mem = ec.ReadMemory(0, CrosEc.MemmapSize);
    if (mem is not null)
        for (int i = 0; i < mem.Length; i += 16) sb.AppendLine($"{i:X2}: {Convert.ToHexString(mem, i, 16)}");

    sb.AppendLine($"\ncharge limit {FrameworkEc.GetChargeLimit()} | throttle {FrameworkEc.GetApThrottleStatus()} | fan auto {FrameworkEc.IsFanAuto()}");
    File.WriteAllText(Path.Combine(dir, "ec.txt"), sb.ToString());
    Console.WriteLine($"EC: {found} host commands, written to ec.txt");
}

static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint EnumSystemFirmwareTables(uint provider, byte[]? buffer, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);
}
