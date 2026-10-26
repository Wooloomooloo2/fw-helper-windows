using FwHelper.Features;

namespace FwHelper.UI
{
    /// <summary>The battery monitor window (ADR 0018): one <see cref="BatteryPanel"/>. Opened from the main window and the tray menu.</summary>
    public class BatteryForm : RForm
    {
        private const int M = 12;

        public BatteryForm()
        {
            SuspendLayout();
            Text = "FW-Helper · Battery";
            FormBorderStyle = FormBorderStyle.Sizable; // the panel zooms with the window (BatteryPanel)
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = TrayIcons.ForMode(ModeControl.CurrentMode);

            ClientSize = new Size(720, 836);
            Controls.Add(new BatteryPanel
            {
                Location = new Point(M, M),
                Size = new Size(ClientSize.Width - 2 * M, ClientSize.Height - 2 * M),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            });
            ResumeLayout(false);
            ScaleToDpi();
            RestoreSize("battery_size", new Size(900, 880), new Size(720, 640));
        }
    }
}
