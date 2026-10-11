using FwHelper.Features;
using Xunit;

namespace FwHelper.Tests
{
    public class StandbyTests
    {
        private static DateTime L(int day, int hour, int minute = 0, int second = 0) =>
            new DateTime(2026, 10, day, hour, minute, second, DateTimeKind.Local).ToUniversalTime();

        /// <summary>The Kernel-Power 507 fields of the session that ended at 01:25:12 on 2026-10-11 (copied from this laptop).</summary>
        private static Dictionary<string, string> Austerity() => new()
        {
            ["DurationInUs"] = "3819406666", ["DripsResidencyInUs"] = "217702844", ["Reason"] = "55", ["AudioPlaying"] = "true",
            ["PowerStateAc"] = "false", ["ScreenOffEnergyCapacityAtStart"] = "42885", ["ScreenOffEnergyCapacityAtEnd"] = "41164",
            ["ScreenOffDurationInUs"] = "3579017820", ["SleepEnergyCapacityAtStart"] = "41164", ["SleepEnergyCapacityAtEnd"] = "41133",
            ["SleepDurationInUs"] = "240387150",
        };

        [Fact]
        public void Parses_a_real_session()
        {
            var s = StandbySession.FromEvent(L(11, 1, 25, 12), Austerity())!;
            Assert.Equal(3819.4, s.Seconds, 1);
            Assert.Equal(L(11, 0, 21, 33), s.EnterUtc, TimeSpan.FromSeconds(1));
            Assert.Equal(1721, s.AwakeMWh);
            Assert.Equal(1.73, s.AwakeW!.Value, 2);
            Assert.Equal(31, s.AsleepMWh);
            Assert.Equal(5.7, s.DeepIdlePercent, 1);
            Assert.Equal(55, s.ExitReason);
            Assert.True(s.Audio);
            Assert.Equal(s, StandbySession.FromCsv(s.ToCsv())! with { Seconds = s.Seconds, AwakeSeconds = s.AwakeSeconds, AsleepSeconds = s.AsleepSeconds, DeepIdlePercent = s.DeepIdlePercent });
        }

        [Fact]
        public void Skips_sessions_on_AC_and_very_short_ones()
        {
            var ac = Austerity();
            ac["PowerStateAc"] = "true";
            Assert.Null(StandbySession.FromEvent(L(11, 1), ac));
            var shortOne = Austerity();
            shortOne["DurationInUs"] = "30000000";
            Assert.Null(StandbySession.FromEvent(L(11, 1), shortOne));
        }

        [Fact]
        public void Phases_go_into_the_days_they_happened_in()
        {
            // Screen off 23:30 to 00:30 (awake, 1.5 Wh), then asleep 00:30 to 08:30 (2.6 Wh)
            var s = new StandbySession(L(11, 8, 30), 9 * 3600, 3600, 1500, 8 * 3600, 2600, 99, 15, false);
            var events = new List<BatteryEvent> { new(L(10, 12), BatteryEventKind.Start, false, true, false, 80, 3000, 4600, 16000, 1) };
            var days = BatteryHistory.Days(events, BatteryArchive.Empty, new[] { s });
            var d10 = days.Single(d => d.Start == new DateTime(2026, 10, 10));
            var d11 = days.Single(d => d.Start == new DateTime(2026, 10, 11));
            Assert.Equal(1800, d10.AwakeOffSeconds, 1);
            Assert.Equal(0.75, d10.AwakeOffWh, 3);
            Assert.Equal(1800, d11.AwakeOffSeconds, 1);
            Assert.Equal(8 * 3600, d11.AsleepSeconds, 1);
            Assert.Equal(0.325, d11.AsleepW!.Value, 3);
        }

        [Fact]
        public void Sessions_from_before_the_log_started_are_left_out()
        {
            var old = new StandbySession(L(5, 8), 3600, 0, 0, 3600, 330, 99, 15, false);
            var events = new List<BatteryEvent> { new(L(10, 12), BatteryEventKind.Start, false, true, false, 80, 3000, 4600, 16000, 1) };
            Assert.DoesNotContain(BatteryHistory.Days(events, BatteryArchive.Empty, new[] { old }), d => d.AsleepSeconds > 0);
        }

        [Fact]
        public void Last_standby_joins_back_to_back_sessions()
        {
            var first = StandbySession.FromEvent(L(11, 1, 25, 12), Austerity())!;
            var second = new StandbySession(L(11, 9, 15, 18), (L(11, 9, 15, 18) - L(11, 1, 25, 12)).TotalSeconds, 0, 0,
                (L(11, 9, 15, 18) - L(11, 1, 25, 12)).TotalSeconds, 2580, 99.3, 15, false);
            var tiny = new StandbySession(L(11, 9, 30), 120, 120, 10, 0, 0, 0, 15, false);
            var last = BatteryHistory.LastStandby(new[] { first, second, tiny })!;
            Assert.Equal(first.EnterUtc, last.EnterUtc, TimeSpan.FromSeconds(1));
            Assert.Equal(second.ExitUtc, last.ExitUtc);
            Assert.Equal(1721, last.AwakeMWh);
            Assert.Equal(2611, last.AsleepMWh);
            Assert.InRange(last.DeepIdlePercent, 87, 89); // time-weighted: 5.7 % for 64 min, 99.3 % for 7 h 50 min
            Assert.Null(BatteryHistory.LastStandby(new[] { tiny }));
        }

        [Fact]
        public void Roll_folds_old_sessions_into_months()
        {
            var events = new List<BatteryEvent>
            {
                new(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(), BatteryEventKind.Start, false, true, false, 80, 3000, 4600, 16000, 1),
                new(L(10, 12), BatteryEventKind.Sample, false, true, false, 79, 2990, 4600, 16000, 1),
            };
            var oldSession = new StandbySession(new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Local).ToUniversalTime(), 3600, 0, 0, 3600, 330, 99, 15, false);
            var newSession = new StandbySession(L(10, 8), 3600, 0, 0, 3600, 330, 99, 15, false);
            var (_, kept, archive, changed) = BatteryHistory.Roll(events, new[] { oldSession, newSession }, BatteryArchive.Empty, L(11, 12));
            Assert.True(changed);
            Assert.Equal(new[] { newSession }, kept);
            Assert.Equal(3600, archive.Months.Single(m => m.Start == new DateTime(2026, 8, 1)).AsleepSeconds, 1);
        }
    }
}
