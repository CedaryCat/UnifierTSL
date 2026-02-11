using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using MySql.Data.MySqlClient;
using Npgsql;
using System.Data.Common;
using TShockAPI.Configuration;

namespace TShockAPI.DB
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbBuilder"/> class.
    /// </summary>
    /// <param name="caller">The TShock instance calling this DbBuilder.</param>
    /// <param name="config">The TShock configuration, supplied by <see cref="TShock.Config" /> at init.</param>
    /// <param name="savePath">The savePath registered by TShock. See <see cref="TShock.SavePath" />.</param>
    public class DbBuilder(TShock caller, TShockConfig config, string savePath)
    {
        /// <summary>
        /// Builds a DB connection based on the provided configuration.
        /// </summary>
        /// <param name="config">The TShock configuration.</param>
        /// <remarks>
        /// Default settings will result in a local sqlite database file named "tshock.db" in the current directory to be used as server DB.
        /// </remarks>
        public DataConnection BuildDbConnection() {
            string dbType = config.GlobalSettings.StorageType.ToLowerInvariant();

            return dbType switch {
                "sqlite" => BuildSqliteConnection(),
                "mysql" => BuildMySqlConnection(),
                "postgres" => BuildPostgresConnection(),
                _ => throw new("Invalid storage type")
            };
        }
        private DataConnection BuildSqliteConnection() {
            if (!string.IsNullOrWhiteSpace(config.GlobalSettings.SqliteConnectionString)) {
                var connBuilder = new DbConnectionStringBuilder {
                    ConnectionString = config.GlobalSettings.SqliteConnectionString
                };

                if (TryGetSqliteDataSource(connBuilder, out var key, out var dataSource)) {
                    connBuilder[key] = ResolveSqliteDataSource(dataSource);
                }

                return new(ProviderName.SQLiteMS, connBuilder.ConnectionString);
            }

            string dbFilePath = ResolveSqliteDataSource(config.GlobalSettings.SqliteDBPath);
            return new(ProviderName.SQLiteMS, $"Data Source={dbFilePath}");
        }

        private string ResolveSqliteDataSource(string dataSource) {
            if (string.IsNullOrWhiteSpace(dataSource))
                throw new DirectoryNotFoundException("The SQLite database path is empty.");

            if (dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
                dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) {
                return dataSource;
            }

            string dbFilePath = Path.IsPathRooted(dataSource)
                ? dataSource
                : Path.Combine(savePath, dataSource);
            dbFilePath = Path.GetFullPath(dbFilePath);

            if (Path.GetDirectoryName(dbFilePath) is not { } dbDirPath) {
                throw new DirectoryNotFoundException($"The SQLite database path '{dbFilePath}' could not be found.");
            }

            Directory.CreateDirectory(dbDirPath);
            return dbFilePath;
        }

        private static bool TryGetSqliteDataSource(DbConnectionStringBuilder builder, out string key, out string value) {
            foreach (var candidate in new[] { "Data Source", "DataSource" }) {
                if (builder.TryGetValue(candidate, out var raw) && raw is not null && !string.IsNullOrWhiteSpace(raw.ToString())) {
                    key = candidate;
                    value = raw.ToString()!;
                    return true;
                }
            }

            key = string.Empty;
            value = string.Empty;
            return false;
        }

        private DataConnection BuildMySqlConnection() {
            try {
                if (!string.IsNullOrWhiteSpace(config.GlobalSettings.MySqlConnectionString)) {
                    var parsedBuilder = new MySqlConnectionStringBuilder(config.GlobalSettings.MySqlConnectionString);
                    return new(ProviderName.MySql, parsedBuilder.ToString());
                }

                string[] hostport = config.GlobalSettings.MySqlHost.Split(':');

                MySqlConnectionStringBuilder connStrBuilder = new() {
                    Server = hostport[0],
                    Port = hostport.Length > 1 ? uint.Parse(hostport[1]) : 3306,
                    Database = config.GlobalSettings.MySqlDbName,
                    UserID = config.GlobalSettings.MySqlUsername,
                    Password = config.GlobalSettings.MySqlPassword
                };

                return new(ProviderName.MySql, connStrBuilder.ToString());
            }
            catch (Exception e) {
                TShock.Log.Error(e.ToString());
                throw new("MySql not setup correctly", e);
            }
        }

        private DataConnection BuildPostgresConnection() {
            try {
                if (!string.IsNullOrWhiteSpace(config.GlobalSettings.PostgresConnectionString)) {
                    var parsedBuilder = new NpgsqlConnectionStringBuilder(config.GlobalSettings.PostgresConnectionString);
                    return new(ProviderName.PostgreSQL, parsedBuilder.ToString());
                }

                string[] hostport = config.GlobalSettings.PostgresHost.Split(':');

                NpgsqlConnectionStringBuilder connStrBuilder = new() {
                    Host = hostport[0],
                    Port = hostport.Length > 1 ? int.Parse(hostport[1]) : 5432,
                    Database = config.GlobalSettings.PostgresDbName,
                    Username = config.GlobalSettings.PostgresUsername,
                    Password = config.GlobalSettings.PostgresPassword
                };

                return new(ProviderName.PostgreSQL, connStrBuilder.ToString());
            }
            catch (Exception e) {
                TShock.Log.Error(e.ToString());
                throw new("Postgres not setup correctly", e);
            }
        }
    }
}
