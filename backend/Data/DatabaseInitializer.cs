using System.Globalization;
using System.Net.Sockets;
using Npgsql;

namespace KiComLibBackend.Data
{
    public class DatabaseInitializerOptions
    {
        public string ConnectionString { get; set; } = string.Empty;

        public string ScriptDirectory { get; set; } = string.Empty;

        public string MaintenanceDatabase { get; set; } = "postgres";

        public string? Locale { get; set; }

        public string? Owner { get; set; }

        /// <summary>
        /// How long to wait when PostgreSQL is still starting. Zero tries once and then fails.
        /// </summary>
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// Creates the database if it is missing and applies the version-1 schema to an empty database.
    /// An existing database is left unchanged. This release has no upgrade path.
    /// </summary>
    public class DatabaseInitializer
    {
        public const int SchemaVersion = 1;

        private static readonly string[] ScriptFiles =
        {
            "01_Schema.sql",
            "02_SeedDefaults.sql",
            "03_SeedLibraries.sql",
            "04_CatalogBaseline.sql",
            "05_Access.sql",
            "06_Projects.sql",
            "07_Warehouse.sql"
        };

        private static readonly string[] RequiredTables =
        {
            "distributor",
            "manufacturer",
            "library_file",
            "library",
            "datasheet",
            "model3d",
            "symbol",
            "footprint",
            "part",
            "part_number",
            "order_number",
            "template",
            "app_user",
            "directory_connection",
            "git_host",
            "project",
            "warehouse_item"
        };

        private readonly DatabaseInitializerOptions _options;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(DatabaseInitializerOptions options, ILogger<DatabaseInitializer> logger)
        {
            _options = options;
            _logger = logger;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            TimeSpan delay = TimeSpan.FromSeconds(1);
            TimeSpan maxDelay = TimeSpan.FromSeconds(5);
            DateTimeOffset deadline = DateTimeOffset.UtcNow + _options.StartupTimeout;

            while (true)
            {
                try
                {
                    await InitializeCoreAsync(cancellationToken);
                    return;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsDatabaseUnavailable(exception))
                {
                    TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw;
                    }

                    TimeSpan wait = delay < remaining ? delay : remaining;
                    _logger.LogWarning(
                        "PostgreSQL is not ready ({Reason}). Retrying schema initialization in {DelaySeconds:0}s.",
                        exception.Message,
                        wait.TotalSeconds);
                    await Task.Delay(wait, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(Math.Min(maxDelay.TotalMilliseconds, delay.TotalMilliseconds * 2));
                }
            }
        }

        private async Task InitializeCoreAsync(CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await OpenDatabaseAsync(cancellationToken);

            List<string> missingTables = await GetMissingTablesAsync(connection, cancellationToken);

            if (missingTables.Count == RequiredTables.Length)
            {
                await InitializeFreshAsync(connection, cancellationToken);
                return;
            }

            if (missingTables.Count > 0)
            {
                _logger.LogError(
                    "Database '{Database}' is only partially initialized, the tables {Tables} are missing. Initialization was skipped.",
                    connection.Database,
                    string.Join(", ", missingTables));
                return;
            }

            int currentVersion = await GetSchemaVersionAsync(connection, cancellationToken);
            _logger.LogInformation(
                "Database '{Database}' is already initialized (schema version {Version}).",
                connection.Database,
                currentVersion);
        }

        private async Task InitializeFreshAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            string owner = ResolveOwner();

            _logger.LogInformation("Database '{Database}' is not initialized, running the initialization scripts.", connection.Database);

            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (string scriptFile in ScriptFiles)
            {
                await ExecuteScriptAsync(connection, transaction, Path.Combine(_options.ScriptDirectory, scriptFile), owner, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            int version = await GetSchemaVersionAsync(connection, cancellationToken);
            _logger.LogInformation("Database '{Database}' was initialized with schema version {Version}.", connection.Database, version);
        }

        private string ResolveOwner()
        {
            return string.IsNullOrWhiteSpace(_options.Owner) ? "CURRENT_USER" : QuoteIdentifier(_options.Owner);
        }

        private async Task<int> GetSchemaVersionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            if (!await TableExistsAsync(connection, "schema_version", cancellationToken))
            {
                return 0;
            }

            await using NpgsqlCommand command = new NpgsqlCommand(
                "SELECT COALESCE(MAX(version), 0) FROM public.schema_version",
                connection);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand command = new NpgsqlCommand(
                "SELECT 1 FROM pg_catalog.pg_tables WHERE schemaname = 'public' AND tablename = @t",
                connection);
            command.Parameters.AddWithValue("t", tableName);
            return await command.ExecuteScalarAsync(cancellationToken) != null;
        }

        private async Task<NpgsqlConnection> OpenDatabaseAsync(CancellationToken cancellationToken)
        {
            NpgsqlConnection connection = new NpgsqlConnection(_options.ConnectionString);

            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InvalidCatalogName)
            {
                await connection.DisposeAsync();
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            await CreateDatabaseAsync(cancellationToken);

            connection = new NpgsqlConnection(_options.ConnectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        private static bool IsDatabaseUnavailable(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException or TimeoutException)
                {
                    return true;
                }

                if (current is NpgsqlException npgsql && npgsql.IsTransient)
                {
                    return true;
                }

                if (current is PostgresException postgres && IsStartupSqlState(postgres.SqlState))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStartupSqlState(string? sqlState)
        {
            return sqlState is PostgresErrorCodes.CannotConnectNow
                or PostgresErrorCodes.AdminShutdown
                or PostgresErrorCodes.CrashShutdown
                or PostgresErrorCodes.ConnectionException
                or PostgresErrorCodes.ConnectionFailure
                or PostgresErrorCodes.ConnectionDoesNotExist
                or PostgresErrorCodes.TooManyConnections
                or "08001" // sqlclient_unable_to_establish_sqlconnection
                or "08004"; // sqlserver_rejected_establishment_of_sqlconnection
        }

        private async Task CreateDatabaseAsync(CancellationToken cancellationToken)
        {
            NpgsqlConnectionStringBuilder connectionStringBuilder = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
            string databaseName = connectionStringBuilder.Database
                ?? throw new InvalidOperationException("The connection string does not contain a database name.");

            connectionStringBuilder.Database = _options.MaintenanceDatabase;
            connectionStringBuilder.Pooling = false;

            _logger.LogInformation("Database '{Database}' does not exist, creating it.", databaseName);

            await using NpgsqlConnection connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            string createStatement = $"CREATE DATABASE {QuoteIdentifier(databaseName)} WITH TEMPLATE = template0 ENCODING = 'UTF8'";
            bool hasLocale = !string.IsNullOrWhiteSpace(_options.Locale);

            try
            {
                await ExecuteAsync(connection, hasLocale ? $"{createStatement} LOCALE = {QuoteLiteral(_options.Locale!)}" : createStatement, cancellationToken);
            }
            catch (PostgresException exception) when (hasLocale && exception.SqlState == PostgresErrorCodes.InvalidParameterValue)
            {
                _logger.LogWarning("The locale '{Locale}' is not available on the server, using the server default instead.", _options.Locale);
                await ExecuteAsync(connection, createStatement, cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.DuplicateDatabase)
            {
                _logger.LogInformation("Database '{Database}' was created by another instance in the meantime.", databaseName);
            }
        }

        private static async Task<List<string>> GetMissingTablesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            HashSet<string> existingTables = new HashSet<string>(StringComparer.Ordinal);

            await using (NpgsqlCommand command = new NpgsqlCommand(
                "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = 'public'",
                connection))
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingTables.Add(reader.GetString(0));
                }
            }

            return RequiredTables.Where(table => !existingTables.Contains(table)).ToList();
        }

        private async Task ExecuteScriptAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string scriptPath,
            string owner,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"The database script '{scriptPath}' was not found.", scriptPath);
            }

            string script = (await File.ReadAllTextAsync(scriptPath, cancellationToken))
                .Replace("{{OWNER}}", owner, StringComparison.Ordinal);

            _logger.LogInformation("Running database script '{Script}'.", Path.GetFileName(scriptPath));

            await using NpgsqlCommand command = new NpgsqlCommand(script, connection, transaction);
            command.CommandTimeout = 600;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task ExecuteAsync(NpgsqlConnection connection, string statement, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        internal static string QuoteIdentifier(string identifier)
        {
            return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        private static string QuoteLiteral(string literal)
        {
            return $"'{literal.Replace("'", "''", StringComparison.Ordinal)}'";
        }
    }
}
