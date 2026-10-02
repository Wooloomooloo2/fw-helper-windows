using Microsoft.Win32.TaskScheduler;

namespace FwHelper.Helpers
{
    /// <summary>
    /// Auto start via Task Scheduler (same approach as G-Helper), so the app can start elevated without a UAC prompt.
    /// </summary>
    public static class Startup
    {
        private const string TaskName = "FwHelper";
        private static readonly string ExePath = Application.ExecutablePath.Trim();

        public static bool IsScheduled()
        {
            try
            {
                using var ts = new TaskService();
                return ts.RootFolder.AllTasks.Any(t => t.Name == TaskName);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Can't check startup task: " + ex.Message);
                return false;
            }
        }

        public static void Schedule()
        {
            using var td = TaskService.Instance.NewTask();
            td.RegistrationInfo.Description = "FW-Helper Auto Start";
            td.Triggers.Add(new LogonTrigger { UserId = Environment.UserDomainName + "\\" + Environment.UserName, Delay = TimeSpan.FromSeconds(2) });
            td.Actions.Add(ExePath);

            td.Principal.LogonType = TaskLogonType.InteractiveToken;
            if (ProcessHelper.IsUserAdministrator())
                td.Principal.RunLevel = TaskRunLevel.Highest;

            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.ExecutionTimeLimit = TimeSpan.Zero;

            try
            {
                TaskService.Instance.RootFolder.RegisterTaskDefinition(TaskName, td);
                Logger.WriteLine("Startup task scheduled: " + ExePath);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Can't create startup task: " + ex.Message);
                MessageBox.Show("Can't create a startup task: " + ex.Message, "FW-Helper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public static void UnSchedule()
        {
            try
            {
                using var ts = new TaskService();
                ts.RootFolder.DeleteTask(TaskName, false);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Can't remove startup task: " + ex.Message);
            }
        }

        /// <summary>Re-point the task if the exe moved.</summary>
        public static void StartupCheck()
        {
            try
            {
                using var ts = new TaskService();
                var task = ts.RootFolder.AllTasks.FirstOrDefault(t => t.Name == TaskName);
                if (task is null) return;
                string action = task.Definition.Actions.FirstOrDefault()?.ToString()?.Trim() ?? "";
                if (!ExePath.Equals(action, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.WriteLine("Rescheduling startup task to " + ExePath);
                    UnSchedule();
                    Schedule();
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Startup check failed: " + ex.Message);
            }
        }
    }
}
