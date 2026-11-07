using FwHelper.Features;
using FwHelper.Helpers;

namespace FwHelper.UI
{
    /// <summary>
    /// Compare projects (plan Phase 3): pick recordings, name them, choose the baseline, and open the comparison report. The
    /// project is saved as Documents\FW Benchmark\projects\&lt;name&gt;.json, so it can be opened again to add runs or remake the report.
    /// </summary>
    public class CompareForm : RForm
    {
        private const int W = 720, M = 12, RowH = 30, NameX = 64, NameW = 250, InfoX = NameX + NameW + 8;

        private readonly ComboBox _project;
        private readonly Panel _rows;
        private readonly Label _hint;
        private readonly RButton _add, _report, _close;
        private readonly List<(CompareRun Run, string Info)> _runs = new();
        private int _baseline;

        private static string SessionsFolder => SessionRecorder.Folder;
        private static string ProjectsFolder => CompareProject.FolderIn(SessionsFolder);

        public CompareForm(IEnumerable<string>? csvs = null)
        {
            SuspendLayout();
            Text = "FW-Helper · Compare runs";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterParent;
            Icon = TrayIcons.ForMode(ModeControl.CurrentMode);

            int y = M;
            Controls.Add(new Label { Text = "Project", Location = new Point(M, y + 4), Size = new Size(60, 20) });
            _project = new ComboBox { Location = new Point(M + 64, y), Size = new Size(W - 2 * M - 64, 24), DropDownStyle = ComboBoxStyle.DropDown, MaxLength = SessionRecorder.MaxNameLength };
            _project.Items.AddRange(ProjectNames());
            _project.SelectionChangeCommitted += (_, _) => BeginInvoke(() => LoadProject(_project.Text));
            Controls.Add(_project);
            y += 34;

            // Column headings over the rows (RebuildRows uses the same x positions)
            foreach (var (text, x, w) in new[] { ("Baseline", 0, NameX - 4), ("Run name", NameX, NameW), ("Recording", InfoX, 200) })
                Controls.Add(new Label { Text = text, Location = new Point(M + 1 + x, y), Size = new Size(w, 20), Tag = "dim" });
            y += 22;
            _rows = new Panel { Location = new Point(M, y), Size = new Size(W - 2 * M, 8 * RowH + 4), AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(_rows);
            y += _rows.Height + 8;

            _hint = new Label { Location = new Point(M, y), Size = new Size(W - 2 * M, 34), Tag = "dim" };
            Controls.Add(_hint);
            y += 40;

            _add = new RButton { Text = "Add runs…", Location = new Point(M, y), Size = new Size(110, 30), Secondary = true };
            _add.Click += (_, _) => AddRuns();
            _report = new RButton { Text = "Save and open report", Location = new Point(W - M - 100 - 8 - 170, y), Size = new Size(170, 30) };
            _report.BorderColor = RForm.Accent;
            _report.Click += (_, _) => MakeReport();
            _close = new RButton { Text = "Close", Location = new Point(W - M - 100, y), Size = new Size(100, 30), Secondary = true, DialogResult = DialogResult.Cancel };
            _close.Click += (_, _) => Close();
            Controls.AddRange(new Control[] { _add, _report, _close });
            CancelButton = _close;
            y += 30 + M;

            ClientSize = new Size(W, y);
            ResumeLayout(false);
            ScaleToDpi();

            if (csvs is not null) foreach (var c in csvs) AddRun(c);
            RebuildRows();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _project.BackColor = RForm.ButtonMain;
            _project.ForeColor = RForm.ForeMain;
            if (_project.Text.Length == 0) _project.Focus();
        }

        private static string[] ProjectNames() => Directory.Exists(ProjectsFolder)
            ? Directory.GetFiles(ProjectsFolder, "*" + CompareProject.Extension).OrderByDescending(File.GetLastWriteTime)
                .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray()
            : Array.Empty<string>();

        /// <summary>Open a saved project (from the list, or a .json picked in the Monitor).</summary>
        public void LoadProject(string nameOrPath)
        {
            string path = nameOrPath.EndsWith(CompareProject.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(nameOrPath)
                ? nameOrPath : CompareProject.PathFor(ProjectsFolder, nameOrPath);
            if (!File.Exists(path)) return;
            try
            {
                var p = CompareProject.FromJson(File.ReadAllText(path));
                _project.Text = p.Name.Length > 0 ? p.Name : Path.GetFileNameWithoutExtension(path);
                _runs.Clear();
                foreach (var r in p.Runs)
                {
                    string csv = CompareProject.Resolve(SessionsFolder, r.File);
                    _runs.Add((r, File.Exists(csv) ? Info(csv, null) : "missing: " + r.File));
                }
                _baseline = p.BaselineIndex;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RebuildRows();
        }

        private void AddRuns()
        {
            using var dialog = new OpenFileDialog
            {
                InitialDirectory = Directory.Exists(SessionsFolder) ? SessionsFolder : Logger.AppDataDir,
                Filter = "FW-Helper sessions and summaries|session-*.csv;session-*.html|CSV files (*.csv)|*.csv",
                Multiselect = true,
                Title = "Add runs to compare",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (var f in dialog.FileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) AddRun(f);
            RebuildRows();
        }

        private void AddRun(string file)
        {
            try
            {
                string csv = RunReport.CsvFor(file);
                string stored = CompareProject.Store(SessionsFolder, csv);
                if (_runs.Any(r => string.Equals(r.Run.File, stored, StringComparison.OrdinalIgnoreCase))) return;
                if (_runs.Count >= CompareProject.MaxRuns)
                {
                    MessageBox.Show(this, $"A project holds up to {CompareProject.MaxRuns} runs.", "Compare", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var s = RunSummary.From(SessionRecorder.Load(csv), RunSummary.LabelFromFileName(csv));
                _runs.Add((new CompareRun(stored, CompareProject.DefaultName(csv, s)), Info(csv, s)));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{Path.GetFileName(file)}: {ex.Message}", "Add run", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>The dim line next to a run: game, length and fps.</summary>
        private static string Info(string csv, RunSummary? s)
        {
            try { s ??= RunSummary.From(SessionRecorder.Load(csv)); }
            catch (Exception ex) { return ex.Message; }
            string game = s.Game is { } g ? Path.GetFileNameWithoutExtension(g) : "no game";
            string fps = s["fps"] is { } f ? $" · {f.Median:0} FPS" : "";
            return $"{s.Start:d MMM HH:mm} · {game} · {s.Steady.Count} s{fps}";
        }

        private void RebuildRows()
        {
            _rows.SuspendLayout();
            foreach (Control c in _rows.Controls.Cast<Control>().ToList()) { _rows.Controls.Remove(c); c.Dispose(); }
            float s = DeviceDpi / 96f;
            int P(int v) => (int)Math.Round(v * s);
            int inner = _rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            for (int i = 0; i < _runs.Count; i++)
            {
                int index = i, top = P(4) + i * P(RowH);
                var radio = new RadioButton { Checked = i == _baseline, Location = new Point(P(16), top + P(4)), Size = new Size(P(18), P(18)) };
                radio.CheckedChanged += (_, _) => { if (radio.Checked) { _baseline = index; UpdateHint(); } };
                var name = new TextBox
                {
                    Text = _runs[i].Run.Name, MaxLength = SessionRecorder.MaxNameLength, BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(P(NameX), top), Size = new Size(P(NameW), P(24)), BackColor = RForm.ButtonMain, ForeColor = RForm.ForeMain,
                };
                name.TextChanged += (_, _) => _runs[index] = (_runs[index].Run with { Name = name.Text }, _runs[index].Info);
                var info = new Label
                {
                    Text = _runs[i].Info, AutoEllipsis = true, ForeColor = RForm.ForeDim, Tag = "dim",
                    Location = new Point(P(InfoX), top + P(4)), Size = new Size(Math.Max(P(40), inner - P(InfoX) - P(36)), P(20)),
                };
                new ToolTip().SetToolTip(info, _runs[i].Run.File);
                var remove = new RButton { Text = "✕", Secondary = true, Location = new Point(inner - P(32), top), Size = new Size(P(26), P(24)) };
                remove.Click += (_, _) =>
                {
                    _runs.RemoveAt(index);
                    if (_baseline >= index && _baseline > 0) _baseline--;
                    BeginInvoke(RebuildRows);
                };
                _rows.Controls.AddRange(new Control[] { radio, name, info, remove });
            }
            if (_runs.Count == 0)
                _rows.Controls.Add(new Label
                {
                    Text = "No runs yet: press Add runs… and pick two or more recordings (Ctrl or Shift to pick several).",
                    Location = new Point(P(8), P(8)), Size = new Size(inner - P(16), P(40)), ForeColor = RForm.ForeDim, Tag = "dim",
                });
            _rows.ResumeLayout();
            UpdateHint();
        }

        private void UpdateHint()
        {
            _report.Enabled = _runs.Count >= 2;
            _hint.Text = _runs.Count < 2
                ? "Add at least two runs. Recordings named when they started get that name here; rename any run in its box."
                : $"{(_runs.Count == 2 ? "Both runs are" : $"All {_runs.Count} runs are")} in the report; their changes are measured from the baseline, " +
                  $"{(_runs[Math.Clamp(_baseline, 0, _runs.Count - 1)].Run.Name is { Length: > 0 } b ? b : "the selected run")}. " +
                  "It opens in your browser and is saved next to the project.";
        }

        private void MakeReport()
        {
            string name = SessionRecorder.CleanName(_project.Text);
            if (name.Length == 0)
            {
                name = SessionRecorder.CleanName(PromptForm.Ask(this, "Compare", "Project name:", _runs.Count > 0 ? _runs[0].Run.Name : "") ?? "");
                if (name.Length == 0) return;
                _project.Text = name;
            }
            var project = new CompareProject
            {
                Name = name,
                Baseline = _baseline,
                Runs = _runs.Select((r, i) => r.Run with { Name = r.Run.Name.Trim() is { Length: > 0 } n ? n : $"Run {i + 1}" }).ToList(),
            };
            try
            {
                Directory.CreateDirectory(ProjectsFolder);
                string path = CompareProject.PathFor(ProjectsFolder, name);
                File.WriteAllText(path, project.ToJson());
                Logger.WriteLine($"Compare project saved: {path} ({project.Runs.Count} runs)");
                if (!_project.Items.Contains(name)) _project.Items.Insert(0, name);
                if (CompareRunner.Open(path) is { } error)
                    MessageBox.Show(this, error, "Compare", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Compare", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>Loads a project's recordings, writes its comparison report next to the project file and opens it.</summary>
    public static class CompareRunner
    {
        public static string? Open(string projectPath)
        {
            try
            {
                var project = CompareProject.FromJson(File.ReadAllText(projectPath));
                var entries = new List<CompareEntry>();
                var missing = new List<string>();
                int baseline = project.BaselineIndex;
                for (int i = 0; i < project.Runs.Count; i++)
                {
                    var run = project.Runs[i];
                    string csv = CompareProject.Resolve(SessionRecorder.Folder, run.File);
                    if (!File.Exists(csv))
                    {
                        missing.Add(run.File);
                        if (i < project.BaselineIndex) baseline--;
                        else if (i == project.BaselineIndex) baseline = 0;
                        continue;
                    }
                    var samples = SessionRecorder.Load(csv);
                    entries.Add(new CompareEntry(run.Name, RunSummary.From(samples, run.Name), Path.GetFileName(csv)));
                }
                if (entries.Count == 0) return "None of the project's recordings could be found.";
                string html = CompareReport.Html(project.Name, entries, Math.Max(0, baseline), Path.GetFileName(projectPath), Application.ProductVersion.Split('+')[0]);
                string report = CompareProject.ReportPathFor(projectPath);
                File.WriteAllText(report, html);
                Logger.WriteLine($"Compare report: {report} ({entries.Count} runs{(missing.Count > 0 ? $", {missing.Count} missing" : "")})");
                ProcessHelper.OpenUrl(report);
                return missing.Count > 0 ? "These recordings are missing and were left out: " + string.Join(", ", missing) : null;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Compare report failed: " + ex.Message);
                return ex.Message;
            }
        }
    }
}
