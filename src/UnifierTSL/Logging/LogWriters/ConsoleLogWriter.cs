using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnifierTSL.CLI;
using UnifierTSL.ConsoleClient.Shell;
using UnifierTSL.Logging.Formatters.ConsoleLog;
using UnifierTSL.Servers;

namespace UnifierTSL.Logging.LogWriters
{
    public class ConsoleLogWriter() : LogWriter<ColoredSegment>(new DefConsoleFormatter())
    {
        public static readonly ConsoleLogWriter Instance = new();
        private static string BuildAnsiLine(Span<ColoredSegment> input) {
            StringBuilder builder = new();
            ref ColoredSegment element0 = ref MemoryMarshal.GetReference(input);
            for (int i = 0; i < input.Length; i++) {
                ColoredSegment element = Unsafe.Add(ref element0, i);
                builder.Append(AnsiColorCodec.GetSgr(element.ForegroundColor, element.BackgroundColor));
                builder.Append(AnsiSanitizer.SanitizeEscapes(element.Text.ToString()));
            }
            builder.Append(AnsiColorCodec.Reset);
            return builder.ToString();
        }

        public sealed override void Write(scoped in LogEntry raw, Span<ColoredSegment> input) {
            int count = input.Length;
            if (count == 0) return;
            string ansiText = BuildAnsiLine(input);

            string? serverName = raw.GetMetadata("ServerContext");
            ServerContext? server;
            if (serverName is not null) {
                server = UnifiedServerCoordinator.Servers.FirstOrDefault(s => s.Name == serverName);
            }
            else {
                server = null;
            }

            if (server is null) {
                ConsoleInput.WriteAnsi(ansiText);
                return;
            }

            if (server.Console is ConsoleClientLauncher launcher) {
                launcher.WriteAnsi(ansiText);
                return;
            }

            server.Console.Write(AnsiSanitizer.StripAnsi(ansiText));
        }
    }
}
