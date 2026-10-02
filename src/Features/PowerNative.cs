using FwHelper.Helpers;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace FwHelper.Features
{
    /// <summary>
    /// Windows power mode slider (power overlay). On the Framework Laptop 13 Intel platforms the
    /// Intel Innovation Platform Framework / DTT driver maps these modes to firmware power and thermal limits.
    /// </summary>
    public static class PowerNative
    {
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetEffectiveOverlayScheme(out Guid effectiveOverlayGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

        public static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid Balanced = Guid.Empty;
        public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

        public static readonly Guid[] Overlays = { BestEfficiency, Balanced, BestPerformance };
        public static readonly string[] OverlayNames = { "Best Power Efficiency", "Balanced", "Best Performance" };

        public static int GetOverlayIndex()
        {
            if (PowerGetEffectiveOverlayScheme(out Guid g) != 0) return -1;
            return Array.IndexOf(Overlays, g);
        }

        public static void SetOverlay(int index)
        {
            if (index < 0 || index >= Overlays.Length) return;
            if (IsBatterySaverOn())
            {
                Logger.WriteLine("Battery saver on, skipping power mode");
                return;
            }
            PowerGetEffectiveOverlayScheme(out Guid current);
            if (current == Overlays[index]) return;
            uint status = PowerSetActiveOverlayScheme(Overlays[index]);
            Logger.WriteLine($"Power mode -> {OverlayNames[index]}: {(status == 0 ? "OK" : status.ToString())}");
        }

        public static bool IsBatterySaverOn()
        {
            try
            {
                var status = Registry.GetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Power", "EnergySaverState", null);
                return status is int i && i == 1;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsOnAC() => SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
    }
}
