using FwHelper.Features;

namespace FwHelper.UI
{
    /// <summary>Small themed text prompt (WinForms has no InputBox).</summary>
    public class PromptForm : RForm
    {
        private const int W = 320, M = 12;
        private readonly TextBox _text;

        private PromptForm(string title, string label, string value)
        {
            SuspendLayout();
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            int y = M;
            Controls.Add(new Label { Text = label, Location = new Point(M, y), Size = new Size(W - 2 * M, 20), AutoEllipsis = true });
            y += 22;
            _text = new TextBox
            {
                Text = value,
                MaxLength = ProfileList.MaxNameLength,
                Location = new Point(M, y),
                Size = new Size(W - 2 * M, 24),
                BorderStyle = BorderStyle.FixedSingle,
            };
            Controls.Add(_text);
            y += 34;

            var ok = new RButton { Text = "OK", Location = new Point(W - M - 2 * 80 - 8, y), Size = new Size(80, 28), DialogResult = DialogResult.OK };
            var cancel = new RButton { Text = "Cancel", Location = new Point(W - M - 80, y), Size = new Size(80, 28), DialogResult = DialogResult.Cancel, Secondary = true };
            ok.BorderColor = RForm.Accent;
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            y += 28 + M;

            ClientSize = new Size(W, y);
            ResumeLayout(false);
            ScaleToDpi();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _text.BackColor = RForm.ButtonMain;
            _text.ForeColor = RForm.ForeMain;
            _text.SelectAll();
            _text.Focus();
        }

        /// <summary>The cleaned text, or null if cancelled or empty.</summary>
        public static string? Ask(IWin32Window owner, string title, string label, string value)
        {
            using var form = new PromptForm(title, label, value);
            return form.ShowDialog(owner) == DialogResult.OK ? ProfileList.CleanName(form._text.Text) : null;
        }
    }
}
