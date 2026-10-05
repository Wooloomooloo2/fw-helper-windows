using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    public interface IGovernorHardware
    {
        PowerReading ReadPower();
        int? CpuTemp();
        /// <summary>Set the CPU frequency cap (MHz, 0 = none).</summary>
        bool SetCap(int mhz);
    }

    public sealed class EcGovernorHardware : IGovernorHardware, IDisposable
    {
        private readonly EnergyMeter _meter = new();
        public PowerReading ReadPower() => _meter.Read();
        public int? CpuTemp() => FrameworkEc.GetControlTemp(FrameworkEc.GetTemperatures());
        public bool SetCap(int mhz) => PowerPlan.SetCap(mhz);
        public void Dispose() => _meter.Dispose();
    }

    /// <summary>
    /// The governor loop (ADR 0016): 1 Hz, applies <see cref="GovernorPolicy"/> through the frequency cap and writes only on change.
    /// Targets are read through callbacks each tick so UI changes apply immediately.
    /// </summary>
    public sealed class Governor : IDisposable
    {
        private readonly IGovernorHardware _hw;
        private readonly Func<(int? powerW, int? tempC)> _targets;
        private readonly int _intervalMs;
        private readonly Action<string> _log;
        private readonly GovernorPolicy _policy;
        private readonly object _lock = new();
        private readonly AutoResetEvent _wake = new(false);
        private Thread? _thread;
        private volatile bool _stopped = true, _disposed;
        private int _appliedCap = -1;
        private string _lastReason = "";

        public int CapMHz { get; private set; }
        public string Status { get; private set; } = "off";
        public PowerReading LastPower { get; private set; } = new(null, null, null, null);
        public double? SmoothedWatts => _policy.SmoothedWatts;

        public Governor(IGovernorHardware hw, Func<(int? powerW, int? tempC)> targets, int maxMHz,
            int intervalMs = 1000, Action<string>? log = null)
        {
            _hw = hw;
            _targets = targets;
            _intervalMs = intervalMs;
            _log = log ?? Logger.WriteLine;
            _policy = new GovernorPolicy(maxMHz);
        }

        public void Start()
        {
            lock (_lock)
            {
                _stopped = false;
                if (_thread is null)
                {
                    _thread = new Thread(Loop) { IsBackground = true, Name = "Governor" };
                    _thread.Start();
                }
            }
            _wake.Set();
        }

        /// <summary>Remove any cap and idle. Always writes "no cap", whatever we think the state is.</summary>
        public void Stop()
        {
            lock (_lock)
            {
                _stopped = true;
                _policy.Reset();
                _hw.SetCap(0);
                _appliedCap = 0;
                CapMHz = 0;
                Status = "off";
            }
        }

        /// <summary>Run one tick now (tests, and right after a target change).</summary>
        public void Tick()
        {
            lock (_lock)
            {
                if (_stopped) return;
                var (powerW, tempC) = _targets();
                LastPower = _hw.ReadPower();
                var d = _policy.Next(LastPower.PackageW, _hw.CpuTemp(), powerW, tempC);

                if (d.CapMHz != _appliedCap)
                {
                    if (_hw.SetCap(d.CapMHz)) _appliedCap = d.CapMHz;
                    else _log("Governor: frequency cap write failed");
                }
                CapMHz = _appliedCap;
                Status = d.Capped ? $"{d.CapMHz} MHz cap · {d.Reason}" : d.Reason;

                string kind = d.Capped ? d.Reason.Split(' ')[0] : d.Reason;
                if (kind != _lastReason)
                {
                    _lastReason = kind;
                    _log($"Governor: {Status}");
                }
            }
        }

        private void Loop()
        {
            while (!_disposed)
            {
                try { Tick(); }
                catch (Exception ex) { _log("Governor tick failed: " + ex.Message); }
                _wake.WaitOne(_intervalMs);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _wake.Set();
        }
    }
}
