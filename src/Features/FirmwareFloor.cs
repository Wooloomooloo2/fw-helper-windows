using FwHelper.Hardware;
using System.Globalization;

namespace FwHelper.Features
{
    /// <summary>
    /// "Never quieter than the EC" (ADR 0013). The EC drives the fan from its board sensors, each with a fan_off → fan_max ramp
    /// read from EC_CMD_THERMAL_GET_THRESHOLD. <see cref="Model"/> is the plain ChromeOS formula: the highest per-sensor ramp position.
    /// The real EC runs ~10 points above it (measured 2026-10-05), so the floor is <b>learned</b>: while the EC owns the fan, the lowest
    /// duty it was seen running at in each 5 % model bucket. Lowest, not highest: that follows the EC's rising branch and can't get
    /// stuck high on an outlier (the Linux floor's open defect). Unlearned buckets fall back to the model itself, which sits below
    /// the EC, and the result never decreases as the model rises.
    /// </summary>
    public sealed class FirmwareFloor
    {
        public const int BucketPct = 5;
        public const int Buckets = 100 / BucketPct + 1;

        private readonly int[] _minDuty = Enumerable.Repeat(-1, Buckets).ToArray();

        public bool Changed { get; private set; }
        public int LearnedBuckets => _minDuty.Count(d => d >= 0);

        /// <summary>Highest per-sensor position (0–100 %) along the EC's fan ramp; 0 if nothing is in range.</summary>
        public static double Model(IEnumerable<TempSensor> temps, IReadOnlyDictionary<int, (int off, int max)> ramps)
        {
            double model = 0;
            foreach (var t in temps)
            {
                if (t.Celsius is not int c || !ramps.TryGetValue(t.Index, out var r) || r.max <= r.off) continue;
                model = Math.Max(model, Math.Clamp((c - r.off) * 100.0 / (r.max - r.off), 0, 100));
            }
            return model;
        }

        private static int Bucket(double model) => Math.Clamp((int)(model / BucketPct), 0, Buckets - 1);

        /// <summary>Record what the EC chose, only while the EC owns the fan.</summary>
        public void Observe(double model, int ecDuty)
        {
            if (ecDuty is < 0 or > 100) return;
            int b = Bucket(model);
            if (_minDuty[b] < 0 || ecDuty < _minDuty[b])
            {
                _minDuty[b] = ecDuty;
                Changed = true;
            }
        }

        /// <summary>Minimum duty for this model value. 100 once any sensor reaches the end of its EC ramp.</summary>
        public int Floor(double model)
        {
            if (model >= 100) return 100;
            int b = Bucket(model);
            int floor = 0;
            for (int i = 0; i <= b; i++)
            {
                double bucketModel = i * BucketPct;
                int value = _minDuty[i] >= 0 ? _minDuty[i] : (int)Math.Floor(bucketModel);
                floor = Math.Max(floor, value);
            }
            // Inside the current bucket, the model itself is still a valid lower bound
            return Math.Clamp(Math.Max(floor, (int)Math.Floor(model)), 0, 100);
        }

        public string Serialize()
        {
            Changed = false;
            return string.Join(",", Enumerable.Range(0, Buckets).Where(i => _minDuty[i] >= 0)
                .Select(i => $"{i * BucketPct}:{_minDuty[i]}"));
        }

        public static FirmwareFloor Parse(string? s)
        {
            var floor = new FirmwareFloor();
            foreach (var part in (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pct)
                    && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int duty)
                    && pct % BucketPct == 0 && duty is >= 0 and <= 100)
                    floor._minDuty[Bucket(pct)] = duty;
            }
            return floor;
        }
    }
}
