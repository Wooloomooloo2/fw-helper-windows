using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// Software fan curve: polls the EC CPU temperature and sets a fixed fan duty.
    /// While running, EC automatic fan control is disabled, so every exit path must call <see cref="Stop"/>.
    /// </summary>
    public static class FanControl
    {
        private const int IntervalMs = 2000;
        private const int SafetyTemp = 95;     // force 100% above this
        private const int RampDownPerTick = 4; // % per tick, avoids audible pulsing

        private static System.Threading.Timer? _timer;
        private static FanCurve? _curve;
        private static int _lastDuty = -1;
        private static int _failures;
        private static readonly object _lock = new();

        public static bool IsCustomActive { get; private set; }
        public static int LastDuty => _lastDuty;

        public static void Start(FanCurve curve)
        {
            lock (_lock)
            {
                _curve = curve.Clone();
                _failures = 0;
                if (!IsCustomActive)
                {
                    IsCustomActive = true;
                    _lastDuty = -1;
                    _timer = new System.Threading.Timer(_ => Tick(), null, 0, IntervalMs);
                    Logger.WriteLine("Custom fan curve on: " + curve);
                }
            }
        }

        /// <summary>Stop the custom curve and hand fans back to the EC.</summary>
        public static void Stop()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                bool wasActive = IsCustomActive;
                IsCustomActive = false;
                _lastDuty = -1;
                // Always send it: also recovers from a previous crash that left a fixed duty behind
                FrameworkEc.SetFanAuto();
                if (wasActive) Logger.WriteLine("Fan control returned to EC");
            }
        }

        private static void Tick()
        {
            lock (_lock)
            {
                if (!IsCustomActive || _curve is null) return;

                int? temp = FrameworkEc.GetCpuTemp();
                if (temp is null)
                {
                    // Can't see temperatures: don't fly blind, give control back to the EC
                    if (++_failures >= 3)
                    {
                        Logger.WriteLine("Fan curve: no temperature, reverting to EC auto");
                        _timer?.Dispose();
                        _timer = null;
                        IsCustomActive = false;
                        FrameworkEc.SetFanAuto();
                    }
                    return;
                }
                _failures = 0;

                int target = temp.Value >= SafetyTemp ? 100 : _curve.DutyAt(temp.Value);
                int duty = target;
                if (_lastDuty >= 0 && target < _lastDuty)
                    duty = Math.Max(target, _lastDuty - RampDownPerTick);

                if (duty != _lastDuty)
                {
                    if (FrameworkEc.SetFanDuty(duty)) _lastDuty = duty;
                    else Logger.WriteLine("Fan duty write failed");
                }
            }
        }
    }
}
