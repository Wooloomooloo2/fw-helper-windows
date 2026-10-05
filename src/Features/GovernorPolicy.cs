namespace FwHelper.Features
{
    public readonly record struct GovernorDecision(int CapMHz, string Reason)
    {
        /// <summary>0 = no cap.</summary>
        public bool Capped => CapMHz > 0;
    }

    /// <summary>
    /// Pure power/temperature governor (ADR 0016), one call per second. The actuator is the CPU frequency cap; the inputs are package
    /// watts (smoothed, so the target behaves like a sustained limit) and PECI temperature (raw, with a deadband).
    /// Lowers the cap in proportion to how far over target it is; raises it slowly when comfortably under; removes it entirely
    /// once it climbs back to the CPU's maximum.
    /// </summary>
    public sealed class GovernorPolicy
    {
        public const int MinMHz = 800;
        public const int StepMHz = 100;
        /// <summary>Power smoothing time constant (s): the target is a sustained limit, short boosts are allowed.</summary>
        public const double PowerTauSeconds = 5;
        /// <summary>°C over the cap that counts as "100 % over".</summary>
        public const double TempScaleC = 5;
        private const double ActAbove = 0.02, RaiseBelow = -0.08;
        private const double MaxCut = 0.25, MinCut = 0.03, CutGain = 0.6, RaiseFactor = 1.04;

        private readonly int _maxMHz;
        private double? _ema;
        private double _freq;       // current cap; == _maxMHz means "no cap"

        public GovernorPolicy(int maxMHz)
        {
            _maxMHz = Math.Max(MinMHz + StepMHz, maxMHz);
            _freq = _maxMHz;
        }

        public double? SmoothedWatts => _ema;

        public void Reset()
        {
            _ema = null;
            _freq = _maxMHz;
        }

        /// <param name="powerTargetW">Sustained package watts to hold, or null for none.</param>
        /// <param name="tempCapC">PECI temperature to hold, or null for none.</param>
        public GovernorDecision Next(double? packageW, int? cpuTemp, int? powerTargetW, int? tempCapC)
        {
            if (packageW is double w) _ema = _ema is double e ? e + (w - e) * (1 - Math.Exp(-1 / PowerTauSeconds)) : w;

            if (powerTargetW is null && tempCapC is null) { Reset(); return new GovernorDecision(0, "off"); }

            // Steer on the average, but only while readings are actually arriving (never on a stale average)
            double power = powerTargetW is int t && t > 0 && packageW is not null && _ema is double avg ? (avg - t) / t : double.NegativeInfinity;
            double temp = tempCapC is int c && cpuTemp is int now ? (now - c) / TempScaleC : double.NegativeInfinity;

            // Blind: no reading for any active target. Don't hold a cap we can't justify.
            if (double.IsNegativeInfinity(power) && double.IsNegativeInfinity(temp))
            {
                _freq = _maxMHz;
                return new GovernorDecision(0, "no reading");
            }

            double pressure = Math.Max(power, temp);
            string why = temp >= power ? $"temp {cpuTemp}°C / {tempCapC}°C" : $"power {_ema:0.0}W / {powerTargetW}W";

            if (pressure > ActAbove)
                _freq *= 1 - Math.Clamp(CutGain * pressure, MinCut, MaxCut);
            else if (pressure < RaiseBelow)
                _freq *= RaiseFactor;

            _freq = Math.Clamp(_freq, MinMHz, _maxMHz);
            int cap = (int)Math.Round(_freq / StepMHz) * StepMHz;
            if (cap >= _maxMHz) return new GovernorDecision(0, "under target");
            return new GovernorDecision(cap, why);
        }
    }
}
