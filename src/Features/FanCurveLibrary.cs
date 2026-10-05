using FwHelper.Helpers;

namespace FwHelper.Features
{
    /// <summary>
    /// Named fan curves that can be loaded into any profile (ADR 0012). Stored in one config string:
    /// "Quiet desk=40:0,50:0,...;Gaming=40:20,...".
    /// </summary>
    public static class FanCurveLibrary
    {
        private const string Key = "fan_curves";

        public static List<(string name, FanCurve curve)> All() => Parse(AppConfig.GetString(Key));

        /// <summary>Save (or overwrite) a curve under a name. Returns the cleaned name, or null if the name is unusable.</summary>
        public static string? Save(string name, FanCurve curve)
        {
            if (ProfileList.CleanName(name) is not string clean) return null;
            var all = All().Where(c => !c.name.Equals(clean, StringComparison.OrdinalIgnoreCase)).ToList();
            all.Add((clean, curve));
            AppConfig.Set(Key, Serialize(all));
            Logger.WriteLine($"Fan curve saved: {clean} = {curve}");
            return clean;
        }

        public static void Delete(string name)
        {
            AppConfig.Set(Key, Serialize(All().Where(c => c.name != name)));
            Logger.WriteLine($"Fan curve deleted: {name}");
        }

        public static List<(string name, FanCurve curve)> Parse(string? s)
        {
            var result = new List<(string, FanCurve)>();
            foreach (var entry in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                if (ProfileList.CleanName(entry[..eq]) is string name && FanCurve.Parse(entry[(eq + 1)..]) is FanCurve curve)
                    result.Add((name, curve));
            }
            return result;
        }

        public static string Serialize(IEnumerable<(string name, FanCurve curve)> curves) =>
            string.Join(";", curves.Select(c => $"{c.name}={c.curve}"));
    }
}
