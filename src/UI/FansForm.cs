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
        private readonly RButton _useButton, _newButton, _renameButton, _deleteButton;
        private readonly ToolTip _tip = new();

        private sealed record ProfileItem(int Id, string Label)
        {
            public override string ToString() => Label;
        }
        private readonly CheckBox _customFan, _floorCheck;
        private readonly Label _floorLabel;
        private readonly FanCurveEditor _editor;
        private readonly Label _liveLabel, _sensorList, _govStatus;
        private readonly RButton[] _overlayButtons = new RButton[3];
        private readonly CheckBox _powerCheck, _tempCheck, _backstopCheck;
        private readonly Slider _powerSlider, _tempSlider;
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
            _newButton = Button("New…", bx + (pb + pg), y, pb, 30);
            _newButton.Secondary = true;
            _newButton.Click += (_, _) => NewProfile();
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
            y += 28 + 6;

            // Global, not per profile: it's a safety preference about the fan, not a performance setting
            _floorCheck = new CheckBox { Text = "Never quieter than the EC's own fan control", Location = new Point(M, y), AutoSize = true };
            _floorCheck.Click += (_, _) => FanControl.SetFloorEnabled(_floorCheck.Checked);
            _tip.SetToolTip(_floorCheck, "Learned from what the EC runs the fan at by itself. Applies on top of any custom curve.");
            Controls.Add(_floorCheck);
            _floorLabel = Label("", M + 280, y + 2, Inner - 280);
            _floorLabel.TextAlign = ContentAlignment.TopRight;
            _floorLabel.Tag = "dim";
            y += 24 + 14;

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

            // ---------- Power & temperature limits (driverless governor, ADR 0016) ----------
            Header("Power && temperature limits", y);
            y += 24;
            _powerCheck = new CheckBox { Location = new Point(M, y + 4), Size = new Size(150, 22) };
            _powerCheck.Click += (_, _) => CommitLimits();
            Controls.Add(_powerCheck);
            _powerSlider = new Slider { Min = GovernorControl.MinPowerW, Max = GovernorControl.MaxPowerW, Step = 1, Location = new Point(M + 156, y), Size = new Size(Inner - 156, 28) };
            _powerSlider.ValueChanged += (_, _) => _powerCheck.Text = $"Power: {_powerSlider.Value} W";
            _powerSlider.ValueCommitted += (_, _) => CommitLimits();
            Controls.Add(_powerSlider);
            _tip.SetToolTip(_powerCheck, "Sustained package power target for this profile. Holds it by lowering the CPU's maximum frequency.");
            y += 30;

            _tempCheck = new CheckBox { Location = new Point(M, y + 4), Size = new Size(150, 22) };
            _tempCheck.Click += (_, _) => CommitLimits();
            Controls.Add(_tempCheck);
            _tempSlider = new Slider { Min = GovernorControl.MinTempC, Max = GovernorControl.MaxTempC, Step = 1, Location = new Point(M + 156, y), Size = new Size(Inner - 156, 28) };
            _tempSlider.ValueChanged += (_, _) => _tempCheck.Text = $"Temp cap: {_tempSlider.Value}°C";
            _tempSlider.ValueCommitted += (_, _) => CommitLimits();
            Controls.Add(_tempSlider);
            _tip.SetToolTip(_tempCheck, "CPU package temperature cap for all profiles. Holds it by lowering the CPU's maximum frequency.");
            y += 30;

            _backstopCheck = new CheckBox { Text = $"EC hard throttle {EcBackstop.AboveCapC}°C above the cap", Location = new Point(M, y), AutoSize = true };
            _backstopCheck.Click += (_, _) => CommitLimits();
            _tip.SetToolTip(_backstopCheck, "Safety net in firmware: the EC forces the CPU to minimum clocks (PROCHOT) if the cap is overshot.");
            Controls.Add(_backstopCheck);
            y += 26;

            _govStatus = Label("", M, y + 2, Inner);
            _govStatus.Tag = "dim";
            y += 22;
            var note = Label("Limits the CPU cores only: GPU power isn't capped. No driver or admin needed.", M, y, Inner);
            note.Tag = "dim";
            y += 20 + 14;

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

            // Hidden rather than disabled: an action that doesn't apply shouldn't take up attention
            _useButton.Visible = !active;
            _useButton.BorderColor = Modes.ColorOf(mode);
            _renameButton.Visible = _deleteButton.Visible = !Modes.IsBuiltIn(mode);

            // Pack the visible buttons against the right edge (positions are in already-scaled pixels)
            int right = ClientSize.Width - _profileCombo.Left; // same margin as the left edge
            int gap = (int)Math.Round(6 * DeviceDpi / 96f);
            // Not b.Visible: that reads false for every child until the form itself is shown
            bool user = !Modes.IsBuiltIn(mode);
            var shown = new[] { (_deleteButton, user), (_renameButton, user), (_newButton, true), (_useButton, !active) };
            foreach (var (b, _) in shown.Where(s => s.Item2))
            {
                b.Left = right - b.Width;
                right = b.Left - gap;
            }

            _customFan.Checked = Modes.IsCustomFan(mode);
            _floorCheck.Checked = FanControl.IsFloorEnabled();
            _editor.LineColor = Modes.ColorOf(mode);
            _editor.Curve = Modes.GetCurve(mode);
            _editor.Enabled = _customFan.Checked;

            int? power = GovernorControl.PowerTarget(mode);
            _powerCheck.Checked = power is not null;
            _powerSlider.SetValueSilently(power ?? Modes.Base(mode) switch { Modes.Silent => 15, Modes.Turbo => 35, _ => 25 });
            _powerCheck.Text = $"Power: {_powerSlider.Value} W";

            int? cap = GovernorControl.TempCap();
            _tempCheck.Checked = cap is not null;
            _tempSlider.SetValueSilently(cap ?? 85);
            _tempCheck.Text = $"Temp cap: {_tempSlider.Value}°C";
            _backstopCheck.Checked = GovernorControl.IsBackstopEnabled();
            _powerSlider.AccentColor = _tempSlider.AccentColor = Modes.ColorOf(mode);

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

        /// <summary>Save the limit controls (power target for the edited profile, global temperature cap) and apply.</summary>
        private void CommitLimits()
        {
            GovernorControl.SetPowerTarget(_editMode, _powerCheck.Checked ? _powerSlider.Value : null);
            GovernorControl.SetTempCap(_tempCheck.Checked ? _tempSlider.Value : null);
            GovernorControl.SetBackstopEnabled(_backstopCheck.Checked);
            // The temperature cap is global; the power target only matters if this is the active profile
            ModeControl.ApplyPowerLimits();
            VisualisePowerLimits();
        }

        private void VisualisePowerLimits()
        {
            _powerSlider.Enabled = _powerCheck.Checked;
            _tempSlider.Enabled = _tempCheck.Checked;
            _backstopCheck.Enabled = _tempCheck.Checked;

            var g = GovernorControl.Instance;
            string governor = g is null || g.Status == "off" ? "Governor off" : "Governor: " + g.Status;
            string ec = EcBackstop.ActiveAtC is int at ? $" · EC throttles at {at}°C" : "";
            _govStatus.Text = governor + ec;
        }

        private void RefreshLive()
        {
            var temps = FrameworkEc.GetTemperatures();
            int? cpu = FrameworkEc.GetCpuTemp(temps);
            var fans = FrameworkEc.GetFanRpms();
            _editor.CurrentTemp = cpu;
            _editor.Invalidate();

            string duty = FanDutyText(FanControl.IsCustomActive, FanControl.LastDuty, FanControl.Status);
            string power = GovernorControl.Instance?.LastPower.PackageW is double w ? $" · {w:0.0}W" : "";
            _liveLabel.Text = $"{(cpu is null ? "-" : cpu + "°C")} · {(fans.Count > 0 ? fans[0] + " RPM" : "-")} · {duty}{power}";

            var loop = FanControl.Loop;
            _floorLabel.Text = $"EC now ~{loop.Floor.Floor(loop.LastModel)}% · {loop.Floor.LearnedBuckets}/{FirmwareFloor.Buckets} learned";

            _sensorList.Text = string.Join("   ", temps.Select(t => $"{t.Name.Split('@')[0]}: {(t.Celsius is null ? "n/a" : t.Celsius + "°C")}"));
            VisualisePowerLimits();
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
