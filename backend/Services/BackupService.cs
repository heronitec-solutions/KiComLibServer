using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KiComLibBackend.Data;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace KiComLibBackend.Services
{
    public sealed class BackupInfo
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string FileName { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public long SizeBytes { get; set; }

        public string Source { get; set; } = "created";

        public bool Encrypted { get; set; }
    }

    public readonly record struct BackupRestoreResult(bool Restarting, bool KeysRestored);

    public class BackupService
    {
        public const long DefaultMaxBytes = 2048L * 1024L * 1024L;

        private static int _restoreInProgress;

        public static bool IsRestoring => Volatile.Read(ref _restoreInProgress) == 1;

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly Regex IdPattern = new("^[a-zA-Z0-9-]{8,80}$", RegexOptions.Compiled);

        private readonly string _directory;
        private readonly string _keysDirectory;
        private readonly string _contentRoot;
        private readonly string _connectionString;
        private readonly string _dockerContainer;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<BackupService> _logger;
        private readonly ProcessRunner _processes;
        private readonly DatabaseInitializer _initializer;

        public BackupService(
            IWebHostEnvironment environment,
            IHostApplicationLifetime lifetime,
            ILogger<BackupService> logger,
            ProcessRunner processes,
            DatabaseInitializer initializer)
        {
            _lifetime = lifetime;
            _logger = logger;
            _processes = processes;
            _initializer = initializer;
            _contentRoot = environment.ContentRootPath;
            _keysDirectory = DataPaths.Resolve(_contentRoot, "DATA_PROTECTION_KEYS_DIR", "Data", "Keys");
            string? configured = Environment.GetEnvironmentVariable("BACKUP_DIR");
            _directory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(environment.ContentRootPath, "Data", "Backups")
                : configured;
            Directory.CreateDirectory(_directory);

            _connectionString = $"Host={Environment.GetEnvironmentVariable("DB_HOST")};Port={Environment.GetEnvironmentVariable("DB_PORT")};Database={Environment.GetEnvironmentVariable("DB_NAME")};Username={Environment.GetEnvironmentVariable("DB_USER")};Password={Environment.GetEnvironmentVariable("DB_PASSWORD")}";
            _dockerContainer = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DB_DOCKER_CONTAINER"))
                ? "kicomlib_db"
                : Environment.GetEnvironmentVariable("DB_DOCKER_CONTAINER")!;
        }

        public long MaxBytes
        {
            get
            {
                if (int.TryParse(Environment.GetEnvironmentVariable("BACKUP_MAX_MB"), out int megabytes) && megabytes > 0)
                {
                    return megabytes * 1024L * 1024L;
                }

                return DefaultMaxBytes;
            }
        }

        public IReadOnlyList<BackupInfo> List()
        {
            if (!Directory.Exists(_directory))
            {
                return Array.Empty<BackupInfo>();
            }

            List<BackupInfo> backups = new();
            foreach (string metaPath in Directory.GetFiles(_directory, "*.json"))
            {
                try
                {
                    BackupInfo? info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(metaPath), JsonOptions);
                    if (info == null || !IsSafeId(info.Id))
                    {
                        continue;
                    }

                    string dumpPath = DumpPath(info.Id);
                    if (!File.Exists(dumpPath))
                    {
                        continue;
                    }

                    info.SizeBytes = new FileInfo(dumpPath).Length;
                    info.Encrypted = BackupArchive.HasMagic(dumpPath);
                    backups.Add(info);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Skipping unreadable backup metadata {Path}.", metaPath);
                }
            }

            return backups.OrderByDescending(backup => backup.CreatedAt).ToList();
        }

        public async Task<BackupInfo> CreateAsync(string? name, string? passphrase, CancellationToken cancellationToken)
        {
            string normalized = BackupArchive.NormalizePassphrase(passphrase);
            await Gate.WaitAsync(cancellationToken);
            string? dumpPath = null;
            string? archivePath = null;
            try
            {
                string id = NewId();
                dumpPath = Path.Combine(Path.GetTempPath(), "kicomlib-dump-" + id + ".dump");
                archivePath = Path.Combine(_directory, id + ".kibkp");
                try
                {
                    await DumpDatabaseAsync(dumpPath, cancellationToken);
                    await EnsureDumpAsync(dumpPath, cancellationToken);
                    BackupManifest manifest = new()
                    {
                        Format = BackupArchive.InnerFormat,
                        Product = "KiComLib",
                        ProductVersion = ProductVersion.Read(_contentRoot),
                        SchemaVersion = await ReadSchemaVersionAsync(cancellationToken),
                        CreatedAt = DateTime.UtcNow
                    };
                    await BackupArchive.CreateAsync(archivePath, dumpPath, ReadKeyRing(), manifest, normalized, cancellationToken);
                    FileInfo archive = new(archivePath);
                    if (archive.Length > MaxBytes)
                    {
                        throw new InvalidOperationException($"Backup is larger than {MaxBytes / (1024 * 1024)} MB");
                    }

                    BackupInfo info = CreateInfo(id, string.IsNullOrWhiteSpace(name) ? DefaultName() : name, "created");
                    info.Encrypted = true;
                    info.SizeBytes = archive.Length;
                    WriteMeta(info);
                    _logger.LogInformation("Created encrypted backup {BackupId}.", info.Id);
                    archivePath = null;
                    return info;
                }
                catch
                {
                    if (archivePath != null && File.Exists(archivePath))
                    {
                        File.Delete(archivePath);
                    }

                    throw;
                }
            }
            finally
            {
                if (dumpPath != null && File.Exists(dumpPath))
                {
                    File.Delete(dumpPath);
                }

                Gate.Release();
            }
        }

        public async Task<BackupInfo> ImportAsync(Stream content, string? name, string? uploadedFileName, CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken);
            string? dumpPath = null;
            try
            {
                string id = NewId();
                dumpPath = DumpPath(id);
                await using (FileStream file = File.Create(dumpPath))
                {
                    await content.CopyToAsync(file, cancellationToken);
                }

                FileInfo written = new(dumpPath);
                if (written.Length > MaxBytes)
                {
                    throw new InvalidOperationException($"Backup is larger than {MaxBytes / (1024 * 1024)} MB");
                }

                await EnsureBackupAsync(dumpPath, cancellationToken);
                string label = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(uploadedFileName ?? "") : name;
                BackupInfo info = CreateInfo(id, string.IsNullOrWhiteSpace(label) ? DefaultName() : label, "uploaded");
                info.Encrypted = BackupArchive.HasMagic(dumpPath);
                info.SizeBytes = written.Length;
                WriteMeta(info);
                dumpPath = null;
                return info;
            }
            finally
            {
                if (dumpPath != null && File.Exists(dumpPath))
                {
                    File.Delete(dumpPath);
                }

                Gate.Release();
            }
        }

        public (BackupInfo Info, string Path)? Open(string id)
        {
            if (!IsSafeId(id))
            {
                return null;
            }

            string metaPath = MetaPath(id);
            string dumpPath = DumpPath(id);
            if (!File.Exists(metaPath) || !File.Exists(dumpPath))
            {
                return null;
            }

            BackupInfo? info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(metaPath), JsonOptions);
            if (info == null)
            {
                return null;
            }

            info.SizeBytes = new FileInfo(dumpPath).Length;
            return (info, dumpPath);
        }

        public void Delete(string id)
        {
            if (!IsSafeId(id))
            {
                throw new InvalidOperationException("Unknown backup");
            }

            string dumpPath = DumpPath(id);
            string metaPath = MetaPath(id);
            if (!File.Exists(dumpPath) && !File.Exists(metaPath))
            {
                throw new InvalidOperationException("Unknown backup");
            }

            if (File.Exists(dumpPath))
            {
                File.Delete(dumpPath);
            }

            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        public async Task<BackupRestoreResult> RestoreAsync(string id, string? passphrase, CancellationToken cancellationToken)
        {
            if (!IsSafeId(id) || !File.Exists(DumpPath(id)))
            {
                throw new InvalidOperationException("Unknown backup");
            }

            string archivePath = DumpPath(id);
            bool encrypted = BackupArchive.HasMagic(archivePath);
            string? normalized = encrypted ? BackupArchive.NormalizePassphrase(passphrase) : null;
            await Gate.WaitAsync(cancellationToken);
            try
            {
                if (encrypted)
                {
                    return await RestoreArchiveAsync(archivePath, normalized!, cancellationToken);
                }

                await EnsureDumpAsync(archivePath, cancellationToken);
                await RestoreDatabaseOnlyAsync(archivePath, cancellationToken);
                return new BackupRestoreResult(false, false);
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<BackupRestoreResult> RestoreArchiveAsync(string archivePath, string passphrase, CancellationToken cancellationToken)
        {
            using OpenedBackup opened = await BackupArchive.OpenAsync(archivePath, passphrase, MaxBytes, cancellationToken);
            await EnsureDumpAsync(opened.DumpPath, cancellationToken);
            await RestoreDatabaseOnlyAsync(opened.DumpPath, cancellationToken);
            try
            {
                bool keysRestored = InstallKeyRing(opened.KeyFiles);
                return new BackupRestoreResult(keysRestored && ScheduleRestart(), keysRestored);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Database restore succeeded but the key ring was not replaced.");
                throw new InvalidOperationException("The database was restored, but the key ring could not be replaced.", exception);
            }
        }

        private async Task RestoreDatabaseOnlyAsync(string dumpPath, CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _restoreInProgress, 1);
            try
            {
                await TerminateConnectionsAsync(cancellationToken);
                await RestoreDatabaseAsync(dumpPath, cancellationToken);
                NpgsqlConnection.ClearAllPools();
                await EnsureSchemaAfterRestoreAsync(cancellationToken);
                NpgsqlConnection.ClearAllPools();
            }
            finally
            {
                Interlocked.Exchange(ref _restoreInProgress, 0);
            }
        }

        private async Task EnsureSchemaAfterRestoreAsync(CancellationToken cancellationToken)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("DB_AUTO_INIT"), "false", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Skipping schema initialization after restore because DB_AUTO_INIT is false.");
                return;
            }

            _logger.LogInformation("Checking the database schema after restore.");
            try
            {
                await _initializer.InitializeAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Schema initialization after restore failed.");
                throw new InvalidOperationException(
                    "The backup was restored, but schema initialization failed. " + exception.Message,
                    exception);
            }
        }

        private async Task DumpDatabaseAsync(string dumpPath, CancellationToken cancellationToken)
        {
            NpgsqlConnectionStringBuilder csb = new(_connectionString);
            if (await CanUseDockerAsync(cancellationToken))
            {
                string remote = "/tmp/" + Path.GetFileName(dumpPath);
                await RunCheckedAsync(
                    "docker",
                    new[]
                    {
                        "exec", "-e", "PGPASSWORD=" + csb.Password, _dockerContainer,
                        "pg_dump", "-U", csb.Username!, "-d", csb.Database!,
                        "-Fc", "--no-owner", "--no-acl", "-f", remote
                    },
                    cancellationToken);
                try
                {
                    await RunCheckedAsync("docker", new[] { "cp", _dockerContainer + ":" + remote, dumpPath }, cancellationToken);
                }
                finally
                {
                    await RunQuietAsync("docker", new[] { "exec", _dockerContainer, "rm", "-f", remote }, cancellationToken);
                }

                return;
            }

            await RunCheckedAsync(
                "pg_dump",
                new[]
                {
                    "-h", csb.Host!, "-p", csb.Port.ToString(), "-U", csb.Username!, "-d", csb.Database!,
                    "-Fc", "--no-owner", "--no-acl", "-f", dumpPath
                },
                cancellationToken,
                new Dictionary<string, string> { ["PGPASSWORD"] = csb.Password ?? "" });
        }

        private async Task RestoreDatabaseAsync(string dumpPath, CancellationToken cancellationToken)
        {
            NpgsqlConnectionStringBuilder csb = new(_connectionString);
            if (await CanUseDockerAsync(cancellationToken))
            {
                string remote = "/tmp/" + Path.GetFileName(dumpPath);
                await RunCheckedAsync("docker", new[] { "cp", dumpPath, _dockerContainer + ":" + remote }, cancellationToken);
                try
                {
                    await ClearDatabaseAsync(cancellationToken);
                    await RunCheckedAsync(
                        "docker",
                        new[]
                        {
                            "exec", "-e", "PGPASSWORD=" + csb.Password, _dockerContainer,
                            "pg_restore", "-U", csb.Username!, "-d", csb.Database!,
                            "--no-owner", "--no-acl", "--exit-on-error", "--single-transaction", remote
                        },
                        cancellationToken);
                }
                finally
                {
                    await RunQuietAsync("docker", new[] { "exec", _dockerContainer, "rm", "-f", remote }, cancellationToken);
                }

                return;
            }

            await ClearDatabaseAsync(cancellationToken);
            await RunCheckedAsync(
                "pg_restore",
                new[]
                {
                    "-h", csb.Host!, "-p", csb.Port.ToString(), "-U", csb.Username!, "-d", csb.Database!,
                    "--no-owner", "--no-acl", "--exit-on-error", "--single-transaction", dumpPath
                },
                cancellationToken,
                new Dictionary<string, string> { ["PGPASSWORD"] = csb.Password ?? "" });
        }

        // pg_restore --clean drops a primary key before foreign keys that depend on it
        // (part_pkey is required by part_number.part_id_foreign). Empty the database first.
        private async Task ClearDatabaseAsync(CancellationToken cancellationToken)
        {
            await TerminateConnectionsAsync(cancellationToken);
            NpgsqlConnectionStringBuilder csb = new(_connectionString) { Pooling = false };
            await using NpgsqlConnection connection = new(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            _logger.LogInformation("Clearing database '{Database}' before restoring the backup.", connection.Database);
            await using NpgsqlCommand command = new(
                """
                DO $$
                DECLARE schema_name text;
                BEGIN
                    FOR schema_name IN
                        SELECT nspname
                        FROM pg_namespace
                        WHERE nspname !~ '^pg_'
                          AND nspname <> 'information_schema'
                    LOOP
                        EXECUTE format('DROP SCHEMA %I CASCADE', schema_name);
                    END LOOP;
                END $$;
                CREATE SCHEMA public;
                """,
                connection);
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task TerminateConnectionsAsync(CancellationToken cancellationToken)
        {
            NpgsqlConnectionStringBuilder csb = new(_connectionString);
            string database = csb.Database!;
            csb.Database = Environment.GetEnvironmentVariable("DB_MAINTENANCE_NAME") ?? "postgres";
            csb.Pooling = false;
            NpgsqlConnection.ClearAllPools();
            await using NpgsqlConnection connection = new(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using NpgsqlCommand command = new(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid()",
                connection);
            command.Parameters.AddWithValue("db", database);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<bool> CanUseDockerAsync(CancellationToken cancellationToken)
        {
            if (!IsLocalHost(new NpgsqlConnectionStringBuilder(_connectionString).Host))
            {
                return false;
            }

            (int code, string stdout, _) = await RunAsync(
                "docker",
                new[] { "inspect", "-f", "{{.State.Running}}", _dockerContainer },
                null,
                cancellationToken);
            return code == 0 && stdout.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalHost(string? host)
        {
            return host != null && (
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1"
                || host == "::1");
        }

        private async Task RunCheckedAsync(
            string fileName,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken,
            IDictionary<string, string>? environment = null)
        {
            (int code, string stdout, string stderr) = await RunAsync(fileName, args, environment, cancellationToken);
            if (code != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                if (code == -1 && IsMissingExecutable(detail))
                {
                    throw new InvalidOperationException(fileName + " was not found. Install the PostgreSQL client tools (pg_dump and pg_restore).");
                }

                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                    ? fileName + " failed"
                    : detail.Trim());
            }
        }

        private static bool IsMissingExecutable(string detail)
        {
            return detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
            string fileName,
            IReadOnlyList<string> args,
            IDictionary<string, string>? environment,
            CancellationToken cancellationToken)
        {
            ProcessRunResult result = await _processes.RunAsync(fileName, args, environment, cancellationToken);
            return (result.ExitCode, result.Stdout, result.Stderr);
        }

        private async Task RunQuietAsync(string fileName, IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            try
            {
                await RunAsync(fileName, args, null, cancellationToken);
            }
            catch (Exception)
            {
                // Cleanup of the temporary file inside the container is best-effort.
            }
        }

        private async Task EnsureBackupAsync(string path, CancellationToken cancellationToken)
        {
            if (BackupArchive.HasMagic(path))
            {
                BackupArchive.Inspect(path, MaxBytes);
                return;
            }

            await EnsureDumpAsync(path, cancellationToken);
        }

        private async Task<int> ReadSchemaVersionAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using NpgsqlConnection connection = new(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using NpgsqlCommand command = new("SELECT COALESCE(MAX(version), 0) FROM public.schema_version", connection);
                command.CommandTimeout = 30;
                object? value = await command.ExecuteScalarAsync(cancellationToken);
                return value == null || value is DBNull ? 0 : Convert.ToInt32(value);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Could not read the schema version for the backup manifest.");
                return 0;
            }
        }

        private IReadOnlyList<BackupKeyFile> ReadKeyRing()
        {
            if (!Directory.Exists(_keysDirectory))
            {
                return Array.Empty<BackupKeyFile>();
            }

            List<BackupKeyFile> files = new();
            foreach (string path in Directory.GetFiles(_keysDirectory))
            {
                string name = Path.GetFileName(path);
                if (!BackupArchive.IsSafeKeyName(name))
                {
                    _logger.LogWarning("Skipping Data Protection file {Name} because the name cannot be stored in a backup.", name);
                    continue;
                }

                files.Add(new BackupKeyFile(name, path));
            }

            return files;
        }

        private bool InstallKeyRing(IReadOnlyList<string> keyFiles)
        {
            if (keyFiles.Count == 0)
            {
                _logger.LogWarning("The backup contains no Data Protection keys. The current key ring was left unchanged.");
                return false;
            }

            Directory.CreateDirectory(_keysDirectory);
            string snapshot = Path.Combine(Path.GetTempPath(), "kicomlib-keys-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshot);
            try
            {
                foreach (string existing in Directory.GetFiles(_keysDirectory))
                {
                    File.Copy(existing, Path.Combine(snapshot, Path.GetFileName(existing)), overwrite: true);
                }

                foreach (string existing in Directory.GetFiles(_keysDirectory))
                {
                    File.Delete(existing);
                }

                foreach (string source in keyFiles)
                {
                    string name = Path.GetFileName(source);
                    if (!BackupArchive.IsSafeKeyName(name))
                    {
                        throw new InvalidOperationException(BackupArchive.InvalidKeyRingMessage);
                    }

                    string destination = Path.GetFullPath(Path.Combine(_keysDirectory, name));
                    EnsureInside(_keysDirectory, destination);
                    File.Copy(source, destination, overwrite: true);
                }

                _logger.LogInformation("Restored {Count} Data Protection key files.", keyFiles.Count);
                return true;
            }
            catch
            {
                try
                {
                    foreach (string existing in Directory.GetFiles(_keysDirectory))
                    {
                        File.Delete(existing);
                    }

                    foreach (string saved in Directory.GetFiles(snapshot))
                    {
                        File.Copy(saved, Path.Combine(_keysDirectory, Path.GetFileName(saved)), overwrite: true);
                    }
                }
                catch (Exception rollback)
                {
                    _logger.LogError(rollback, "Could not restore the previous Data Protection key ring.");
                }

                throw;
            }
            finally
            {
                try
                {
                    Directory.Delete(snapshot, recursive: true);
                }
                catch (IOException)
                {
                    // The snapshot is in the temp directory.
                }
            }
        }

        private bool ScheduleRestart()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("BACKUP_RESTART_AFTER_RESTORE"), "false", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Restored the Data Protection key ring. Process restart is disabled.");
                return false;
            }

            _logger.LogInformation("Restored the Data Protection key ring. The process will stop so it can reload the keys.");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    _lifetime.StopApplication();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Could not stop the process after restoring the key ring.");
                }
            });
            return true;
        }

        private static void EnsureInside(string directory, string candidate)
        {
            string root = Path.GetFullPath(directory);
            string full = Path.GetFullPath(candidate);
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(prefix, comparison))
            {
                throw new InvalidOperationException(BackupArchive.InvalidKeyRingMessage);
            }
        }

        private static async Task EnsureDumpAsync(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = File.OpenRead(path);
            byte[] magic = new byte[5];
            int read = await stream.ReadAsync(magic, cancellationToken);
            if (read < 5 || Encoding.ASCII.GetString(magic) != "PGDMP")
            {
                throw new InvalidOperationException("The file is not a PostgreSQL custom-format backup");
            }
        }

        private BackupInfo CreateInfo(string id, string name, string source)
        {
            string cleaned = name.Trim();
            if (cleaned.Length > 120)
            {
                cleaned = cleaned[..120];
            }

            string fileName = string.Join("-", cleaned.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Replace(' ', '-');
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = id;
            }

            foreach (string extension in new[] { ".kibkp", ".dump", ".backup" })
            {
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    fileName = fileName[..^extension.Length];
                    break;
                }
            }

            fileName += ".kibkp";

            return new BackupInfo
            {
                Id = id,
                Name = cleaned,
                FileName = fileName,
                CreatedAt = DateTime.UtcNow,
                Source = source
            };
        }

        private void WriteMeta(BackupInfo info)
        {
            File.WriteAllText(MetaPath(info.Id), JsonSerializer.Serialize(info, JsonOptions));
        }

        private string DumpPath(string id)
        {
            string path = Path.Combine(_directory, id + ".kibkp");
            if (File.Exists(path))
            {
                return path;
            }

            string legacy = Path.Combine(_directory, id + ".dump");
            return File.Exists(legacy) ? legacy : path;
        }

        private string MetaPath(string id) => Path.Combine(_directory, id + ".json");

        private static string NewId() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];

        private static string DefaultName() => "Backup " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        private static bool IsSafeId(string? id) => id != null && IdPattern.IsMatch(id);
    }
}
