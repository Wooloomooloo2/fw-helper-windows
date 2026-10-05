using FwHelper.Features;
using FwHelper.Helpers;
using System.Drawing.Drawing2D;

namespace FwHelper.UI
{
    public record OverlayRow(string Key, string Value);

    /// <summary>
    /// In-game style readout (ADR 0017): FPS, package temperature, CPU/GPU load and watts, power against the limit, RAM, GPU memory,
    /// battery. Topmost, non-activating, click-through-free tool window; drag to move (remembered), right-click to close.
    /// Visible over windowed and borderless games, not over exclusive full-screen.
    /// </summary>
    public class OverlayForm : Form
    {
        /// <summary>EC's sustained PL1 on this board (ADR 0016): the "limit" when no power target is set.</summary>
        public const int EcPl1W = 35;

        private static readonly Color Back = Color.FromArgb(18, 18, 18);
        private static readonly Color KeyColor = Color.FromArgb(58, 174, 239);
        private static readonly Color ValueColor = Color.FromArgb(235, 235, 235);
        private static readonly Color FpsColor = Color.FromArgb(120, 230, 120);

        private IReadOnlyList<OverlayRow> _rows = new[] { new OverlayRow("FW-Helper", "starting…") };
        private IDisposable? _telemetry;
        private Point _dragFrom;
        private readonly Font _font = new("Consolas", 9.5f);
        private readonly Font _fpsFont = new("Consolas", 14f, FontStyle.Bold);

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Back;
            Opacity = 0.88;
            DoubleBuffered = true;
            ClientSize = new Size(10, 10);

            int x = AppConfig.Get("overlay_x", -1), y = AppConfig.Get("overlay_y", -1);
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            Location = x >= 0 && y >= 0 && SystemInformation.VirtualScreen.Contains(x, y) ? new Point(x, y) : new Point(area.Left + 16, area.Top + 16);

            MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _dragFrom = e.Location; };
            MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) Location = new Point(Left + e.X - _dragFrom.X, Top + e.Y - _dragFrom.Y); };
            MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) { AppConfig.Set("overlay_x", Left); AppConfig.Set("overlay_y", Top); }
                if (e.Button == MouseButtons.Right) Close();
            };

            Telemetry.Sampled += OnSampled;
        }

        // Don't take focus from the game when shown
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 /* WS_EX_TOOLWINDOW: no Alt+Tab entry */ | 0x08000000 /* WS_EX_NOACTIVATE */;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _telemetry ??= Telemetry.Use();
            Relayout();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Telemetry.Sampled -= OnSampled;
            _telemetry?.Dispose();
            base.OnFormClosed(e);
        }

        private void OnSampled(TelemetrySample s)
        {
            if (!IsHandleCreated) return;
            var rows = Rows(s, Telemetry.FpsError);
            BeginInvoke(() =>
            {
                _rows = rows;
                Relayout();
                Invalidate();
            });
        }

        private float S(float v) => v * DeviceDpi / 96f;

        private void Relayout()
        {
            using var g = CreateGraphics();
            float keyW = _rows.Max(r => g.MeasureString(r.Key, _font).Width);
            float valW = _rows.Max(r => g.MeasureString(r.Value, r.Key == "FPS" ? _fpsFont : _font).Width);
            float h = _rows.Sum(r => (r.Key == "FPS" ? _fpsFont : _font).GetHeight(g)) + S(10);
            var size = new Size((int)(keyW + valW + S(22)), (int)h);
            if (size != ClientSize) ClientSize = size;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float keyW = _rows.Max(r => g.MeasureString(r.Key, _font).Width);
            float y = S(5);
            using var keyBrush = new SolidBrush(KeyColor);
            using var valBrush = new SolidBrush(ValueColor);
            using var fpsBrush = new SolidBrush(FpsColor);
            foreach (var row in _rows)
            {
                bool fps = row.Key == "FPS";
                var font = fps ? _fpsFont : _font;
                float lineH = font.GetHeight(g);
                g.DrawString(row.Key, _font, keyBrush, S(8), y + (lineH - _font.GetHeight(g)) / 2);
                g.DrawString(row.Value, font, fps ? fpsBrush : valBrush, S(14) + keyW, y);
                y += lineH;
            }
        }

        // ---------- Content (pure, unit-tested) ----------

        public static IReadOnlyList<OverlayRow> Rows(TelemetrySample s, string? fpsError = null)
        {
            static string W(double? w) => w is double v ? $"{v:0.0} W" : "– W";
            static string Pct(double? p) => p is double v ? $"{v:0}%" : "–%";

            var rows = new List<OverlayRow>
            {
                new("FPS", s.Fps is double fps ? $"{fps:0}  {s.FpsApp}" : fpsError is null ? "–" : "n/a"),
                new("CPU", $"{Pct(s.CpuPct),4}  {(s.CpuMhz is double mhz ? $"{mhz / 1000:0.0} GHz" : "– GHz"),8}  {(s.CpuTemp is int t ? $"{t}°C" : "–°C"),5}  {W(s.CpuW)}"),
                new("GPU", $"{Pct(s.GpuPct),4}  {(s.GpuSharedGb is double gb ? $"{gb:0.0} GB" : "– GB"),8}  {"",5}  {W(s.GpuW)}"),
            };

            int limit = s.PL1 ?? EcPl1W;
            string pkg = s.PackageW is double p ? $"{p:0.0} W / {limit} W ({p / limit * 100:0}%)" : "– W";
            if (s.FreqCapMHz is int cap) pkg += $"  cap {cap / 1000.0:0.0} GHz";
            rows.Add(new("PKG", pkg));

            rows.Add(new("RAM", s.MemTotalGb > 0 ? $"{s.MemUsedGb / s.MemTotalGb * 100:0}%  ({s.MemUsedGb:0.0} / {s.MemTotalGb:0} GB)" : "–"));

            string battery = s.BatteryPct is int b ? $"{b}%" : "–";
            if (s.OnAC) battery += "  AC";
            else
            {
                if (s.BatteryMinutes is int min) battery += $"  {min / 60}h{min % 60:00}";
                if (s.BatteryW is double bw) battery += $"  {bw:0.0} W";
            }
            rows.Add(new("BAT", battery));
            return rows;
        }
    }
}
