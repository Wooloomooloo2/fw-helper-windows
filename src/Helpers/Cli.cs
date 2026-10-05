using FwHelper.Hardware;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace FwHelper.Helpers
{
    /// <summary>
    /// "FwHelper.exe --status | --profiles | --mode X | --charge-limit N | --fan-floor on|off" (ADR 0015).
    /// The exe is a GUI app, so it attaches to the parent console to print. Changes go to the running tray app over a pipe;
    /// --status and --profiles also work without it.
    /// </summary>
    public static class Cli
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        public static int Run(string[] args)
        {
            // Redirected output (| Out-String, > file) already works; otherwise borrow the parent console
            if (!Console.IsOutputRedirected) AttachConsole(-1 /* ATTACH_PARENT_PROCESS */);
            Console.WriteLine();

            var (command, error) = CommandProtocol.Parse(args);
            if (command is null)
            {
                Console.WriteLine(error);
                return error == CommandProtocol.Usage ? 0 : 2;
            }

            string? reply = Send(command);
            if (reply is not null)
            {
                Console.WriteLine(reply);
                return reply.StartsWith("error:", StringComparison.Ordinal) ? 1 : 0;
            }

            // Tray app not running: read-only commands still work against the EC directly
            switch (command.Verb)
            {
                case "status":
                    if (!FrameworkEc.Connect()) { Console.WriteLine("error: can't reach the Framework EC driver"); return 1; }
                    Console.WriteLine(CommandServer.Status(running: false));
                    return 0;
                case "profiles":
                    Console.WriteLine(CommandServer.Profiles());
                    return 0;
                default:
                    Console.WriteLine("error: FW-Helper isn't running, or is a version without the command line (start or update it, then try again)");
                    return 1;
            }
        }

        private static string? Send(CliCommand command)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", CommandProtocol.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
                pipe.Connect(500);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                writer.WriteLine(CommandProtocol.Encode(command));
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (TimeoutException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
