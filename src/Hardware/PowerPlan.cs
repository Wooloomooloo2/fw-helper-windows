using System.Runtime.InteropServices;

namespace FwHelper.Hardware
{
    /// <summary>
    /// CPU frequency caps in the active Windows power plan (ADR 0016): "Maximum processor frequency" for both efficiency classes,
    /// AC and DC, in MHz (0 = no cap). Writable without admin. This is the governor's only actuator.
    /// </summary>
    public static class PowerPlan
    {
        private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid ProcFreqMax = new("75b0ae3f-bce0-45a7-8c89-c9611c25e100");   // efficiency class 0
        private static readonly Guid ProcFreqMax1 = new("75b0ae3f-bce0-45a7-8c89-c9611c25e101");  // efficiency class 1

        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

        /// <summary>AC/DC values for class 0 and class 1, as stored in the plan.</summary>
        public record FrequencyCaps(uint Ac0, uint Dc0, uint Ac1, uint Dc1)
        {
            public static readonly FrequencyCaps None = new(0, 0, 0, 0);
            public override string ToString() => $"{Ac0},{Dc0},{Ac1},{Dc1}";

            public static FrequencyCaps? Parse(string? s)
            {
                var p = (s ?? "").Split(',');
                return p.Length == 4 && p.All(x => uint.TryParse(x, out _))
                    ? new FrequencyCaps(uint.Parse(p[0]), uint.Parse(p[1]), uint.Parse(p[2]), uint.Parse(p[3])) : null;
            }
        }

        private static Guid? ActiveScheme()
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) != 0) return null;
            try { return Marshal.PtrToStructure<Guid>(ptr); }
            finally { LocalFree(ptr); }
        }

        public static FrequencyCaps? Read()
        {
            if (ActiveScheme() is not Guid scheme) return null;
            var sub = SubProcessor;
            var s0 = ProcFreqMax;
            var s1 = ProcFreqMax1;
            if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s0, out uint ac0) != 0) return null;
            PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s0, out uint dc0);
            PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s1, out uint ac1);
            PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s1, out uint dc1);
            return new FrequencyCaps(ac0, dc0, ac1, dc1);
        }

        public static bool Write(FrequencyCaps caps)
        {
            if (ActiveScheme() is not Guid scheme) return false;
            var sub = SubProcessor;
            var s0 = ProcFreqMax;
            var s1 = ProcFreqMax1;
            bool ok = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s0, caps.Ac0) == 0
                    & PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s0, caps.Dc0) == 0
                    & PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s1, caps.Ac1) == 0
                    & PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s1, caps.Dc1) == 0;
            // Re-activating the scheme is what makes the new values take effect
            return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0 && ok;
        }

        // ---------- Sleep after, on battery (ADR 0018) ----------

        private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");  // STANDBYIDLE, "Sleep after"
        private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
        private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");    // VIDEOIDLE, "Turn off display after"

        private static int? ReadDc(Guid sub, Guid setting)
        {
            if (ActiveScheme() is not Guid scheme) return null;
            return PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint v) == 0 ? (int)Math.Min(v, int.MaxValue) : null;
        }

        /// <summary>
        /// On battery: seconds until Modern Standby moves from the screen-off phase (still awake, 1-1.7 W measured) to the sleep
        /// phase (about 0.33 W). On this laptop it counts from the screen turning off. 0 = never. It's the same value as Windows
        /// Settings → Power → "Make my device sleep after", on battery.
        /// </summary>
        public static int? GetSleepAfterDc() => ReadDc(SubSleep, StandbyIdle);

        /// <summary>On battery: seconds of inactivity before the screen turns off.</summary>
        public static int? GetDisplayOffDc() => ReadDc(SubVideo, VideoIdle);

        public static bool SetSleepAfterDc(int seconds)
        {
            if (ActiveScheme() is not Guid scheme) return false;
            var sub = SubSleep;
            var setting = StandbyIdle;
            bool ok = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)Math.Max(0, seconds)) == 0
                      && PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
            Helpers.Logger.WriteLine($"Sleep after (battery) {seconds} s: {(ok ? "OK" : "failed")}");
            return ok;
        }

        /// <summary>Same cap on every core class, AC and DC (0 = no cap).</summary>
        public static bool SetCap(int mhz) => Write(new FrequencyCaps((uint)mhz, (uint)mhz, (uint)mhz, (uint)mhz));
    }
}
