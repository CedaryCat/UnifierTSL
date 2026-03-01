using System.Globalization;
using System.Text;

namespace UnifierTSL.Logging.LogWriters
{
    internal sealed class TextDurableSink : IDurableLogSink
    {
        private readonly string logDirectory;
        private readonly string sessionFileName;
        private readonly StreamWriter mainWriter;
        private readonly Dictionary<string, StreamWriter> serverWriters = new(StringComparer.OrdinalIgnoreCase);
        private readonly StringBuilder builder = new();

        public string FilePath { get; }

        public TextDurableSink(string logDirectory) {
            ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
            Directory.CreateDirectory(logDirectory);
            this.logDirectory = logDirectory;

            sessionFileName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".log";
            FilePath = Path.Combine(logDirectory, sessionFileName);

            mainWriter = new StreamWriter(
                new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                Encoding.UTF8) {
                AutoFlush = false,
            };
        }

        public void WriteBatch(ReadOnlySpan<QueuedDurableLogRecord> records) {
            for (int i = 0; i < records.Length; i++) {
                WriteCore(in records[i]);
            }
        }

        private void WriteCore(scoped in QueuedDurableLogRecord record) {
            string[] lines = SplitLines(record.Message);
            builder.Clear();
            builder.Append(record.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append('[').Append(record.Level).Append(']');
            builder.Append('[').Append(record.Role);
            if (!string.IsNullOrEmpty(record.Category)) {
                builder.Append('|').Append(record.Category);
            }
            builder.Append(']');

            string? serverContext = record.ServerContext;
            if (!string.IsNullOrWhiteSpace(serverContext)) {
                serverContext = serverContext.Trim();
                builder.Append(' ').Append('[').Append(GetString("Server")).Append(':').Append(serverContext).Append(']');
            }

            if (lines.Length == 0) {
                builder.Append(' ');
            }
            else {
                builder.Append(' ').Append(lines[0]);
            }

            StreamWriter targetWriter = GetWriter(serverContext);
            targetWriter.WriteLine(builder.ToString());

            for (int i = 1; i < lines.Length; i++) {
                targetWriter.WriteLine(" | " + lines[i]);
            }

            if (record.MetadataOverflowed) {
                targetWriter.WriteLine(" | [" + GetString("Metadata truncated due to per-entry limit") + "]");
            }

            if (record.Exception is not null) {
                foreach (string exceptionLine in SplitLines(record.Exception.ToString())) {
                    targetWriter.WriteLine(" | " + exceptionLine);
                }
            }
        }

        private StreamWriter GetWriter(string? serverContext) {
            if (string.IsNullOrWhiteSpace(serverContext)) {
                return mainWriter;
            }

            string normalized = serverContext.Trim();
            if (serverWriters.TryGetValue(normalized, out StreamWriter? existing)) {
                return existing;
            }

            string serverDirName = BuildServerDirectoryName(normalized);
            string serverDir = Path.Combine(logDirectory, serverDirName);
            Directory.CreateDirectory(serverDir);
            string serverFilePath = Path.Combine(serverDir, sessionFileName);

            StreamWriter writer = new(
                new FileStream(serverFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                Encoding.UTF8) {
                AutoFlush = false,
            };
            serverWriters.Add(normalized, writer);
            return writer;
        }

        private static string BuildServerDirectoryName(string serverContext) {
            return SanitizePathSegment(serverContext);
        }

        private static string SanitizePathSegment(string value) {
            StringBuilder sanitized = new(value.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                if (Array.IndexOf(invalid, c) >= 0) {
                    // Escape invalid file-name characters using a reversible ASCII token.
                    sanitized.Append("~u");
                    sanitized.Append(((ushort)c).ToString("X4", CultureInfo.InvariantCulture));
                }
                else {
                    sanitized.Append(c);
                }
            }

            string result = sanitized.ToString().Trim();
            return result.Length == 0 ? "_" : result;
        }

        private static string[] SplitLines(string value) {
            return value.Split(["\r\n", "\n"], StringSplitOptions.None);
        }

        public void Flush() {
            mainWriter.Flush();
            foreach (StreamWriter writer in serverWriters.Values) {
                writer.Flush();
            }
        }

        public void Dispose() {
            mainWriter.Dispose();
            foreach (StreamWriter writer in serverWriters.Values) {
                writer.Dispose();
            }

            serverWriters.Clear();
        }
    }
}
