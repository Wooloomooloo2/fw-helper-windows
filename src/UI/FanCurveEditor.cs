using FwHelper.Features;
using System.Drawing.Drawing2D;

namespace FwHelper.UI
{
    /// <summary>Drag-to-edit fan curve chart (temperature on X, duty on Y), in the spirit of G-Helper's fan charts.</summary>
    public class FanCurveEditor : Control
    {
        private FanCurve _curve = FanCurve.Default(Modes.Balanced);
        private int _drag = -1;
        private int _hover = -1;

        public Color LineColor { get; set; } = RForm.Accent;
        public int? CurrentTemp { get; set; }
        public event EventHandler? CurveChanged;

        public FanCurve Curve
        {
            get => _curve.Clone();
            set { _curve = value.Clone(); Invalidate(); }
        }

        public FanCurveEditor()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        private float S(float v) => v * DeviceDpi / 96f;

        private RectangleF Plot => new(S(40), S(12), Width - S(52), Height - S(40));

        private PointF ToScreen(int temp, int duty)
        {
            var p = Plot;
            float x = p.Left + p.Width * (temp - FanCurve.MinTemp) / (FanCurve.MaxTemp - FanCurve.MinTemp);
            float y = p.Bottom - p.Height * duty / 100f;
            return new PointF(x, y);
        }

        private (int temp, int duty) FromScreen(Point pt)
        {
            var p = Plot;
            int temp = (int)Math.Round(FanCurve.MinTemp + (pt.X - p.Left) / p.Width * (FanCurve.MaxTemp - FanCurve.MinTemp));
            int duty = (int)Math.Round((p.Bottom - pt.Y) / p.Height * 100);
            return (Math.Clamp(temp, FanCurve.MinTemp, FanCurve.MaxTemp), Math.Clamp(duty, 0, 100));
        }

        private int HitTest(Point pt)
        {
            for (int i = 0; i < FanCurve.Points; i++)
            {
                var sp = ToScreen(_curve.Temps[i], _curve.Duties[i]);
                if (Math.Abs(sp.X - pt.X) <= S(9) && Math.Abs(sp.Y - pt.Y) <= S(9)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(RForm.ChartMain);
            var p = Plot;

            using var grid = new Pen(RForm.ChartGrid, 1);
            using var text = new SolidBrush(RForm.ForeDim);
            using var font = new Font(Font.FontFamily, 7.5f);

            for (int d = 0; d <= 100; d += 20)
            {
                var a = ToScreen(FanCurve.MinTemp, d);
                g.DrawLine(grid, p.Left, a.Y, p.Right, a.Y);
                var sz = g.MeasureString($"{d}%", font);
                g.DrawString($"{d}%", font, text, p.Left - sz.Width - S(3), a.Y - sz.Height / 2);
            }
            for (int t = FanCurve.MinTemp; t <= FanCurve.MaxTemp; t += 10)
            {
                var a = ToScreen(t, 0);
                g.DrawLine(grid, a.X, p.Top, a.X, p.Bottom);
                var sz = g.MeasureString($"{t}°", font);
                g.DrawString($"{t}°", font, text, a.X - sz.Width / 2, p.Bottom + S(4));
            }

            if (CurrentTemp is int ct && ct >= FanCurve.MinTemp && ct <= FanCurve.MaxTemp)
            {
                var a = ToScreen(ct, 0);
                using var tempPen = new Pen(Color.FromArgb(160, 255, 128, 0), S(1.5f)) { DashStyle = DashStyle.Dash };
                g.DrawLine(tempPen, a.X, p.Top, a.X, p.Bottom);
                using var tempBrush = new SolidBrush(Color.FromArgb(255, 128, 0));
                g.DrawString($"{ct}°C", font, tempBrush, a.X + S(3), p.Top);
            }

            var pts = Enumerable.Range(0, FanCurve.Points).Select(i => ToScreen(_curve.Temps[i], _curve.Duties[i])).ToList();
            // Flat segments before the first and after the last point, matching DutyAt()
            pts.Insert(0, new PointF(p.Left, pts[0].Y));
            pts.Add(new PointF(p.Right, pts[^1].Y));

            using (var fill = new GraphicsPath())
            {
                fill.AddLines(pts.ToArray());
                fill.AddLine(pts[^1], new PointF(p.Right, p.Bottom));
                fill.AddLine(new PointF(p.Right, p.Bottom), new PointF(p.Left, p.Bottom));
                using var fb = new SolidBrush(Color.FromArgb(35, LineColor));
                g.FillPath(fb, fill);
            }
            using (var line = new Pen(LineColor, S(2)))
                g.DrawLines(line, pts.ToArray());

            using var dot = new SolidBrush(LineColor);
            using var dotBorder = new Pen(RForm.ChartMain, S(2));
            for (int i = 0; i < FanCurve.Points; i++)
            {
                var sp = ToScreen(_curve.Temps[i], _curve.Duties[i]);
                float r = S(i == _hover || i == _drag ? 6 : 4.5f);
                g.FillEllipse(dot, sp.X - r, sp.Y - r, 2 * r, 2 * r);
                g.DrawEllipse(dotBorder, sp.X - r, sp.Y - r, 2 * r, 2 * r);
            }

            if (_drag >= 0 || _hover >= 0)
            {
                int i = _drag >= 0 ? _drag : _hover;
                var sp = ToScreen(_curve.Temps[i], _curve.Duties[i]);
                string label = $"{_curve.Temps[i]}°C, {_curve.Duties[i]}%";
                var sz = g.MeasureString(label, Font);
                float x = Math.Min(sp.X + S(8), Width - sz.Width - S(2));
                float y = Math.Max(sp.Y - sz.Height - S(6), 0);
                using var fore = new SolidBrush(RForm.ForeMain);
                g.DrawString(label, Font, fore, x, y);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _drag = HitTest(e.Location);
            Capture = _drag >= 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag >= 0)
            {
                var (temp, duty) = FromScreen(e.Location);
                int i = _drag;
                int minT = i > 0 ? _curve.Temps[i - 1] + 1 : FanCurve.MinTemp;
                int maxT = i < FanCurve.Points - 1 ? _curve.Temps[i + 1] - 1 : FanCurve.MaxTemp;
                int minD = i > 0 ? _curve.Duties[i - 1] : 0;
                int maxD = i < FanCurve.Points - 1 ? _curve.Duties[i + 1] : 100;
                _curve.Temps[i] = Math.Clamp(temp, minT, maxT);
                _curve.Duties[i] = Math.Clamp(duty, minD, maxD);
                Invalidate();
            }
            else
            {
                int h = HitTest(e.Location);
                if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag >= 0)
            {
                _drag = -1;
                Capture = false;
                Invalidate();
                CurveChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover >= 0) { _hover = -1; Invalidate(); }
        }
    }
}
