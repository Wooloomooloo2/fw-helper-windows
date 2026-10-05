using FwHelper.Features;
using FwHelper.Hardware;
using FwHelper.Helpers;
using System.Runtime.InteropServices;

namespace FwHelper.UI
{
    /// <summary>Main tray popup, laid out like G-Helper's main window.</summary>
    public class SettingsForm : RForm
    {
        private const int W = 420, M = 12, Inner = W - 2 * M;

        private readonly RButton[] _modeButtons = new RButton[Modes.Count];
        private readonly RButton _fansButton;
        private readonly Label _sensorsLabel, _batteryLabel, _chargeLabel, _screenLabel, _kbLabel, _ledLabel, _footerLabel;
        private readonly Slider _chargeSlider, _kbSlider;
        private readonly RButton[] _screenButtons = new RButton[3];
        private readonly RButton[] _ledButtons = new RButton[4];
        private readonly CheckBox _startupCheck;
        private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2000 };
        private readonly ToolTip _tip = new();

        public FansForm? FansForm;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int WM_HOTKEY = 0x0312, HOTKEY_MODE = 1;

        public SettingsForm()
        {
            SuspendLayout();
            Text = "FW-Helper";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            Icon = TrayIcons.ForMode(Modes.Balanced);

            int y = M;

            // ---------- Performance ----------
            AddHeader("Performance mode", ref y, out _sensorsLabel);
            int bw = (Inner - 3 * 8) / 4;
            for (int i = 0; i < Modes.Count; i++)
            {
                int mode = i;
                var b = MakeButton(Modes.Names[i], M + i * (bw + 8), y, bw, 64);
                b.BorderColor = Modes.Colors[i];
                b.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
                b.Click += (_, _) => ModeControl.SetMode(mode);
                _modeButtons[i] = b;
            }
            _fansButton = MakeButton("Fans +\nPower", M + 3 * (bw + 8), y, bw, 64);
            _fansButton.Secondary = true;
            _fansButton.Click += (_, _) => ToggleFans();
            y += 64 + 16;

            // ---------- Battery ----------
            AddHeader("Battery", ref y, out _batteryLabel);
            _chargeLabel = MakeLabel("Charge limit", M, y + 4, 130);
            _chargeSlider = new Slider { Min = BatteryControl.MinLimit, Max = 100, Step = 5, Location = new Point(M + 136, y), Size = new Size(Inner - 136, 28) };
            _chargeSlider.ValueChanged += (_, _) => _chargeLabel.Text = $"Charge limit: {_chargeSlider.Value}%";
            _chargeSlider.ValueCommitted += (_, _) => BatteryControl.SetLimit(_chargeSlider.Value);
            Controls.Add(_chargeSlider);
            y += 28 + 16;

            // ---------- Display ----------
            AddHeader("Display", ref y, out _screenLabel);
            int sw = (Inner - 2 * 8) / 3;
            string[] screenNames = { "60Hz", "Max Hz", "Auto" };
            for (int i = 0; i < 3; i++)
            {
                int m = i;
                _screenButtons[i] = MakeButton(screenNames[i], M + i * (sw + 8), y, sw, 36);
                _screenButtons[i].BorderColor = RForm.Accent;
                _screenButtons[i].Click += (_, _) => { ScreenControl.SetScreenMode(m); VisualiseScreen(); };
            }
            _tip.SetToolTip(_screenButtons[2], "Max refresh rate on AC, 60Hz on battery");
            y += 36 + 16;

            // ---------- Lighting ----------
            AddHeader("Lighting", ref y, out _);
            _kbLabel = MakeLabel("Keyboard", M, y + 4, 130);
            _kbSlider = new Slider { Min = 0, Max = 100, Step = 5, Location = new Point(M + 136, y), Size = new Size(Inner - 136, 28) };
            _kbSlider.ValueChanged += (_, _) =>
            {
                _kbLabel.Text = $"Keyboard: {_kbSlider.Value}%";
                FrameworkEc.SetKeyboardBacklight(_kbSlider.Value); // live preview while dragging
            };
            _kbSlider.ValueCommitted += (_, _) => AppConfig.Set("kb_brightness", _kbSlider.Value);
            Controls.Add(_kbSlider);
            y += 28 + 8;

            _ledLabel = MakeLabel("Power LED", M, y + 6, 130);
            string[] ledNames = { "High", "Medium", "Low", "Min" };
            int lw = (Inner - 136 - 3 * 6) / 4;
            for (int i = 0; i < 4; i++)
            {
                var level = (FrameworkEc.PowerLedLevel)i;
                int idx = i;
                _ledButtons[i] = MakeButton(ledNames[i], M + 136 + i * (lw + 6), y, lw, 30);
                _ledButtons[i].BorderColor = RForm.Accent;
                _ledButtons[i].Click += (_, _) =>
                {
                    if (FrameworkEc.SetPowerLedLevel(level)) AppConfig.Set("power_led", idx);
                    VisualiseLed();
                };
            }
            y += 30 + 18;

            // ---------- Footer ----------
            _startupCheck = new CheckBox { Text = "Run on startup", Location = new Point(M, y + 4), AutoSize = true };
            _startupCheck.Click += (_, _) =>
            {
                if (_startupCheck.Checked) Startup.Schedule(); else Startup.UnSchedule();
                _startupCheck.Checked = Startup.IsScheduled();
            };
            Controls.Add(_startupCheck);

            var monitor = MakeButton("Monitor", W - M - 2 * 90 - 8, y, 90, 30);
            monitor.Secondary = true;
            monitor.Click += (_, _) => Program.ShowMonitor();

            var quit = MakeButton("Quit", W - M - 90, y, 90, 30);
            quit.Secondary = true;
            quit.Click += (_, _) => Program.Exit();
            y += 30 + 8;

            _footerLabel = MakeLabel("", M, y, Inner);
            _footerLabel.Tag = "dim";
            y += 20 + M;

            ClientSize = new Size(W, y);
            ResumeLayout(false);
            ScaleToDpi();

            _refreshTimer.Tick += (_, _) => RefreshSensors();
            ModeControl.ModeChanged += () => { if (IsHandleCreated) BeginInvoke(VisualiseMode); };
        }

        // ---------- Layout helpers ----------

        private void AddHeader(string text, ref int y, out Label right)
        {
            var header = MakeLabel(text, M, y, 200);
            header.Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold);
            right = MakeLabel("", M + 200, y + 1, Inner - 200);
            right.TextAlign = ContentAlignment.TopRight;
            right.Tag = "dim";
            y += 26;
        }

        private Label MakeLabel(string text, int x, int y, int w)
        {
            var l = new Label { Text = text, Location = new Point(x, y), Size = new Size(w, 20), AutoEllipsis = true };
            Controls.Add(l);
            return l;
        }

        private RButton MakeButton(string text, int x, int y, int w, int h)
        {
            var b = new RButton { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            Controls.Add(b);
            return b;
        }

        // ---------- State ----------

        public void InitState()
        {
            _chargeSlider.SetValueSilently(BatteryControl.GetLimit());
            _chargeLabel.Text = $"Charge limit: {_chargeSlider.Value}%";

            int kb = AppConfig.Get("kb_brightness", FrameworkEc.GetKeyboardBacklight() ?? 0);
            _kbSlider.SetValueSilently(kb);
            _kbLabel.Text = $"Keyboard: {kb}%";

            _startupCheck.Checked = Startup.IsScheduled();

            string? screen = ScreenControl.FindLaptopScreen();
            int max = ScreenControl.GetMaxRefreshRate(screen);
            int low = ScreenControl.GetLowRefreshRate(screen);
            if (low > 0) _screenButtons[0].Text = $"{low}Hz";
            if (max > 0) _screenButtons[1].Text = $"{max}Hz";

            _footerLabel.Text = $"v{Application.ProductVersion.Split('+')[0]} · EC {FrameworkEc.GetVersion() ?? "?"}";

            VisualiseMode();
            VisualiseScreen();
            VisualiseLed();
            RefreshSensors();
        }

        public void VisualiseMode()
        {
            int mode = ModeControl.CurrentMode;
            for (int i = 0; i < Modes.Count; i++) _modeButtons[i].Activated = i == mode;
            _chargeSlider.AccentColor = _kbSlider.AccentColor = Modes.Colors[mode];
            _chargeSlider.Invalidate();
            _kbSlider.Invalidate();
            Icon = TrayIcons.ForMode(mode);
            FansForm?.OnModeChanged();
        }

        private void VisualiseScreen()
        {
            int mode = ScreenControl.GetScreenMode();
            for (int i = 0; i < 3; i++) _screenButtons[i].Activated = i == mode;
            int hz = ScreenControl.GetRefreshRate(ScreenControl.FindLaptopScreen());
            _screenLabel.Text = hz > 0 ? $"Built-in panel: {hz}Hz" : "Built-in panel off";
        }

        private void VisualiseLed()
        {
            int led = AppConfig.Get("power_led", -1);
            for (int i = 0; i < 4; i++) _ledButtons[i].Activated = i == led;
            int? pct = FrameworkEc.GetPowerLedPercent();
            _ledLabel.Text = pct is null ? "Power LED" : $"Power LED: {pct}%";
        }

        public void RefreshSensors()
        {
            var temps = FrameworkEc.GetTemperatures();
            int? cpu = FrameworkEc.GetCpuTemp(temps);
            var fans = FrameworkEc.GetFanRpms();
            string fan = fans.Count > 0 ? $"{fans[0]} RPM" : "-";
            string duty = FanControl.IsCustomActive ? $" ({FansForm.FanDutyText(true, FanControl.LastDuty, FanControl.Status)})" : "";
            _sensorsLabel.Text = $"CPU {(cpu is null ? "-" : cpu + "°C")} · Fan {fan}{duty}";

            var batt = FrameworkEc.GetBattery();
            if (batt is { Present: true })
            {
                string state = batt.Charging ? $"+{batt.Watts:0.0}W" : batt.Discharging ? $"{batt.Watts:0.0}W" : "idle";
                _batteryLabel.Text = $"{batt.Percent}% · {state} · Health {batt.HealthPercent}%";
                _tip.SetToolTip(_batteryLabel, $"{batt.Manufacturer} {batt.Model} {batt.Chemistry}\n" +
                    $"{batt.RemainingMah} / {batt.FullMah} mAh (design {batt.DesignMah} mAh)\n{batt.Cycles} cycles · {batt.VoltageMv / 1000.0:0.00} V");
            }
            else _batteryLabel.Text = "";

            Program.UpdateTrayText(cpu, fans.FirstOrDefault(), batt);
        }

        private void ToggleFans()
        {
            if (FansForm is null || FansForm.IsDisposed)
            {
                FansForm = new FansForm();
                FansForm.FormClosed += (_, _) => FansForm = null;
            }
            if (FansForm.Visible) { FansForm.Close(); return; }
            FansForm.Show();
            FansForm.Location = new Point(Math.Max(0, Left - FansForm.Width - 10), Math.Max(0, Bottom - FansForm.Height));
            FansForm.Activate();
        }

        // ---------- Window behaviour ----------

        public void ShowAtTray()
        {
            InitState();
            var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
            Location = new Point(area.Right - Width - 10, area.Bottom - Height - 10);
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        public void Toggle()
        {
            if (Visible && WindowState != FormWindowState.Minimized) Hide();
            else ShowAtTray();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _refreshTimer.Start(); else _refreshTimer.Stop();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!Program.IsExiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                FansForm?.Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Ctrl+Shift+F5 cycles performance modes (G-Helper uses Fn+F5 / Ctrl+Shift+F5)
            if (!RegisterHotKey(Handle, HOTKEY_MODE, 0x0002 | 0x0004 | 0x4000, (uint)Keys.F5))
                Logger.WriteLine("Can't register Ctrl+Shift+F5");
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(Handle, HOTKEY_MODE);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == HOTKEY_MODE)
            {
                ModeControl.CycleMode();
                Program.Toast.ShowToast(Modes.Name(ModeControl.CurrentMode), Modes.Colors[ModeControl.CurrentMode]);
            }
            base.WndProc(ref m);
        }
    }
}
