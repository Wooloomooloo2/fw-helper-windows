namespace FwHelper.UI
{
    /// <summary>Small OSD shown when the performance mode changes via hotkey (like G-Helper's toast).</summary>
    public class ToastForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1800 };
        private string _text = "";
        private Color _color = RForm.Accent;

        public ToastForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(32, 32, 32);
            Opacity = 0.92;
            _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */ | 0x00000020 /* WS_EX_TRANSPARENT */;
                return cp;
            }
        }

        public void ShowToast(string text, Color color)
        {
            _text = text;
            _color = color;
            float s = DeviceDpi / 96f;
            Size = new Size((int)(220 * s), (int)(56 * s));
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - (int)(80 * s));
            using (var path = RButton.RoundedRect(new RectangleF(0, 0, Width, Height), 10 * s))
                Region = new Region(path);
            Invalidate();
            Show();
            _timer.Stop();
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float s = DeviceDpi / 96f;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var dot = new SolidBrush(_color);
            float r = 7 * s;
            g.FillEllipse(dot, 22 * s - r, Height / 2f - r, 2 * r, 2 * r);
            using var font = new Font("Segoe UI", 13f, FontStyle.Bold);
            TextRenderer.DrawText(g, _text, font, new Rectangle((int)(38 * s), 0, Width - (int)(46 * s), Height),
                Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }
}
