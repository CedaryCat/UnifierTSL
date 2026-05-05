using UnifierTSL.Logging.Formatters.ConsoleLog;
using UnifierTSL.Servers;

namespace UnifierTSL.Logging.LogWriters
{
    internal sealed class ServerConsoleLogWriter(ServerContext server) : LogWriter<ColoredSegment>(new DefConsoleFormatter())
    {
        private readonly ServerContext server = server;

        public override void Write(scoped in LogEntry raw, Span<ColoredSegment> input) {
            if (input.Length == 0) {
                return;
            }

            string ansiText = ConsoleLogWriter.BuildAnsiLine(input);
            server.Console.WriteAnsi(ansiText);
        }
    }
}
