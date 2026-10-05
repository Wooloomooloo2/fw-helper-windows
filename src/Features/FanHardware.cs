using FwHelper.Hardware;

namespace FwHelper.Features
{
    /// <summary>Everything the fan loop needs from the EC, behind an interface so the loop can run against a fake in tests.</summary>
    public interface IFanHardware
    {
        List<TempSensor> GetTemperatures();
        bool SetFanDuty(int percent);
        bool SetFanAuto();
        /// <summary>Current duty, including the EC's own choice in auto mode.</summary>
        int? GetFanDuty();
        /// <summary>Hardware truth of who owns the fan.</summary>
        bool? IsFanAuto();
        /// <summary>EC fan ramp per sensor index, °C: fan_off → fan_max.</summary>
        IReadOnlyDictionary<int, (int off, int max)> GetFanRamps();
    }

    public sealed class EcFanHardware : IFanHardware
    {
        private IReadOnlyDictionary<int, (int off, int max)>? _ramps;

        public List<TempSensor> GetTemperatures() => FrameworkEc.GetTemperatures();
        public bool SetFanDuty(int percent) => FrameworkEc.SetFanDuty(percent);
        public bool SetFanAuto() => FrameworkEc.SetFanAuto();
        public int? GetFanDuty() => FrameworkEc.GetFanDuty();
        public bool? IsFanAuto() => FrameworkEc.IsFanAuto();

        public IReadOnlyDictionary<int, (int off, int max)> GetFanRamps()
        {
            if (_ramps is not null) return _ramps;
            var ramps = new Dictionary<int, (int, int)>();
            foreach (var t in FrameworkEc.GetTemperatures())
                if (FrameworkEc.GetThermalConfig(t.Index) is { FanOff: int off, FanMax: int max } && max > off)
                    ramps[t.Index] = (off, max);
            if (ramps.Count > 0) _ramps = ramps; // firmware constants: read once
            return ramps;
        }
    }
}
