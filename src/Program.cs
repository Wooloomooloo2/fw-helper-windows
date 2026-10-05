using FwHelper.Features;
using FwHelper.Hardware;
using FwHelper.Helpers;
using FwHelper.UI;
using Microsoft.Win32;

namespace FwHelper
{
    public static class Program
    {
        public static NotifyIcon TrayIcon = null!;
        public static SettingsForm SettingsForm = null!;
        public static ToastForm Toast = null!;
        public static bool IsExiting { get; private set; }

        private static PowerLineStatus _lastLine = SystemInformation.PowerStatus.PowerLineStatus;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Contains(Guardian.Arg))
            {
                Guardian.Run(args);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Whatever happens, never leave the fan stuck at a fixed duty
            AppDomain.CurrentDomain.UnhandledException += (_, e) => { Logger.WriteLine("Crash: " + e.ExceptionObject); SafeShutdown(); };
            Application.ThreadException += (_, e) => Logger.WriteLine("UI exception: " + e.Exception);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeShutdown();

            Logger.WriteLine("------------");
            Logger.WriteLine($"FW-Helper {Application.ProductVersion} starting, admin={ProcessHelper.IsUserAdministrator()}");

            if (args.Contains("--selftest"))
            {
                SelfTest.Run();
                SafeShutdown();
                return;
            }

            if (args.Contains(HardwareTests.Arg))
            {
                // A running tray app would fight the test for the fan and PL MSR: stop it, and bring it back afterwards
                bool wasRunning = ProcessHelper.CloseOtherInstances();
                HardwareTests.Run(args);
                SafeShutdown();
                if (wasRunning) System.Diagnostics.Process.Start(Application.ExecutablePath, "-tray");
                return;
            }

            ProcessHelper.CloseOtherInstances();

            if (!FrameworkEc.Connect())
            {
                var answer = MessageBox.Show(
                    "Can't talk to the Framework embedded controller.\n\n" +
                    "Make sure the Framework driver bundle is installed (it provides the \"Framework EC\" / CrosEC driver).\n\n" +
                    "Open the Framework driver download page?",
                    "FW-Helper", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (answer == DialogResult.Yes) ProcessHelper.OpenUrl("https://knowledgebase.frame.work/");
                return;
            }
            Logger.WriteLine("EC: " + FrameworkEc.GetVersion());
            Guardian.Launch();

            Toast = new ToastForm();
            SettingsForm = new SettingsForm();
            _ = SettingsForm.Handle; // create handle so the global hotkey works while hidden

            TrayIcon = new NotifyIcon { Text = "FW-Helper", Icon = TrayIcons.ForMode(Modes.Balanced, 32), Visible = true };
            TrayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) SettingsForm.Toggle(); };
            TrayIcon.MouseMove += (_, _) => SettingsForm.RefreshSensors();
            TrayIcon.ContextMenuStrip = BuildContextMenu();

            ModeControl.ModeChanged += () => TrayIcon.Icon = TrayIcons.ForMode(ModeControl.CurrentMode, 32);

            ApplyAll();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnding += (_, _) => SafeShutdown();
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General)
                {
                    SettingsForm.ApplyTheme();
                    SettingsForm.FansForm?.ApplyTheme();
                }
            };

            Task.Run(Startup.StartupCheck);

            // Started manually (not from the startup task): show the window right away
            if (!args.Contains("-tray") && Environment.CurrentDirectory.Trim('\\') != Environment.SystemDirectory.Trim('\\'))
                SettingsForm.ShowAtTray();

            Application.Run();
        }

        /// <summary>Apply every saved setting to the hardware (startup, resume).</summary>
        private static void ApplyAll()
        {
            try
            {
                BatteryControl.AutoLimit();
                ModeControl.AutoMode();
                ScreenControl.AutoScreen();

                int kb = AppConfig.Get("kb_brightness");
                if (kb >= 0) FrameworkEc.SetKeyboardBacklight(kb);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("ApplyAll: " + ex);
            }
        }

        private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    Logger.WriteLine("Suspend");
                    FanControl.Stop();
                    break;

                case PowerModes.Resume:
                    Logger.WriteLine("Resume");
                    Task.Delay(3000).ContinueWith(_ => SettingsForm.BeginInvoke(ApplyAll));
                    break;

                case PowerModes.StatusChange:
                    var line = SystemInformation.PowerStatus.PowerLineStatus;
                    if (line == _lastLine) return;
                    _lastLine = line;
                    Logger.WriteLine("Power source: " + line);
                    // Let the charger settle, then switch to the mode saved for this power source
                    Task.Delay(1500).ContinueWith(_ => SettingsForm.BeginInvoke(() =>
                    {
                        ModeControl.AutoMode();
                        ScreenControl.AutoScreen();
                    }));
                    break;
            }
        }

        private static ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            var modeItems = new ToolStripMenuItem[Modes.Count];
            for (int i = 0; i < Modes.Count; i++)
            {
                int mode = i;
                modeItems[i] = new ToolStripMenuItem(Modes.Names[i], null, (_, _) => ModeControl.SetMode(mode));
                menu.Items.Add(modeItems[i]);
            }
            menu.Items.Add(new ToolStripSeparator());
            foreach (int limit in new[] { 60, 80, 100 })
                menu.Items.Add(new ToolStripMenuItem($"Charge limit {limit}%", null, (_, _) => BatteryControl.SetLimit(limit)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open", null, (_, _) => SettingsForm.ShowAtTray());
            menu.Items.Add("Open log", null, (_, _) => ProcessHelper.OpenUrl(Logger.LogFile));
            menu.Items.Add("Quit", null, (_, _) => Exit());
            menu.Opening += (_, _) =>
            {
                for (int i = 0; i < Modes.Count; i++) modeItems[i].Checked = i == ModeControl.CurrentMode;
            };
            return menu;
        }

        public static void UpdateTrayText(int? cpu, int fanRpm, BatteryInfo? batt)
        {
            string text = $"FW-Helper · {Modes.Name(ModeControl.CurrentMode)}\nCPU {(cpu is null ? "-" : cpu + "°C")} · Fan {fanRpm} RPM";
            if (batt is { Present: true }) text += $"\nBattery {batt.Percent}% {(batt.Discharging ? $"{batt.Watts:0.0}W" : "")}";
            TrayIcon.Text = text.Length > 127 ? text[..127] : text;
        }

        /// <summary>Return the fans to EC control. Safe to call many times / from any thread.</summary>
        private static void SafeShutdown()
        {
            try { FanControl.Stop(); } catch { }
            try { PowerLimitControl.Shutdown(); } catch { }
        }

        public static void Exit()
        {
            IsExiting = true;
            SafeShutdown();
            if (TrayIcon is not null)
            {
                TrayIcon.Visible = false;
                TrayIcon.Dispose();
            }
            Application.Exit();
        }
    }
}
