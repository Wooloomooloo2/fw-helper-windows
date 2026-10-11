using FwHelper.Features;

namespace FwHelper.UI
{
    /// <summary>The battery monitor window (ADR 0018): one <see cref="BatteryPanel"/>. Opened from the main window and the tray menu.</summary>
    public class BatteryForm : RForm
    {
        private const int W = 720, M = 12, H = 836;

        public BatteryForm()
        {
            SuspendLayout();
            Text = "FW-Helper · Battery";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = TrayIcons.ForMode(ModeControl.CurrentMode);

            Controls.Add(new BatteryPanel { Location = new Point(M, M), Size = new Size(W - 2 * M, H - 2 * M) });
            ClientSize = new Size(W, H);
            ResumeLayout(false);
            ScaleToDpi();
        }
    }
}
