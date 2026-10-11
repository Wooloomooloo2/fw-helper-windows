using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class BatteryHistoryTests
    {
        // 16 V flat: 1000 mAh = 16 Wh, so 100 mAh/h = 1.6 W
        private const int Mv = 16000;

        private static DateTime L(int day, int hour, int minute = 0, int month = 10, int year = 2026) =>
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

        private static BatteryEvent E(DateTime utc, bool ac, int pct, int mah, bool screen = true, BatteryEventKind kind = BatteryEventKind.Sample) =>
            new(utc, kind, ac, screen, ac && pct < 100, pct, mah, 4000, Mv, 12);

        /// <summary>On AC at 85 %, unplugged at 10:00, 1 h screen on (400 mAh), 1 h screen off (50 mAh), plugged in at 12:00.</summary>
        private static List<BatteryEvent> OneDischarge() => new()
        {
            E(L(5, 8), true, 40, 1600, kind: BatteryEventKind.Start),
            E(L(5, 9), true, 85, 3400),
            E(L(5, 10), false, 85, 3400, kind: BatteryEventKind.Power),
            E(L(5, 11), false, 75, 3000, screen: false, kind: BatteryEventKind.Display),
            E(L(5, 12), true, 74, 2950, kind: BatteryEventKind.Power),
        };

        [Fact]
        public void Csv_round_trips()
        {
            var e = E(L(5, 10, 30), false, 85, 3400, screen: false, kind: BatteryEventKind.Display);
            Assert.Equal(e, BatteryEvent.FromCsv(e.ToCsv()));
            Assert.Null(BatteryEvent.FromCsv("garbage"));
            Assert.Null(BatteryEvent.FromCsv(BatteryEvent.CsvHeader));
        }

        [Fact]
        public void Day_totals_split_screen_on_off_and_ac()
        {
            var day = Assert.Single(BatteryHistory.Days(OneDischarge(), BatteryArchive.Empty));
            Assert.Equal(L(5, 0).ToLocalTime(), day.Start);
            Assert.Equal(3600, day.BatterySeconds, 3);
            Assert.Equal(6.4, day.BatteryWh, 3);
            Assert.Equal(6.4, day.AverageW!.Value, 3);
            Assert.Equal(3600, day.ScreenOffSeconds, 3);
            Assert.Equal(0.8, day.ScreenOffWh, 3);
            Assert.Equal(7200, day.AcSeconds, 3); // 8-10
            Assert.Equal(28.8, day.ChargedWh, 3); // 1600 → 3400 mAh
            Assert.Equal(1, day.Unplugs);
            Assert.Equal(85, day.AverageChargedTo);
        }

        [Fact]
        public void Time_the_app_was_not_running_is_not_counted()
        {
            var events = new List<BatteryEvent>
            {
                E(L(5, 10), false, 80, 3200, kind: BatteryEventKind.Start),
                E(L(5, 11), false, 70, 2800, kind: BatteryEventKind.Stop),
                E(L(5, 15), false, 50, 2000, kind: BatteryEventKind.Start),
                E(L(5, 16), false, 45, 1800),
            };
            var day = Assert.Single(BatteryHistory.Days(events, BatteryArchive.Empty));
            Assert.Equal(7200, day.BatterySeconds, 3);
            Assert.Equal((400 + 200) * 16 / 1000.0, day.BatteryWh, 3);
        }

        [Fact]
        public void A_long_quiet_night_on_AC_is_counted()
        {
            // At the charge limit the percent doesn't move, so nothing is logged for hours
            var events = new List<BatteryEvent> { E(L(5, 8), true, 80, 3200), E(L(5, 23), true, 80, 3200), E(L(6, 7), false, 80, 3200) };
            var days = BatteryHistory.Days(events, BatteryArchive.Empty);
            Assert.Equal(16 * 3600, days[0].AcSeconds, 3);
            Assert.Equal(7 * 3600, days[1].AcSeconds, 3);
        }

        [Fact]
        public void Interval_across_midnight_is_split_by_time()
        {
            var events = new List<BatteryEvent> { E(L(5, 23), false, 80, 3200, screen: false), E(L(6, 1), false, 78, 3100, screen: false) };
            var days = BatteryHistory.Days(events, BatteryArchive.Empty);
            Assert.Equal(2, days.Count);
            Assert.All(days, d => Assert.Equal(3600, d.ScreenOffSeconds, 3));
            Assert.All(days, d => Assert.Equal(0.8, d.ScreenOffWh, 3));
        }

        [Fact]
        public void Session_reports_the_current_discharge_and_the_last_charge()
        {
            var events = OneDischarge().Take(4).ToList(); // still on battery
            var live = E(L(5, 11, 30), false, 74, 2975, screen: false);
            var s = BatteryHistory.Session(events, live);
            Assert.Equal(L(5, 10), s.OnBatterySince);
            Assert.Equal(TimeSpan.FromMinutes(90), s.OnBattery);
            Assert.Equal(TimeSpan.FromMinutes(30), s.ScreenOff);
            Assert.Equal(85, s.StartPercent);
            Assert.Equal((3400 - 2975) * 16 / 1000.0, s.UsedWh, 3);
            Assert.Equal(6.4, s.AverageW!.Value, 3);
            Assert.Equal(2975 * 16 / 1000.0 / 6.4, s.HoursLeft!.Value, 3);
            Assert.Equal(40, s.ChargedFrom);
            Assert.Equal(85, s.ChargedTo);
            Assert.Equal(L(5, 8), s.ChargeStarted);
            Assert.Equal(L(5, 10), s.ChargeEnded);
        }

        [Fact]
        public void Session_on_ac_shows_the_last_discharge_and_the_charge_in_progress()
        {
            var s = BatteryHistory.Session(OneDischarge(), E(L(5, 13), true, 90, 3600));
            Assert.Equal(L(5, 10), s.OnBatterySince);
            Assert.Equal(TimeSpan.FromHours(2), s.OnBattery);
            Assert.Null(s.HoursLeft);
            Assert.Equal(74, s.ChargedFrom);
            Assert.Equal(90, s.ChargedTo);
        }

        [Fact]
        public void A_charge_while_the_app_was_off_starts_a_new_discharge()
        {
            var events = new List<BatteryEvent>
            {
                E(L(5, 10), false, 60, 2400),
                E(L(5, 11), false, 50, 2000, kind: BatteryEventKind.Stop),
                E(L(5, 14), false, 95, 3800, kind: BatteryEventKind.Start),
                E(L(5, 15), false, 90, 3600),
            };
            Assert.Equal(L(5, 14), BatteryHistory.Session(events, null).OnBatterySince);
        }

        [Fact]
        public void A_charger_bounce_is_not_an_unplug()
        {
            // Seen on 2026-10-10: battery for 2 s between two AC readings
            var events = new List<BatteryEvent>
            {
                E(L(5, 8), true, 40, 1600),
                E(L(5, 9), false, 85, 3400, kind: BatteryEventKind.Power),
                E(L(5, 9).AddSeconds(2), true, 85, 3400, kind: BatteryEventKind.Power),
                E(L(5, 10), true, 100, 4000),
                E(L(5, 11), false, 100, 4000, kind: BatteryEventKind.Power),
            };
            var day = Assert.Single(BatteryHistory.Days(events, BatteryArchive.Empty));
            Assert.Equal(1, day.Unplugs);
            Assert.Equal(100, day.AverageChargedTo);
            var s = BatteryHistory.Session(events, E(L(5, 12), false, 90, 3600));
            Assert.Equal(L(5, 11), s.OnBatterySince);
            Assert.Equal(40, s.ChargedFrom);
            Assert.Equal(L(5, 8), s.ChargeStarted);
        }

        [Fact]
        public void Session_without_events_is_empty()
        {
            var s = BatteryHistory.Session(new List<BatteryEvent>(), null);
            Assert.Null(s.OnBatterySince);
            Assert.Null(s.ChargedTo);
        }

        /// <summary>One discharge a day from 1 Aug to 9 Oct: 1 h on battery, 6.4 Wh.</summary>
        private static List<BatteryEvent> Daily(DateTime first, int days)
        {
            var events = new List<BatteryEvent>();
            for (int d = 0; d < days; d++)
            {
                var day = first.AddDays(d);
                events.Add(E(day.AddHours(9), true, 85, 3400));
                events.Add(E(day.AddHours(10), false, 85, 3400));
                events.Add(E(day.AddHours(11), true, 75, 3000));
            }
            return events;
        }

        [Fact]
        public void Roll_moves_old_days_into_months_without_losing_totals()
        {
            var now = L(10, 12);
            var events = Daily(L(1, 0, month: 8), 70);
            var before = BatteryHistory.Months(events, BatteryArchive.Empty);

            var (kept, archive, changed) = BatteryHistory.Roll(events, BatteryArchive.Empty, now);
            Assert.True(changed);
            Assert.True(kept.Count < events.Count);
            Assert.Equal(L(10, 0).ToLocalTime().AddDays(-BatteryHistory.KeepDays).ToUniversalTime(), archive.RolledUntil);
            Assert.All(kept.Skip(1), e => Assert.True(e.Time >= archive.RolledUntil));
            Assert.Equal(BatteryHistory.KeepDays, BatteryHistory.Days(kept, archive).Count); // 10 Sep to 9 Oct

            var after = BatteryHistory.Months(kept, archive);
            Assert.Equal(before.Select(m => m.Start), after.Select(m => m.Start));
            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].BatterySeconds, after[i].BatterySeconds, 3);
                Assert.Equal(before[i].BatteryWh, after[i].BatteryWh, 3);
                Assert.Equal(before[i].Unplugs, after[i].Unplugs);
                Assert.Equal(before[i].AcSeconds, after[i].AcSeconds, 3);
            }

            // Rolling again the same day changes nothing
            var again = BatteryHistory.Roll(kept, archive, now);
            Assert.False(again.changed);
        }

        [Fact]
        public void Roll_moves_months_older_than_a_year_into_years()
        {
            var events = Daily(L(1, 0, month: 6, year: 2025), 20);
            var before = BatteryHistory.Years(events, BatteryArchive.Empty);
            var (kept, archive, _) = BatteryHistory.Roll(events, BatteryArchive.Empty, L(10, 12));
            Assert.Empty(archive.Months);
            var year = Assert.Single(archive.Years);
            Assert.Equal(2025, year.Start.Year);
            Assert.Equal(20 * 3600, year.BatterySeconds, 3);
            Assert.Equal(20, year.Unplugs);

            var after = BatteryHistory.Years(kept, archive);
            Assert.Equal(before.Sum(y => y.BatteryWh), after.Sum(y => y.BatteryWh), 3);
        }

        [Fact]
        public void Measured_cycles_count_all_energy_drawn_including_rolled_up_history()
        {
            var events = Daily(L(1, 0, month: 8), 70); // 70 discharges of 400 mAh = 6.4 Wh
            var (kept, archive, _) = BatteryHistory.Roll(events, BatteryArchive.Empty, L(10, 12));
            var (cycles, since) = BatteryHistory.MeasuredCycles(kept, archive, 64);
            Assert.Equal(7.0, cycles, 3);
            Assert.Equal(new DateTime(2026, 8, 1), since);
            Assert.Equal((0.0, (DateTime?)null), BatteryHistory.MeasuredCycles(new List<BatteryEvent>(), BatteryArchive.Empty, 64));
        }

        [Fact]
        public void Report_describes_the_session()
        {
            var events = OneDischarge().Take(4).ToList();
            string text = BatteryLog.Report(events, BatteryArchive.Empty, E(L(5, 11, 30), false, 74, 2975, screen: false), null, L(5, 11, 30));
            Assert.Contains("on battery", text);
            Assert.Contains("1 h 30 min", text);
            Assert.Contains("40% -> 85%", text);
            Assert.All(text, c => Assert.True(c < 128, $"non-ASCII '{c}'"));
            Assert.Equal("battery log  empty (it fills in as the battery level changes)",
                BatteryLog.Report(new List<BatteryEvent>(), BatteryArchive.Empty, null, null, L(5, 12)));
        }
    }
}
