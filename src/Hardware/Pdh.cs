using System.Runtime.InteropServices;

namespace FwHelper.Hardware
{
    /// <summary>
    /// Minimal Windows performance counter (PDH) query. English counter paths, so it works on any display language.
    /// Wildcard counters ("\GPU Engine(*)\...") are re-expanded on every collect, so instances that come and go are picked up.
    /// No elevation needed.
    /// </summary>
    public sealed class Pdh : IDisposable
    {
        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_FMT_NOCAP100 = 0x00008000;
        private const uint PDH_MORE_DATA = 0x800007D2;

        [StructLayout(LayoutKind.Explicit)]
        private struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double DoubleValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
        private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        private IntPtr _query;

        public Pdh()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) _query = IntPtr.Zero;
        }

        /// <summary>Add a counter; returns <see cref="IntPtr.Zero"/> if the counter doesn't exist on this machine.</summary>
        public IntPtr Add(string path)
        {
            if (_query == IntPtr.Zero) return IntPtr.Zero;
            return PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) == 0 ? counter : IntPtr.Zero;
        }

        public bool Collect() => _query != IntPtr.Zero && PdhCollectQueryData(_query) == 0;

        /// <summary>Value of a single-instance counter, or null (rate counters need two collects first).</summary>
        public double? Value(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return null;
            if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) != 0) return null;
            return v.CStatus <= 1 ? v.DoubleValue : null;
        }

        /// <summary>All instances of a wildcard counter as (instance name, value).</summary>
        public List<(string name, double value)> Values(IntPtr counter)
        {
            var result = new List<(string, double)>();
            if (counter == IntPtr.Zero) return result;

            uint size = 0;
            uint status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero);
            if (status != PDH_MORE_DATA || size == 0) return result;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, buffer) != 0) return result;
                // PDH_FMT_COUNTERVALUE_ITEM_W { wchar* szName; PDH_FMT_COUNTERVALUE FmtValue; }
                int itemSize = IntPtr.Size + 16;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buffer + i * itemSize;
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                    var value = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE>(item + IntPtr.Size);
                    if (value.CStatus <= 1) result.Add((name, value.DoubleValue));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}
