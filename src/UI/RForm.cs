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

        /// <summary>The DPI the bounds are currently laid out for (null until <see cref="ScaleToDpi"/>).</summary>
        private int? _layoutDpi;

        /// <summary>Call at the end of the constructor, after all controls are added at 96 DPI coordinates.</summary>
        protected void ScaleToDpi()
        {
            _layoutDpi = DeviceDpi;
            float s = DeviceDpi / 96f;
            if (Math.Abs(s - 1) < 0.01f) return;
            var client = ClientSize;
            Scale(new SizeF(s, s));
            ClientSize = new Size((int)Math.Round(client.Width * s), (int)Math.Round(client.Height * s));
        }

        /// <summary>
        /// Before its handle exists a form reports the primary display's DPI, so the constructor lays it out for that. When the
        /// window is then created on a display with another scale (the 200 % laptop panel next to a 150 % external screen),
        /// Windows sends no DPI change, so rescale here. Fonts already follow the real DPI; only the bounds lag.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTheme();
            bool rescale = _layoutDpi is int laid && DeviceDpi != laid;
            if (rescale)
            {
                float s = (float)DeviceDpi / _layoutDpi!.Value;
                var client = ClientSize;
                MinimumSize = Size.Empty;
                Scale(new SizeF(s, s));
                ClientSize = new Size((int)Math.Round(client.Width * s), (int)Math.Round(client.Height * s));
            }
            if (_layoutDpi is not null) _layoutDpi = DeviceDpi;

            // Resizable windows: the exact saved size, now that the real DPI and frame are known (no drift between sessions)
            if (_restore is var (size, minimum))
            {
                _restore = null;
                float s = DeviceDpi / 96f;
                var work = Screen.FromHandle(Handle).WorkingArea;
                Size Window(Size client) => SizeFromClientSize(new Size((int)Math.Round(client.Width * s), (int)Math.Round(client.Height * s)));
                Size Fit(Size window) => new(Math.Min(window.Width, work.Width), Math.Min(window.Height, work.Height));
                MinimumSize = Fit(Window(minimum));
                Size = Fit(Window(size));
                if (StartPosition == FormStartPosition.CenterScreen)
                    Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
            }
        }

        private (Size size, Size minimum)? _restore;

        // ---------- Displays coming and going ----------

        private bool _watchingDisplays, _wasMinimized;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_watchingDisplays) return;
            _watchingDisplays = true;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Disposed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            // Windows moves and minimises windows around the same moment: check once it has settled
            Task.Delay(1500).ContinueWith(_ =>
            {
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke(KeepOnScreen); } catch (InvalidOperationException) { }
            });
        }

        /// <summary>
        /// After a display change, or when showing a window again: un-minimise a window with no taskbar button (nothing else could
        /// restore it), and bring back a window whose title bar is no longer on any screen.
        /// </summary>
        public void KeepOnScreen()
        {
            if (!Visible) return;
            if (WindowState == FormWindowState.Minimized)
            {
                if (ShowInTaskbar) return; // the taskbar button restores it, and Windows puts it back on screen then
                WindowState = FormWindowState.Normal;
            }
            if (WindowState != FormWindowState.Normal) return;
            var areas = Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            var fallback = Screen.PrimaryScreen?.WorkingArea ?? areas[0];
            var fitted = ScreenGuard.Fit(Bounds, areas, fallback);
            if (fitted == Bounds) return;
            Logger.WriteLine($"{Text}: off screen at {Bounds} after a display change, moved to {fitted}");
            Bounds = fitted;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Minimised without a taskbar button (Windows does this on unplugging a screen): there'd be no way back
            if (WindowState == FormWindowState.Minimized && !ShowInTaskbar && IsHandleCreated)
                BeginInvoke(() => { if (!IsDisposed) WindowState = FormWindowState.Normal; });
            // Restored from the taskbar: its restore position may be on a screen that has gone since
            else if (WindowState == FormWindowState.Normal && _wasMinimized && IsHandleCreated)
                BeginInvoke(KeepOnScreen);
            _wasMinimized = WindowState == FormWindowState.Minimized;
        }

        /// <summary>
        /// For resizable windows, after <see cref="ScaleToDpi"/>: opens at the size saved under <paramref name="key"/> (or
        /// <paramref name="preferred"/>), kept inside the screen, and saves the size on close. Sizes are client sizes at 96 DPI.
        /// </summary>
        protected void RestoreSize(string key, Size preferred, Size minimum)
        {
            float s = DeviceDpi / 96f;
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size Window(Size client) => SizeFromClientSize(new Size((int)Math.Round(client.Width * s), (int)Math.Round(client.Height * s)));
            Size Fit(Size window) => new(Math.Min(window.Width, work.Width), Math.Min(window.Height, work.Height));

            var size = preferred;
            if (AppConfig.GetString(key)?.Split(',') is [var a, var b] && int.TryParse(a, out int w) && int.TryParse(b, out int h))
                size = new Size(Math.Max(w, minimum.Width), Math.Max(h, minimum.Height));
            // Approximate for now, so the constructor can lay out; OnHandleCreated applies it exactly
            MinimumSize = Fit(Window(minimum));
            Size = Fit(Window(size));
            _restore = (size, minimum);

            FormClosing += (_, _) =>
            {
                if (WindowState != FormWindowState.Normal) return;
                float k = 96f / DeviceDpi;
                AppConfig.Set(key, $"{(int)Math.Round(ClientSize.Width * k)},{(int)Math.Round(ClientSize.Height * k)}");
            };
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            // Moved to a monitor with a different scale factor
            float s = (float)e.DeviceDpiNew / e.DeviceDpiOld;
            var client = ClientSize;
            base.OnDpiChanged(e);
            _layoutDpi = e.DeviceDpiNew;
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
