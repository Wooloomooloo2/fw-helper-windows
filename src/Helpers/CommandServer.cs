using FwHelper.Features;
using FwHelper.Hardware;
using System.IO.Pipes;
using System.Text;

namespace FwHelper.Helpers
{
    /// <summary>
    /// Named pipe the CLI talks to (ADR 0015). Current user only. One request line in, a text reply out; commands run on the UI thread.
    /// Every request is validated again here: the pipe is an input like any other.
    /// </summary>
    public static class CommandServer
    {
        private static Control? _ui;

        public static void Start(Control uiThread)
        {
            _ui = uiThread;
            var thread = new Thread(Serve) { IsBackground = true, Name = "CommandServer" };
            thread.Start();
        }

        private static void Serve()
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(CommandProtocol.PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

                    var command = CommandProtocol.Decode(reader.ReadLine());
                    string reply = command is null ? "error: unknown command" : Run(command);
                    writer.Write(reply);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Command server: " + ex.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        private static string Run(CliCommand c)
        {
            if (_ui is null || !_ui.IsHandleCreated) return "error: not ready";
            return (string)_ui.Invoke(() => Execute(c));
        }

        /// <summary>Runs on the UI thread.</summary>
        private static string Execute(CliCommand c)
        {
            Logger.WriteLine($"CLI: {c.Verb} {c.Arg}");
            switch (c.Verb)
            {
                case "status":
                    return Status(running: true);

                case "profiles":
                    return Profiles();

                case "mode":
                    if (CommandProtocol.ResolveProfile(c.Arg ?? "", Modes.All().Select(id => (id, Modes.Name(id)))) is not int mode)
                        return $"error: no profile '{c.Arg}' (see --profiles)";
                    ModeControl.SetMode(mode);
                    return $"mode: {Modes.Name(mode)}";

                case "charge-limit":
                    if (!int.TryParse(c.Arg, out int limit) || limit is < BatteryControl.MinLimit or > 100) return "error: charge limit must be 50-100";
                    BatteryControl.SetLimit(limit);
                    return $"charge limit: {BatteryControl.GetLimit()}%";

                case "fan-floor":
                    bool? on = c.Arg?.ToLowerInvariant() switch { "on" => true, "off" => false, _ => null };
                    if (on is null) return "error: fan-floor needs on or off";
                    FanControl.SetFloorEnabled(on.Value);
                    return $"fan floor: {(on.Value ? "on" : "off")}";

                default:
                    return "error: unknown command";
            }
        }

        public static string Profiles() =>
            string.Join("\n", Modes.All().Select(id => $"{id,3}  {Modes.Name(id)}{(id == ModeControl.CurrentMode ? "  (active)" : "")}"));

        /// <summary>Also used by the CLI directly when FW-Helper isn't running (then the mode is the saved one).</summary>
        public static string Status(bool running)
        {
            var sb = new StringBuilder();
            var temps = FrameworkEc.GetTemperatures();
            int mode = running ? ModeControl.CurrentMode : ModeControl.SavedMode();
            sb.AppendLine($"FW-Helper {Application.ProductVersion.Split('+')[0]} | EC {FrameworkEc.GetVersion() ?? "?"} | {(running ? "running" : "not running")}");
            sb.AppendLine($"mode       {Modes.Name(mode)}{(running ? "" : " (saved)")} | {(PowerNative.IsOnAC() ? "AC" : "battery")}");
            sb.AppendLine($"cpu        {FrameworkEc.GetCpuTemp(temps)?.ToString() ?? "-"} C");
            sb.AppendLine($"sensors    {string.Join("  ", temps.Select(t => $"{t.Name.Split('@')[0]} {t.Celsius?.ToString() ?? "-"}"))}");
            string owner = FrameworkEc.IsFanAuto() switch { true => "EC", false => "FW-Helper", null => "?" };
            sb.AppendLine($"fan        {FrameworkEc.GetFanRpms().FirstOrDefault()} rpm | {FrameworkEc.GetFanDuty()?.ToString() ?? "-"}% | owner {owner}" +
                          (running && FanControl.IsCustomActive ? $" | {FanControl.Status}" : ""));
            sb.AppendLine($"fan floor  {(FanControl.IsFloorEnabled() ? "on" : "off")}");
            if (FrameworkEc.GetBattery() is { Present: true } b)
                sb.AppendLine($"battery    {b.Percent}% | {(b.Charging ? "charging" : b.Discharging ? $"{-b.Watts:0.0} W draw" : "idle")} | limit {FrameworkEc.GetChargeLimit()?.max.ToString() ?? "-"}% | health {b.HealthPercent}%");
            return sb.ToString().TrimEnd();
        }
    }
}
