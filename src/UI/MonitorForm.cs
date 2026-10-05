using FwHelper.Features;
using FwHelper.Helpers;

namespace FwHelper.UI
{
    /// <summary>Live telemetry (last 5 minutes at 1 Hz) and recorded sessions, six cards like the Linux monitor page.</summary>
    public class MonitorForm : RForm
    {
        private const int W = 720, M = 12, Gap = 10, CardH = 165;

        private static readonly Color Cpu = RForm.Accent;
        private static readonly Color Gpu = Color.FromArgb(255, 150, 30);
        private static readonly Color Battery = Color.FromArgb(6, 180, 138);
        private static readonly Color Package = Color.FromArgb(235, 70, 70);
        private static readonly Color Memory = Color.FromArgb(160, 110, 255);

        private readonly LineChart _load, _clock, _power, _temp, _fan, _memory;
        private readonly LineChart[] _charts;
        private readonly Label _status;
        private readonly RButton _record, _open, _live;
        private IDisposable? _telemetry;
        private List<TelemetrySample>? _session;
        private string? _sessionName;

        public MonitorForm()
        {
            SuspendLayout();
            Text = "FW-Helper · Monitor";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = TrayIcons.ForMode(ModeControl.CurrentMode);

            int y = M;
            _status = new Label { Location = new Point(M, y + 7), Size = new Size(W - 2 * M - 3 * 118, 20), AutoEllipsis = true, Tag = "dim" };
            Controls.Add(_status);
            _live = Button("Live", W - M - 3 * 118 + 8, y, 110);
            _open = Button("Open session…", W - M - 2 * 118 + 8, y, 110);
            _record = Button("● Record", W - M - 118 + 8, y, 110);
            _live.Click += (_, _) => { _session = null; _sessionName = null; Redraw(); };
            _open.Click += (_, _) => OpenSession();
            _record.Click += (_, _) => { if (Telemetry.IsRecording) Telemetry.StopRecording(); else Telemetry.StartRecording(); };
            y += 32 + Gap;

            int cw = (W - 2 * M - Gap) / 2;
            LineChart Card(int col, int row, string title)
            {
                var c = new LineChart { Title = title, Location = new Point(M + col * (cw + Gap), y + row * (CardH + Gap)), Size = new Size(cw, CardH) };
                Controls.Add(c);
                return c;
            }

            _load = Card(0, 0, "Load");
            _load.Unit = "%";
            _load.YMax = 100;
            _load.Series.Add(new("CPU", Cpu, s => s.CpuPct));
            _load.Series.Add(new("GPU", Gpu, s => s.GpuPct));
            _load.Note = s => s.GpuEngine is { } e ? $"GPU: {e}" : null;

            _clock = Card(1, 0, "CPU clock (effective)");
            _clock.Unit = " MHz";
            _clock.Series.Add(new("CPU", Cpu, s => s.CpuMhz));

            _power = Card(0, 1, "Power");
            _power.Unit = "W";
            _power.Series.Add(new("Package", Package, s => s.PackageW, "0.0"));
            _power.Series.Add(new("System", Battery, s => s.BatteryW, "0.0"));
            _power.Note = s => s.PackageW is null ? "package: needs admin + PawnIO" : null;

            _temp = Card(1, 1, "Temperature");
            _temp.Unit = "°C";
            _temp.YMin = 20;
            _temp.YMax = 100;
            _temp.Series.Add(new("CPU", Cpu, s => s.CpuTemp));
            _temp.Series.Add(new("Battery", Battery, s => s.BatteryTemp));
            _temp.Series.Add(new("DDR", Memory, s => s.DdrTemp));
            _temp.Lines.Add(new(FanController.CpuFullDutyC, "CPU: fan 100%"));
            _temp.Lines.Add(new(FanController.BatteryGuardStartC, "battery guard"));

            _fan = Card(0, 2, "Fan");
            _fan.Unit = " rpm";
            _fan.Series.Add(new("Fan", Cpu, s => s.FanRpm));
            _fan.Note = s => s.FanDuty >= 0 ? $"{s.FanDuty}% · {s.FanStatus}" : s.FanStatus;

            _memory = Card(1, 2, "Memory");
            _memory.Unit = " GB";
            _memory.Series.Add(new("RAM", Memory, s => s.MemUsedGb, "0.0"));
            _memory.Series.Add(new("GPU shared", Gpu, s => s.GpuSharedGb, "0.0"));

            _charts = new[] { _load, _clock, _power, _temp, _fan, _memory };
            y += 3 * (CardH + Gap);

            ClientSize = new Size(W, y + M - Gap);
            ResumeLayout(false);
            ScaleToDpi();

            Telemetry.Sampled += OnSampled;
            Telemetry.RecordingChanged += OnRecordingChanged;
        }

        private RButton Button(string text, int x, int y, int w)
        {
            var b = new RButton { Text = text, Location = new Point(x, y), Size = new Size(w, 32), Secondary = true };
            Controls.Add(b);
            return b;
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
            if (data.LastOrDefault()?.PL1 is int pl1) _power.Lines.Add(new(pl1, $"PL1 {pl1}W"));
            _memory.YMax = data.LastOrDefault()?.MemTotalGb is double total and > 0 ? Math.Ceiling(total) : null;

            foreach (var c in _charts) c.SetData(data, capacity);

            _live.Visible = _session is not null;
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
