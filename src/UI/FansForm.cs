using FwHelper.Features;
using FwHelper.Hardware;
using FwHelper.Helpers;

namespace FwHelper.UI
{
    /// <summary>Per-mode fan curve, Windows power mode and CPU power limits (G-Helper's "Fans + Power").</summary>
    public class FansForm : RForm
    {
        private const int W = 460, M = 12, Inner = W - 2 * M;

        private int _editMode;
        private readonly RButton[] _modeTabs = new RButton[Modes.Count];
        private readonly CheckBox _customFan;
        private readonly FanCurveEditor _editor;
        private readonly Label _liveLabel, _sensorList, _plStatus, _pl1Label, _pl2Label;
        private readonly RButton[] _overlayButtons = new RButton[3];
        private readonly CheckBox _plCheck;
        private readonly Slider _pl1, _pl2;
        private readonly RButton _adminButton;
        private readonly LinkLabel _pawnLink;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

        public FansForm()
        {
            SuspendLayout();
            Text = "Fans + Power";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            int y = M;

            // Mode tabs: edit the settings of any mode, defaults to the active one
            Header("Mode", y);
            y += 24;
            int tw = (Inner - 2 * 8) / 3;
            for (int i = 0; i < Modes.Count; i++)
            {
                int mode = i;
                _modeTabs[i] = Button(Modes.Names[i], M + i * (tw + 8), y, tw, 32);
                _modeTabs[i].BorderColor = Modes.Colors[i];
                _modeTabs[i].Click += (_, _) => LoadMode(mode);
            }
            y += 32 + 14;

            // ---------- Fan curve ----------
            Header("Fan", y);
            _liveLabel = Label("", M + 140, y + 1, Inner - 140);
            _liveLabel.TextAlign = ContentAlignment.TopRight;
            _liveLabel.Tag = "dim";
            y += 24;

            _customFan = new CheckBox { Text = "Custom fan curve (unchecked = Framework EC automatic)", Location = new Point(M, y), AutoSize = true };
            _customFan.Click += (_, _) =>
            {
                Modes.SetCustomFan(_editMode, _customFan.Checked);
                _editor!.Enabled = _customFan.Checked;
                _editor.Invalidate();
                if (_editMode == ModeControl.CurrentMode) ModeControl.ApplyFan();
            };
            Controls.Add(_customFan);
            y += 26;

            _editor = new FanCurveEditor { Location = new Point(M, y), Size = new Size(Inner, 230) };
            _editor.CurveChanged += (_, _) =>
            {
                Modes.SetCurve(_editMode, _editor.Curve);
                if (_editMode == ModeControl.CurrentMode) ModeControl.ApplyFan();
            };
            Controls.Add(_editor);
            y += 230 + 6;

            var reset = Button("Reset curve", W - M - 110, y, 110, 28);
            reset.Secondary = true;
            reset.Click += (_, _) =>
            {
                var def = FanCurve.Default(_editMode);
                _editor.Curve = def;
                Modes.SetCurve(_editMode, def);
                if (_editMode == ModeControl.CurrentMode) ModeControl.ApplyFan();
            };
            var hint = Label("Drag points to edit. CPU ≥95°C or a hot battery overrides it.", M, y + 6, Inner - 120);
            hint.Tag = "dim";
            y += 28 + 14;

            // ---------- Windows power mode ----------
            Header("Windows power mode", y);
            y += 24;
            string[] overlayNames = { "Efficiency", "Balanced", "Performance" };
            for (int i = 0; i < 3; i++)
            {
                int overlay = i;
                _overlayButtons[i] = Button(overlayNames[i], M + i * (tw + 8), y, tw, 32);
                _overlayButtons[i].BorderColor = RForm.Accent;
                _overlayButtons[i].Click += (_, _) =>
                {
                    Modes.SetOverlay(_editMode, overlay);
                    if (_editMode == ModeControl.CurrentMode) PowerNative.SetOverlay(overlay);
                    VisualiseOverlay();
                };
            }
            y += 32 + 14;

            // ---------- CPU power limits ----------
            Header("CPU power limits (experimental)", y);
            y += 24;
            _plCheck = new CheckBox { Text = "Override PL1 / PL2 for this mode", Location = new Point(M, y), AutoSize = true };
            _plCheck.Click += (_, _) =>
            {
                Modes.SetPowerLimit(_editMode, _plCheck.Checked);
                if (_plCheck.Checked && _editMode == ModeControl.CurrentMode) ModeControl.ApplyPowerLimits();
                VisualisePowerLimits();
            };
            Controls.Add(_plCheck);
            y += 26;

            _pl1Label = Label("PL1", M, y + 4, 150);
            _pl1 = new Slider { Min = PowerLimitControl.MinPL1, Max = PowerLimitControl.MaxPL1, Step = 1, Location = new Point(M + 156, y), Size = new Size(Inner - 156, 28) };
            Controls.Add(_pl1);
            y += 30;
            _pl2Label = Label("PL2", M, y + 4, 150);
            _pl2 = new Slider { Min = PowerLimitControl.MinPL2, Max = PowerLimitControl.MaxPL2, Step = 1, Location = new Point(M + 156, y), Size = new Size(Inner - 156, 28) };
            Controls.Add(_pl2);
            y += 30;

            _pl1.ValueChanged += (_, _) => { if (_pl2.Value < _pl1.Value) _pl2.Value = _pl1.Value; _pl1Label.Text = $"Sustained (PL1): {_pl1.Value}W"; };
            _pl2.ValueChanged += (_, _) => { if (_pl1.Value > _pl2.Value) _pl1.Value = _pl2.Value; _pl2Label.Text = $"Boost (PL2): {_pl2.Value}W"; };
            EventHandler commit = (_, _) =>
            {
                Modes.SetPL(_editMode, _pl1.Value, _pl2.Value);
                if (_editMode == ModeControl.CurrentMode) ModeControl.ApplyPowerLimits();
                VisualisePowerLimits();
            };
            _pl1.ValueCommitted += commit;
            _pl2.ValueCommitted += commit;

            _plStatus = Label("", M, y + 6, Inner - 150);
            _plStatus.Tag = "dim";
            _adminButton = Button("Restart as admin", W - M - 140, y, 140, 28);
            _adminButton.Secondary = true;
            _adminButton.Click += (_, _) => ProcessHelper.RunAsAdmin();
            _pawnLink = new LinkLabel { Text = "Get PawnIO", Location = new Point(W - M - 140, y + 6), Size = new Size(140, 20), TextAlign = ContentAlignment.TopRight, Visible = false };
            _pawnLink.LinkClicked += (_, _) => ProcessHelper.OpenUrl("https://pawnio.eu/");
            Controls.Add(_pawnLink);
            y += 28 + 14;

            // ---------- Sensors ----------
            Header("EC sensors", y);
            y += 24;
            _sensorList = Label("", M, y, Inner);
            _sensorList.Height = 40;
            _sensorList.Tag = "dim";
            y += 40 + M;

            ClientSize = new Size(W, y);
            ResumeLayout(false);
            ScaleToDpi();

            _timer.Tick += (_, _) => RefreshLive();
            LoadMode(ModeControl.CurrentMode);
        }

        private void Header(string text, int y)
        {
            var l = Label(text, M, y, 260);
            l.Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold);
        }

        private Label Label(string text, int x, int y, int w)
        {
            var l = new Label { Text = text, Location = new Point(x, y), Size = new Size(w, 20), AutoEllipsis = true };
            Controls.Add(l);
            return l;
        }

        private RButton Button(string text, int x, int y, int w, int h)
        {
            var b = new RButton { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            Controls.Add(b);
            return b;
        }

        public void OnModeChanged() => LoadMode(ModeControl.CurrentMode);

        private void LoadMode(int mode)
        {
            _editMode = mode;
            for (int i = 0; i < Modes.Count; i++) _modeTabs[i].Activated = i == mode;
            Text = $"Fans + Power · {Modes.Name(mode)}{(mode == ModeControl.CurrentMode ? " (active)" : "")}";

            _customFan.Checked = Modes.IsCustomFan(mode);
            _editor.LineColor = Modes.Colors[mode];
            _editor.Curve = Modes.GetCurve(mode);
            _editor.Enabled = _customFan.Checked;

            _plCheck.Checked = Modes.IsPowerLimit(mode);
            _pl1.SetValueSilently(Modes.GetPL1(mode));
            _pl2.SetValueSilently(Modes.GetPL2(mode));
            _pl1Label.Text = $"Sustained (PL1): {_pl1.Value}W";
            _pl2Label.Text = $"Boost (PL2): {_pl2.Value}W";
            _pl1.AccentColor = _pl2.AccentColor = Modes.Colors[mode];

            VisualiseOverlay();
            VisualisePowerLimits();
            RefreshLive();
        }

        private void VisualiseOverlay()
        {
            int overlay = Modes.GetOverlay(_editMode);
            for (int i = 0; i < 3; i++) _overlayButtons[i].Activated = i == overlay;
        }

        private void VisualisePowerLimits()
        {
            bool admin = ProcessHelper.IsUserAdministrator();
            if (admin) IntelPowerLimits.Init();
            bool available = IntelPowerLimits.IsAvailable;

            _pl1.Enabled = _pl2.Enabled = _plCheck.Checked && available;
            _adminButton.Visible = !admin;
            _pawnLink.Visible = admin && !available && IntelPowerLimits.Status.Contains("PawnIO");

            var current = IntelPowerLimits.Get();
            _plStatus.Text = !admin ? "Needs admin + PawnIO driver"
                : current is { } pl ? $"MSR now: PL1 {pl.pl1}W · PL2 {pl.pl2}W ({IntelPowerLimits.Status})"
                : IntelPowerLimits.Status;
        }

        private void RefreshLive()
        {
            var temps = FrameworkEc.GetTemperatures();
            int? cpu = FrameworkEc.GetCpuTemp(temps);
            var fans = FrameworkEc.GetFanRpms();
            _editor.CurrentTemp = cpu;
            _editor.Invalidate();

            string duty = FanDutyText(FanControl.IsCustomActive, FanControl.LastDuty, FanControl.Status);
            string power = IntelPowerLimits.GetPackagePower() is float w ? $" · CPU {w:0.0}W" : "";
            _liveLabel.Text = $"{(cpu is null ? "-" : cpu + "°C")} · {(fans.Count > 0 ? fans[0] + " RPM" : "-")} · {duty}{power}";

            _sensorList.Text = string.Join("   ", temps.Select(t => $"{t.Name.Split('@')[0]}: {(t.Celsius is null ? "n/a" : t.Celsius + "°C")}"));
        }

        /// <summary>"duty 40%", "duty 100% · battery guard", "EC: CPU ≥100°C", "EC auto".</summary>
        public static string FanDutyText(bool active, int duty, string status)
        {
            if (!active) return "EC auto";
            if (duty < 0) return status;
            return status == "curve" ? $"duty {duty}%" : $"duty {duty}% · {status}";
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _timer.Start(); else _timer.Stop();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
