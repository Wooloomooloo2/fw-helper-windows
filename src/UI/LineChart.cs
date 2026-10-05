using FwHelper.Features;
using System.Drawing.Drawing2D;

namespace FwHelper.UI
{
    public record ChartSeries(string Name, Color Color, Func<TelemetrySample, double?> Value, string Format = "0");
    public record ChartLine(double Value, string Label);

    /// <summary>Telemetry history card: one or more series over time, latest values in the header, optional reference lines.</summary>
    public class LineChart : Control
    {
        public string Title { get; set; } = "";
        public string Unit { get; set; } = "";
        public List<ChartSeries> Series { get; } = new();
        public List<ChartLine> Lines { get; } = new();
        public double YMin { get; set; }
        /// <summary>Fixed top of the axis, or null to fit the data (rounded up).</summary>
        public double? YMax { get; set; }
        /// <summary>Extra text after the latest values (e.g. fan status).</summary>
        public Func<TelemetrySample, string?>? Note { get; set; }

        private IReadOnlyList<TelemetrySample> _data = Array.Empty<TelemetrySample>();
        private int _capacity = Telemetry.HistoryLength;

        public LineChart()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        /// <param name="capacity">Width of the time axis in samples; live data fills from the right.</param>
        public void SetData(IReadOnlyList<TelemetrySample> data, int capacity)
        {
            _data = data;
            _capacity = Math.Max(2, capacity);
            Invalidate();
        }

        private float S(float v) => v * DeviceDpi / 96f;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(RForm.FormBack);

            var card = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var bg = new SolidBrush(RForm.ChartMain))
            using (var path = Rounded(card, S(6)))
            using (var border = new Pen(RForm.BorderMain))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            using var titleFont = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            using var small = new Font(Font.FontFamily, 8f);
            using var fore = new SolidBrush(RForm.ForeMain);
            using var dim = new SolidBrush(RForm.ForeDim);

            g.DrawString(Title, titleFont, fore, S(8), S(6));

            // Latest values, right-aligned in series colours
            var last = _data.Count > 0 ? _data[^1] : null;
            float x = Width - S(8);
            if (last is not null && Note?.Invoke(last) is string note)
            {
                var size = g.MeasureString(note, small);
                x -= size.Width;
                g.DrawString(note, small, dim, x, S(8));
                x -= S(6);
            }
            for (int i = Series.Count - 1; i >= 0; i--)
            {
                var s = Series[i];
                string text = last is not null && s.Value(last) is double v ? $"{s.Name} {v.ToString(s.Format)}{Unit}" : $"{s.Name} –";
                var size = g.MeasureString(text, small);
                x -= size.Width;
                using var brush = new SolidBrush(s.Color);
                g.DrawString(text, small, brush, x, S(8));
                x -= S(4);
            }

            var plot = new RectangleF(S(38), S(28), Width - S(46), Height - S(36));
            double top = YMax ?? _data.SelectMany(d => Series.Select(s => s.Value(d))).OfType<double>()
                .Concat(Lines.Select(l => l.Value)).DefaultIfEmpty(1).Max();
            double min = YMin;
            // Round grid steps (about 5 divisions), axis top on a step
            double gridStep = NiceStep(Math.Max(top - min, 1e-6) / 5);
            int divisions = Math.Max(1, (int)Math.Ceiling((top - min) / gridStep - 1e-9));
            double max = min + divisions * gridStep;

            float Y(double v) => plot.Bottom - (float)((Math.Clamp(v, min, max) - min) / (max - min)) * plot.Height;

            // Grid + axis labels
            using var grid = new Pen(RForm.ChartGrid);
            for (int i = 0; i <= divisions; i++)
            {
                double v = min + gridStep * i;
                float y = Y(v);
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                string label = v >= 100 ? v.ToString("0") : v.ToString("0.#");
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, dim, plot.Left - size.Width - S(2), y - size.Height / 2);
            }

            // Reference lines
            using var refPen = new Pen(RForm.ForeDim, S(1)) { DashStyle = DashStyle.Dash };
            foreach (var line in Lines)
            {
                if (line.Value < min || line.Value > max) continue;
                float y = Y(line.Value);
                g.DrawLine(refPen, plot.Left, y, plot.Right, y);
                // Label above the line, or below it when that would run into the header
                float labelY = y - S(14) < plot.Top ? y + S(1) : y - S(14);
                g.DrawString(line.Label, small, dim, plot.Left + S(2), labelY);
            }

            // Series, right-aligned: the newest sample sits at the right edge
            float step = plot.Width / (_capacity - 1);
            int offset = _capacity - _data.Count;
            foreach (var s in Series)
            {
                using var pen = new Pen(s.Color, S(1.6f)) { LineJoin = LineJoin.Round };
                var points = new List<PointF>();
                for (int i = 0; i < _data.Count; i++)
                {
                    if (s.Value(_data[i]) is double v && double.IsFinite(v))
                        points.Add(new PointF(plot.Left + (offset + i) * step, Y(v)));
                    else
                        Flush();
                }
                Flush();

                void Flush()
                {
                    if (points.Count >= 2) g.DrawLines(pen, points.ToArray());
                    points.Clear();
                }
            }
        }

        /// <summary>Round up to 1 / 2 / 2.5 / 5 × 10^n.</summary>
        public static double NiceStep(double v)
        {
            if (v <= 0) return 1;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (double m in new[] { 1, 2, 2.5, 5, 10 })
                if (v <= m * magnitude) return m * magnitude;
            return 10 * magnitude;
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
