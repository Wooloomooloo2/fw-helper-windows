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

        /// <summary>Same cap on every core class, AC and DC (0 = no cap).</summary>
        public static bool SetCap(int mhz) => Write(new FrequencyCaps((uint)mhz, (uint)mhz, (uint)mhz, (uint)mhz));
    }
}
