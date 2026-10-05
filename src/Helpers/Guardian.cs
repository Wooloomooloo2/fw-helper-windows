using FwHelper.Hardware;
using System.Diagnostics;

namespace FwHelper.Helpers
{
    /// <summary>
    /// Hard-kill fan safety (ADR 0009): the tray app starts a second copy of itself with <c>--guard &lt;pid&gt;</c>,
    /// which waits for the tray app to exit by any route, including End task / taskkill /f, and then hands the fan back to the EC.
    /// Sending EC auto is always safe: the tray app sends it too on every clean exit path.
    /// </summary>
    public static class Guardian
    {
        public const string Arg = "--guard";

        /// <summary>Called by the tray app once the EC is reachable.</summary>
        public static void Launch()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = $"{Arg} {Environment.ProcessId}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                });
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Can't start fan guard: " + ex.Message);
            }
        }

        /// <summary>Entry point of the guard process.</summary>
        public static void Run(string[] args)
        {
            int index = Array.IndexOf(args, Arg);
            if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out int pid)) return;

            try
            {
                using var parent = Process.GetProcessById(pid);
                parent.WaitForExit();
            }
            catch (ArgumentException)
            {
                // Already gone
            }

            if (FrameworkEc.Connect() && FrameworkEc.SetFanAuto())
                Logger.WriteLine($"Guard: FW-Helper ({pid}) exited, fan returned to EC");
            else
                Logger.WriteLine($"Guard: FW-Helper ({pid}) exited, couldn't return fan to EC");
        }
    }
}
