using FwHelper.Features;
using FwHelper.Helpers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FwHelper.Hardware
{
    /// <summary>
    /// Real-time ETW session on the present events PresentMon uses (ADR 0017): DXGI and D3D9 Present_Start, plus DxgKrnl
    /// Present_Info for Vulkan/OpenGL. Needs admin or membership of "Performance Log Users"; without either, <see cref="Error"/>
    /// says so and FPS just shows "–". Event ids and GUIDs are from GameTechDev/PresentMon PresentData/ETW/*.h.
    /// </summary>
    public sealed class PresentMonitor : IDisposable
    {
        private const string SessionName = "FwHelper-Presents";

        private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

        private const int DxgiPresentStart = 0x2A, DxgiPresentMpoStart = 0x37, D3D9PresentStart = 0x01, DxgKrnlPresentInfo = 0xB8;

        private TraceEventSession? _session;
        private Thread? _thread;

        public FpsCounter Counter { get; } = new();
        public string? Error { get; private set; }
        public bool Running => _session is not null && Error is null;

        public void Start()
        {
            if (_session is not null) return;
            try
            {
                // A session left behind by a crashed run keeps the name: take it over
                _session = new TraceEventSession(SessionName) { StopOnDispose = true };
                // Filter by event id in the kernel: unfiltered, DxgKrnl alone sends ~5000 events/s at idle (measured), which cost
                // ~4 % of a core and kept the CPU awake (+1–1.5 W package at idle)
                _session.EnableProvider(Dxgi, TraceEventLevel.Informational, 0x8000000000000002, Only(DxgiPresentStart, DxgiPresentMpoStart));
                _session.EnableProvider(D3D9, TraceEventLevel.Informational, 0x8000000000000002, Only(D3D9PresentStart));
                _session.EnableProvider(DxgKrnl, TraceEventLevel.Informational, 0x4000000008000001, Only(DxgKrnlPresentInfo));
                _session.Source.AllEvents += OnEvent;
                _thread = new Thread(() =>
                {
                    try { _session.Source.Process(); }
                    catch (Exception ex) { Error = ex.Message; Logger.WriteLine("FPS capture stopped: " + ex.Message); }
                }) { IsBackground = true, Name = "PresentMonitor" };
                _thread.Start();
                Logger.WriteLine("FPS capture started");
            }
            catch (Exception ex)
            {
                Error = ex is UnauthorizedAccessException
                    ? "FPS needs the \"Performance Log Users\" group or admin"
                    : ex.Message;
                Logger.WriteLine("FPS capture unavailable: " + ex.Message);
                _session?.Dispose();
                _session = null;
            }
        }

        private static TraceEventProviderOptions Only(params int[] ids) => new() { EventIDsToEnable = ids.ToList() };

        private void OnEvent(TraceEvent e)
        {
            int id = (int)e.ID;
            PresentSource? source =
                e.ProviderGuid == Dxgi && (id == DxgiPresentStart || id == DxgiPresentMpoStart) ? PresentSource.Api :
                e.ProviderGuid == D3D9 && id == D3D9PresentStart ? PresentSource.Api :
                e.ProviderGuid == DxgKrnl && id == DxgKrnlPresentInfo ? PresentSource.Kernel : null;
            if (source is PresentSource s) Counter.Record(e.ProcessID, s, e.TimeStampRelativeMSec);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        /// <summary>FPS of the foreground app, or of the busiest presenter if the foreground app isn't drawing frames.</summary>
        public (double fps, string app)? Current()
        {
            if (!Running) return null;
            Counter.Prune();
            GetWindowThreadProcessId(GetForegroundWindow(), out uint fg);
            if (fg != 0 && Counter.Fps((int)fg) is double f) return (f, ProcessName((int)fg));
            return Counter.Busiest() is (int pid, double fps) ? (fps, ProcessName(pid)) : null;
        }

        private static readonly Dictionary<int, string> _names = new();

        private static string ProcessName(int pid)
        {
            lock (_names)
            {
                if (_names.TryGetValue(pid, out var n)) return n;
                try { n = Process.GetProcessById(pid).ProcessName; } catch { n = pid.ToString(); }
                if (_names.Count > 200) _names.Clear();
                return _names[pid] = n;
            }
        }

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
