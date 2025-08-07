using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging.Formatters.ConsoleLog
{
    public class DefConsoleFormatter : ILogFormatter<ColoredSegment>
    {
        public string FormatName => "Default Console Formatter";
        public string Description => "A default console formatter implementation";

        static readonly char[] separator = ['\r', '\n'];

        public void Format(scoped in LogEntry entry, scoped ref ColoredSegment buffer, out int written) {
            written = 0;

            string levelText = entry.Level switch {
                LogLevel.Trace =>    "[Trace]",
                LogLevel.Debug =>    "[Debug]",
                LogLevel.Info =>     "[+Info]",
                LogLevel.Success =>  "[Succe]",
                LogLevel.Warning =>  "[+Warn]",
                LogLevel.Error =>    "[Error]",
                LogLevel.Critical => "[Criti]",
                _ =>                 "[+-·-+]"
            };
            string roleCatText = string.IsNullOrEmpty(entry.Category) 
                ? $"[{entry.Role}]" 
                : $"[{entry.Role}|{entry.Category}]";

            ConsoleColor fgLevel = GetLevelColor(entry.Level);
            ConsoleColor bg = ConsoleColor.Black;

            // Segment 0: [Level]
            Unsafe.Add(ref buffer, written) = new(levelText.AsMemory(), fgLevel, bg);
            written++;

            // Segment 1: [Role|Category]
            Unsafe.Add(ref buffer, written) = new(roleCatText.AsMemory(), GetRoleCategoryFg(in entry), GetRoleCategoryBg(in entry));
            written++;

            // Segment 2: Main Message
            string[] lines = entry.Message.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            string messageText = lines.Length == 1
                ? $" {lines[0]}\r\n"
                : $" {lines[0]}\r\n{FormatMultiline(lines.Skip(1))}\r\n";

            Unsafe.Add(ref buffer, written) = new(messageText.AsMemory(), fgLevel, bg);
            written++;

            // Segment 3: Exception (optional)
            if (entry.Exception != null) {
                bool isHandled = entry.Level <= LogLevel.Warning;
                string label = isHandled ? "Handled Exception:" : "Unexpected Exception:";
                string[] exceptionLines = entry.Exception.ToString().Split(separator, StringSplitOptions.RemoveEmptyEntries);

                var sb = new StringBuilder();
                sb.AppendLine($" │ {label}");

                for (int i = 0; i < exceptionLines.Length; i++) {
                    if (i == exceptionLines.Length - 1)
                        sb.AppendLine($" └── {exceptionLines[i]}");
                    //else if (i == 0)
                    //    sb.AppendLine($" ├── {exceptionLines[i]}");
                    else
                        sb.AppendLine($" │   {exceptionLines[i]}");
                }

                Unsafe.Add(ref buffer, written) = new(sb.ToString().AsMemory(), ConsoleColor.Red, ConsoleColor.White);
                written++;
            }
        }

        static ConsoleColor GetLevelColor(LogLevel level) => level switch {
            LogLevel.Trace => ConsoleColor.Gray,
            LogLevel.Debug => ConsoleColor.Blue,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Success => ConsoleColor.Green,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };

        static ConsoleColor GetRoleCategoryFg(scoped in LogEntry entry) => ConsoleColor.Cyan;
        static ConsoleColor GetRoleCategoryBg(scoped in LogEntry entry) => ConsoleColor.Black;
        static string FormatMultiline(IEnumerable<string> lines) {
            var sb = new StringBuilder();
            var arr = lines.ToArray();
            for (int i = 0; i < arr.Length; i++) {
                if (i == arr.Length - 1)
                    sb.AppendLine($" └── {arr[i]}");
                //else if (i == 0)
                //    sb.AppendLine($" ├── {arr[i]}");
                else
                    sb.AppendLine($" │   {arr[i]}");
            }
            return sb.ToString().TrimEnd();
        }

        public int GetEstimatedSize(scoped in LogEntry entry) {
            return entry.Exception is null ? 3 : 4;
        }
    }
}
