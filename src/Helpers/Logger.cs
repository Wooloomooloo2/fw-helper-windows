using System.Diagnostics;

namespace FwHelper.Helpers
{
    public static class Logger
    {
        public static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FwHelper");

        public static readonly string LogFile = Path.Combine(AppDataDir, "log.txt");

        private static readonly object _lock = new();

        public static void WriteLine(string message)
        {
            Debug.WriteLine(message);
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(AppDataDir);
                    File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {message}{Environment.NewLine}");

                    // Keep the log from growing forever
                    var info = new FileInfo(LogFile);
                    if (info.Length > 1_000_000)
                    {
                        var lines = File.ReadAllLines(LogFile);
                        File.WriteAllLines(LogFile, lines.Skip(lines.Length / 2));
                    }
                }
            }
            catch { }
        }
    }
}
