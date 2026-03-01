using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging.LogWriters
{
    internal sealed class SqliteDurableSink : IDurableLogSink
    {
        public const int SqliteBusyTimeoutMs = 3000;

        private readonly SqliteConnection connection;
        private readonly SqliteCommand insertCommand;

        public string FilePath { get; }

        public SqliteDurableSink(string logDirectory) {
            ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
            Directory.CreateDirectory(logDirectory);

            FilePath = Path.Combine(
                logDirectory,
                DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".sqlite3");

            connection = new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource = FilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
            connection.Open();

            ApplyPragmas();
            EnsureSchema();

            insertCommand = connection.CreateCommand();
            insertCommand.CommandText =
                """
                INSERT INTO log_records(
                    timestamp_utc,
                    level,
                    event_id,
                    role,
                    category,
                    server,
                    message,
                    exception,
                    source_file_path,
                    member_name,
                    source_line_number,
                    has_trace_context,
                    correlation_id,
                    trace_id,
                    span_id,
                    metadata_json,
                    metadata_overflowed
                )
                VALUES(
                    $timestamp_utc,
                    $level,
                    $event_id,
                    $role,
                    $category,
                    $server,
                    $message,
                    $exception,
                    $source_file_path,
                    $member_name,
                    $source_line_number,
                    $has_trace_context,
                    $correlation_id,
                    $trace_id,
                    $span_id,
                    $metadata_json,
                    $metadata_overflowed
                );
                """;

            insertCommand.Parameters.Add("$timestamp_utc", SqliteType.Text);
            insertCommand.Parameters.Add("$level", SqliteType.Integer);
            insertCommand.Parameters.Add("$event_id", SqliteType.Integer);
            insertCommand.Parameters.Add("$role", SqliteType.Text);
            insertCommand.Parameters.Add("$category", SqliteType.Text);
            insertCommand.Parameters.Add("$server", SqliteType.Text);
            insertCommand.Parameters.Add("$message", SqliteType.Text);
            insertCommand.Parameters.Add("$exception", SqliteType.Text);
            insertCommand.Parameters.Add("$source_file_path", SqliteType.Text);
            insertCommand.Parameters.Add("$member_name", SqliteType.Text);
            insertCommand.Parameters.Add("$source_line_number", SqliteType.Integer);
            insertCommand.Parameters.Add("$has_trace_context", SqliteType.Integer);
            insertCommand.Parameters.Add("$correlation_id", SqliteType.Text);
            insertCommand.Parameters.Add("$trace_id", SqliteType.Text);
            insertCommand.Parameters.Add("$span_id", SqliteType.Text);
            insertCommand.Parameters.Add("$metadata_json", SqliteType.Text);
            insertCommand.Parameters.Add("$metadata_overflowed", SqliteType.Integer);
            insertCommand.Prepare();
        }

        private void ApplyPragmas() {
            using SqliteCommand pragma = connection.CreateCommand();
            pragma.CommandText =
                $"""
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA busy_timeout={SqliteBusyTimeoutMs};
                """;
            pragma.ExecuteNonQuery();
        }

        private void EnsureSchema() {
            using SqliteCommand create = connection.CreateCommand();
            create.CommandText =
                """
                CREATE TABLE IF NOT EXISTS log_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp_utc TEXT NOT NULL,
                    level INTEGER NOT NULL,
                    event_id INTEGER NOT NULL,
                    role TEXT NOT NULL,
                    category TEXT NOT NULL,
                    server TEXT NULL,
                    message TEXT NOT NULL,
                    exception TEXT NULL,
                    source_file_path TEXT NULL,
                    member_name TEXT NULL,
                    source_line_number INTEGER NULL,
                    has_trace_context INTEGER NOT NULL,
                    correlation_id TEXT NULL,
                    trace_id TEXT NULL,
                    span_id TEXT NULL,
                    metadata_json TEXT NULL,
                    metadata_overflowed INTEGER NOT NULL
                );
                """;
            create.ExecuteNonQuery();
        }

        public void WriteBatch(ReadOnlySpan<QueuedDurableLogRecord> records) {
            if (records.Length <= 0) {
                return;
            }

            using SqliteTransaction transaction = connection.BeginTransaction();
            insertCommand.Transaction = transaction;
            for (int i = 0; i < records.Length; i++) {
                WriteOne(in records[i]);
            }

            transaction.Commit();
            insertCommand.Transaction = null;
        }

        private void WriteOne(scoped in QueuedDurableLogRecord record) {
            SqliteParameterCollection p = insertCommand.Parameters;
            p["$timestamp_utc"].Value = record.TimestampUtc.ToString("O", CultureInfo.InvariantCulture);
            p["$level"].Value = (int)record.Level;
            p["$event_id"].Value = record.EventId;
            p["$role"].Value = record.Role;
            p["$category"].Value = record.Category;
            p["$server"].Value = record.ServerContext is string serverContext ? serverContext : DBNull.Value;
            p["$message"].Value = record.Message;
            p["$exception"].Value = record.Exception?.ToString() is string exceptionText ? exceptionText : DBNull.Value;
            p["$source_file_path"].Value = record.SourceFilePath is string sourceFilePath ? sourceFilePath : DBNull.Value;
            p["$member_name"].Value = record.MemberName is string memberName ? memberName : DBNull.Value;
            p["$source_line_number"].Value = record.SourceLineNumber.HasValue ? record.SourceLineNumber.Value : DBNull.Value;
            p["$has_trace_context"].Value = record.HasTraceContext ? 1 : 0;
            p["$metadata_overflowed"].Value = record.MetadataOverflowed ? 1 : 0;
            p["$metadata_json"].Value = SerializeMetadata(record.Metadata) is string metadataJson ? metadataJson : DBNull.Value;

            if (record.HasTraceContext) {
                p["$correlation_id"].Value = record.TraceContext.CorrelationId.ToString("D");
                p["$trace_id"].Value = record.TraceContext.TraceId.ToString();
                p["$span_id"].Value = record.TraceContext.SpanId.ToString();
            }
            else {
                p["$correlation_id"].Value = DBNull.Value;
                p["$trace_id"].Value = DBNull.Value;
                p["$span_id"].Value = DBNull.Value;
            }

            insertCommand.ExecuteNonQuery();
        }

        private static string? SerializeMetadata(ReadOnlySpan<KeyValueMetadata> metadata) {
            if (metadata.Length <= 0) {
                return null;
            }

            Dictionary<string, string> dictionary = new(metadata.Length, StringComparer.Ordinal);
            for (int i = 0; i < metadata.Length; i++) {
                KeyValueMetadata entry = metadata[i];
                dictionary[entry.Key] = entry.Value;
            }

            return JsonSerializer.Serialize(dictionary);
        }

        public void Flush() {
        }

        public void Dispose() {
            insertCommand.Dispose();
            connection.Dispose();
        }
    }
}
