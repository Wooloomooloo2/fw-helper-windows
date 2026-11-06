namespace FwHelper.Features
{
    public readonly record struct GovernorDecision(int CapMHz, string Reason)
    {
        /// <summary>0 = no cap.</summary>
        public bool Capped => CapMHz > 0;
    }

    /// <summary>What the governor holds, per tick. Every field is optional (null = not set).</summary>
    /// <param name="PackageW">Sustained package watts.</param>
    /// <param name="TempC">PECI temperature.</param>
    /// <param name="CpuW">Sustained CPU-cores watts (RAPL "cores"): leaves the rest of the package budget to the GPU.</param>
    /// <param name="MaxMHz">Fixed CPU frequency ceiling.</param>
    public readonly record struct GovernorTargets(int? PackageW, int? TempC, int? CpuW = null, int? MaxMHz = null)
    {
        public bool Any => PackageW is not null || TempC is not null || CpuW is not null || MaxMHz is not null;
    }

    /// <summary>
    /// Pure power/temperature governor (ADR 0016), one call per second. The actuator is the CPU frequency cap; the inputs are package
    /// and CPU-cores watts (smoothed, so targets behave like sustained limits) and PECI temperature (raw, with a deadband).
    /// Lowers the cap in proportion to how far over target it is; raises it slowly when comfortably under; removes it entirely
    /// once it climbs back to the CPU's maximum. A fixed ceiling (<c>maxMHz</c>) bounds the result and is never "blind".
    /// <para>
    /// Anti-windup: Windows ignores the plan's frequency cap under the Best Performance overlay (measured 2026-11-06). When the
    /// fastest core stays well above the cap for <see cref="NotBindingTicks"/> ticks, the cap is released and cutting pauses for
    /// <see cref="NotBindingHoldTicks"/> ticks, instead of winding down to the floor.
    /// </para>
    /// </summary>
    public sealed class GovernorPolicy
    {
        public const int MinMHz = 800;
        public const int StepMHz = 100;
        /// <summary>Power smoothing time constant (s): the target is a sustained limit, short boosts are allowed.</summary>
        public const double PowerTauSeconds = 5;
        /// <summary>°C over the cap that counts as "100 % over".</summary>
        public const double TempScaleC = 5;
        public const int NotBindingTicks = 3, NotBindingHoldTicks = 30;
        public const string NotBinding = "cap not binding";
        private const double ActAbove = 0.02, RaiseBelow = -0.08;
        private const double MaxCut = 0.25, MinCut = 0.03, CutGain = 0.6, RaiseFactor = 1.04;

        private readonly int _maxMHz;
        private double? _ema, _cpuEma;
        private double _freq;       // current dynamic cap; == ceiling means "no dynamic cap"
        private int _lastCap;       // cap returned last tick (0 = none)
        private int _overCap, _hold;

        public GovernorPolicy(int maxMHz)
        {
            _maxMHz = Math.Max(MinMHz + StepMHz, maxMHz);
            _freq = _maxMHz;
        }

        public double? SmoothedWatts => _ema;
        public double? SmoothedCpuWatts => _cpuEma;

        /// <summary>Start over: no cap, no averages, no "not binding" hold. Called on stop, profile switch and settings change.</summary>
        public void Reset()
        {
            _ema = _cpuEma = null;
            _freq = _maxMHz;
            _lastCap = _overCap = _hold = 0;
        }

        /// <summary>True when the fastest core runs well above the cap: the cap isn't being applied.</summary>
        public static bool IsAboveCap(int cap, double? fastestCoreMHz) =>
            cap > 0 && fastestCoreMHz is double f && f > cap * 1.15 + 150;

        /// <param name="powerTargetW">Sustained package watts to hold, or null for none.</param>
        /// <param name="tempCapC">PECI temperature to hold, or null for none.</param>
        /// <param name="cpuW">CPU-cores watts this second.</param>
        /// <param name="cpuTargetW">Sustained CPU-cores watts to hold, or null for none.</param>
        /// <param name="maxMHz">Fixed frequency ceiling, or null for none.</param>
        /// <param name="fastestCoreMHz">Highest per-core effective clock this second, for the anti-windup check (null = skip it).</param>
        public GovernorDecision Next(double? packageW, int? cpuTemp, int? powerTargetW, int? tempCapC,
            double? cpuW = null, int? cpuTargetW = null, int? maxMHz = null, double? fastestCoreMHz = null)
        {
            var d = Decide(packageW, cpuTemp, powerTargetW, tempCapC, cpuW, cpuTargetW, maxMHz, fastestCoreMHz);
            _lastCap = d.CapMHz;
            return d;
        }

        private GovernorDecision Decide(double? packageW, int? cpuTemp, int? powerTargetW, int? tempCapC,
            double? cpuW, int? cpuTargetW, int? maxMHz, double? fastestCoreMHz)
        {
            _ema = Smooth(_ema, packageW);
            _cpuEma = Smooth(_cpuEma, cpuW);

            int ceiling = maxMHz is int m && m > 0 ? Math.Clamp(m / StepMHz * StepMHz, MinMHz, _maxMHz) : _maxMHz;
            bool fixedCap = ceiling < _maxMHz;
            bool dynamic = powerTargetW is not null || tempCapC is not null || cpuTargetW is not null;
            GovernorDecision AtCeiling(string why) => fixedCap ? new(ceiling, $"max {ceiling} MHz · {why}") : new(0, why);

            // Anti-windup: a cap the CPU isn't following must not keep being cut
            _overCap = IsAboveCap(_lastCap, fastestCoreMHz) ? _overCap + 1 : 0;
            if (_overCap >= NotBindingTicks)
            {
                _overCap = 0;
                _hold = NotBindingHoldTicks;
            }
            if (_hold > 0)
            {
                _hold--;
                _freq = ceiling;
                return fixedCap ? new(ceiling, $"max {ceiling} MHz · {NotBinding}") : new(0, NotBinding);
            }

            if (!dynamic)
            {
                _freq = _maxMHz;
                return fixedCap ? new(ceiling, $"max {ceiling} MHz") : new(0, "off");
            }

            // Steer on the averages, but only while readings are actually arriving (never on a stale average)
            double power = powerTargetW is int t && t > 0 && packageW is not null && _ema is double avg ? (avg - t) / t : double.NegativeInfinity;
            double cores = cpuTargetW is int ct && ct > 0 && cpuW is not null && _cpuEma is double cavg ? (cavg - ct) / ct : double.NegativeInfinity;
            double temp = tempCapC is int c && cpuTemp is int now ? (now - c) / TempScaleC : double.NegativeInfinity;

            // Blind: no reading for any active target. Don't hold a dynamic cap we can't justify; the fixed ceiling still applies.
            if (double.IsNegativeInfinity(power) && double.IsNegativeInfinity(cores) && double.IsNegativeInfinity(temp))
            {
                _freq = ceiling;
                return AtCeiling("no reading");
            }

            double pressure = Math.Max(power, Math.Max(cores, temp));
            string why = pressure == temp ? $"temp {cpuTemp}°C / {tempCapC}°C"
                       : pressure == cores ? $"cpu {_cpuEma:0.0}W / {cpuTargetW}W"
                       : $"power {_ema:0.0}W / {powerTargetW}W";

            if (pressure > ActAbove)
                _freq *= 1 - Math.Clamp(CutGain * pressure, MinCut, MaxCut);
            else if (pressure < RaiseBelow)
            {
                _freq *= RaiseFactor;
                why = "easing off";
            }

            // Never wind up above the ceiling: when a target needs a cut, it starts from the fixed cap, not from max turbo
            _freq = Math.Clamp(_freq, MinMHz, ceiling);
            int cap = (int)Math.Round(_freq / StepMHz) * StepMHz;
            if (cap >= ceiling) return fixedCap ? new(ceiling, $"max {ceiling} MHz") : new(0, "under target");
            return new GovernorDecision(cap, why);
        }

        private static double? Smooth(double? ema, double? w) =>
            w is double v ? ema is double e ? e + (v - e) * (1 - Math.Exp(-1 / PowerTauSeconds)) : v : ema;
    }
}
