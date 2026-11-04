using FwHelper.Helpers;
using System.Runtime.InteropServices;

namespace FwHelper.Hardware
{
    /// <summary>One GPU frequency reading (MHz): what the driver asks for, what the GPU runs at, and why it is held back.</summary>
    public readonly record struct GpuClockReading(double? RequestMhz, double? ActualMhz, uint ThrottleReasons)
    {
        /// <summary>Short text for ctl_freq_throttle_reason_flags_t (igcl_api.h), e.g. "PL1+thermal"; "" when not throttled.</summary>
        public string ThrottleText => IntelGpuClock.Describe(ThrottleReasons);
    }

    /// <summary>
    /// GPU core frequency from the Intel Graphics Control Library (ControlLib.dll, installed with the Intel graphics driver). No admin,
    /// no extra driver. Layouts and calls from intel/drivers.gpu.control-library include/igcl_api.h: ctlInit, ctlEnumerateDevices,
    /// ctlEnumFrequencyDomains, ctlFrequencyGetProperties, ctlFrequencyGetState (ctl_freq_state_t: request/actual in MHz, -1 = unknown).
    /// </summary>
    public sealed class IntelGpuClock : IDisposable
    {
        private const string Dll = "ControlLib.dll";
        private const uint CTL_INIT_FLAG_USE_LEVEL_ZERO = 1;
        private const int CTL_FREQ_DOMAIN_GPU = 0;
        private const uint CtlVersion = (1u << 16) | 1; // CTL_MAKE_VERSION(CTL_IMPL_MAJOR_VERSION 1, CTL_IMPL_MINOR_VERSION 1)

        [StructLayout(LayoutKind.Sequential)]
        private struct InitArgs // ctl_init_args_t
        {
            public uint Size;
            public byte Version;
            public uint AppVersion;
            public uint Flags;
            public uint SupportedVersion;
            public Guid ApplicationUID;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FreqProperties // ctl_freq_properties_t
        {
            public uint Size;
            public byte Version;
            public int Type;           // ctl_freq_domain_t
            public byte CanControl;    // bool
            public double Min, Max;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FreqState // ctl_freq_state_t
        {
            public uint Size;
            public byte Version;
            public double CurrentVoltage, Request, Tdp, Efficient, Actual;
            public uint ThrottleReasons;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int ctlInit(ref InitArgs args, out IntPtr api);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int ctlClose(IntPtr api);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int ctlEnumerateDevices(IntPtr api, ref uint count, [Out] IntPtr[]? devices);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int ctlEnumFrequencyDomains(IntPtr device, ref uint count, [Out] IntPtr[]? domains);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int ctlFrequencyGetProperties(IntPtr domain, ref FreqProperties props);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int ctlFrequencyGetState(IntPtr domain, ref FreqState state);

        private IntPtr _api, _domain;
        private bool _tried;

        public string? Error { get; private set; }
        public double? MaxMhz { get; private set; }
        public double? MinMhz { get; private set; }

        /// <summary>Current request/actual, or null if the library or a GPU frequency domain isn't available.</summary>
        public GpuClockReading? Read()
        {
            if (!Open()) return null;
            var s = new FreqState { Size = (uint)Marshal.SizeOf<FreqState>() };
            int r = ctlFrequencyGetState(_domain, ref s);
            if (r != 0) return null;
            return new GpuClockReading(s.Request >= 0 ? s.Request : null, s.Actual >= 0 ? s.Actual : null, s.ThrottleReasons);
        }

        private bool Open()
        {
            if (_domain != IntPtr.Zero) return true;
            if (_tried) return false;
            _tried = true;
            try
            {
                var args = new InitArgs
                {
                    Size = (uint)Marshal.SizeOf<InitArgs>(),
                    AppVersion = CtlVersion,
                    Flags = CTL_INIT_FLAG_USE_LEVEL_ZERO, // frequency/telemetry need Level Zero (igcl_api.h)
                    ApplicationUID = Guid.Empty, // "Application can pass all 0's as the default ID"; any other ID is rejected (0x40000021)
                };
                int r = ctlInit(ref args, out _api);
                if (r != 0) return Fail($"ctlInit 0x{r:X}");

                uint n = 0;
                ctlEnumerateDevices(_api, ref n, null);
                var devices = new IntPtr[n];
                if (n == 0 || ctlEnumerateDevices(_api, ref n, devices) != 0) return Fail("no Intel GPU");

                foreach (var device in devices)
                {
                    uint m = 0;
                    if (ctlEnumFrequencyDomains(device, ref m, null) != 0 || m == 0) continue;
                    var domains = new IntPtr[m];
                    if (ctlEnumFrequencyDomains(device, ref m, domains) != 0) continue;
                    foreach (var d in domains)
                    {
                        var p = new FreqProperties { Size = (uint)Marshal.SizeOf<FreqProperties>() };
                        if (ctlFrequencyGetProperties(d, ref p) != 0 || p.Type != CTL_FREQ_DOMAIN_GPU) continue;
                        _domain = d;
                        MinMhz = p.Min > 0 ? p.Min : null;
                        MaxMhz = p.Max > 0 ? p.Max : null;
                        Logger.WriteLine($"GPU clock: IGCL GPU domain, {p.Min:0}-{p.Max:0} MHz");
                        return true;
                    }
                }
                return Fail("no GPU frequency domain");
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return Fail(ex.Message);
            }
        }

        private bool Fail(string why)
        {
            Error = why;
            Logger.WriteLine("GPU clock unavailable: " + why);
            Close();
            return false;
        }

        private static readonly (uint bit, string name)[] Reasons =
        {
            (1 << 0, "PL1"), (1 << 1, "PL2"), (1 << 2, "PL4"), (1 << 3, "thermal"), (1 << 4, "PSU"), (1 << 5, "SW range"), (1 << 6, "HW range"),
        };

        /// <summary>ctl_freq_throttle_reason_flags_t → "PL1+thermal" (igcl_api.h CTL_FREQ_THROTTLE_REASON_FLAG_*); "" for none.</summary>
        public static string Describe(uint flags) =>
            string.Join("+", Reasons.Where(r => (flags & r.bit) != 0).Select(r => r.name)
                .Concat((flags & ~0x7Fu) != 0 ? new[] { $"0x{flags & ~0x7Fu:X}" } : Array.Empty<string>()));

        private void Close()
        {
            _domain = IntPtr.Zero;
            if (_api != IntPtr.Zero) { try { ctlClose(_api); } catch { } _api = IntPtr.Zero; }
        }

        public void Dispose() => Close();
    }
}
