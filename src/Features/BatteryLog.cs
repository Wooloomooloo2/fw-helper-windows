using FwHelper.Hardware;
using FwHelper.Helpers;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FwHelper.Features
{
    /// <summary>
    /// Always-on battery log (ADR 0018). There is no polling loop: Windows calls us when the battery percent, the power source or
    /// the display state changes, and a single low-priority EcoQoS thread then reads the EC once and appends one line.
    /// While on battery with the screen on, a 10-minute heartbeat bounds the gap if a notification is missed.
    /// Standby sessions come from Windows' own records (Kernel-Power event 507), pushed by an event log subscription.
    /// Files: battery\events.csv and battery\standby.csv (last 30 days, append-only), and battery\history.json (months, years).
    /// </summary>
    public static class BatteryLog
    {
        public static readonly string Folder = Path.Combine(Logger.AppDataDir, "battery");
        public static readonly string EventsFile = Path.Combine(Folder, "events.csv");
        public static readonly string ArchiveFile = Path.Combine(Folder, "history.json");
        public static readonly string StandbyFile = Path.Combine(Folder, "standby.csv");
        public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(10);

        /// <summary>Raised on the logger thread after each new event.</summary>
        public static event Action? Changed;

        private static readonly object _lock = new();
        private static List<BatteryEvent> _events = new();
        private static BatteryArchive _archive = BatteryArchive.Empty;
        private static List<StandbySession> _sessions = new();
        private static EventLogWatcher? _standbyWatcher;
        private static BatteryEvent? _last;
        private static BatterySession? _session; // as of _last, for the tray tooltip
        private static DateTime _rolledOn;

        private static readonly AutoResetEvent _signal = new(false);
        private static Thread? _thread;
        private static volatile bool _stopping;
        // Latest state from the notifications; -1 = not reported yet. Written by the callback thread.
        private static volatile int _ac = -1, _screen = -1;
        private static int _pending; // BatteryEventKind flags: 1 sample, 2 power, 4 display
        private static readonly List<IntPtr> _registrations = new();
        private static IntPtr _recipient;
        private static DeviceNotifyCallback? _callback; // must outlive the registrations

        public static BatteryEvent[] Events() { lock (_lock) return _events.ToArray(); }
        public static BatteryArchive Archive() { lock (_lock) return _archive; }
        public static StandbySession[] Sessions() { lock (_lock) return _sessions.ToArray(); }

        public static void Start()
        {
            if (_thread is not null) return;
            try
            {
                var (events, sessions, archive) = Load();
                lock (_lock)
                {
                    _events = events;
                    _sessions = sessions;
                    _archive = archive;
                    _last = events.LastOrDefault();
                }
                Register();
                _thread = new Thread(Run) { IsBackground = true, Name = "BatteryLog", Priority = ThreadPriority.Lowest };
                _thread.Start();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Battery log: " + ex.Message);
            }
        }

        /// <summary>Writes the Stop marker so the time FW-Helper isn't running isn't counted. Safe to call many times, from any thread.</summary>
        public static void Stop()
        {
            if (_thread is null || _stopping) return;
            _stopping = true;
            _signal.Set();
            foreach (var h in _registrations) PowerSettingUnregisterNotification(h);
            _registrations.Clear();
            try { _standbyWatcher?.Dispose(); } catch { }
            try { Record(BatteryEventKind.Stop); } catch { }
        }

        private static void Run()
        {
            EcoQos.Enable();
            Record(BatteryEventKind.Start);
            try { StartStandby(); }
            catch (Exception ex) { Logger.WriteLine("Battery log: standby sessions unavailable: " + ex.Message); }
            while (!_stopping)
            {
                bool heartbeat = _last is { OnAC: false, ScreenOn: true };
                _signal.WaitOne(heartbeat ? Heartbeat : Timeout.InfiniteTimeSpan);
                if (_stopping) break;
                int pending = Interlocked.Exchange(ref _pending, 0);
                var kind = (pending & 2) != 0 ? BatteryEventKind.Power : (pending & 4) != 0 ? BatteryEventKind.Display : BatteryEventKind.Sample;
                try { Record(kind); }
                catch (Exception ex) { Logger.WriteLine("Battery log: " + ex.Message); }
            }
        }

        /// <summary>A fresh EC reading as an event, or null without a battery.</summary>
        public static BatteryEvent? Read(BatteryEventKind kind = BatteryEventKind.Sample)
        {
            if (FrameworkEc.GetBattery() is not { Present: true } b) return null;
            bool ac = _ac >= 0 ? _ac == 1 : b.AcPresent;
            bool screen = _screen != 0;
            return new BatteryEvent(DateTime.UtcNow, kind, ac, screen, b.Charging, b.Percent, b.RemainingMah, b.FullMah, b.VoltageMv, b.Cycles);
        }

        private static void Record(BatteryEventKind kind)
        {
            if (Read(kind) is not { } e) return;
            lock (_lock)
            {
                // Notifications repeat (dimming, the same percent from two sources): skip lines that add nothing
                if (_last is { } l && kind is BatteryEventKind.Sample or BatteryEventKind.Power or BatteryEventKind.Display &&
                    l.OnAC == e.OnAC && l.ScreenOn == e.ScreenOn && l.Percent == e.Percent && l.Charging == e.Charging &&
                    l.Kind != BatteryEventKind.Stop && e.Time - l.Time < TimeSpan.FromMinutes(1))
                    return;

                Directory.CreateDirectory(Folder);
                if (!File.Exists(EventsFile)) File.WriteAllText(EventsFile, BatteryEvent.CsvHeader + Environment.NewLine);
                File.AppendAllText(EventsFile, e.ToCsv() + Environment.NewLine);
                _events.Add(e);
                _last = e;
                _session = BatteryHistory.Session(_events, null);

                // Once a day: fold events older than 30 days into months, months older than 12 into years
                var today = DateTime.Now.Date;
                if (_rolledOn != today)
                {
                    _rolledOn = today;
                    var (events, sessions, archive, changed) = BatteryHistory.Roll(_events, _sessions, _archive, e.Time);
                    if (changed)
                    {
                        _events = events;
                        _sessions = sessions;
                        _archive = archive;
                        _session = BatteryHistory.Session(_events, null);
                        Save();
                        Logger.WriteLine($"Battery log rolled up: {events.Count} events kept, {archive.Months.Count} months, {archive.Years.Count} years");
                    }
                }
            }
            if (kind is BatteryEventKind.Power or BatteryEventKind.Start or BatteryEventKind.Stop)
                Logger.WriteLine($"Battery log: {kind} {(e.OnAC ? "AC" : "battery")} {e.Percent}% {e.Wh:0.0} Wh");
            Changed?.Invoke();
        }

        // ---------- Files ----------

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        /// <summary>Reads the files only; used by the CLI when FW-Helper isn't running.</summary>
        public static (List<BatteryEvent> events, List<StandbySession> sessions, BatteryArchive archive) Load()
        {
            var events = File.Exists(EventsFile)
                ? File.ReadLines(EventsFile).Skip(1).Select(BatteryEvent.FromCsv).OfType<BatteryEvent>().OrderBy(e => e.Time).ToList()
                : new List<BatteryEvent>();
            var sessions = File.Exists(StandbyFile)
                ? File.ReadLines(StandbyFile).Skip(1).Select(StandbySession.FromCsv).OfType<StandbySession>().OrderBy(x => x.ExitUtc).ToList()
                : new List<StandbySession>();
            var archive = BatteryArchive.Empty;
            try
            {
                if (File.Exists(ArchiveFile)) archive = JsonSerializer.Deserialize<BatteryArchive>(File.ReadAllText(ArchiveFile), Json) ?? archive;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Battery history unreadable: " + ex.Message);
            }
            return (events, sessions, archive);
        }

        /// <summary>Caller holds the lock. Writes to temp files first, so a crash mid-write can't lose the history.</summary>
        private static void Save()
        {
            File.WriteAllText(ArchiveFile + ".tmp", JsonSerializer.Serialize(_archive, Json));
            File.Move(ArchiveFile + ".tmp", ArchiveFile, true);
            File.WriteAllLines(EventsFile + ".tmp", _events.Select(e => e.ToCsv()).Prepend(BatteryEvent.CsvHeader));
            File.Move(EventsFile + ".tmp", EventsFile, true);
            File.WriteAllLines(StandbyFile + ".tmp", _sessions.Select(x => x.ToCsv()).Prepend(StandbySession.CsvHeader));
            File.Move(StandbyFile + ".tmp", StandbyFile, true);
        }

        // ---------- Standby sessions (Kernel-Power 507) ----------

        private const string StandbyQuery = "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=507]]";

        /// <summary>
        /// Catches up on sessions that ended while FW-Helper wasn't running (the System log goes back weeks), then subscribes.
        /// The subscription is pushed by the event log service: nothing runs until a standby session ends.
        /// </summary>
        private static void StartStandby()
        {
            DateTime after;
            lock (_lock) after = _sessions.Count > 0 ? _sessions[^1].ExitUtc : DateTime.MinValue;
            string query = after == DateTime.MinValue ? StandbyQuery
                : StandbyQuery.Replace("EventID=507]", $"EventID=507 and TimeCreated[@SystemTime>'{after:yyyy-MM-ddTHH:mm:ss.fffZ}']]");
            int added = 0;
            using (var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query)))
                for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
                    using (record)
                        if (AddStandby(record) is not null) added++;
            if (added > 0) Logger.WriteLine($"Battery log: {added} standby sessions read from the System log");

            _standbyWatcher = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, StandbyQuery));
            _standbyWatcher.EventRecordWritten += (_, e) =>
            {
                try
                {
                    using var record = e.EventRecord;
                    if (record is not null && AddStandby(record) is { } s)
                    {
                        Logger.WriteLine($"Standby: {Duration(TimeSpan.FromSeconds(s.Seconds))}, awake with the screen off {Duration(TimeSpan.FromSeconds(s.AwakeSeconds))}" +
                                         (s.AwakeW is double a ? $" at {a:0.00} W" : "") + $", asleep {Duration(TimeSpan.FromSeconds(s.AsleepSeconds))}" +
                                         (s.AsleepW is double z ? $" at {z:0.00} W" : "") + $", deep idle {s.DeepIdlePercent:0}%");
                        Changed?.Invoke();
                    }
                }
                catch (Exception ex) { Logger.WriteLine("Battery log: standby event: " + ex.Message); }
            };
            _standbyWatcher.Enabled = true;
        }

        private static StandbySession? AddStandby(EventRecord record)
        {
            if (record.TimeCreated is not DateTime created) return null;
            var xml = XElement.Parse(record.ToXml());
            var ns = xml.Name.Namespace;
            var data = (xml.Element(ns + "EventData")?.Elements(ns + "Data") ?? Enumerable.Empty<XElement>())
                .Where(d => d.Attribute("Name") is not null)
                .GroupBy(d => (string)d.Attribute("Name")!)
                .ToDictionary(g => g.Key, g => g.First().Value);
            if (StandbySession.FromEvent(created.ToUniversalTime(), data) is not { } s) return null;
            lock (_lock)
            {
                if (_sessions.Count > 0 && s.ExitUtc <= _sessions[^1].ExitUtc) return null;
                Directory.CreateDirectory(Folder);
                if (!File.Exists(StandbyFile)) File.WriteAllText(StandbyFile, StandbySession.CsvHeader + Environment.NewLine);
                File.AppendAllText(StandbyFile, s.ToCsv() + Environment.NewLine);
                _sessions.Add(s);
            }
            return s;
        }

        // ---------- Report (tray, CLI) ----------

        /// <summary>For table cells: "11h 16m", "47m".</summary>
        public static string ShortDuration(TimeSpan t) =>
            t.TotalMinutes < 1 ? "0m" : t.TotalHours < 1 ? $"{(int)t.TotalMinutes}m" : $"{(int)t.TotalHours}h {t.Minutes:00}m";

        public static string Duration(TimeSpan t) =>
            t.TotalMinutes < 1 ? "0 min" : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min" : $"{(int)t.TotalHours} h {t.Minutes:00} min";

        /// <summary>Tray tooltip line: "On battery 2 h 14 min · 6.1 W avg". Called on every mouse move, so it only uses the cached session.</summary>
        public static string? TrayLine()
        {
            var (s, last) = (_session, _last);
            if (s?.OnBatterySince is null || last is null || last.OnAC || last.Kind == BatteryEventKind.Stop) return null;
            var time = s.OnBattery + (DateTime.UtcNow - last.Time);
            return $"On battery {Duration(time)}" + (s.AverageW is double w ? $" · {w:0.0} W avg" : "");
        }

        /// <summary>The text for --battery. ASCII only: Windows PowerShell decodes CLI output with the OEM code page.</summary>
        public static string Report(IReadOnlyList<BatteryEvent> logged, BatteryArchive archive, BatteryEvent? live, BatteryInfo? info, DateTime nowUtc,
            IReadOnlyList<StandbySession>? sessions = null)
        {
            var sb = new StringBuilder();
            var events = live is null ? logged : logged.Append(live).ToList();
            if (events.Count == 0) return "battery log  empty (it fills in as the battery level changes)";
            string Clock(DateTime utc)
            {
                var local = utc.ToLocalTime();
                return local.Date == nowUtc.ToLocalTime().Date ? local.ToString("HH:mm", CultureInfo.CurrentCulture)
                    : local.ToString("ddd d MMM HH:mm", CultureInfo.CurrentCulture);
            }

            var s = BatteryHistory.Session(logged, live);
            bool onAC = events[^1].OnAC;
            if (s.OnBatterySince is DateTime since)
            {
                sb.AppendLine($"{(onAC ? "last on batt" : "on battery"),-13}{Duration(s.OnBattery)} since {Clock(since)} (screen off {Duration(s.ScreenOff)})");
                string used = s.StartPercent is int p ? $"{p - (onAC ? events.Last(e => !e.OnAC).Percent : events[^1].Percent)}% | " : "";
                sb.AppendLine($"{"used",-13}{used}{s.UsedWh:0.0} Wh" + (s.AverageW is double w ? $" | {w:0.0} W screen on" : "") +
                              (s.ScreenOffW is double o ? $" | {o:0.00} W screen off" : ""));
                if (s.HoursLeft is double h) sb.AppendLine($"{"time left",-13}~{Duration(TimeSpan.FromHours(h))} at that average");
            }
            if (s.ChargedTo is int to)
            {
                string took = s.ChargeStarted is DateTime a && s.ChargeEnded is DateTime b ? $" ({(onAC ? "on AC" : "plugged in")} {Duration(b - a)})" : "";
                sb.AppendLine($"{(onAC ? "charging" : "last charge"),-13}{s.ChargedFrom}% -> {to}%" +
                              (s.ChargeEnded is DateTime end ? $", {(onAC ? "now" : "unplugged " + Clock(end))}" : "") + took);
            }

            if (sessions is not null && BatteryHistory.LastStandby(sessions) is { } st)
                sb.AppendLine($"{"last standby",-13}{Clock(st.EnterUtc)} -> {Clock(st.ExitUtc)} | {Standby(st.AwakeSeconds, st.AwakeW, st.AsleepSeconds, st.AsleepW)} | deep idle {st.DeepIdlePercent:0}%");

            var days = BatteryHistory.Days(events, archive, sessions);
            if (days.LastOrDefault(d => d.Start == nowUtc.ToLocalTime().Date) is { } today) sb.AppendLine($"{"today",-13}{Period(today)}");
            if (days.Count > 0)
            {
                var all = days.Aggregate((x, y) => x.Merge(y));
                sb.AppendLine($"{$"last {days.Count} days",-13}{Period(all)}");
                if (all.AwakeOffSeconds + all.AsleepSeconds > 0)
                    sb.AppendLine($"{"  standby",-13}{Standby(all.AwakeOffSeconds, all.AwakeOffW, all.AsleepSeconds, all.AsleepW)}");
            }
            foreach (var m in BatteryHistory.Months(events, archive, sessions).Where(m => m.Start < BatteryHistory.MonthOf(nowUtc.ToLocalTime())).TakeLast(3))
                sb.AppendLine($"{m.Start.ToString("MMM yyyy", CultureInfo.CurrentCulture),-13}{Period(m)}");
            if (info is { Present: true })
            {
                double designWh = info.DesignMah * (double)info.DesignVoltageMv / 1e6;
                sb.AppendLine($"{"health",-13}{info.HealthPercent}% | full {info.FullMah * (double)info.DesignVoltageMv / 1e6:0.0} of {designWh:0.0} Wh");
                var (measured, measuredSince) = BatteryHistory.MeasuredCycles(events, archive, designWh);
                sb.AppendLine($"{"cycles",-13}" + (measuredSince is DateTime from ? $"{measured:0.0} measured since {from.ToString("d MMM", CultureInfo.InvariantCulture)} | " : "") +
                              $"battery says {info.Cycles}");
            }
            return sb.ToString().TrimEnd();
        }

        public static string Standby(double awakeSeconds, double? awakeW, double asleepSeconds, double? asleepW) =>
            $"awake, screen off {Duration(TimeSpan.FromSeconds(awakeSeconds))}" + (awakeW is double a ? $" at {a:0.00} W" : "") +
            $" | asleep {Duration(TimeSpan.FromSeconds(asleepSeconds))}" + (asleepW is double z ? $" at {z:0.00} W" : "");

        public static string Period(BatteryPeriod p) =>
            $"{Duration(TimeSpan.FromSeconds(p.TotalBatterySeconds))} on battery | {p.TotalBatteryWh:0.0} Wh" +
            (p.AverageW is double w ? $" | {w:0.0} W" : "") +
            (p.Unplugs > 0 ? $" | {p.Unplugs} unplug{(p.Unplugs == 1 ? "" : "s")}, charged to {p.AverageChargedTo:0}%" : "");

        // ---------- Windows notifications ----------

        private const uint DEVICE_NOTIFY_CALLBACK = 2;
        private const uint PBT_POWERSETTINGCHANGE = 0x8013;
        private static readonly Guid AcDcPowerSource = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");
        private static readonly Guid BatteryPercentage = new("a7ad8041-b45a-4cae-87a3-eecbb468a9e1");
        private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint DeviceNotifyCallback(IntPtr context, uint type, IntPtr setting);

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceNotifySubscribeParameters
        {
            public IntPtr Callback;
            public IntPtr Context;
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerSettingRegisterNotification(ref Guid settingGuid, uint flags, IntPtr recipient, out IntPtr registrationHandle);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSettingUnregisterNotification(IntPtr registrationHandle);

        private static void Register()
        {
            _ac = PowerNative.IsOnAC() ? 1 : 0;
            _callback = OnPowerSetting;
            _recipient = Marshal.AllocHGlobal(Marshal.SizeOf<DeviceNotifySubscribeParameters>()); // kept for the life of the process
            Marshal.StructureToPtr(new DeviceNotifySubscribeParameters { Callback = Marshal.GetFunctionPointerForDelegate(_callback) }, _recipient, false);
            foreach (var guid in new[] { AcDcPowerSource, BatteryPercentage, ConsoleDisplayState })
            {
                var g = guid;
                uint status = PowerSettingRegisterNotification(ref g, DEVICE_NOTIFY_CALLBACK, _recipient, out var handle);
                if (status == 0) _registrations.Add(handle);
                else Logger.WriteLine($"Battery log: notification {guid} not registered ({status})");
            }
        }

        /// <summary>Runs on a Windows thread: note what changed and wake the logger, nothing else.</summary>
        private static uint OnPowerSetting(IntPtr context, uint type, IntPtr setting)
        {
            if (type != PBT_POWERSETTINGCHANGE || setting == IntPtr.Zero || _stopping) return 0;
            var guid = Marshal.PtrToStructure<Guid>(setting);
            int value = Marshal.ReadInt32(setting, 20); // POWERBROADCAST_SETTING: GUID, DWORD DataLength, data
            int flag;
            if (guid == AcDcPowerSource) { _ac = value == 0 ? 1 : 0; flag = 2; }
            else if (guid == ConsoleDisplayState) { _screen = value == 0 ? 0 : 1; flag = 4; } // 2 = dimmed: still on
            else if (guid == BatteryPercentage) flag = 1;
            else return 0;
            Interlocked.Or(ref _pending, flag);
            _signal.Set();
            return 0;
        }
    }
}
