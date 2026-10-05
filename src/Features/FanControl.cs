using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// The app's single <see cref="FanLoop"/> on the real EC, with the learned firmware floor persisted in config.
    /// Every exit path must call <see cref="Stop"/> (ADR 0005).
    /// </summary>
    public static class FanControl
    {
        private const string FloorKey = "ec_floor", FloorFirmwareKey = "ec_floor_firmware";

        private static readonly Lazy<FanLoop> _loop = new(Create);

        private static FanLoop Create()
        {
            // A learned floor belongs to one EC firmware: start over after an EC update
            string firmware = FrameworkEc.GetVersion() ?? "";
            string? saved = AppConfig.GetString(FloorFirmwareKey) == firmware ? AppConfig.GetString(FloorKey) : null;
            var floor = FirmwareFloor.Parse(saved);
            Logger.WriteLine($"EC floor: {floor.LearnedBuckets} learned points");

            return new FanLoop(new EcFanHardware(), new FanLoopOptions
            {
                Floor = floor,
                FloorEnabled = IsFloorEnabled,
                SaveFloor = s =>
                {
                    AppConfig.Set(FloorKey, s);
                    AppConfig.Set(FloorFirmwareKey, firmware);
                },
            });
        }

        public static FanLoop Loop => _loop.Value;

        public static bool IsCustomActive => _loop.IsValueCreated && _loop.Value.IsCustomActive;
        public static int LastDuty => _loop.IsValueCreated ? _loop.Value.LastDuty : -1;
        /// <summary>Why the fan is doing what it does right now, for the UI ("curve", "battery guard", "EC floor", "EC: CPU ≥100°C"...).</summary>
        public static string Status => _loop.IsValueCreated ? _loop.Value.Status : "EC auto";

        /// <summary>"Never quieter than the EC's own fan control" (ADR 0013), on by default.</summary>
        public static bool IsFloorEnabled() => AppConfig.Get("fan_floor", 1) == 1;
        public static void SetFloorEnabled(bool on) => AppConfig.Set("fan_floor", on ? 1 : 0);

        /// <summary>Start the loop threads at app start so the floor is learned even when no curve is active.</summary>
        public static void Init() => Loop.Run();

        public static void Start(FanCurve curve) => Loop.Start(curve);

        /// <summary>Stop the custom curve and hand fans back to the EC. Safe before <see cref="Init"/>.</summary>
        public static void Stop()
        {
            if (_loop.IsValueCreated) _loop.Value.Stop();
            else FrameworkEc.SetFanAuto();
        }

        internal static void StallLoopForTest(TimeSpan duration) => Loop.StallLoopForTest(duration);
    }
}
