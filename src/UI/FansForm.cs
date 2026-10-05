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
        private bool _loading;
        private readonly ComboBox _profileCombo;
        private readonly RButton _useButton, _renameButton, _deleteButton;
        private readonly ToolTip _tip = new();

        private sealed record ProfileItem(int Id, string Label)
        {
            public override string ToString() => Label;
        }
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

            // Profile picker: edit the settings of any profile (defaults to the active one), manage user profiles
            Header("Profile", y);
            y += 24;
            const int pb = 62, pg = 6;
            _profileCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(M, y + 4),
                Size = new Size(Inner - 4 * (pb + pg), 24),
            };
            _profileCombo.SelectedIndexChanged += (_, _) =>
            {
                if (!_loading && _profileCombo.SelectedItem is ProfileItem p) LoadMode(p.Id);
            };
            Controls.Add(_profileCombo);
            int bx = M + Inner - 4 * pb - 3 * pg;
            _useButton = Button("Use", bx, y, pb, 30);
            _useButton.Click += (_, _) => ModeControl.SetMode(_editMode);
            var newButton = Button("New…", bx + (pb + pg), y, pb, 30);
            newButton.Secondary = true;
            newButton.Click += (_, _) => NewProfile();
            _renameButton = Button("Rename", bx + 2 * (pb + pg), y, pb, 30);
            _renameButton.Secondary = true;
            _renameButton.Click += (_, _) => RenameProfile();
            _deleteButton = Button("Delete", bx + 3 * (pb + pg), y, pb, 30);
            _deleteButton.Secondary = true;
            _deleteButton.Click += (_, _) => DeleteProfile();
            y += 30 + 14;

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

            var reset = Button("Reset curve", W - M - 100, y, 100, 28);
            reset.Secondary = true;
            reset.Click += (_, _) => UseCurve(FanCurve.Default(Modes.Base(_editMode)));
            var curves = Button("Curves ▾", W - M - 100 - 8 - 90, y, 90, 28);
            curves.Secondary = true;
            curves.Click += (_, _) => ShowCurvesMenu(curves);
            _tip.SetToolTip(curves, "Save this curve under a name, or load a saved one");
            var hint = Label("Drag points to edit. CPU ≥95°C or a hot battery overrides it.", M, y + 6, Inner - 206);
            _tip.SetToolTip(hint, hint.Text);
            hint.Tag = "dim";
            y += 28 + 14;

            // ---------- Windows power mode ----------
            Header("Windows power mode", y);
            y += 24;
            string[] overlayNames = { "Efficiency", "Balanced", "Performance" };
            int tw = (Inner - 2 * 8) / 3;
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
            if (!Modes.Exists(mode)) mode = ModeControl.CurrentMode;
            _editMode = mode;
            bool active = mode == ModeControl.CurrentMode;
            Text = $"Fans + Power · {Modes.Name(mode)}{(active ? " (active)" : "")}";

            _loading = true;
            _profileCombo.Items.Clear();
            foreach (int id in Modes.All())
            {
                var item = new ProfileItem(id, Modes.Name(id) + (id == ModeControl.CurrentMode ? "  (active)" : ""));
                _profileCombo.Items.Add(item);
                if (id == mode) _profileCombo.SelectedItem = item;
            }
            _loading = false;

            // Hidden rather than disabled: RButton draws disabled text twice
            _useButton.Visible = !active;
            _useButton.BorderColor = Modes.ColorOf(mode);
            _renameButton.Visible = _deleteButton.Visible = !Modes.IsBuiltIn(mode);

            _customFan.Checked = Modes.IsCustomFan(mode);
            _editor.LineColor = Modes.ColorOf(mode);
            _editor.Curve = Modes.GetCurve(mode);
            _editor.Enabled = _customFan.Checked;

            _plCheck.Checked = Modes.IsPowerLimit(mode);
            _pl1.SetValueSilently(Modes.GetPL1(mode));
            _pl2.SetValueSilently(Modes.GetPL2(mode));
            _pl1Label.Text = $"Sustained (PL1): {_pl1.Value}W";
            _pl2Label.Text = $"Boost (PL2): {_pl2.Value}W";
            _pl1.AccentColor = _pl2.AccentColor = Modes.ColorOf(mode);

            VisualiseOverlay();
            VisualisePowerLimits();
            RefreshLive();
        }

        // ---------- Profiles ----------

        private void NewProfile()
        {
            string? name = PromptForm.Ask(this, "New profile", $"Copy of {Modes.Name(_editMode)}. Name:", $"My {Modes.Name(_editMode)}");
            if (name is null) return;
            LoadMode(Modes.Create(name, _editMode));
        }

        private void RenameProfile()
        {
            if (Modes.IsBuiltIn(_editMode)) return;
            string? name = PromptForm.Ask(this, "Rename profile", "Name:", Modes.Name(_editMode));
            if (name is null || !Modes.Rename(_editMode, name)) return;
            AfterProfileListChanged();
        }

        private void DeleteProfile()
        {
            if (Modes.IsBuiltIn(_editMode)) return;
            if (MessageBox.Show(this, $"Delete profile \"{Modes.Name(_editMode)}\"?", "Delete profile",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            int deleted = _editMode;
            Modes.Delete(deleted);
            _editMode = ModeControl.CurrentMode == deleted ? Modes.Balanced : ModeControl.CurrentMode;
            ModeControl.OnProfileDeleted(deleted); // switches away if it was active, which reloads this form
            AfterProfileListChanged();
        }

        private void AfterProfileListChanged()
        {
            LoadMode(_editMode);
            Program.SettingsForm.VisualiseMode(); // header shows the active profile's name
        }

        // ---------- Named curves ----------

        private void UseCurve(FanCurve curve)
        {
            _editor.Curve = curve;
            Modes.SetCurve(_editMode, curve);
            if (_editMode == ModeControl.CurrentMode) ModeControl.ApplyFan();
        }

        private void ShowCurvesMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Save this curve as…", null, (_, _) =>
            {
                string? name = PromptForm.Ask(this, "Save fan curve", "Name:", Modes.Name(_editMode));
                if (name is not null) FanCurveLibrary.Save(name, _editor.Curve);
            });

            var saved = FanCurveLibrary.All();
            if (saved.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                foreach (var (name, curve) in saved)
                {
                    var item = new ToolStripMenuItem(name, null, (_, _) => UseCurve(curve)) { ToolTipText = curve.ToString() };
                    menu.Items.Add(item);
                }
                var delete = new ToolStripMenuItem("Delete saved curve");
                foreach (var (name, _) in saved)
                    delete.DropDownItems.Add(name, null, (_, _) => FanCurveLibrary.Delete(name));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(delete);
            }
            menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
            menu.Show(anchor, new Point(0, anchor.Height));
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null; // don't open with the profile picker focused and highlighted
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
