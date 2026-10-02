using FwHelper.Helpers;
using System.Runtime.InteropServices;

namespace FwHelper.Features
{
    /// <summary>
    /// Built-in panel refresh rate switching (trimmed from G-Helper's ScreenNative / DisplayNative).
    /// Modes: fixed 60Hz, fixed max, or Auto (max on AC, 60Hz on battery).
    /// </summary>
    public static class ScreenControl
    {
        public const int ModeLow = 0, ModeMax = 1, ModeAuto = 2;

        public static int GetScreenMode() => AppConfig.Get("screen_mode", -1);

        public static void SetScreenMode(int mode)
        {
            AppConfig.Set("screen_mode", mode);
            AutoScreen(force: true);
        }

        public static void AutoScreen(bool force = false)
        {
            int mode = GetScreenMode();
            if (mode < 0 && !force) return; // user never touched it: leave Windows settings alone

            string? screen = FindLaptopScreen();
            if (screen is null) return;

            int max = GetMaxRefreshRate(screen);
            int low = GetLowRefreshRate(screen);
            if (max <= 0) return;

            int target = mode switch
            {
                ModeLow => low,
                ModeMax => max,
                _ => PowerNative.IsOnAC() ? max : low,
            };
            if (GetRefreshRate(screen) != target) SetRefreshRate(screen, target);
        }

        // ---------------- Native ----------------

        public static string? FindLaptopScreen()
        {
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return null;
                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return null;

                foreach (var path in paths)
                {
                    var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = 2; // GET_TARGET_NAME
                    target.header.size = (uint)Marshal.SizeOf(target);
                    target.header.adapterId = path.targetInfo.adapterId;
                    target.header.id = path.targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;

                    bool isInternal = target.outputTechnology == OUTPUT_INTERNAL || target.outputTechnology == OUTPUT_DISPLAYPORT_EMBEDDED;
                    if (!isInternal) continue;

                    var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    source.header.type = 1; // GET_SOURCE_NAME
                    source.header.size = (uint)Marshal.SizeOf(source);
                    source.header.adapterId = path.sourceInfo.adapterId;
                    source.header.id = path.sourceInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref source) == 0) return source.viewGdiDeviceName;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("FindLaptopScreen: " + ex.Message);
            }
            return null;
        }

        private static IEnumerable<DEVMODE> EnumModes(string screen)
        {
            var current = CreateDevmode();
            if (EnumDisplaySettingsEx(screen, ENUM_CURRENT_SETTINGS, ref current, 0) == 0) yield break;
            var dm = CreateDevmode();
            for (int i = 0; EnumDisplaySettingsEx(screen, i, ref dm, 0) != 0; i++)
            {
                // only rates available at the current resolution
                if (dm.dmPelsWidth == current.dmPelsWidth && dm.dmPelsHeight == current.dmPelsHeight)
                    yield return dm;
            }
        }

        public static int GetMaxRefreshRate(string? screen) =>
            screen is null ? -1 : EnumModes(screen).Select(m => m.dmDisplayFrequency).DefaultIfEmpty(-1).Max();

        public static int GetLowRefreshRate(string? screen)
        {
            if (screen is null) return -1;
            var rates = EnumModes(screen).Select(m => m.dmDisplayFrequency).Where(f => f > 1).Distinct().ToList();
            if (rates.Count == 0) return -1;
            // Prefer 60Hz; otherwise the lowest the panel offers
            return rates.Contains(60) ? 60 : rates.Min();
        }

        public static int GetRefreshRate(string? screen)
        {
            if (screen is null) return -1;
            var dm = CreateDevmode();
            return EnumDisplaySettingsEx(screen, ENUM_CURRENT_SETTINGS, ref dm, 0) != 0 ? dm.dmDisplayFrequency : -1;
        }

        public static void SetRefreshRate(string screen, int frequency)
        {
            var dm = CreateDevmode();
            if (EnumDisplaySettingsEx(screen, ENUM_CURRENT_SETTINGS, ref dm, 0) == 0) return;
            dm.dmDisplayFrequency = frequency;
            dm.dmFields = DM_DISPLAYFREQUENCY;
            int result = ChangeDisplaySettingsEx(screen, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            Logger.WriteLine($"Screen {screen} = {frequency}Hz: {(result == 0 ? "OK" : result.ToString())}");
        }

        private static DEVMODE CreateDevmode()
        {
            var dm = new DEVMODE { dmDeviceName = new string('\0', 32), dmFormName = new string('\0', 32) };
            dm.dmSize = (short)Marshal.SizeOf(dm);
            return dm;
        }

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 1;
        private const int DM_DISPLAYFREQUENCY = 0x400000;
        private const uint QDC_ONLY_ACTIVE_PATHS = 2;
        private const uint OUTPUT_INTERNAL = 0x80000000;
        private const uint OUTPUT_DISPLAYPORT_EMBEDDED = 11;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL { public uint Numerator, Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id, modeInfoIdx, outputTechnology, rotation, scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        // 64 bytes: infoType, id, adapterId + 48 byte union
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct DISPLAYCONFIG_MODE_INFO { public uint infoType, id; public LUID adapterId; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type, size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags, outputTechnology;
            public ushort edidManufactureId, edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels, dmBitsPerPel;
            public int dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
            ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topology);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME info);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME info);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int EnumDisplaySettingsEx(string deviceName, int modeNum, ref DEVMODE devMode, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr lParam);
    }
}
