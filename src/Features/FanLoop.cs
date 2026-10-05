using FwHelper.Helpers;

namespace FwHelper.Features
{
    public sealed class FanLoopOptions
    {
        public int IntervalMs { get; init; } = 1000;
        public int WatchdogTimeoutMs { get; init; } = 5000;
        /// <summary>While the EC owns the fan, sample its choice every N ticks to learn the floor.</summary>
        public int LearnEveryTicks { get; init; } = 5;
        public Func<bool> FloorEnabled { get; init; } = () => true;
        public FirmwareFloor Floor { get; init; } = new();
        /// <summary>Called with <see cref="FirmwareFloor.Serialize"/> when the learned floor changed (throttled).</summary>
        public Action<string>? SaveFloor { get; init; }
        public int SaveFloorEveryTicks { get; init; } = 60;
        public Action<string> Log { get; init; } = Logger.WriteLine;
    }

    /// <summary>
    /// The fan control loop (ADR 0005/0009/0013): a 1 Hz thread that holds a fixed duty decided by <see cref="FanController"/>,
    /// a watchdog thread that hands the fan back to the EC if the loop stalls, and floor learning while the EC owns the fan.
    /// Talks to hardware only through <see cref="IFanHardware"/>, so all of it runs against a fake in tests.
    /// </summary>
    public sealed class FanLoop : IDisposable
    {
        private readonly IFanHardware _hw;
        private readonly FanLoopOptions _o;
        private readonly object _lock = new();
        private readonly AutoResetEvent _wake = new(false);
        private Thread? _loop, _watchdog;
        private volatile bool _disposed;

        private FanController? _controller;
        private bool _holding;
        private string _lastReason = "";
        private long _heartbeat;
        private volatile bool _watchdogFired;
        private long _ticks;

        public bool IsCustomActive { get; private set; }
        public int LastDuty { get; private set; } = -1;
        public string Status { get; private set; } = "EC auto";
        public FirmwareFloor Floor => _o.Floor;
        /// <summary>Latest EC-ramp model value (%), for the UI.</summary>
        public double LastModel { get; private set; }

        public FanLoop(IFanHardware hw, FanLoopOptions? options = null)
        {
            _hw = hw;
            _o = options ?? new FanLoopOptions();
        }

        /// <summary>Start the threads without taking the fan (floor learning runs while the EC owns it).</summary>
        public void Run()
        {
            lock (_lock) EnsureThreads();
        }

        public void Start(FanCurve curve)
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
                    _o.Log("Custom fan curve on: " + curve);
                }
                EnsureThreads();
            }
            _wake.Set();
        }

        /// <summary>Stop the custom curve and hand the fan back to the EC.</summary>
        public void Stop()
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
                _hw.SetFanAuto();
                if (wasActive) _o.Log("Fan control returned to EC");
            }
        }

        /// <summary>Test hook: block the control loop (as a hung EC call would) so the watchdog has to step in.</summary>
        public void StallLoopForTest(TimeSpan duration)
        {
            lock (_lock) Thread.Sleep(duration);
        }

        private void EnsureThreads()
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

        private void Loop()
        {
            while (!_disposed)
            {
                try { Tick(); }
                catch (Exception ex)
                {
                    // Don't heartbeat on failure: a loop that keeps throwing gets released by the watchdog
                    _o.Log("Fan tick failed: " + ex.Message);
                }
                _wake.WaitOne(_o.IntervalMs);
            }
        }

        private void Tick()
        {
            lock (_lock)
            {
                long tick = ++_ticks;
                var temps = _hw.GetTemperatures();
                LastModel = FirmwareFloor.Model(temps, _hw.GetFanRamps());

                if (tick % _o.SaveFloorEveryTicks == 0 && _o.Floor.Changed) _o.SaveFloor?.Invoke(_o.Floor.Serialize());

                if (!IsCustomActive || _controller is null)
                {
                    if (tick % _o.LearnEveryTicks == 0) Learn();
                    return;
                }

                if (_watchdogFired)
                {
                    // The watchdog released the fan while we were stuck; start over from a fresh reading
                    _watchdogFired = false;
                    _controller.Reset();
                    _holding = false;
                    _o.Log("Fan loop recovered after watchdog release");
                }

                int? cpu = Hardware.FrameworkEc.GetControlTemp(temps);
                int? battery = temps.FirstOrDefault(t => t.Name.Contains("batt", StringComparison.OrdinalIgnoreCase))?.Celsius;
                int floor = _o.FloorEnabled() ? _o.Floor.Floor(LastModel) : 0;

                var decision = _controller.Next(cpu, battery, floor);
                if (decision.Action == FanAction.Release)
                {
                    // Retry every tick until the EC accepts it
                    if (_holding || LastDuty >= 0 || Status != "EC: " + decision.Reason)
                    {
                        if (_hw.SetFanAuto())
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
                    if (_hw.SetFanDuty(decision.Duty))
                    {
                        _holding = true;
                        LastDuty = decision.Duty;
                    }
                    else
                    {
                        if (_holding || LastDuty >= 0) _o.Log("Fan duty write failed");
                        _holding = false;
                        LastDuty = -1;
                    }
                    SetStatus(LastDuty >= 0 ? decision.Reason : "EC: duty write failed");
                }

                Interlocked.Exchange(ref _heartbeat, Environment.TickCount64);
            }
        }

        /// <summary>Only learn from what the EC chose itself: check ownership in hardware, not our own flags.</summary>
        private void Learn()
        {
            if (_hw.IsFanAuto() != true || _hw.GetFanDuty() is not int duty) return;
            _o.Floor.Observe(LastModel, duty);
        }

        private void SetStatus(string reason)
        {
            Status = reason;
            if (reason == _lastReason) return;
            _lastReason = reason;
            _o.Log($"Fan: {reason}{(LastDuty >= 0 ? $" ({LastDuty}%)" : "")}");
        }

        /// <summary>
        /// Independent of the control loop and of its lock: if the loop hasn't completed a tick for the timeout,
        /// hand the fan to the EC. Retries every second until the EC accepts.
        /// </summary>
        private void Watchdog()
        {
            int period = Math.Max(10, Math.Min(1000, _o.WatchdogTimeoutMs / 5));
            while (!_disposed)
            {
                Thread.Sleep(period);
                if (!IsCustomActive || _watchdogFired) continue;

                long stale = Environment.TickCount64 - Interlocked.Read(ref _heartbeat);
                if (stale <= _o.WatchdogTimeoutMs) continue;

                try
                {
                    if (_hw.SetFanAuto())
                    {
                        _watchdogFired = true;
                        Status = "EC: watchdog";
                        _o.Log($"Fan watchdog: control loop stalled {stale} ms, fan returned to EC");
                    }
                }
                catch (Exception ex)
                {
                    _o.Log("Fan watchdog release failed: " + ex.Message);
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _wake.Set();
        }
    }
}
