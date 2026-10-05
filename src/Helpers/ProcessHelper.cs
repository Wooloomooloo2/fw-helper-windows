using System.Diagnostics;
using System.Security.Principal;

namespace FwHelper.Helpers
{
    public static class ProcessHelper
    {
        public static bool IsUserAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Close any other running instance (new instance wins, as in G-Helper).</summary>
        /// <returns>Whether another instance was running.</returns>
        public static bool CloseOtherInstances()
        {
            var current = Process.GetCurrentProcess();
            bool found = false;
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id == current.Id) continue;
                found = true;
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(1500)) p.Kill();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Can't close other instance: " + ex.Message);
                }
            }
            return found;
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.WriteLine(ex.Message); }
        }
    }
}
