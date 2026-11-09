using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// 1 Hz telemetry sampler. Runs only while someone uses it (the Monitor window, a recording), keeps the last
    /// <see cref="HistoryLength"/> samples, and feeds the session recorder.
    /// </summary>
    public static class Telemetry
    {
        public const int HistoryLength = 300;
        private const int IntervalMs = 1000;

        private static readonly object _lock = new();
        private static readonly Queue<TelemetrySample> _history = new();
        private static System.Threading.Timer? _timer;
        private static SystemMetrics? _metrics;
        private static EnergyMeter? _energy;
        private static PresentMonitor? _presents;
        private static IntelGpuClock? _gpuClock;

        /// <summary>Why FPS isn't available (no ETW rights), for the UI; null when it works or isn't running.</summary>
        public static string? FpsError => _presents?.Error;
        private static int _users;
        private static SessionRecorder? _recorder;
        private static FrameLog? _frames;
        /// <summary>The process the last sample's FPS came from (the frames file follows it).</summary>
        private static int? _fpsPid;

        /// <summary>Raised on a background thread after each sample.</summary>
        public static event Action<TelemetrySample>? Sampled;
        /// <summary>Raised when recording starts or stops (any thread).</summary>
        public static event Action? RecordingChanged;
        /// <summary>A recording finished: its CSV path and row count.</summary>
        public static event Action<string, int>? RecordingStopped;

        public static bool IsRecording => _recorder is not null;
        public static string? RecordingFile => _recorder?.FilePath;
        public static int RecordedRows => _recorder?.Rows ?? 0;
        public static DateTime? RecordingStarted => _recorder?.Started;

        /// <summary>Start (with this name) or stop: the Ctrl+Shift+F6 hotkey and the tray item.</summary>
        public static void ToggleRecording(string? name = null)
        {
            if (IsRecording) StopRecording();
            else StartRecording(name);
        }

        public static List<TelemetrySample> History()
        {
            lock (_lock) return _history.ToList();
        }

        /// <summary>Keep the sampler running until the returned handle is disposed.</summary>
        public static IDisposable Use()
        {
            lock (_lock)
            {
                if (_users++ == 0)
                {
                    _metrics ??= new SystemMetrics();
                    _energy ??= new EnergyMeter();
                    // The ETW session only runs while something shows or records telemetry
                    _presents = new PresentMonitor();
                    _presents.Start();
                    // IGCL loads Level Zero (about 20 MB): only while telemetry is in use
                    _gpuClock = new IntelGpuClock();
                    _timer = new System.Threading.Timer(_ => Tick(), null, 0, IntervalMs);
                }
            }
            return new Handle();
        }

        private static void Release()
        {
            lock (_lock)
            {
                if (--_users > 0) return;
                _timer?.Dispose();
                _timer = null;
                _presents?.Dispose();
                _presents = null;
                _gpuClock?.Dispose();
                _gpuClock = null;
            }
        }

        private static IDisposable? _recordingUse;

        public static void StartRecording(string? name = null)
        {
            lock (_lock)
            {
                if (_recorder is not null) return;
                try { _recorder = new SessionRecorder(name); }
                catch (Exception ex) { Logger.WriteLine("Can't start recording: " + ex.Message); return; }
                try { _frames = new FrameLog(FrameLog.PathFor(_recorder.FilePath), _recorder.Started); }
                catch (Exception ex) { Logger.WriteLine("Can't record frame times: " + ex.Message); }
                try { CaptureContext().Save(_recorder.FilePath); }
                catch (Exception ex) { Logger.WriteLine("Can't write run notes: " + ex.Message); }
            }
            _recordingUse = Use();
            RecordingChanged?.Invoke();
        }

        /// <summary>What FW-Helper knows about the run at its start: firmware, its own version and the fan curve.</summary>
        private static RunContext CaptureContext()
        {
            string? bios = null;
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                bios = key?.GetValue("BIOSVersion") as string;
            }
            catch (Exception) { }
            int mode = ModeControl.CurrentMode;
            return new RunContext
            {
                Bios = bios,
                Ec = FrameworkEc.GetVersion(),
                App = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3),
                FanCurve = RunContext.FanName(Modes.IsCustomFan(mode), Modes.GetCurve(mode).ToString(),
                    FanCurveLibrary.All().Select(c => (c.name, c.curve.ToString()))),
            };
        }

        public static void StopRecording()
        {
            (string Path, int Rows)? stopped = null;
            lock (_lock)
            {
                if (_recorder is null) return;
                stopped = (_recorder.FilePath, _recorder.Rows);
                _recorder.Dispose();
                _recorder = null;
                if (_frames is not null) Logger.WriteLine($"Frame times: {_frames.Frames} frames");
                _frames?.Dispose();
                _frames = null;
            }
            _recordingUse?.Dispose();
            _recordingUse = null;
            RecordingChanged?.Invoke();
            if (stopped is var (path, rows)) RecordingStopped?.Invoke(path, rows);
        }

        private static void Tick()
        {
            TelemetrySample sample;
            try
            {
                sample = Collect();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Telemetry: " + ex.Message);
                return;
            }

            bool expired = false;
            lock (_lock)
            {
                _history.Enqueue(sample);
                while (_history.Count > HistoryLength) _history.Dequeue();
                if (_recorder is not null)
                {
                    try { _recorder.Write(sample); }
                    catch (Exception ex) { Logger.WriteLine("Recording write failed: " + ex.Message); }
                    // Every frame of the app the FPS figure follows. A new app starts from its last second, not its 3 s backlog
                    if (_frames is not null && _presents is { Origin: DateTime origin } p && _fpsPid is int pid)
                    {
                        double after = pid == _frames.Pid ? _frames.LastMs : p.Counter.Latest - FpsCounter.WindowMs;
                        try { _frames.Write(pid, p.Counter.PresentsAfter(pid, after), origin); }
                        catch (Exception ex) { Logger.WriteLine("Frame times write failed: " + ex.Message); _frames.Dispose(); _frames = null; }
                    }
                    expired = _recorder.Expired;
                }
            }
            if (expired) StopRecording();
            Sampled?.Invoke(sample);
        }

        private static TelemetrySample Collect()
        {
            SystemSnapshot sys;
            lock (_lock) sys = _metrics!.Sample();

            PowerReading power;
            lock (_lock) power = _energy!.Read();
            var fps = _presents?.Current();
            _fpsPid = fps?.pid;
            GpuClockReading? gpuClock;
            lock (_lock) gpuClock = _gpuClock?.Read();

            var temps = FrameworkEc.GetTemperatures();
            int? Temp(string prefix) => temps.FirstOrDefault(t => t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?.Celsius;
            var batt = FrameworkEc.GetBattery();
            bool onAC = PowerNative.IsOnAC();
            int mode = ModeControl.CurrentMode;
            var governor = GovernorControl.Instance;

            return new TelemetrySample(
                DateTime.Now, Modes.Name(mode), onAC,
                sys.CpuPct, sys.CpuMhz, sys.GpuPct, sys.GpuEngine, sys.GpuSharedGb, sys.MemUsedGb, sys.MemTotalGb,
                FrameworkEc.GetCpuTemp(temps), Temp("battery"), Temp("ddr"), Temp("local"),
                FrameworkEc.GetFanRpms().FirstOrDefault(),
                FanControl.IsCustomActive ? FanControl.LastDuty : -1,
                FanControl.IsCustomActive ? FanControl.Status : "EC auto",
                batt is { Present: true } ? batt.Percent : null,
                // System draw is only measurable from the battery while discharging
                batt is { Present: true, Discharging: true } ? -batt.Watts : null,
                power.PackageW,
                GovernorControl.PowerTarget(mode),
                power.CoresW,
                power.GpuW,
                Throttle(governor),
                power.DramW,
                governor is { CapMHz: > 0 } g ? g.CapMHz : null,
                fps?.fps,
                fps?.app,
                batt is { Present: true, Discharging: true, RateMa: > 0 } b ? (int)(b.RemainingMah * 60L / b.RateMa) : null,
                gpuClock?.RequestMhz,
                gpuClock?.ActualMhz,
                gpuClock?.ThrottleText,
                PowerModeWatcher.Status is var w && w.BatterySaver ? "Battery Saver" : WindowsPowerMode.Valid(w.Actual) ? WindowsPowerMode.Short[w.Actual] : null,
                CpuBoost.Active);
        }

        /// <summary>What is holding the CPU back right now: our governor and/or the EC (soft event, hard PROCHOT). "" if nothing.</summary>
        private static string Throttle(Governor? governor)
        {
            var parts = new List<string>();
            if (governor is { CapMHz: > 0 } g) parts.Add("governor: " + g.Status);
            if (FrameworkEc.GetApThrottleStatus() is var (soft, hard) && (soft || hard)) parts.Add(hard ? "EC: PROCHOT" : "EC: soft");
            return string.Join(" · ", parts);
        }

        private sealed class Handle : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Release();
            }
        }
    }
}
