using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// Software fan curve: polls EC temperatures at 1 Hz and holds a fixed fan duty decided by <see cref="FanController"/>.
    /// While a duty is held, EC automatic fan control is off, so every exit path must call <see cref="Stop"/>.
    /// A watchdog thread hands the fan back to the EC if the control loop stops ticking (ADR 0005).
    /// </summary>
    public static class FanControl
    {
        private const int IntervalMs = 1000;
        private const int WatchdogTimeoutMs = 5000;

        private static readonly object _lock = new();
        private static readonly AutoResetEvent _wake = new(false);
        private static Thread? _loop, _watchdog;

        private static FanController? _controller;
        private static bool _holding;              // we hold a fixed duty (EC auto is off)
        private static string _lastReason = "";

        private static long _heartbeat;            // TickCount64 of the last completed tick
        private static volatile bool _watchdogFired;

        public static bool IsCustomActive { get; private set; }
        public static int LastDuty { get; private set; } = -1;
        /// <summary>Why the fan is doing what it does right now, for the UI ("curve", "battery guard", "EC: CPU ≥100°C"...).</summary>
        public static string Status { get; private set; } = "EC auto";

        public static void Start(FanCurve curve)
        {
            lock (_lock)
            {
                if (_controller is null) _controller = new FanController(curve);
                else _controller.Curve = curve;

                if (!IsCustomActive)
                {
                    IsCustomActive = true;
                    _controller.Reset();
                    _holding = false;
                    _heartbeat = Environment.TickCount64;
                    Logger.WriteLine("Custom fan curve on: " + curve);
                }
                EnsureThreads();
            }
            _wake.Set();
        }

        /// <summary>Stop the custom curve and hand fans back to the EC.</summary>
        public static void Stop()
        {
            lock (_lock)
            {
                bool wasActive = IsCustomActive;
                IsCustomActive = false;
                _holding = false;
                LastDuty = -1;
                Status = "EC auto";
                _lastReason = "";
                // Always send it: also recovers from a previous crash that left a fixed duty behind
                FrameworkEc.SetFanAuto();
                if (wasActive) Logger.WriteLine("Fan control returned to EC");
            }
        }

        private static void EnsureThreads()
        {
            if (_loop is null)
            {
                _loop = new Thread(Loop) { IsBackground = true, Name = "FanControl" };
                _loop.Start();
            }
            if (_watchdog is null)
            {
                _watchdog = new Thread(Watchdog) { IsBackground = true, Name = "FanWatchdog", Priority = ThreadPriority.AboveNormal };
                _watchdog.Start();
            }
        }

        private static void Loop()
        {
            while (true)
            {
                try { Tick(); }
                catch (Exception ex)
                {
                    // Don't heartbeat on failure: a loop that keeps throwing gets released by the watchdog
                    Logger.WriteLine("Fan tick failed: " + ex.Message);
                }
                _wake.WaitOne(IntervalMs);
            }
        }

        private static void Tick()
        {
            lock (_lock)
            {
                if (!IsCustomActive || _controller is null) return;

                if (_watchdogFired)
                {
                    // The watchdog released the fan while we were stuck; start over from a fresh reading
                    _watchdogFired = false;
                    _controller.Reset();
                    _holding = false;
                    Logger.WriteLine("Fan loop recovered after watchdog release");
                }

                var temps = FrameworkEc.GetTemperatures();
                int? cpu = FrameworkEc.GetCpuTemp(temps);
                int? battery = temps.FirstOrDefault(t => t.Name.Contains("batt", StringComparison.OrdinalIgnoreCase))?.Celsius;

                var decision = _controller.Next(cpu, battery);
                if (decision.Action == FanAction.Release)
                {
                    // Retry every tick until the EC accepts it
                    if (_holding || LastDuty >= 0 || Status != "EC: " + decision.Reason)
                    {
                        if (FrameworkEc.SetFanAuto())
                        {
                            _holding = false;
                            LastDuty = -1;
                        }
                    }
                    SetStatus("EC: " + decision.Reason);
                }
                else
                {
                    // Written every tick, not only on change: something else (an old instance's guard,
                    // framework_tool) may have handed the fan back to the EC behind our back
                    if (FrameworkEc.SetFanDuty(decision.Duty))
                    {
                        _holding = true;
                        LastDuty = decision.Duty;
                    }
                    else
                    {
                        if (_holding || LastDuty >= 0) Logger.WriteLine("Fan duty write failed");
                        _holding = false;
                        LastDuty = -1;
                    }
                    SetStatus(LastDuty >= 0 ? decision.Reason : "EC: duty write failed");
                }

                Interlocked.Exchange(ref _heartbeat, Environment.TickCount64);
            }
        }

        private static void SetStatus(string reason)
        {
            Status = reason;
            if (reason == _lastReason) return;
            _lastReason = reason;
            Logger.WriteLine($"Fan: {reason}{(LastDuty >= 0 ? $" ({LastDuty}%)" : "")}");
        }

        /// <summary>
        /// Independent of the control loop and of <see cref="_lock"/>: if the loop hasn't completed a tick for
        /// <see cref="WatchdogTimeoutMs"/>, hand the fan to the EC. Retries every second until the EC accepts.
        /// </summary>
        private static void Watchdog()
        {
            while (true)
            {
                Thread.Sleep(1000);
                if (!IsCustomActive || _watchdogFired) continue;

                long stale = Environment.TickCount64 - Interlocked.Read(ref _heartbeat);
                if (stale <= WatchdogTimeoutMs) continue;

                try
                {
                    if (FrameworkEc.SetFanAuto())
                    {
                        _watchdogFired = true;
                        Status = "EC: watchdog";
                        Logger.WriteLine($"Fan watchdog: control loop stalled {stale} ms, fan returned to EC");
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Fan watchdog release failed: " + ex.Message);
                }
            }
        }
    }
}
