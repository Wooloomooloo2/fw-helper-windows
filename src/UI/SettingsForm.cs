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

        private readonly RButton[] _modeButtons = new RButton[Modes.BuiltInCount];
        private readonly Label _modeHeader, _winMode;
        private readonly RButton _winModeFix;
        private readonly ContextMenuStrip _profileMenu = new();
        private readonly RButton _fansButton;
        private readonly Label _sensorsLabel, _batteryLabel, _chargeLabel, _screenLabel, _kbLabel, _ledLabel, _footerLabel;
        private readonly Slider _chargeSlider, _kbSlider;
        private readonly Label _kbState;
        private readonly System.Windows.Forms.Timer _kbCheck = new() { Interval = 1000 };
        private DateTime _kbTouched;
        /// <summary>Set when a slider write was seen overridden by EC Auto; cleared by a write that holds or a level Auto never sets.</summary>
        private bool _kbAuto;
        private readonly RButton[] _screenButtons = new RButton[3];
        private readonly RButton[] _ledButtons = new RButton[4];
        private readonly CheckBox _startupCheck;
        private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2000 };
        private readonly ToolTip _tip = new();

        public FansForm? FansForm;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int WM_HOTKEY = 0x0312, HOTKEY_MODE = 1, HOTKEY_RECORD = 2;

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
            _modeHeader = AddHeader("Performance mode ▾", ref y, out _sensorsLabel);
            _modeHeader.Cursor = Cursors.Hand;
            _modeHeader.Click += (_, _) => ShowProfileMenu();
            _tip.SetToolTip(_modeHeader, "All profiles, including your own");
            int bw = (Inner - 3 * 8) / 4;
            for (int i = 0; i < Modes.BuiltInCount; i++)
            {
                int mode = i;
                var b = MakeButton(Modes.Name(i), M + i * (bw + 8), y, bw, 64);
                b.BorderColor = Modes.ColorOf(i);
                b.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
                b.Click += (_, _) => ModeControl.SetMode(mode);
                _modeButtons[i] = b;
            }
            _fansButton = MakeButton("Fans +\nPower", M + 3 * (bw + 8), y, bw, 64);
            _fansButton.Secondary = true;
            _fansButton.Click += (_, _) => ToggleFans();
            y += 64 + 6;

            // The Windows power mode actually in effect: it sets the CPU + GPU power budget, so it matters more than the profile name
            _winMode = MakeLabel("", M, y + 3, Inner);
            _winMode.Tag = "dim";
            _winModeFix = MakeButton("Fix", M + Inner - 52, y, 52, 24);
            _winModeFix.Visible = false;
            _winModeFix.Click += (_, _) => PowerModeWatcher.Fix();
            _tip.SetToolTip(_winModeFix, "Set Windows to the profile's power mode");
            PowerModeWatcher.Changed += () => { if (IsHandleCreated) BeginInvoke(VisualiseWindowsMode); };
            CpuBoost.Changed += () => { if (IsHandleCreated) BeginInvoke(VisualiseWindowsMode); };
            y += 24 + 12;

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
                _kbTouched = DateTime.UtcNow;
                FrameworkEc.SetKeyboardBacklight(_kbSlider.Value); // live preview while dragging
            };
            _kbSlider.ValueCommitted += (_, _) =>
            {
                AppConfig.Set("kb_brightness", _kbSlider.Value);
                // EC Auto overrides a write within ~300 ms: look again after a second to see whether it held
                _kbCheck.Stop();
                _kbCheck.Start();
            };
            _kbCheck.Tick += (_, _) =>
            {
                _kbCheck.Stop();
                int? actual = FrameworkEc.GetKeyboardBacklight(), lux = FrameworkEc.GetAmbientLux();
                if (KeyboardBacklight.WriteOverridden(_kbSlider.Value, actual, lux) is bool overridden) _kbAuto = overridden;
                ShowKeyboardState(actual, lux);
            };
            Controls.Add(_kbSlider);
            string kbTip = "Fn+Space cycles Off → Low → Medium → High → Auto. In Auto the EC sets the backlight from the light sensor " +
                                 $"(off above {KeyboardBacklight.AutoThresholdLux} lux, {KeyboardBacklight.AutoDarkPercent}% below) and overrides this slider. " +
                                 "Only Fn+Space leaves Auto. There is no idle timeout.";
            _tip.SetToolTip(_kbSlider, kbTip);
            _tip.SetToolTip(_kbLabel, kbTip);
            y += 28 + 2;
            _kbState = MakeLabel("", M + 136, y, Inner - 136);
            _kbState.Tag = "dim";
            _tip.SetToolTip(_kbState, kbTip);
            y += 20 + 8;

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

            var battery = MakeButton("Battery", W - M - 3 * 80 - 2 * 8, y, 80, 30);
            battery.Secondary = true;
            battery.Click += (_, _) => Program.ShowBattery();

            var monitor = MakeButton("Monitor", W - M - 2 * 80 - 8, y, 80, 30);
            monitor.Secondary = true;
            monitor.Click += (_, _) => Program.ShowMonitor();

            var quit = MakeButton("Quit", W - M - 80, y, 80, 30);
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

        private Label AddHeader(string text, ref int y, out Label right)
        {
            var header = MakeLabel(text, M, y, 200);
            header.Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold);
            right = MakeLabel("", M + 200, y + 1, Inner - 200);
            right.TextAlign = ContentAlignment.TopRight;
            right.Tag = "dim";
            y += 26;
            return header;
        }

        private void ShowProfileMenu()
        {
            _profileMenu.Items.Clear();
            _profileMenu.Items.Add(new ToolStripSeparator());
            _profileMenu.Items.Add("Manage profiles…", null, (_, _) => { if (FansForm is null || !FansForm.Visible) ToggleFans(); });
            ProfileMenu.Fill(_profileMenu.Items, 0);
            _profileMenu.Show(_modeHeader, new Point(0, _modeHeader.Height));
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
            RefreshKeyboard();

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
            for (int i = 0; i < Modes.BuiltInCount; i++) _modeButtons[i].Activated = i == mode;
            _modeHeader.Text = Modes.IsBuiltIn(mode) ? "Performance mode ▾" : $"Profile: {Modes.Name(mode)} ▾";
            _modeHeader.ForeColor = Modes.IsBuiltIn(mode) ? RForm.ForeMain : Modes.ColorOf(mode);
            _chargeSlider.AccentColor = _kbSlider.AccentColor = Modes.ColorOf(mode);
            _chargeSlider.Invalidate();
            _kbSlider.Invalidate();
            Icon = TrayIcons.ForMode(mode);
            FansForm?.OnModeChanged();
            VisualiseWindowsMode();
        }

        private static readonly Color Warn = Color.FromArgb(214, 140, 0);

        private void VisualiseWindowsMode()
        {
            var s = PowerModeWatcher.Status;
            _winModeFix.Visible = s.CanFix;
            _winMode.Width = s.CanFix ? Inner - _winModeFix.Width - 8 : Inner;
            _winMode.Text = s.Line + (CpuBoost.Active ? " · CPU boost off" : "");
            _winMode.ForeColor = s.Mismatch ? Warn : RForm.ForeDim;
            _tip.SetToolTip(_winMode, s.Explanation + "\n\nThe profile sets this when you switch. Change it per profile in Fans + Power.");
            for (int i = 0; i < Modes.BuiltInCount; i++)
            {
                int overlay = Modes.GetOverlay(i);
                _tip.SetToolTip(_modeButtons[i], $"{Modes.Name(i)}: Windows {PowerNative.OverlayNames[overlay]}. {WindowsPowerMode.Explain[overlay]}");
            }
            FansForm?.VisualiseWindowsMode();
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
            RefreshKeyboard();
        }

        /// <summary>
        /// Show the EC's actual backlight level and the light reading. Outside Auto, follow Fn+Space: the slider and the saved
        /// level move to what the EC has (not while the user is dragging), so startup doesn't put back a stale level.
        /// </summary>
        private void RefreshKeyboard()
        {
            int? actual = FrameworkEc.GetKeyboardBacklight(), lux = FrameworkEc.GetAmbientLux();
            if (actual is int a)
            {
                if (KeyboardBacklight.RulesOutAuto(a)) _kbAuto = false;
                bool idle = (DateTime.UtcNow - _kbTouched).TotalSeconds > 3 && !_kbCheck.Enabled;
                if (!_kbAuto && idle && a != _kbSlider.Value)
                {
                    _kbSlider.SetValueSilently(a);
                    _kbLabel.Text = $"Keyboard: {a}%";
                    AppConfig.Set("kb_brightness", a);
                }
            }
            ShowKeyboardState(actual, lux);
        }

        private void ShowKeyboardState(int? actual, int? lux) => _kbState.Text = KeyboardBacklight.Describe(actual, lux, _kbAuto);

        private void ToggleFans()
        {
            if (FansForm is null || FansForm.IsDisposed)
            {
                FansForm = new FansForm();
                FansForm.FormClosed += (_, _) => FansForm = null;
            }
            if (FansForm.Visible) { FansForm.Close(); return; }
            FansForm.Show();
            // To the left of the main window, on the same screen (which can sit at negative coordinates)
            var area = Screen.FromControl(this).WorkingArea;
            FansForm.Location = new Point(Math.Max(area.Left, Left - FansForm.Width - 10), Math.Max(area.Top, Bottom - FansForm.Height));
            FansForm.KeepOnScreen();
            FansForm.Activate();
        }

        // ---------- Window behaviour ----------

        public void ShowAtTray()
        {
            InitState();
            // Restore first: a window Windows minimised when a screen was unplugged would otherwise come back at its old
            // position, on the screen that's gone, and setting Location while minimised doesn't move it
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            RepairSize(); // a bare title bar left by minimise + rescale: put the size back before placing it at the tray
            var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
            Location = new Point(area.Right - Width - 10, area.Bottom - Height - 10);
            Show();
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
            // Ctrl+Shift+F6 starts and stops recording, from inside a game (plan Phase 1)
            if (!RegisterHotKey(Handle, HOTKEY_RECORD, 0x0002 | 0x0004 | 0x4000, (uint)Keys.F6))
                Logger.WriteLine("Can't register Ctrl+Shift+F6 (another app has it)");
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(Handle, HOTKEY_MODE);
            UnregisterHotKey(Handle, HOTKEY_RECORD);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == HOTKEY_MODE)
            {
                ModeControl.CycleMode();
                Program.Toast.ShowToast(Modes.Name(ModeControl.CurrentMode), Modes.ColorOf(ModeControl.CurrentMode));
            }
            else if (m.Msg == WM_HOTKEY && m.WParam == HOTKEY_RECORD)
                Program.ToggleRecording();
            base.WndProc(ref m);
        }
    }
}
