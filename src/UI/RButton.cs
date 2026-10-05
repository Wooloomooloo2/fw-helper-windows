using System.Drawing.Drawing2D;

namespace FwHelper.UI
{
    /// <summary>Rounded flat button with an accent border when activated (after G-Helper's RButton).</summary>
    public class RButton : Button
    {
        public Color BorderColor { get; set; } = Color.Transparent;
        public bool Secondary { get; set; }
        public int BorderRadius { get; set; } = 6;

        private bool _activated;
        public bool Activated
        {
            get => _activated;
            set
            {
                if (_activated == value) return;
                _activated = value;
                AccessibleDescription = value ? "Active" : null;
                Invalidate();
            }
        }

        protected override bool ShowFocusCues => false;

        public RButton()
        {
            DoubleBuffered = true;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            BackColorChanged += (_, _) => UpdateHoverColor();
        }

        private void UpdateHoverColor()
        {
            int lum = (BackColor.R * 30 + BackColor.G * 59 + BackColor.B * 11) / 100;
            Color target = lum > 128 ? Color.Black : Color.White;
            FlatAppearance.MouseOverBackColor = Shift(BackColor, target, 0.05f);
            FlatAppearance.MouseDownBackColor = Shift(BackColor, target, 0.10f);
        }

        private static Color Shift(Color c, Color t, float a) => Color.FromArgb(c.A,
            (int)(c.R + (t.R - c.R) * a), (int)(c.G + (t.G - c.G) * a), (int)(c.B + (t.B - c.B) * a));

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            float scale = DeviceDpi / 96f;
            float radius = BorderRadius * scale;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var outer = new RectangleF(0, 0, Width, Height);
            using (var clip = RoundedRect(outer, radius)) Region = new Region(clip);

            // Disabled: the base button already drew embossed text; paint over it so only our grey text shows
            if (!Enabled)
            {
                using var back = new SolidBrush(BackColor);
                g.FillRectangle(back, ClientRectangle);
            }

            float stroke = Math.Max(1f, (_activated ? 2f : 1f) * scale);
            var inner = new RectangleF(stroke / 2, stroke / 2, Width - stroke - 1, Height - stroke - 1);
            Color border = _activated && BorderColor.A > 0 ? BorderColor : FlatAppearance.BorderColor;

            if (_activated && BorderColor.A > 0)
            {
                using var bg = new LinearGradientBrush(new PointF(0, 0), new PointF(0, Height),
                    Color.FromArgb(40, BorderColor), Color.FromArgb(0, BorderColor));
                using var path = RoundedRect(inner, radius);
                g.FillPath(bg, path);
            }

            using (var path = RoundedRect(inner, radius))
            using (var pen = new Pen(border, stroke))
                g.DrawPath(pen, path);

            if (_activated || !Enabled)
            {
                // Redraw text above the gradient overlay
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
        }
    }
}
