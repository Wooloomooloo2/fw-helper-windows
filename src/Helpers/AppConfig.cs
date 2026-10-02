using System.Text.Json;
using System.Text.Json.Nodes;

namespace FwHelper.Helpers
{
    /// <summary>
    /// Flat key/value settings stored as JSON in %AppData%\FwHelper\config.json (same idea as G-Helper's AppConfig).
    /// </summary>
    public static class AppConfig
    {
        private static readonly string ConfigFile = Path.Combine(Logger.AppDataDir, "config.json");
        private static readonly object _lock = new();
        private static JsonObject _config;

        static AppConfig()
        {
            _config = Load();
        }

        private static JsonObject Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                    return JsonNode.Parse(File.ReadAllText(ConfigFile)) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Broken config, starting fresh: " + ex.Message);
                try { File.Copy(ConfigFile, ConfigFile + ".bak", true); } catch { }
            }
            return new JsonObject();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Logger.AppDataDir);
                string tmp = ConfigFile + ".tmp";
                File.WriteAllText(tmp, _config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, ConfigFile, true);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Can't save config: " + ex.Message);
            }
        }

        public static bool Exists(string name)
        {
            lock (_lock) return _config.ContainsKey(name);
        }

        public static int Get(string name, int empty = -1)
        {
            lock (_lock)
            {
                if (_config[name] is JsonValue v && v.TryGetValue(out int i)) return i;
                return empty;
            }
        }

        public static bool Is(string name) => Get(name, 0) == 1;

        public static string? GetString(string name, string? empty = null)
        {
            lock (_lock)
            {
                if (_config[name] is JsonValue v && v.TryGetValue(out string? s)) return s;
                return empty;
            }
        }

        public static void Set(string name, int value)
        {
            lock (_lock) { _config[name] = value; Save(); }
        }

        public static void Set(string name, string value)
        {
            lock (_lock) { _config[name] = value; Save(); }
        }

        public static void Remove(string name)
        {
            lock (_lock) { if (_config.Remove(name)) Save(); }
        }

        // Per performance mode settings, e.g. "fan_curve_1"
        public static string ModeKey(string name, int mode) => $"{name}_{mode}";
    }
}
