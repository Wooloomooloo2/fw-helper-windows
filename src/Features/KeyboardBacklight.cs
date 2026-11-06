namespace FwHelper.Features
{
    /// <summary>
    /// The Framework EC's keyboard-backlight "Auto" mode, as the EC implements it (FrameworkComputer/EmbeddedController,
    /// branch fwk-sakura-20260429: zephyr/program/framework/src/led.c <c>auto_als_led_brightness</c>,
    /// keyboard_customization_13.c, include/led.h). Pure, no I/O.
    /// <list type="bullet">
    /// <item>Fn+Space cycles Off → Low (20) → Medium (75) → High (100) → Auto → Off. Only Fn+Space enters or leaves Auto; no
    /// host command reads or changes it.</item>
    /// <item>In Auto the EC follows the ambient light sensor: above 5 lux (<c>KB_BL_THRESHOLD</c>) the backlight is off, at or
    /// below it 5 % (<c>ULT_LOW</c>). It re-reads the level on every tick, so a host write is overridden within about 300 ms.</item>
    /// <item>There is no idle timeout in the EC.</item>
    /// </list>
    /// </summary>
    public static class KeyboardBacklight
    {
        public const int AutoThresholdLux = 5;
        public const int AutoDarkPercent = 5;

        /// <summary>The level the EC picks in Auto for this light reading.</summary>
        public static int AutoLevel(int lux) => lux > AutoThresholdLux ? 0 : AutoDarkPercent;

        /// <summary>
        /// Read back a moment after writing <paramref name="commanded"/>: true = Auto overrode it, false = the write held,
        /// null = can't tell (no reading, the level written is also what Auto would pick, or the level changed some other way,
        /// such as Fn+Space).
        /// </summary>
        public static bool? WriteOverridden(int commanded, int? actual, int? lux)
        {
            if (actual is not int a) return null;
            int? auto = lux is int l ? AutoLevel(l) : null;
            if (a == commanded) return auto == commanded ? null : false;
            return auto == a ? true : null;
        }

        /// <summary>A level Auto never sets: seeing it means Fn+Space (or a write that held) took the EC out of Auto.</summary>
        public static bool RulesOutAuto(int actual) => actual != 0 && actual != AutoDarkPercent;

        /// <summary>The status line under the slider.</summary>
        public static string Describe(int? actual, int? lux, bool auto)
        {
            if (actual is not int a) return "Backlight: no reading";
            string light = lux is int l ? $"{l} lux" : "light n/a";
            if (auto)
                return $"EC Auto: {(a > 0 ? $"on {a}%" : "off")} at {light} · Fn+Space exits";
            return $"{(a > 0 ? $"On · {a}%" : "Off")} · {light}";
        }
    }
}
