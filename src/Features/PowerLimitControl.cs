using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    public enum PowerLimitCheck { Ok, Rewrite, GiveUp }

    /// <summary>
    /// Pure re-assert policy (ADR 0010). Firmware re-derives PL1 a few seconds after a platform profile / power mode change
    /// (Linux finding), so a written limit is checked periodically and rewritten a bounded number of times.
    /// </summary>
    public sealed class PowerLimitKeeper
    {
        public const int MaxCorrections = 5;
        public const int ToleranceW = 1;

        public int Corrections { get; private set; }
        public (int pl1, int pl2)? Target { get; private set; }

        public void SetTarget(int pl1, int pl2)
        {
            Target = (pl1, pl2);
            Corrections = 0;
        }

        public void Clear()
        {
            Target = null;
            Corrections = 0;
        }

        public PowerLimitCheck Check((int pl1, int pl2)? actual)
        {
            if (Target is not { } t || actual is not { } a) return PowerLimitCheck.Ok;
            if (Math.Abs(a.pl1 - t.pl1) <= ToleranceW && Math.Abs(a.pl2 - t.pl2) <= ToleranceW) return PowerLimitCheck.Ok;
            if (Corrections >= MaxCorrections) return PowerLimitCheck.GiveUp;
            Corrections++;
            return PowerLimitCheck.Rewrite;
        }
    }

    /// <summary>Applies per-mode PL1/PL2 through <see cref="IntelPowerLimits"/>, keeps them applied, and restores firmware's values.</summary>
    public static class PowerLimitControl
    {
        // Linux measured a ~35 W real PL1 ceiling on this board (40 W setpoint → 35.07 W); below 8 W is untested
        public const int MinPL1 = 8, MaxPL1 = 35;
        // Stock PL2 is 60 W, PL4 75–80 W
        public const int MinPL2 = 15, MaxPL2 = 80;

        private const int CheckIntervalMs = 5000;

        private static readonly PowerLimitKeeper _keeper = new();
        private static readonly object _lock = new();
        private static System.Threading.Timer? _timer;
        private static bool _gaveUp;

        public static string Status { get; private set; } = "";

        /// <summary>Apply the current mode's limits, or restore firmware's if the mode doesn't override them.</summary>
        public static void Apply(int mode)
        {
            lock (_lock)
            {
                if (!Modes.IsPowerLimit(mode))
                {
                    StopKeeping();
                    if (IntelPowerLimits.IsAvailable) IntelPowerLimits.Restore();
                    Status = "";
                    return;
                }

                if (!IntelPowerLimits.Init())
                {
                    Status = IntelPowerLimits.Status;
                    Logger.WriteLine("Power limits unavailable: " + IntelPowerLimits.Status);
                    return;
                }

                int pl1 = Math.Clamp(Modes.GetPL1(mode), MinPL1, MaxPL1);
                int pl2 = Math.Clamp(Modes.GetPL2(mode), Math.Max(MinPL2, pl1), MaxPL2);
                if (!IntelPowerLimits.Set(pl1, pl2))
                {
                    Status = "write failed";
                    return;
                }

                _keeper.SetTarget(pl1, pl2);
                _gaveUp = false;
                Status = $"PL1 {pl1}W · PL2 {pl2}W";
                _timer ??= new System.Threading.Timer(_ => Keep(), null, CheckIntervalMs, CheckIntervalMs);
            }
        }

        /// <summary>Exit path: put firmware's limits back. Safe to call any time, from any thread.</summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                StopKeeping();
                IntelPowerLimits.Restore();
            }
        }

        private static void StopKeeping()
        {
            _timer?.Dispose();
            _timer = null;
            _keeper.Clear();
        }

        private static void Keep()
        {
            lock (_lock)
            {
                if (_keeper.Target is not { } target) return;
                var actual = IntelPowerLimits.Get();
                switch (_keeper.Check(actual))
                {
                    case PowerLimitCheck.Rewrite:
                        Logger.WriteLine($"PL drifted to {actual}, rewriting {target} ({_keeper.Corrections}/{PowerLimitKeeper.MaxCorrections})");
                        IntelPowerLimits.Set(target.pl1, target.pl2);
                        break;
                    case PowerLimitCheck.GiveUp when !_gaveUp:
                        _gaveUp = true;
                        Status = $"firmware keeps overriding ({actual?.pl1}W)";
                        Logger.WriteLine($"PL: firmware keeps setting {actual}, giving up after {PowerLimitKeeper.MaxCorrections} rewrites");
                        break;
                }
            }
        }
    }
}
