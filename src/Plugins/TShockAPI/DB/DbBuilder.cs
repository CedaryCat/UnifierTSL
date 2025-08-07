using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using MySql.Data.MySqlClient;
using Npgsql;
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
            string dbFilePath = Path.Combine(savePath, config.GlobalSettings.SqliteDBPath);

            if (Path.GetDirectoryName(dbFilePath) is not { } dbDirPath) {
                throw new DirectoryNotFoundException($"The SQLite database path '{dbFilePath}' could not be found.");
            }

            Directory.CreateDirectory(dbDirPath);

            return new(ProviderName.SQLiteMS, $"Data Source={dbFilePath}");
        }
        private DataConnection BuildMySqlConnection() {
            try {
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
            catch (MySqlException e) {
                TShock.Log.Error(e.ToString());
                throw new("MySql not setup correctly", e);
            }
        }

        private DataConnection BuildPostgresConnection() {
            try {
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
            catch (NpgsqlException e) {
                TShock.Log.Error(e.ToString());
                throw new("Postgres not setup correctly", e);
            }
        }
    }
}
