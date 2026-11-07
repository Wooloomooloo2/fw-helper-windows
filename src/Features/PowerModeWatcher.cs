using FwHelper.Helpers;
using System.Runtime.InteropServices;

namespace FwHelper.Features
{
    /// <summary>
    /// Follows Windows' effective power mode (powrprof notifications: no polling, no admin). Notices when Windows leaves the
    /// profile's power mode (Settings, Battery Saver, another app) and, when "keep in step" is on, puts the profile's mode back.
    /// </summary>
    public static class PowerModeWatcher
    {
        private delegate void EffectivePowerModeCallback(int mode, IntPtr context);

        [DllImport("powrprof.dll")]
        private static extern int PowerRegisterForEffectivePowerModeNotifications(uint version, EffectivePowerModeCallback callback, IntPtr context, out IntPtr handle);

        [DllImport("powrprof.dll")]
        private static extern int PowerUnregisterFromEffectivePowerModeNotifications(IntPtr handle);

        private static EffectivePowerModeCallback? _callback; // kept alive: native code holds it
        private static IntPtr _handle;
        private static int _actual = -1;
        private static bool _batterySaver;
        private static readonly Queue<DateTime> _resyncs = new();
        private static readonly object _lock = new();

        public static EffectiveMode? Effective { get; private set; }

        /// <summary>Raised on any thread when what Windows reports changes, or after a profile switch.</summary>
        public static event Action? Changed;

        /// <summary>Raised when FW-Helper put the profile's mode back: (what Windows had, what was restored).</summary>
        public static event Action<string, string>? Resynced;

        public static bool SyncEnabled
        {
            get => AppConfig.Get("win_mode_sync", 1) == 1;
            set => AppConfig.Set("win_mode_sync", value ? 1 : 0);
        }

        public static PowerModeStatus Status =>
            new(Modes.GetOverlay(ModeControl.CurrentMode), _actual, _batterySaver, Effective == EffectiveMode.GameMode, PowerNative.IsOnAC());

        public static void Start()
        {
            Refresh();
            _callback = OnEffectiveModeChanged;
            // V2 also reports Game Mode; older builds only take V1
            int hr = PowerRegisterForEffectivePowerModeNotifications(2, _callback, IntPtr.Zero, out _handle);
            if (hr != 0) hr = PowerRegisterForEffectivePowerModeNotifications(1, _callback, IntPtr.Zero, out _handle);
            Logger.WriteLine($"Windows power mode notifications: {(hr == 0 ? "on" : $"failed 0x{hr:X8}")} · {Status.ShortLabel}");
            ModeControl.ModeChanged += () =>
            {
                lock (_lock) _resyncs.Clear(); // a profile switch starts the loop guard over
                Refresh();
                Changed?.Invoke();
            };
        }

        public static void Stop()
        {
            if (_handle != IntPtr.Zero) PowerUnregisterFromEffectivePowerModeNotifications(_handle);
            _handle = IntPtr.Zero;
        }

        /// <summary>One click from the UI: apply the profile's mode now.</summary>
        public static void Fix()
        {
            PowerNative.SetOverlay(Modes.GetOverlay(ModeControl.CurrentMode));
            Refresh();
            Changed?.Invoke();
        }

        private static void Refresh()
        {
            _actual = PowerNative.GetOverlayIndex();
            _batterySaver = PowerNative.IsBatterySaverOn() || Effective == EffectiveMode.BatterySaver;
        }

        private static void OnEffectiveModeChanged(int mode, IntPtr context)
        {
            Effective = (EffectiveMode)mode;
            // Leave the native callback quickly; settle for a moment (a profile switch may be writing the overlay right now)
            Task.Delay(500).ContinueWith(_ =>
            {
                try { Check(); }
                catch (Exception ex) { Logger.WriteLine("Power mode check: " + ex.Message); }
            });
        }

        private static void Check()
        {
            Refresh();
            var s = Status;
            if (WindowsPowerMode.ShouldResync(SyncEnabled, s.Wanted, s.Actual, s.BatterySaver))
            {
                bool giveUp;
                lock (_lock)
                {
                    while (_resyncs.Count > 0 && DateTime.Now - _resyncs.Peek() > TimeSpan.FromMinutes(1)) _resyncs.Dequeue();
                    giveUp = _resyncs.Count >= 3;
                    if (!giveUp) _resyncs.Enqueue(DateTime.Now);
                }
                string had = PowerNative.OverlayNames[s.Actual], want = PowerNative.OverlayNames[s.Wanted];
                if (giveUp)
                {
                    Logger.WriteLine($"Windows power mode: something keeps setting {had}; leaving it until the next profile switch");
                }
                else
                {
                    Logger.WriteLine($"Windows power mode changed to {had} outside FW-Helper → back to {want} ({Modes.Name(ModeControl.CurrentMode)})");
                    PowerNative.SetOverlay(s.Wanted);
                    Refresh();
                    Resynced?.Invoke(had, want);
                }
            }
            Changed?.Invoke();
        }
    }
}
