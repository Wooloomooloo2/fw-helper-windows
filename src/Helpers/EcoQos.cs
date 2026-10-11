using System.Runtime.InteropServices;

namespace FwHelper.Helpers
{
    /// <summary>
    /// Windows 11 "efficiency mode" for one thread: EcoQoS asks the scheduler to run it on the most efficient cores (the E/LP-E cores
    /// on this CPU) at a low clock. For background work that never needs to be fast.
    /// </summary>
    public static class EcoQos
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ThreadPowerThrottlingState
        {
            public uint Version, ControlMask, StateMask;
        }

        private const int ThreadPowerThrottling = 3;
        private const uint THREAD_POWER_THROTTLING_EXECUTION_SPEED = 1;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref ThreadPowerThrottlingState info, uint size);

        /// <summary>Applies to the calling thread only.</summary>
        public static bool Enable()
        {
            var state = new ThreadPowerThrottlingState
            {
                Version = 1,
                ControlMask = THREAD_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = THREAD_POWER_THROTTLING_EXECUTION_SPEED,
            };
            bool ok = SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref state, (uint)Marshal.SizeOf<ThreadPowerThrottlingState>());
            if (!ok) Logger.WriteLine($"EcoQoS: {Marshal.GetLastWin32Error()}");
            return ok;
        }
    }
}
