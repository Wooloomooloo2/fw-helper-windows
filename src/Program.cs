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

            // Command line: talks to the running instance, never replaces it
            if (CommandProtocol.IsCommand(args))
            {
                Environment.ExitCode = Cli.Run(args);
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

            FanControl.Init();
            GovernorControl.Init(); // undo frequency caps / EC thresholds a crashed run left behind
            ApplyAll();
            BatteryLog.Start();
            PowerModeWatcher.Resynced += (had, restored) => SettingsForm.BeginInvoke(() =>
                Balloon(null, "Windows power mode put back",
                    $"Windows switched to {had}. The profile {Modes.Name(ModeControl.CurrentMode)} uses {restored}, so FW-Helper put it back. " +
                    "To let Windows' setting win, untick \"Keep Windows in step\" in Fans + Power.", ToolTipIcon.Info));
            PowerModeWatcher.Start();
            TrayIcon.BalloonTipClicked += (_, _) => { var action = _balloonAction; _balloonAction = null; action?.Invoke(); };
            // Stopped from the Monitor, the notes window is already open: no notification then
            Telemetry.RecordingStopped += (path, rows) => { if (rows >= 10 && !IsExiting) SettingsForm.BeginInvoke(() => { if (!NotesForm.IsOpenFor(path))
                Balloon(() => NotesForm.ShowFor(path, offerSummary: true), "Recording saved",
                    $"{Path.GetFileName(path)}, {rows / 60} min {rows % 60:00} s. Click to add the game settings and see its summary.", ToolTipIcon.Info); }); };

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
            CommandServer.Start(SettingsForm);

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
            menu.Items.Add(new ToolStripSeparator());
            foreach (int limit in new[] { 60, 80, 100 })
                menu.Items.Add(new ToolStripMenuItem($"Charge limit {limit}%", null, (_, _) => BatteryControl.SetLimit(limit)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open", null, (_, _) => SettingsForm.ShowAtTray());
            menu.Items.Add("Monitor", null, (_, _) => ShowMonitor());
            menu.Items.Add("Battery", null, (_, _) => ShowBattery());
            var overlayItem = new ToolStripMenuItem("Overlay", null, (_, _) => ToggleOverlay());
            menu.Items.Add(overlayItem);
            menu.Opening += (_, _) => overlayItem.Checked = _overlay is { IsDisposed: false };
            var recording = new ToolStripMenuItem("Start recording", null, (_, _) => ToggleRecording()) { ShortcutKeyDisplayString = "Ctrl+Shift+F6" };
            menu.Items.Add(recording);
            menu.Items.Add("Open log", null, (_, _) => ProcessHelper.OpenUrl(Logger.LogFile));
            menu.Items.Add("Quit", null, (_, _) => Exit());
            menu.Opening += (_, _) =>
            {
                // Profiles can be added or renamed at any time: rebuild them on every open
                ProfileMenu.Fill(menu.Items, 0);
                recording.Text = Telemetry.IsRecording ? "Stop recording" : "Start recording";
            };
            return menu;
        }

        /// <summary>
        /// Ctrl+Shift+F6 and the tray: start or stop recording without a prompt (it can't ask inside a game). The file is named
        /// after the game if one is presenting frames, and the profile. A toast says what happened; the overlay shows REC.
        /// </summary>
        public static void ToggleRecording()
        {
            if (Telemetry.IsRecording)
            {
                Telemetry.StopRecording();
                Toast.ShowToast("■ Recording saved", Color.FromArgb(120, 120, 120));
                return;
            }
            string? game = Telemetry.History().LastOrDefault(s => s.Fps > 0 && s.Time > DateTime.Now.AddSeconds(-10))?.FpsApp;
            string name = string.Join(" ", new[] { game is null ? null : Path.GetFileNameWithoutExtension(game), Modes.Name(ModeControl.CurrentMode) }
                .Where(x => !string.IsNullOrEmpty(x)));
            Telemetry.StartRecording(name);
            if (Telemetry.IsRecording) Toast.ShowToast("● Recording", Color.FromArgb(235, 70, 70));
        }

        private static OverlayForm? _overlay;

        public static void ToggleOverlay()
        {
            if (_overlay is { IsDisposed: false })
            {
                _overlay.Close();
                return;
            }
            _overlay = new OverlayForm();
            _overlay.FormClosed += (_, _) => _overlay = null;
            _overlay.Show();
        }

        private static Action? _balloonAction;

        /// <summary>A tray balloon; <paramref name="onClick"/> runs if it is clicked (only the latest balloon's action is kept).</summary>
        private static void Balloon(Action? onClick, string title, string text, ToolTipIcon icon)
        {
            _balloonAction = onClick;
            TrayIcon.ShowBalloonTip(5000, title, text, icon);
        }

        private static MonitorForm? _monitor;

        public static void ShowMonitor()
        {
            if (_monitor is null || _monitor.IsDisposed)
            {
                _monitor = new MonitorForm();
                _monitor.FormClosed += (_, _) => _monitor = null;
            }
            _monitor.Show();
            if (_monitor.WindowState == FormWindowState.Minimized) _monitor.WindowState = FormWindowState.Normal;
            _monitor.KeepOnScreen();
            _monitor.Activate();
        }

        private static BatteryForm? _battery;

        public static void ShowBattery()
        {
            if (_battery is null || _battery.IsDisposed)
            {
                _battery = new BatteryForm();
                _battery.FormClosed += (_, _) => _battery = null;
            }
            _battery.Show();
            if (_battery.WindowState == FormWindowState.Minimized) _battery.WindowState = FormWindowState.Normal;
            _battery.KeepOnScreen();
            _battery.Activate();
        }

        public static void UpdateTrayText(int? cpu, int fanRpm, BatteryInfo? batt)
        {
            string text = $"FW-Helper · {Modes.Name(ModeControl.CurrentMode)} · Windows {PowerModeWatcher.Status.ShortLabel}\nCPU {(cpu is null ? "-" : cpu + "°C")} · Fan {fanRpm} RPM";
            if (batt is { Present: true }) text += $"\nBattery {batt.Percent}% {(batt.Discharging ? $"{batt.Watts:0.0}W" : "")}";
            if (BatteryLog.TrayLine() is string line) text += "\n" + line;
            TrayIcon.Text = text.Length > 127 ? text[..127] : text;
        }

        /// <summary>Return the fans to EC control. Safe to call many times / from any thread.</summary>
        private static void SafeShutdown()
        {
            try { FanControl.Stop(); } catch { }
            try { GovernorControl.Shutdown(); } catch { }
            try { Telemetry.StopRecording(); } catch { }
            try { BatteryLog.Stop(); } catch { }
            try { PowerModeWatcher.Stop(); } catch { }
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
