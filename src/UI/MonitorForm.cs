using FwHelper.Features;
using FwHelper.Helpers;

namespace FwHelper.UI
{
    /// <summary>Live telemetry (last 5 minutes at 1 Hz) and recorded sessions, six cards like the Linux monitor page.</summary>
    public class MonitorForm : RForm
    {
        // 96 DPI pixels. The window is resizable: cards share the client area, and their text grows with them (LineChart)
        private const int M = 12, Gap = 10, ButtonW = 110, ButtonStep = 118, ButtonH = 32;

        private static readonly Color Cpu = RForm.Accent;
        private static readonly Color Gpu = Color.FromArgb(255, 150, 30);
        private static readonly Color Battery = Color.FromArgb(6, 180, 138);
        private static readonly Color Package = Color.FromArgb(235, 70, 70);
        private static readonly Color Memory = Color.FromArgb(160, 110, 255);

        private readonly LineChart _load, _clock, _power, _temp, _fan, _memory, _fps, _battery;
        private readonly LineChart[] _charts;
        private readonly Label _status;
        private readonly RButton _record, _open, _live, _view;
        private IDisposable? _telemetry;
        private List<TelemetrySample>? _session;
        private string? _sessionName;

        public MonitorForm()
        {
            SuspendLayout();
            Text = "FW-Helper · Monitor";
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = TrayIcons.ForMode(ModeControl.CurrentMode);

            _status = new Label { AutoEllipsis = true, Tag = "dim" };
            Controls.Add(_status);
            _view = Button("Battery");
            _view.Click += (_, _) => Program.ShowBattery();
            _live = Button("Live");
            _open = Button("Open session…");
            _record = Button("● Record");
            _live.Click += (_, _) => { _session = null; _sessionName = null; Redraw(); };
            _open.Click += (_, _) => OpenSession();
            _record.Click += (_, _) => { if (Telemetry.IsRecording) Telemetry.StopRecording(); else Telemetry.StartRecording(); };

            LineChart Card(string title)
            {
                var c = new LineChart { Title = title };
                Controls.Add(c);
                return c;
            }

            _load = Card("Load");
            _load.Unit = "%";
            _load.YMax = 100;
            _load.Series.Add(new("CPU", Cpu, s => s.CpuPct));
            _load.Series.Add(new("GPU", Gpu, s => s.GpuPct));
            _load.Note = s => s.GpuEngine is { } e ? $"GPU: {e}" : null;

            _clock = Card("CPU clock (effective)");
            _clock.Unit = " MHz";
            _clock.Series.Add(new("CPU", Cpu, s => s.CpuMhz));
            _clock.Series.Add(new("Cap", Package, s => s.FreqCapMHz));
            _clock.Note = s => s.Throttle is { Length: > 0 } t ? "limited by " + t : null;

            _power = Card("Power");
            _power.Unit = "W";
            _power.Series.Add(new("Package", Package, s => s.PackageW, "0.0"));
            _power.Series.Add(new("CPU", Cpu, s => s.CpuW, "0.0"));
            _power.Series.Add(new("GPU", Gpu, s => s.GpuW, "0.0"));
            _power.Series.Add(new("System", Battery, s => s.BatteryW, "0.0"));
            _power.Series.Add(new("DRAM", Memory, s => s.DramW, "0.0"));
            _power.Note = s => s.PackageW is null ? "Energy Meter unavailable" : null;

            _temp = Card("Temperature");
            _temp.Unit = "°C";
            _temp.YMin = 20;
            _temp.YMax = 100;
            _temp.Series.Add(new("CPU", Cpu, s => s.CpuTemp));
            _temp.Series.Add(new("Battery", Battery, s => s.BatteryTemp));
            _temp.Series.Add(new("DDR", Memory, s => s.DdrTemp));
            _temp.Lines.Add(new(FanController.CpuFullDutyC, "CPU: fan 100%"));
            _temp.Lines.Add(new(FanController.BatteryGuardStartC, "battery guard"));

            _fan = Card("Fan");
            _fan.Unit = " rpm";
            _fan.Series.Add(new("Fan", Cpu, s => s.FanRpm));
            _fan.Note = s => s.FanDuty >= 0 ? $"{s.FanDuty}% · {s.FanStatus}" : s.FanStatus;

            _memory = Card("Memory");
            _memory.Unit = " GB";
            _memory.Series.Add(new("RAM", Memory, s => s.MemUsedGb, "0.0"));
            _memory.Series.Add(new("GPU shared", Gpu, s => s.GpuSharedGb, "0.0"));

            _fps = Card("FPS");
            _fps.Series.Add(new("FPS", RForm.Accent, s => s.Fps));
            _fps.Note = s => s.FpsApp ?? Telemetry.FpsError;

            _battery = Card("Battery");
            _battery.Unit = "%";
            _battery.YMax = 100;
            _battery.Series.Add(new("Charge", Battery, s => s.BatteryPct));
            _battery.Note = s => s.OnAC ? "on AC" : s.BatteryMinutes is int m ? $"{m / 60}h{m % 60:00} left · {s.BatteryW:0.0} W" : null;

            _charts = new[] { _load, _clock, _power, _temp, _fan, _memory, _fps, _battery };

            ClientSize = new Size(720, 756);
            ResumeLayout(false);
            ScaleToDpi();
            RestoreSize("monitor_size", new Size(1080, 820), new Size(720, 600));
            LayoutCards();

            Telemetry.Sampled += OnSampled;
            Telemetry.RecordingChanged += OnRecordingChanged;
        }

        private RButton Button(string text)
        {
            var b = new RButton { Text = text, Secondary = true };
            Controls.Add(b);
            return b;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCards();
        }

        /// <summary>Buttons along the top right, then the cards in a 2 × 4 grid filling the rest of the window.</summary>
        private void LayoutCards()
        {
            if (_charts is null || WindowState == FormWindowState.Minimized) return;
            float s = DeviceDpi / 96f;
            int P(float v) => (int)Math.Round(v * s);
            int width = ClientSize.Width, m = P(M), gap = P(Gap), step = P(ButtonStep);

            // Visible buttons packed from the right ("Live" only shows while a session is open)
            var buttons = new[] { _view, _live, _open, _record }.Where(b => b != _live || _session is not null).ToArray();
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].SetBounds(width - m - (buttons.Length - i) * step + P(8), m, P(ButtonW), P(ButtonH));
            _status.SetBounds(m, m + P(7), Math.Max(0, width - 2 * m - buttons.Length * step), P(20));

            int top = m + P(ButtonH) + gap;
            int cw = (width - 2 * m - gap) / 2;
            int ch = Math.Max(P(60), (ClientSize.Height - top - m - 3 * gap) / 4);
            for (int i = 0; i < _charts.Length; i++)
                _charts[i].SetBounds(m + i % 2 * (cw + gap), top + i / 2 * (ch + gap), cw, ch);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _telemetry ??= Telemetry.Use();
            Redraw();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Telemetry.Sampled -= OnSampled;
            Telemetry.RecordingChanged -= OnRecordingChanged;
            _telemetry?.Dispose(); // a running recording keeps its own handle
            base.OnFormClosed(e);
        }

        private void OnSampled(TelemetrySample _)
        {
            if (IsHandleCreated && _session is null) BeginInvoke(Redraw);
        }

        private void OnRecordingChanged()
        {
            if (IsHandleCreated) BeginInvoke(Redraw);
        }

        private void Redraw()
        {
            var data = _session ?? Telemetry.History();
            int capacity = _session?.Count ?? Telemetry.HistoryLength;

            // PL1 reference line follows the active override
            _power.Lines.Clear();
            if (data.LastOrDefault()?.PL1 is int target) _power.Lines.Add(new(target, $"target {target}W"));
            else _power.Lines.Add(new(OverlayForm.EcPl1W, $"EC PL1 {OverlayForm.EcPl1W}W"));
            _memory.YMax = data.LastOrDefault()?.MemTotalGb is double total and > 0 ? Math.Ceiling(total) : null;

            foreach (var c in _charts) c.SetData(data, capacity);

            _live.Visible = _session is not null;
            LayoutCards();
            _record.Text = Telemetry.IsRecording ? "■ Stop" : "● Record";
            _record.BorderColor = Telemetry.IsRecording ? Package : RForm.BorderMain;

            string recording = Telemetry.IsRecording ? $"Recording: {Path.GetFileName(Telemetry.RecordingFile)} ({Telemetry.RecordedRows} rows)" : "";
            _status.Text = _session is not null
                ? $"Session {_sessionName}: {_session.Count} samples, {(_session.Count > 0 ? _session[0].Time.ToString("g") : "")}" + (recording.Length > 0 ? " · " + recording : "")
                : recording.Length > 0 ? recording : "Live · last 5 minutes";
        }

        private void OpenSession()
        {
            using var dialog = new OpenFileDialog
            {
                InitialDirectory = Directory.Exists(SessionRecorder.Folder) ? SessionRecorder.Folder : Logger.AppDataDir,
                Filter = "FW-Helper sessions (session-*.csv)|session-*.csv|CSV files (*.csv)|*.csv",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _session = SessionRecorder.Load(dialog.FileName);
                _sessionName = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open session", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Redraw();
        }
    }
}
