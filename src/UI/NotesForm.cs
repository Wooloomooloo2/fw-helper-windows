using FwHelper.Features;
using FwHelper.Helpers;

namespace FwHelper.UI
{
    /// <summary>
    /// Run notes for one recording (plan Phase 1): the game settings and free notes, saved to <c>session-….notes.json</c> next to
    /// it with what FW-Helper filled in at the start. Opened from the "Recording saved" notification, after stopping in the
    /// Monitor, and with ✎ in Compare.
    /// </summary>
    public class NotesForm : RForm
    {
        private const int W = 460, M = 12;

        private readonly string _csv;
        private readonly RunContext _context;
        private readonly TextBox _settings, _notes;
        private readonly bool _offerSummary;

        private static readonly List<NotesForm> Open = new();

        /// <summary>Raised after notes are saved, with the session CSV.</summary>
        public static event Action<string>? Saved;

        public static bool IsOpenFor(string csv) => Open.Any(f => !f.IsDisposed && string.Equals(f._csv, csv, StringComparison.OrdinalIgnoreCase));

        /// <summary>Show the notes for a recording (or bring its open window forward). <paramref name="offerSummary"/>: the buttons also open the summary.</summary>
        public static void ShowFor(string csv, bool offerSummary, IWin32Window? owner = null)
        {
            if (Open.FirstOrDefault(f => !f.IsDisposed && string.Equals(f._csv, csv, StringComparison.OrdinalIgnoreCase)) is { } open)
            {
                open.Activate();
                return;
            }
            var form = new NotesForm(csv, offerSummary);
            Open.Add(form);
            form.FormClosed += (_, _) => { Open.Remove(form); form.Dispose(); };
            if (owner is not null) form.Show(owner); else form.Show();
            form.Activate();
        }

        private NotesForm(string csv, bool offerSummary)
        {
            _csv = csv;
            _offerSummary = offerSummary;
            _context = RunContext.Load(csv) ?? new RunContext();

            SuspendLayout();
            Text = "FW-Helper · Run notes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true; // it may open over a game that's still running
            StartPosition = FormStartPosition.CenterScreen;
            Icon = TrayIcons.ForMode(ModeControl.CurrentMode);

            int y = M;
            Controls.Add(new Label
            {
                Text = Path.GetFileNameWithoutExtension(csv), Location = new Point(M, y), Size = new Size(W - 2 * M, 20), AutoEllipsis = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            });
            y += 22;
            string recorded = string.Join(" · ", new[]
            {
                _context.Bios is { } b ? "BIOS " + b : null, _context.Ec is { } ec ? "EC " + ec : null,
                _context.FanCurve is { } fan ? "fan " + fan : null,
            }.Where(x => x is not null));
            Controls.Add(new Label
            {
                Text = recorded.Length > 0 ? "Recorded automatically: " + recorded : "Recorded before FW-Helper kept run notes: only what you type is saved.",
                Location = new Point(M, y), Size = new Size(W - 2 * M, 20), Tag = "dim", AutoEllipsis = true,
            });
            y += 30;

            Controls.Add(new Label { Text = "Game settings", Location = new Point(M, y), Size = new Size(W - 2 * M, 20) });
            y += 22;
            _settings = new TextBox
            {
                Text = _context.GameSettings ?? "", MaxLength = 120, BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "e.g. 1080p, Medium, XeSS Quality, frame generation off",
                Location = new Point(M, y), Size = new Size(W - 2 * M, 24),
            };
            Controls.Add(_settings);
            y += 34;

            Controls.Add(new Label { Text = "Notes", Location = new Point(M, y), Size = new Size(W - 2 * M, 20) });
            y += 22;
            _notes = new TextBox
            {
                Text = (_context.Notes ?? "").Replace("\n", Environment.NewLine), MaxLength = 1000, Multiline = true, AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Where in the game, what you changed, how it felt",
                Location = new Point(M, y), Size = new Size(W - 2 * M, 80),
            };
            Controls.Add(_notes);
            y += 90;

            var save = new RButton { Text = offerSummary ? "Save and open summary" : "Save", Location = new Point(W - M - 100 - 8 - 180, y), Size = new Size(180, 30) };
            save.BorderColor = RForm.Accent;
            save.Click += (_, _) => { if (SaveNotes()) Finish(); };
            var skip = new RButton { Text = offerSummary ? "Summary only" : "Cancel", Location = new Point(W - M - 100, y), Size = new Size(100, 30), Secondary = true };
            skip.Click += (_, _) => Finish();
            Controls.AddRange(new Control[] { save, skip });
            CancelButton = skip;
            y += 30 + M;

            ClientSize = new Size(W, y);
            ResumeLayout(false);
            ScaleToDpi();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            foreach (var box in new[] { _settings, _notes })
            {
                box.BackColor = RForm.ButtonMain;
                box.ForeColor = RForm.ForeMain;
            }
            _settings.Focus();
            _settings.SelectAll();
        }

        private bool SaveNotes()
        {
            _context.GameSettings = RunContext.Clean(_settings.Text);
            _context.Notes = RunContext.Clean(_notes.Text.Replace("\r\n", "\n"));
            try
            {
                _context.Save(_csv);
                Logger.WriteLine($"Run notes saved: {RunContext.PathFor(_csv)}");
                Saved?.Invoke(_csv);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Run notes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void Finish()
        {
            Close();
            if (_offerSummary && RunReport.Open(_csv) is { } error)
                MessageBox.Show(error, "Summary", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
