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
        private static int _users;
        private static SessionRecorder? _recorder;

        /// <summary>Raised on a background thread after each sample.</summary>
        public static event Action<TelemetrySample>? Sampled;
        /// <summary>Raised when recording starts or stops (any thread).</summary>
        public static event Action? RecordingChanged;

        public static bool IsRecording => _recorder is not null;
        public static string? RecordingFile => _recorder?.FilePath;
        public static int RecordedRows => _recorder?.Rows ?? 0;

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
            }
        }

        private static IDisposable? _recordingUse;

        public static void StartRecording()
        {
            lock (_lock)
            {
                if (_recorder is not null) return;
                try { _recorder = new SessionRecorder(); }
                catch (Exception ex) { Logger.WriteLine("Can't start recording: " + ex.Message); return; }
            }
            _recordingUse = Use();
            RecordingChanged?.Invoke();
        }

        public static void StopRecording()
        {
            lock (_lock)
            {
                if (_recorder is null) return;
                _recorder.Dispose();
                _recorder = null;
            }
            _recordingUse?.Dispose();
            _recordingUse = null;
            RecordingChanged?.Invoke();
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

            var temps = FrameworkEc.GetTemperatures();
            int? Temp(string prefix) => temps.FirstOrDefault(t => t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?.Celsius;
            var batt = FrameworkEc.GetBattery();
            bool onAC = PowerNative.IsOnAC();
            int mode = ModeControl.CurrentMode;

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
                Watts(_package, IntelPowerLimits.MSR_PKG_ENERGY_STATUS),
                Modes.IsPowerLimit(mode) && IntelPowerLimits.IsAvailable
                    ? Math.Clamp(Modes.GetPL1(mode), PowerLimitControl.MinPL1, PowerLimitControl.MaxPL1) : null,
                Watts(_cores, IntelPowerLimits.MSR_PP0_ENERGY_STATUS),
                Watts(_uncore, IntelPowerLimits.MSR_PP1_ENERGY_STATUS),
                Throttle());
        }

        // Our own meters: sharing one with the Fans + Power window would split the measuring interval
        private static readonly RaplEnergy _package = new(), _cores = new(), _uncore = new();

        private static double? Watts(RaplEnergy meter, uint msr) =>
            IntelPowerLimits.ReadEnergy(msr) is uint raw ? meter.Next(raw, IntelPowerLimits.EnergyUnit, Environment.TickCount64) : null;

        /// <summary>"core: EDP · gpu: PL1" style summary of what is limiting clocks right now; null without PawnIO, "" if nothing.</summary>
        private static string? Throttle()
        {
            if (IntelPowerLimits.Read(PerfLimitReasons.MSR_CORE_PERF_LIMIT_REASONS) is not ulong core) return null;
            var parts = new List<string>();
            void Add(string domain, ulong? value)
            {
                if (value is ulong v && PerfLimitReasons.Decode(v) is { Length: > 0 } s) parts.Add($"{domain}: {s}");
            }
            Add("core", core);
            Add("gpu", IntelPowerLimits.Read(PerfLimitReasons.MSR_GRAPHICS_PERF_LIMIT_REASONS));
            Add("ring", IntelPowerLimits.Read(PerfLimitReasons.MSR_RING_PERF_LIMIT_REASONS));
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
