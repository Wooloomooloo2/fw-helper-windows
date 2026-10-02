using FwHelper.Helpers;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace FwHelper.UI
{
    /// <summary>Base form with G-Helper style light/dark theming.</summary>
    public class RForm : Form
    {
        public static Color ButtonMain, ButtonSecond, FormBack, ForeMain, ForeDim, BorderMain, ChartMain, ChartGrid;
        public static Color Accent = Color.FromArgb(255, 58, 174, 239);
        public static bool Dark;

        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);

        [DllImport("UXTheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int preferredAppMode);

        static RForm() => InitColors(IsDarkTheme());

        public RForm()
        {
            // Layout is written in 96 DPI pixels and scaled once by ScaleToDpi(); fonts are in points so they scale by themselves
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;
        }

        /// <summary>Call at the end of the constructor, after all controls are added at 96 DPI coordinates.</summary>
        protected void ScaleToDpi()
        {
            float s = DeviceDpi / 96f;
            if (Math.Abs(s - 1) < 0.01f) return;
            var client = ClientSize;
            Scale(new SizeF(s, s));
            ClientSize = new Size((int)Math.Round(client.Width * s), (int)Math.Round(client.Height * s));
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            // Moved to a monitor with a different scale factor
            float s = (float)e.DeviceDpiNew / e.DeviceDpiOld;
            var client = ClientSize;
            base.OnDpiChanged(e);
            Scale(new SizeF(s, s));
            ClientSize = new Size((int)Math.Round(client.Width * s), (int)Math.Round(client.Height * s));
        }

        public static bool IsDarkTheme()
        {
            string? mode = AppConfig.GetString("ui_mode")?.ToLowerInvariant();
            if (mode == "dark") return true;
            if (mode == "light") return false;
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }

        public static void InitColors(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                ButtonMain = Color.FromArgb(46, 46, 46);
                ButtonSecond = Color.FromArgb(36, 36, 36);
                FormBack = Color.FromArgb(28, 28, 28);
                ForeMain = Color.FromArgb(240, 240, 240);
                ForeDim = Color.FromArgb(160, 160, 160);
                BorderMain = Color.FromArgb(58, 58, 58);
                ChartMain = Color.FromArgb(35, 35, 35);
                ChartGrid = Color.FromArgb(70, 70, 70);
            }
            else
            {
                ButtonMain = Color.FromArgb(252, 252, 252);
                ButtonSecond = Color.FromArgb(236, 236, 236);
                FormBack = Color.FromArgb(243, 243, 243);
                ForeMain = Color.FromArgb(20, 20, 20);
                ForeDim = Color.FromArgb(100, 100, 100);
                BorderMain = Color.FromArgb(215, 215, 215);
                ChartMain = Color.FromArgb(252, 252, 252);
                ChartGrid = Color.FromArgb(215, 215, 215);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            InitColors(IsDarkTheme());
            try
            {
                DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, new[] { Dark ? 1 : 0 }, 4);
                SetPreferredAppMode(Dark ? 1 : 0);
            }
            catch { }
            BackColor = FormBack;
            ForeColor = ForeMain;
            ApplyTheme(Controls);
            Invalidate(true);
        }

        private static void ApplyTheme(Control.ControlCollection controls)
        {
            foreach (Control c in controls)
            {
                switch (c)
                {
                    case RButton b:
                        b.BackColor = b.Secondary ? ButtonSecond : ButtonMain;
                        b.ForeColor = ForeMain;
                        b.FlatAppearance.BorderColor = BorderMain;
                        break;
                    case Label l:
                        l.ForeColor = l.Tag as string == "dim" ? ForeDim : ForeMain;
                        break;
                    case CheckBox cb:
                        cb.ForeColor = ForeMain;
                        break;
                    case ComboBox combo:
                        combo.BackColor = ButtonMain;
                        combo.ForeColor = ForeMain;
                        break;
                    case Panel p when p.Tag as string == "card":
                        p.BackColor = ButtonSecond;
                        break;
                }
                ApplyTheme(c.Controls);
            }
        }
    }
}
