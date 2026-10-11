using System.Globalization;
using System.Text.Json.Serialization;

namespace FwHelper.Features
{
    public enum BatteryEventKind { Start, Stop, Sample, Power, Display }

    /// <summary>
    /// One battery log line (ADR 0018). Written when something changes (percent, AC, display), never on a fast timer.
    /// The state fields describe the interval that starts at this event and runs to the next one.
    /// </summary>
    public record BatteryEvent(
        DateTime Time, BatteryEventKind Kind, bool OnAC, bool ScreenOn, bool Charging,
        int Percent, int RemainingMah, int FullMah, int VoltageMv, int Cycles)
    {
        /// <summary>Stored energy. The EC reports mAh; voltage varies with charge, so energy is tracked rather than mAh.</summary>
        [JsonIgnore] public double Wh => RemainingMah * (double)VoltageMv / 1_000_000;

        public const string CsvHeader = "time_utc,kind,ac,screen,charging,pct,mah,full_mah,mv,cycles";

        public string ToCsv() => string.Join(",",
            Time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Kind.ToString().ToLowerInvariant(), OnAC ? 1 : 0, ScreenOn ? 1 : 0, Charging ? 1 : 0,
            Percent, RemainingMah, FullMah, VoltageMv, Cycles);

        public static BatteryEvent? FromCsv(string line)
        {
            var f = line.Split(',');
            if (f.Length < 10) return null;
            if (!DateTime.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)) return null;
            if (!Enum.TryParse<BatteryEventKind>(f[1], true, out var kind)) return null;
            int[] n = new int[8];
            for (int i = 0; i < 8; i++)
                if (!int.TryParse(f[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return null;
            return new BatteryEvent(DateTime.SpecifyKind(time, DateTimeKind.Utc), kind, n[0] == 1, n[1] == 1, n[2] == 1, n[3], n[4], n[5], n[6], n[7]);
        }
    }

    /// <summary>
    /// One Modern Standby session on battery, from Windows' own record of it (Kernel-Power event 507, written when standby ends).
    /// Windows splits standby into a screen-off phase, where the system is still awake (it lasts until "Sleep after" runs out, or
    /// until something stops holding it awake), and the sleep phase (deep idle, about 0.33 W on this laptop). Energy is in mWh, as
    /// Windows measured it at the phase boundaries.
    /// </summary>
    public record StandbySession(
        DateTime ExitUtc, double Seconds, double AwakeSeconds, int AwakeMWh, double AsleepSeconds, int AsleepMWh,
        double DeepIdlePercent, int ExitReason, bool Audio)
    {
        [JsonIgnore] public DateTime EnterUtc => ExitUtc.AddSeconds(-Seconds);
        [JsonIgnore] public double? AwakeW => AwakeSeconds >= 60 ? AwakeMWh * 3.6 / AwakeSeconds : null;
        [JsonIgnore] public double? AsleepW => AsleepSeconds >= 60 ? AsleepMWh * 3.6 / AsleepSeconds : null;

        public const string CsvHeader = "exit_utc,seconds,awake_s,awake_mwh,asleep_s,asleep_mwh,deep_idle_pct,exit_reason,audio";

        public string ToCsv() => string.Join(",",
            ExitUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Seconds.ToString("0", CultureInfo.InvariantCulture), AwakeSeconds.ToString("0", CultureInfo.InvariantCulture), AwakeMWh,
            AsleepSeconds.ToString("0", CultureInfo.InvariantCulture), AsleepMWh, DeepIdlePercent.ToString("0.0", CultureInfo.InvariantCulture),
            ExitReason, Audio ? 1 : 0);

        public static StandbySession? FromCsv(string line)
        {
            var f = line.Split(',');
            if (f.Length < 9) return null;
            if (!DateTime.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var exit)) return null;
            var d = new double[8];
            for (int i = 0; i < 8; i++)
                if (!double.TryParse(f[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out d[i])) return null;
            return new StandbySession(DateTime.SpecifyKind(exit, DateTimeKind.Utc), d[0], d[1], (int)d[2], d[3], (int)d[4], d[5], (int)d[6], d[7] == 1);
        }

        /// <summary>
        /// From the named fields of a Kernel-Power 507 event. Null for sessions on AC, and for ones too short to mean anything.
        /// Field names checked on this laptop on 2026-10-11 (Windows 11 26100).
        /// </summary>
        public static StandbySession? FromEvent(DateTime timeUtc, IReadOnlyDictionary<string, string> d)
        {
            long L(string name) => d.TryGetValue(name, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long x) ? x : 0;
            bool B(string name) => d.TryGetValue(name, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (B("PowerStateAc")) return null;
            double seconds = L("DurationInUs") / 1e6;
            if (seconds < 60) return null;
            double awake = L("ScreenOffDurationInUs") / 1e6, asleep = L("SleepDurationInUs") / 1e6;
            int awakeMWh = (int)Math.Max(0, L("ScreenOffEnergyCapacityAtStart") - L("ScreenOffEnergyCapacityAtEnd"));
            int asleepMWh = (int)Math.Max(0, L("SleepEnergyCapacityAtStart") - L("SleepEnergyCapacityAtEnd"));
            double deepIdle = 100.0 * L("DripsResidencyInUs") / 1e6 / seconds;
            return new StandbySession(DateTime.SpecifyKind(timeUtc, DateTimeKind.Utc), seconds, awake, awakeMWh, asleep, asleepMWh,
                Math.Clamp(deepIdle, 0, 100), (int)L("Reason"), B("AudioPlaying"));
        }
    }

    /// <summary>
    /// Totals for a day, month or year. Every field merges by addition (or last/min), so days roll into months and months into years
    /// without going back to the raw events.
    /// </summary>
    public record BatteryPeriod(
        DateTime Start,
        double BatterySeconds = 0, double BatteryWh = 0,
        double ScreenOffSeconds = 0, double ScreenOffWh = 0,
        double AcSeconds = 0, double ChargedWh = 0,
        int Unplugs = 0, int ChargedToSum = 0, int MinPercent = 100,
        int Cycles = 0, int FullMah = 0,
        double AwakeOffSeconds = 0, double AwakeOffWh = 0, double AsleepSeconds = 0, double AsleepWh = 0)
    {
        /// <summary>Time on battery, screen on and off.</summary>
        [JsonIgnore] public double TotalBatterySeconds => BatterySeconds + ScreenOffSeconds;
        [JsonIgnore] public double TotalBatteryWh => BatteryWh + ScreenOffWh;
        /// <summary>Average draw with the screen on.</summary>
        [JsonIgnore] public double? AverageW => BatterySeconds >= 60 ? BatteryWh * 3600 / BatterySeconds : null;
        [JsonIgnore] public double? ScreenOffW => ScreenOffSeconds >= 60 ? ScreenOffWh * 3600 / ScreenOffSeconds : null;
        /// <summary>Standby, from Windows' records: the screen-off phase (still awake) and the sleep phase.</summary>
        [JsonIgnore] public double? AwakeOffW => AwakeOffSeconds >= 60 ? AwakeOffWh * 3600 / AwakeOffSeconds : null;
        [JsonIgnore] public double? AsleepW => AsleepSeconds >= 60 ? AsleepWh * 3600 / AsleepSeconds : null;
        [JsonIgnore] public double? AverageChargedTo => Unplugs > 0 ? (double)ChargedToSum / Unplugs : null;
        [JsonIgnore] public bool IsEmpty => TotalBatterySeconds < 1 && AcSeconds < 1 && Unplugs == 0 && AsleepSeconds + AwakeOffSeconds < 1;

        public BatteryPeriod Merge(BatteryPeriod later) => new(
            Start < later.Start ? Start : later.Start,
            BatterySeconds + later.BatterySeconds, BatteryWh + later.BatteryWh,
            ScreenOffSeconds + later.ScreenOffSeconds, ScreenOffWh + later.ScreenOffWh,
            AcSeconds + later.AcSeconds, ChargedWh + later.ChargedWh,
            Unplugs + later.Unplugs, ChargedToSum + later.ChargedToSum, Math.Min(MinPercent, later.MinPercent),
            later.Cycles > 0 ? later.Cycles : Cycles, later.FullMah > 0 ? later.FullMah : FullMah,
            AwakeOffSeconds + later.AwakeOffSeconds, AwakeOffWh + later.AwakeOffWh, AsleepSeconds + later.AsleepSeconds, AsleepWh + later.AsleepWh);
    }

    /// <summary>Months and years already rolled up from old events. Events before <see cref="RolledUntil"/> are counted here.</summary>
    public record BatteryArchive(DateTime RolledUntil, List<BatteryPeriod> Months, List<BatteryPeriod> Years)
    {
        public static BatteryArchive Empty => new(DateTime.MinValue, new(), new());
    }

    /// <summary>What the current (or last) discharge and charge looked like.</summary>
    public record BatterySession(
        DateTime? OnBatterySince, TimeSpan OnBattery, TimeSpan ScreenOff,
        int? StartPercent, double UsedWh, double? AverageW, double? ScreenOffW, double? HoursLeft,
        DateTime? ChargeStarted, DateTime? ChargeEnded, int? ChargedFrom, int? ChargedTo);

    /// <summary>
    /// Pure battery history maths (ADR 0018): intervals between events, day/month/year totals, roll-up and the current session.
    /// Times are UTC in events; periods are local calendar days, months and years.
    /// </summary>
    public static class BatteryHistory
    {
        public const int KeepDays = 30;
        public const int KeepMonths = 12;
        /// <summary>A battery reading between two AC readings this close together is the charger connector bouncing, not an unplug.</summary>
        public static readonly TimeSpan Bounce = TimeSpan.FromMinutes(1);

        private enum State { Unknown, Battery, ScreenOff, AC }

        private static bool IsBounce(IReadOnlyList<BatteryEvent> e, int i) =>
            i > 0 && i + 1 < e.Count && !e[i].OnAC && e[i - 1].OnAC && e[i + 1].OnAC && e[i + 1].Time - e[i].Time < Bounce;

        /// <summary>
        /// Long quiet intervals are normal (overnight on AC at the charge limit, or in standby) and are counted. Only a gap with
        /// the app not running (a Stop, or a Start after a crash) is unknown.
        /// </summary>
        private static State StateOf(BatteryEvent a, BatteryEvent b)
        {
            if (a.Kind == BatteryEventKind.Stop || b.Kind == BatteryEventKind.Start) return State.Unknown;
            return a.OnAC ? State.AC : a.ScreenOn ? State.Battery : State.ScreenOff;
        }

        /// <summary>
        /// Adds every interval between consecutive events into <paramref name="bucket"/>, split at local period boundaries and
        /// clipped to [from, to). To count time up to now, append a fresh reading as the last event.
        /// </summary>
        private static void Walk(IReadOnlyList<BatteryEvent> events, DateTime fromUtc, DateTime toUtc,
            Func<DateTime, DateTime> periodStart, Func<DateTime, DateTime> nextPeriod, Dictionary<DateTime, BatteryPeriod> bucket)
        {
            BatteryPeriod Get(DateTime utc)
            {
                var key = periodStart(utc.ToLocalTime());
                return bucket.TryGetValue(key, out var p) ? p : bucket[key] = new BatteryPeriod(key);
            }
            void Set(BatteryPeriod p) => bucket[p.Start] = p;

            for (int i = 0; i < events.Count; i++)
            {
                var a = events[i];
                var b = i + 1 < events.Count ? events[i + 1] : null;

                // Point facts: an unplug records the charge it was unplugged at (read at the unplug itself)
                if (i > 0 && !a.OnAC && events[i - 1].OnAC && !IsBounce(events, i) && a.Time >= fromUtc && a.Time < toUtc)
                {
                    var p = Get(a.Time);
                    Set(p with { Unplugs = p.Unplugs + 1, ChargedToSum = p.ChargedToSum + a.Percent });
                }
                if (a.Time >= fromUtc && a.Time < toUtc)
                {
                    var p = Get(a.Time);
                    Set(p with { MinPercent = Math.Min(p.MinPercent, a.Percent), Cycles = a.Cycles > 0 ? a.Cycles : p.Cycles, FullMah = a.FullMah > 0 ? a.FullMah : p.FullMah });
                }

                if (b is null) break;
                var endUtc = b.Time;
                var state = StateOf(a, b);
                if (state == State.Unknown || endUtc <= a.Time) continue;

                double total = (endUtc - a.Time).TotalSeconds;
                double dWh = b.Wh - a.Wh;

                Slice(a.Time, endUtc, fromUtc, toUtc, periodStart, nextPeriod, (start, secs) =>
                {
                    double share = dWh * secs / total;
                    var p = Get(start);
                    Set(state switch
                    {
                        State.Battery => p with { BatterySeconds = p.BatterySeconds + secs, BatteryWh = p.BatteryWh + Math.Max(0, -share) },
                        State.ScreenOff => p with { ScreenOffSeconds = p.ScreenOffSeconds + secs, ScreenOffWh = p.ScreenOffWh + Math.Max(0, -share) },
                        _ => p with { AcSeconds = p.AcSeconds + secs, ChargedWh = p.ChargedWh + Math.Max(0, share) },
                    });
                });
            }
        }

        /// <summary>Calls <paramref name="add"/> for each part of [start, end) clipped to [from, to) and split at local period boundaries.</summary>
        private static void Slice(DateTime startUtc, DateTime endUtc, DateTime fromUtc, DateTime toUtc,
            Func<DateTime, DateTime> periodStart, Func<DateTime, DateTime> nextPeriod, Action<DateTime, double> add)
        {
            var start = startUtc < fromUtc ? fromUtc : startUtc;
            var stop = endUtc > toUtc ? toUtc : endUtc;
            while (start < stop)
            {
                var boundary = nextPeriod(periodStart(start.ToLocalTime())).ToUniversalTime();
                var sliceEnd = boundary < stop ? boundary : stop;
                add(start, (sliceEnd - start).TotalSeconds);
                start = sliceEnd;
            }
        }

        /// <summary>
        /// Adds the standby phases into <paramref name="bucket"/>: the screen-off (awake) phase first, then the sleep phase at the end.
        /// Sessions from before the battery log started are left out, so a day never has standby without its time on battery.
        /// </summary>
        private static void AddStandby(IReadOnlyList<StandbySession> sessions, DateTime fromUtc, DateTime toUtc,
            Func<DateTime, DateTime> periodStart, Func<DateTime, DateTime> nextPeriod, Dictionary<DateTime, BatteryPeriod> bucket)
        {
            BatteryPeriod Get(DateTime utc)
            {
                var key = periodStart(utc.ToLocalTime());
                return bucket.TryGetValue(key, out var p) ? p : bucket[key] = new BatteryPeriod(key);
            }
            foreach (var s in sessions)
            {
                if (s.AwakeSeconds > 0)
                    Slice(s.EnterUtc, s.EnterUtc.AddSeconds(s.AwakeSeconds), fromUtc, toUtc, periodStart, nextPeriod, (start, secs) =>
                    {
                        var p = Get(start);
                        bucket[p.Start] = p with { AwakeOffSeconds = p.AwakeOffSeconds + secs, AwakeOffWh = p.AwakeOffWh + s.AwakeMWh / 1000.0 * secs / s.AwakeSeconds };
                    });
                if (s.AsleepSeconds > 0)
                    Slice(s.ExitUtc.AddSeconds(-s.AsleepSeconds), s.ExitUtc, fromUtc, toUtc, periodStart, nextPeriod, (start, secs) =>
                    {
                        var p = Get(start);
                        bucket[p.Start] = p with { AsleepSeconds = p.AsleepSeconds + secs, AsleepWh = p.AsleepWh + s.AsleepMWh / 1000.0 * secs / s.AsleepSeconds };
                    });
            }
        }

        public static DateTime DayOf(DateTime local) => local.Date;
        public static DateTime MonthOf(DateTime local) => new(local.Year, local.Month, 1);
        public static DateTime YearOf(DateTime local) => new(local.Year, 1, 1);

        private static List<BatteryPeriod> Periods(IReadOnlyList<BatteryEvent> events, IReadOnlyList<StandbySession>? sessions,
            DateTime fromUtc, DateTime toUtc, Func<DateTime, DateTime> periodStart, Func<DateTime, DateTime> nextPeriod)
        {
            var bucket = new Dictionary<DateTime, BatteryPeriod>();
            Walk(events, fromUtc, toUtc, periodStart, nextPeriod, bucket);
            if (sessions is { Count: > 0 } && events.Count > 0)
                AddStandby(sessions, events[0].Time > fromUtc ? events[0].Time : fromUtc, toUtc, periodStart, nextPeriod, bucket);
            return bucket.Values.Where(p => !p.IsEmpty).OrderBy(p => p.Start).ToList();
        }

        // To include the time up to now, pass the logged events with a fresh reading appended

        /// <summary>Daily totals from the raw events (the last <see cref="KeepDays"/> days).</summary>
        public static List<BatteryPeriod> Days(IReadOnlyList<BatteryEvent> events, BatteryArchive archive, IReadOnlyList<StandbySession>? sessions = null) =>
            Periods(events, sessions, archive.RolledUntil, DateTime.MaxValue, DayOf, d => d.AddDays(1));

        /// <summary>Monthly totals: rolled-up months plus whatever the raw events add (the current month is usually split across both).</summary>
        public static List<BatteryPeriod> Months(IReadOnlyList<BatteryEvent> events, BatteryArchive archive, IReadOnlyList<StandbySession>? sessions = null) =>
            Combine(archive.Months, Periods(events, sessions, archive.RolledUntil, DateTime.MaxValue, MonthOf, d => d.AddMonths(1)));

        public static List<BatteryPeriod> Years(IReadOnlyList<BatteryEvent> events, BatteryArchive archive, IReadOnlyList<StandbySession>? sessions = null) =>
            Combine(Combine(archive.Years, archive.Months.Select(m => m with { Start = YearOf(m.Start) })),
                Periods(events, sessions, archive.RolledUntil, DateTime.MaxValue, YearOf, d => d.AddYears(1)));

        private static List<BatteryPeriod> Combine(IEnumerable<BatteryPeriod> a, IEnumerable<BatteryPeriod> b) =>
            a.Concat(b).GroupBy(p => p.Start).Select(g => g.OrderBy(p => p.Cycles).Aggregate((x, y) => x.Merge(y)))
                .OrderBy(p => p.Start).ToList();

        /// <summary>
        /// Moves events older than <see cref="KeepDays"/> local days into month totals, and months older than <see cref="KeepMonths"/>
        /// into year totals. Keeps the last event before the cut so the interval that crosses it can still be measured.
        /// </summary>
        public static (List<BatteryEvent> events, BatteryArchive archive, bool changed) Roll(IReadOnlyList<BatteryEvent> events, BatteryArchive archive, DateTime nowUtc)
        {
            var (kept, _, rolled, changed) = Roll(events, Array.Empty<StandbySession>(), archive, nowUtc);
            return (kept, rolled, changed);
        }

        /// <summary>Same, with standby sessions: those that ended before the cut are rolled up with the events and dropped.</summary>
        public static (List<BatteryEvent> events, List<StandbySession> sessions, BatteryArchive archive, bool changed) Roll(
            IReadOnlyList<BatteryEvent> events, IReadOnlyList<StandbySession> sessions, BatteryArchive archive, DateTime nowUtc)
        {
            var cut = DayOf(nowUtc.ToLocalTime()).AddDays(-KeepDays).ToUniversalTime();
            var months = archive.Months.ToList();
            var years = archive.Years.ToList();
            var rolledUntil = archive.RolledUntil;
            bool changed = false;

            int firstKept = events.Count;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Time >= cut) { firstKept = i; break; }

            if (cut > rolledUntil && firstKept > 0)
            {
                // Intervals from the old events, up to the cut (the next event closes the last one)
                months = Combine(months, Periods(events, sessions, rolledUntil, cut, MonthOf, d => d.AddMonths(1)));
                rolledUntil = cut;
                changed = true;
            }
            var kept = events.Skip(Math.Max(0, firstKept - 1)).ToList();
            if (firstKept > 1) changed = true;

            var monthCut = MonthOf(nowUtc.ToLocalTime()).AddMonths(-KeepMonths);
            var old = months.Where(m => m.Start < monthCut).ToList();
            if (old.Count > 0)
            {
                years = Combine(years, old.Select(m => m with { Start = YearOf(m.Start) }));
                months = months.Where(m => m.Start >= monthCut).ToList();
                changed = true;
            }
            var keptSessions = sessions.Where(x => x.ExitUtc >= rolledUntil).ToList();
            if (keptSessions.Count < sessions.Count) changed = true;
            return (kept, keptSessions, new BatteryArchive(rolledUntil, months, years), changed);
        }

        /// <summary>
        /// The last standby (at least 5 min), with back-to-back sessions joined into one: Windows ends a session and starts the next
        /// at once when it changes policy (for example "Austerity Battery Drain Budget Exceeded"), which is still one night.
        /// </summary>
        public static StandbySession? LastStandby(IReadOnlyList<StandbySession> sessions)
        {
            int end = sessions.Count - 1;
            while (end >= 0 && sessions[end].Seconds < 300) end--;
            if (end < 0) return null;
            int begin = end;
            while (begin > 0 && (sessions[begin].EnterUtc - sessions[begin - 1].ExitUtc).Duration() < TimeSpan.FromMinutes(1)) begin--;
            var run = sessions.Skip(begin).Take(end - begin + 1).ToList();
            double seconds = run.Sum(x => x.Seconds);
            return new StandbySession(run[^1].ExitUtc, seconds, run.Sum(x => x.AwakeSeconds), run.Sum(x => x.AwakeMWh),
                run.Sum(x => x.AsleepSeconds), run.Sum(x => x.AsleepMWh), run.Sum(x => x.DeepIdlePercent * x.Seconds) / seconds,
                run[^1].ExitReason, run.Any(x => x.Audio));
        }

        /// <summary>
        /// Full-cycle equivalents measured from the log: all energy drawn from the battery divided by its design energy. The battery's own
        /// cycle counter can't be relied on: on this laptop it stayed at 1 while Windows logged 4.5 design capacities drained in 8 days.
        /// </summary>
        public static (double cycles, DateTime? since) MeasuredCycles(IReadOnlyList<BatteryEvent> events, BatteryArchive archive, double designWh)
        {
            var years = Years(events, archive);
            if (years.Count == 0 || designWh <= 0) return (0, null);
            var first = archive.Months.Concat(archive.Years).Select(p => (DateTime?)p.Start).Min()
                        ?? events.Select(e => (DateTime?)e.Time.ToLocalTime()).FirstOrDefault();
            return (years.Sum(y => y.TotalBatteryWh) / designWh, first);
        }

        /// <summary>
        /// The current discharge (or, on AC, the last one) and the last charge. <paramref name="live"/> is a fresh reading that is
        /// treated as an event at its own time, so energy and time run up to now.
        /// </summary>
        public static BatterySession Session(IReadOnlyList<BatteryEvent> logged, BatteryEvent? live)
        {
            var events = live is null ? logged.ToList() : logged.Append(live with { Kind = BatteryEventKind.Sample }).ToList();
            if (events.Count == 0) return new(null, TimeSpan.Zero, TimeSpan.Zero, null, 0, null, null, null, null, null, null, null);
            var last = events[^1];

            // Find the run of battery events that ends at the newest event (or the last such run, when on AC)
            int end = events.Count - 1;
            while (end >= 0 && (events[end].OnAC || IsBounce(events, end))) end--;
            int begin = end;
            while (begin > 0 && !events[begin - 1].OnAC && !ChargedAcross(events[begin - 1], events[begin])) begin--;

            DateTime? since = null;
            TimeSpan onBattery = TimeSpan.Zero, screenOff = TimeSpan.Zero;
            double used = 0, offWh = 0;
            int? startPct = null;
            double? avgW = null, offW = null, hoursLeft = null;
            if (end >= 0)
            {
                since = events[begin].Time;
                startPct = events[begin].Percent;
                double onSecs = 0, offSecs = 0, onWh = 0;
                int stop = end < events.Count - 1 ? end + 1 : end; // include the interval that ended at the plug-in
                for (int i = begin; i < stop; i++)
                {
                    var state = StateOf(events[i], events[i + 1]);
                    double secs = (events[i + 1].Time - events[i].Time).TotalSeconds;
                    double wh = Math.Max(0, events[i].Wh - events[i + 1].Wh);
                    if (state == State.Battery) { onSecs += secs; onWh += wh; }
                    else if (state == State.ScreenOff) { offSecs += secs; offWh += wh; }
                }
                onBattery = TimeSpan.FromSeconds(onSecs + offSecs);
                screenOff = TimeSpan.FromSeconds(offSecs);
                used = onWh + offWh;
                avgW = onSecs >= 300 && onWh > 0 ? onWh * 3600 / onSecs : null;
                offW = offSecs >= 300 ? offWh * 3600 / offSecs : null;
                if (!last.OnAC && avgW is double w) hoursLeft = last.Wh / w;
            }

            // Last charge: the AC run before the current discharge, or the current one when on AC
            int acEnd = last.OnAC ? events.Count - 1 : begin - 1;
            while (acEnd >= 0 && !events[acEnd].OnAC) acEnd--;
            DateTime? chargeStart = null, chargeEnd = null;
            int? from = null, to = null;
            if (acEnd >= 0)
            {
                int acBegin = acEnd;
                while (acBegin > 0 && (events[acBegin - 1].OnAC || IsBounce(events, acBegin - 1))) acBegin--;
                chargeStart = events[acBegin].Time;
                from = events[acBegin].Percent; // read at the plug-in
                // Charged to: the highest reading on AC, reached when the percent last went up
                int best = acBegin;
                for (int i = acBegin; i <= acEnd; i++) if (events[i].Percent > events[best].Percent) best = i;
                to = events[best].Percent;
                chargeEnd = acEnd + 1 < events.Count ? events[acEnd + 1].Time : events[best].Time;
            }
            return new(since, onBattery, screenOff, startPct, used, avgW, offW, hoursLeft, chargeStart, chargeEnd, from, to);
        }

        /// <summary>The app was off and the battery went up meanwhile: it was charged, so a new discharge started.</summary>
        private static bool ChargedAcross(BatteryEvent before, BatteryEvent after) =>
            after.Kind == BatteryEventKind.Start && after.Percent > before.Percent + 1;
    }
}
