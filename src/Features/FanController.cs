namespace FwHelper.Features
{
    public enum FanAction
    {
        /// <summary>Hold <see cref="FanDecision.Duty"/> (write it if it changed).</summary>
        Duty,
        /// <summary>Hand the fan back to the EC.</summary>
        Release,
    }

    public readonly record struct FanDecision(FanAction Action, int Duty, string Reason);

    /// <summary>
    /// Pure fan curve decision logic, one call per tick. No I/O, so every safety rule is unit-testable.
    /// Rules and numbers follow ADR 0005 and the Linux sibling's ADR 0006 / 0011 (see docs/hardware-baseline.md).
    /// </summary>
    public sealed class FanController
    {
        /// <summary>Falling temperatures are followed this many °C late; rising ones immediately. PECI dithers ±1 °C.</summary>
        public const int HysteresisC = 2;
        /// <summary>Max duty decrease per tick (% at 1 Hz), avoids audible pulsing. Increases are immediate.</summary>
        public const int RampDownPerTick = 2;
        /// <summary>The fan stalls below about 8–12 % (duty 20–30/255 on Linux): never request 1..11.</summary>
        public const int MinSpinDuty = 12;

        /// <summary>CPU at or above this: full duty, whatever the curve says.</summary>
        public const int CpuFullDutyC = 95;
        /// <summary>CPU at or above this (Tjmax): hand over to the EC as a last resort.</summary>
        public const int CpuReleaseC = 100;
        /// <summary>After a ceiling release, retake only below this.</summary>
        public const int CpuRetakeC = CpuFullDutyC;

        /// <summary>battery_temp crit is 49.85 °C and nothing self-protects it. Guard ramps from here (crit − 8)...</summary>
        public const int BatteryGuardStartC = 42;
        /// <summary>...to 100 % here (crit − 2). Unlike Linux we hold 100 % rather than release: the EC curve follows CPU temp
        /// and would stop the fan with an idle CPU and a hot, charging battery.</summary>
        public const int BatteryGuardFullC = 48;

        /// <summary>Consecutive missing CPU readings before releasing to the EC.</summary>
        public const int SensorFailuresToRelease = 3;

        private FanCurve _curve;
        private int? _cpuHeld, _batteryHeld;
        private int _lastDuty = -1;
        private int _failures;
        private bool _ceilingReleased;

        public FanController(FanCurve curve) => _curve = curve.Clone();

        public FanCurve Curve
        {
            get => _curve;
            set => _curve = value.Clone();
        }

        /// <summary>Forget the last written duty (after the fan was released elsewhere), so the next decision is written again.</summary>
        public void Reset()
        {
            _lastDuty = -1;
            _cpuHeld = _batteryHeld = null;
            _failures = 0;
            _ceilingReleased = false;
        }

        /// <param name="floor">Minimum duty from <see cref="FirmwareFloor"/> ("never quieter than the EC"), 0 for none.</param>
        public FanDecision Next(int? cpu, int? battery, int floor = 0)
        {
            // No CPU temperature, no manual fan
            if (cpu is null)
            {
                if (_lastDuty < 0 || ++_failures >= SensorFailuresToRelease)
                    return Release("no CPU temperature");
                return new FanDecision(FanAction.Duty, _lastDuty, "holding, CPU temperature missing");
            }
            _failures = 0;

            // Last resort: at Tjmax firmware thermal control takes over, until we're back below the full-duty line
            if (cpu >= CpuReleaseC) _ceilingReleased = true;
            else if (cpu < CpuRetakeC) _ceilingReleased = false;
            if (_ceilingReleased) return Release($"CPU ≥{CpuReleaseC}°C");

            _cpuHeld = Hold(_cpuHeld, cpu.Value);
            _batteryHeld = battery is null ? null : Hold(_batteryHeld, battery.Value);

            int request = _curve.DutyAt(_cpuHeld.Value);
            string reason = "curve";

            if (_cpuHeld >= CpuFullDutyC)
            {
                request = 100;
                reason = $"CPU ≥{CpuFullDutyC}°C";
            }

            int guard = BatteryGuardDuty(_batteryHeld);
            if (guard > request)
            {
                request = guard;
                reason = "battery guard";
            }

            if (floor > request)
            {
                request = Math.Min(floor, 100);
                reason = "EC floor";
            }

            int duty = request;
            if (_lastDuty >= 0 && request < _lastDuty)
                duty = Math.Max(request, _lastDuty - RampDownPerTick);

            if (duty > 0 && duty < MinSpinDuty)
                duty = request == 0 ? 0 : MinSpinDuty;

            _lastDuty = duty;
            return new FanDecision(FanAction.Duty, duty, reason);
        }

        /// <summary>Minimum duty the battery temperature demands (0 = no constraint).</summary>
        public static int BatteryGuardDuty(int? battery)
        {
            if (battery is null || battery < BatteryGuardStartC) return 0;
            if (battery >= BatteryGuardFullC) return 100;
            double f = (double)(battery.Value - BatteryGuardStartC) / (BatteryGuardFullC - BatteryGuardStartC);
            return (int)Math.Round(MinSpinDuty + f * (100 - MinSpinDuty));
        }

        /// <summary>Rise immediately, fall only once the reading is more than <see cref="HysteresisC"/> below the held value.</summary>
        private static int Hold(int? held, int current) =>
            held is null ? current : Math.Clamp(held.Value, current, current + HysteresisC);

        private FanDecision Release(string reason)
        {
            _lastDuty = -1;
            _cpuHeld = _batteryHeld = null;
            return new FanDecision(FanAction.Release, -1, reason);
        }
    }
}
