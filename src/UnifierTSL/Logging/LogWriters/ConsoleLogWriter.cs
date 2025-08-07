using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging.Formatters.ConsoleLog;
using UnifierTSL.Servers;

namespace UnifierTSL.Logging.LogWriters
{
    public class ConsoleLogWriter() : LogWriter<ColoredSegment>(new DefConsoleFormatter())
    {
        public readonly static ConsoleLogWriter Instance = new ConsoleLogWriter();
        public sealed override void Write(scoped in LogEntry raw, Span<ColoredSegment> input) {

            int count = input.Length;
            if (count == 0) return;

            var serverName = raw.GetMetadata("ServerContext");
            ServerContext? server;
            if (serverName is not null) {
                server = UnifiedServerCoordinator.Servers.FirstOrDefault(s => s.Name == serverName);
            }
            else {
                server = null;
            }

            if (server is null) {
                ref var element0 = ref MemoryMarshal.GetReference(input);
                for (int i = 0; i < count; i++) {
                    var element = Unsafe.Add(ref element0, i);
                    lock (SynchronizedGuard.ConsoleLock) {
                        Console.BackgroundColor = element.BackgroundColor;
                        Console.ForegroundColor = element.ForegroundColor;
                        Console.Write(element.Text);
                        Console.ResetColor();
                    }
                }
                return;
            }
            else {
                ref var element0 = ref MemoryMarshal.GetReference(input);
                for (int i = 0; i < count; i++) {
                    var element = Unsafe.Add(ref element0, i);
                    lock (SynchronizedGuard.ConsoleLock) {
                        server.Console.BackgroundColor = element.BackgroundColor;
                        server.Console.ForegroundColor = element.ForegroundColor;
                        server.Console.Write(element.Text);
                        server.Console.ResetColor();
                    }
                }
            }
        }
    }
}
