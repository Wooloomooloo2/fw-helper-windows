namespace FwHelper.Features
{
    public enum PresentSource
    {
        /// <summary>DXGI / D3D9 Present_Start: one per frame the app presents (PresentMon's API-level events).</summary>
        Api,
        /// <summary>DxgKrnl Present_Info: kernel-level, also covers Vulkan/OpenGL; only used for processes with no API events.</summary>
        Kernel,
    }

    /// <summary>
    /// Frames per second per process from present events (ADR 0017). Pure: timestamps in, numbers out. A process counts its API-level
    /// presents if it has any, otherwise its kernel presents, so frames aren't counted twice. The window ends at the newest event seen,
    /// not "now", because real-time ETW delivers events in ~1 s batches.
    /// </summary>
    public sealed class FpsCounter
    {
        public const double WindowMs = 1000;
        private const double KeepMs = 3000;

        private readonly object _lock = new();
        private readonly Dictionary<(int pid, PresentSource source), Queue<double>> _presents = new();
        private double _latest;

        public void Record(int pid, PresentSource source, double timestampMs)
        {
            lock (_lock)
            {
                if (!_presents.TryGetValue((pid, source), out var q)) _presents[(pid, source)] = q = new Queue<double>();
                q.Enqueue(timestampMs);
                if (timestampMs > _latest) _latest = timestampMs;
                while (q.Count > 0 && q.Peek() < _latest - KeepMs) q.Dequeue();
            }
        }

        /// <summary>Frames in the last second for a process, or null if it hasn't presented recently.</summary>
        public double? Fps(int pid)
        {
            lock (_lock)
            {
                double? api = Count(pid, PresentSource.Api);
                return api is > 0 ? api : Count(pid, PresentSource.Kernel) is > 0 and var k ? k : null;
            }
        }

        /// <summary>The process presenting the most frames right now (fallback when the foreground window isn't a game).</summary>
        public (int pid, double fps)? Busiest()
        {
            lock (_lock)
            {
                return _presents.Keys.Select(k => k.pid).Distinct()
                    .Select(pid => (pid, fps: Fps(pid) ?? 0))
                    .Where(x => x.fps > 0)
                    .OrderByDescending(x => x.fps)
                    .Cast<(int, double)?>()
                    .FirstOrDefault();
            }
        }

        private double? Count(int pid, PresentSource source)
        {
            if (!_presents.TryGetValue((pid, source), out var q)) return null;
            double from = _latest - WindowMs;
            int n = q.Count(t => t > from);
            return n;
        }

        /// <summary>Forget processes that stopped presenting (call occasionally).</summary>
        public void Prune()
        {
            lock (_lock)
            {
                foreach (var key in _presents.Where(kv => kv.Value.Count == 0 || kv.Value.Last() < _latest - KeepMs).Select(kv => kv.Key).ToList())
                    _presents.Remove(key);
            }
        }
    }
}
