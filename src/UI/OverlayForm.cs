using FwHelper.Features;
using FwHelper.Helpers;

namespace FwHelper.UI
{
    /// <summary>
    /// Small always-on-top readout (the Linux overlay window's equivalent). Drag to move (position is remembered),
    /// right-click to close. Visible over windowed and borderless games, not over exclusive full-screen.
    /// </summary>
    public class OverlayForm : Form
    {
        private readonly Label _text;
        private IDisposable? _telemetry;
        private Point _dragFrom;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 20, 20);
            Opacity = 0.85;
            AutoScaleMode = AutoScaleMode.Dpi;
            Padding = new Padding(8, 6, 8, 6);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _text = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(235, 235, 235),
                Font = new Font("Consolas", 9.5f),
                Text = "FW-Helper",
            };
            Controls.Add(_text);

            int x = AppConfig.Get("overlay_x", -1), y = AppConfig.Get("overlay_y", -1);
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            Location = x >= 0 && y >= 0 && SystemInformation.VirtualScreen.Contains(x, y) ? new Point(x, y) : new Point(area.Left + 16, area.Top + 16);

            foreach (Control c in new Control[] { this, _text })
            {
                c.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _dragFrom = e.Location; };
                c.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) Location = new Point(Left + e.X - _dragFrom.X, Top + e.Y - _dragFrom.Y); };
                c.MouseUp += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left) { AppConfig.Set("overlay_x", Left); AppConfig.Set("overlay_y", Top); }
                    if (e.Button == MouseButtons.Right) Close();
                };
            }

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
            string text = Format(s);
            BeginInvoke(() => _text.Text = text);
        }

        /// <summary>Four short lines, fixed width so the box doesn't jitter.</summary>
        public static string Format(TelemetrySample s)
        {
            static string N(double? v, string format, string unit) => v is double d ? d.ToString(format) + unit : "–";
            string fan = s.FanDuty >= 0 ? $"{s.FanRpm} rpm {s.FanDuty}%" : $"{s.FanRpm} rpm EC";
            string power = s.PackageW is double p ? $"{p:0.0}W pkg" : s.BatteryW is double b ? $"{b:0.0}W sys" : "–";
            return $"CPU {N(s.CpuPct, "0", "%"),4} {N(s.CpuMhz, "0", "MHz"),8} {N(s.CpuTemp, "0", "°C"),5}\n" +
                   $"GPU {N(s.GpuPct, "0", "%"),4} {N(s.GpuW, "0.0", "W"),8}\n" +
                   $"PWR {power}\n" +
                   $"FAN {fan} · {s.Mode}";
        }
    }
}
