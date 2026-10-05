namespace FwHelper.Hardware
{
    /// <summary>
    /// Watts from a 32-bit RAPL energy counter. One instance per counter per consumer, so two windows reading the same MSR
    /// don't shorten each other's intervals. Rules from Linux ADR 0009: handle one wrap, discard implausible values and long gaps
    /// (suspend, multiple wraps).
    /// </summary>
    public sealed class RaplEnergy
    {
        public const double ImplausibleWatts = 200;
        public const double MaxGapSeconds = 60;
        public const double MinGapSeconds = 0.05;

        private uint? _last;
        private long _lastTick;

        /// <summary>Feed a raw counter reading; returns watts since the previous reading, or null (first reading, gap, garbage).</summary>
        public double? Next(uint raw, double joulesPerUnit, long tickMs)
        {
            uint? previous = _last;
            long previousTick = _lastTick;
            double seconds = (tickMs - previousTick) / 1000.0;
            if (previous is not null && seconds < MinGapSeconds) return null; // too soon: keep the older baseline

            _last = raw;
            _lastTick = tickMs;
            return previous is uint p ? Watts(p, raw, seconds, joulesPerUnit) : null;
        }

        public static double? Watts(uint previous, uint now, double seconds, double joulesPerUnit)
        {
            if (seconds < MinGapSeconds || seconds > MaxGapSeconds || joulesPerUnit <= 0) return null;
            double watts = unchecked(now - previous) * joulesPerUnit / seconds; // unsigned subtraction handles one wrap
            return watts is >= 0 and <= ImplausibleWatts ? watts : null;
        }
    }

    /// <summary>Decoded *_PERF_LIMIT_REASONS (Intel SDM; MSR numbers confirmed on the 358H by the Linux sibling).</summary>
    public static class PerfLimitReasons
    {
        public const uint MSR_CORE_PERF_LIMIT_REASONS = 0x64F;
        public const uint MSR_GRAPHICS_PERF_LIMIT_REASONS = 0x6B0;
        public const uint MSR_RING_PERF_LIMIT_REASONS = 0x6B1;

        // Status bits (0–15); the sticky log copies are the same bits + 16. Only bits whose meaning is stable across generations.
        private static readonly (int bit, string name)[] Known =
        {
            (0, "PROCHOT"), (1, "thermal"), (6, "VR thermal"), (8, "EDP"), (10, "PL1"), (11, "PL2"), (12, "max turbo"), (13, "turbo attenuation"),
        };

        /// <summary>Active (status) limits, e.g. "EDP + PL1"; empty when nothing limits; unknown bits as "bit N".</summary>
        public static string Decode(ulong value)
        {
            var names = new List<string>();
            for (int bit = 0; bit < 16; bit++)
            {
                if ((value & (1UL << bit)) == 0) continue;
                var known = Known.FirstOrDefault(k => k.bit == bit);
                names.Add(known.name ?? $"bit {bit}");
            }
            return string.Join(" + ", names); // no commas: it goes into CSV
        }
    }
}
