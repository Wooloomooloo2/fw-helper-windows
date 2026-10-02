using System.Globalization;

namespace FwHelper.Features
{
    /// <summary>Eight point temperature (°C) → fan duty (%) curve, stored as "t:d,t:d,...".</summary>
    public class FanCurve
    {
        public const int Points = 8;
        public const int MinTemp = 20, MaxTemp = 100;

        public int[] Temps { get; } = new int[Points];
        public int[] Duties { get; } = new int[Points];

        public static FanCurve Default(int mode) => Parse(mode switch
        {
            Modes.Silent => "40:0,50:0,60:15,70:30,78:45,85:65,90:85,95:100",
            Modes.Turbo => "40:20,50:30,60:40,70:55,78:70,85:85,90:100,95:100",
            _ => "40:0,50:15,60:25,70:40,78:55,85:75,90:90,95:100",
        })!;

        public static FanCurve? Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != Points) return null;
            var curve = new FanCurve();
            for (int i = 0; i < Points; i++)
            {
                var kv = parts[i].Split(':');
                if (kv.Length != 2 ||
                    !int.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) ||
                    !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)) return null;
                curve.Temps[i] = Math.Clamp(t, MinTemp, MaxTemp);
                curve.Duties[i] = Math.Clamp(d, 0, 100);
            }
            curve.Normalize();
            return curve;
        }

        /// <summary>Keep temperatures and duties monotonically increasing.</summary>
        public void Normalize()
        {
            for (int i = 1; i < Points; i++)
            {
                if (Temps[i] <= Temps[i - 1]) Temps[i] = Math.Min(MaxTemp, Temps[i - 1] + 1);
                if (Duties[i] < Duties[i - 1]) Duties[i] = Duties[i - 1];
            }
        }

        public int DutyAt(int temp)
        {
            if (temp <= Temps[0]) return Duties[0];
            if (temp >= Temps[Points - 1]) return Duties[Points - 1];
            for (int i = 1; i < Points; i++)
            {
                if (temp <= Temps[i])
                {
                    double f = (double)(temp - Temps[i - 1]) / (Temps[i] - Temps[i - 1]);
                    return (int)Math.Round(Duties[i - 1] + f * (Duties[i] - Duties[i - 1]));
                }
            }
            return Duties[Points - 1];
        }

        public FanCurve Clone()
        {
            var c = new FanCurve();
            Temps.CopyTo(c.Temps, 0);
            Duties.CopyTo(c.Duties, 0);
            return c;
        }

        public override string ToString() =>
            string.Join(",", Enumerable.Range(0, Points).Select(i => $"{Temps[i]}:{Duties[i]}"));
    }
}
