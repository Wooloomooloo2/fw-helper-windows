namespace FwHelper.Features
{
    /// <summary>Windows' effective power mode (EFFECTIVE_POWER_MODE from powrprof's effective power mode notifications).</summary>
    public enum EffectiveMode { BatterySaver = 0, BetterBattery = 1, Balanced = 2, HighPerformance = 3, MaxPerformance = 4, GameMode = 5, MixedReality = 6 }

    /// <summary>
    /// What each Windows power mode (overlay index 0–2) does on this laptop, and whether Windows is in the mode the profile asked
    /// for. Pure: the watcher feeds it what Windows reports.
    /// </summary>
    public static class WindowsPowerMode
    {
        /// <summary>The figures below were measured in games on this firmware (hardware-baseline.md → "Firmware 03.02 → 03.04").</summary>
        public const string MeasuredOn = "measured in games on BIOS 03.04, on AC";

        public static readonly string[] Short = { "Efficiency", "Balanced", "Performance" };
        public static readonly string[] Budget = { "about 15 W", "about 25 W", "30–33 W" };

        public static readonly string[] Explain =
        {
            "CPU + GPU share about 15 W sustained. Quietest and coolest. FW-Helper's CPU limits work.",
            "CPU + GPU share about 25 W sustained. Most frames per watt. FW-Helper's CPU limits work.",
            "CPU + GPU share 30–33 W sustained: about 10 % more FPS in games for 5–7 W. Windows ignores FW-Helper's CPU limits.",
        };

        public static bool Valid(int overlay) => overlay is >= 0 and <= 2;

        /// <summary>Put Windows back to the profile's mode? Never during Battery Saver (Windows owns it then) or when unknown.</summary>
        public static bool ShouldResync(bool syncOn, int wanted, int actual, bool batterySaver) =>
            syncOn && !batterySaver && Valid(wanted) && Valid(actual) && actual != wanted;
    }

    /// <param name="Wanted">The profile's Windows power mode (overlay index).</param>
    /// <param name="Actual">The overlay Windows has now, or -1 if unknown.</param>
    public sealed record PowerModeStatus(int Wanted, int Actual, bool BatterySaver, bool GameMode, bool OnAC)
    {
        public bool Mismatch => BatterySaver || (WindowsPowerMode.Valid(Actual) && Actual != Wanted);

        /// <summary>A mismatch FW-Helper can fix with one click (not Battery Saver, which Windows controls).</summary>
        public bool CanFix => Mismatch && !BatterySaver && WindowsPowerMode.Valid(Wanted);

        private static string Name(int overlay) => WindowsPowerMode.Valid(overlay) ? PowerNative.OverlayNames[overlay] : "unknown";

        /// <summary>For the tray, the overlay and the CSV: "Performance", "⚠ Balanced", "⚠ Battery Saver".</summary>
        public string ShortLabel =>
            (Mismatch ? "⚠ " : "") + (BatterySaver ? "Battery Saver" : WindowsPowerMode.Valid(Actual) ? WindowsPowerMode.Short[Actual] : "?");

        /// <summary>The main window's line under the profile buttons.</summary>
        public string Line
        {
            get
            {
                if (BatterySaver)
                    return $"⚠ Windows: Battery Saver is on. {Name(Wanted)} comes back when it turns off";
                if (Mismatch)
                    return $"⚠ Windows: {Name(Actual)}, changed outside FW-Helper. This profile wants {Name(Wanted)}";
                string line = $"Windows: {Name(Actual)}";
                if (WindowsPowerMode.Valid(Actual)) line += OnAC ? $" · CPU + GPU {WindowsPowerMode.Budget[Actual]} sustained" : " · on battery";
                if (GameMode) line += " · Game Mode";
                return line;
            }
        }

        /// <summary>Tooltip: what the mode in effect does.</summary>
        public string Explanation =>
            WindowsPowerMode.Valid(Actual) && !BatterySaver
                ? $"{Name(Actual)}: {WindowsPowerMode.Explain[Actual]}\n({WindowsPowerMode.MeasuredOn})"
                : "Battery Saver limits performance until it turns off.";
    }
}
