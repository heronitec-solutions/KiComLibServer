using System.Data;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KiComLibBackend.Services
{
    public interface IProjectJobHandler
    {
        string Kind { get; }

        Task RunAsync(ProjectJob job, CancellationToken cancellationToken);

        /// <summary>
        /// Optional note stored on a job that completed. Warnings stay here; failures still throw.
        /// </summary>
        string? CompletionNote => null;
    }

    public sealed class ProjectJobQueue
    {
        public const int MaxAttempts = 3;

        public static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15)
        };

        /// <summary>
        /// Claims the next queued job. A second session skips rows locked by the first (SKIP LOCKED).
        /// Fetch and discover jobs for a repository that already has one of those running are left queued.
        /// </summary>
        public const string ClaimSql =
            """
            UPDATE public.project_job SET status = 'running', started_at = now(), attempts = attempts + 1
            WHERE id = (
                SELECT j.id FROM public.project_job j
                WHERE j.status = 'queued' AND j.run_after <= now()
                  AND NOT (j.kind IN ('fetch', 'discover') AND EXISTS (
                      SELECT 1 FROM public.project_job r
                      WHERE r.status = 'running' AND r.kind IN ('fetch', 'discover') AND r.repository_id = j.repository_id))
                ORDER BY j.run_after, j.id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING *;
            """;

        private readonly IServiceScopeFactory _scopes;

        public ProjectJobQueue(IServiceScopeFactory scopes)
        {
            _scopes = scopes;
        }

        public static string FetchKey(int repositoryId) => "fetch:" + repositoryId.ToString();

        public static string DiscoverKey(int repositoryId) => "discover:" + repositoryId.ToString();

        public static string AnalyzeKey(int projectId, string sha) => "analyze:" + projectId.ToString() + ":" + sha;

        public static string RematchKey(int projectId, string sha) => "rematch:" + projectId.ToString() + ":" + sha;

        public static string RenderKey(int projectId, string sha) => "render:" + projectId.ToString() + ":" + sha;

        public static string VisualDiffKey(int diffId) => "visual_diff:" + diffId.ToString();

        public static string OutputsKey(int releaseId) => "outputs:" + releaseId.ToString();

        public async Task<long?> EnqueueAsync(
            string kind,
            string? dedupKey,
            int? repositoryId,
            int? projectId,
            string payload,
            CancellationToken cancellationToken)
        {
            if (BackupService.IsRestoring)
            {
                return null;
            }

            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            NpgsqlConnection connection = await OpenAsync(db, cancellationToken);
            await using NpgsqlCommand command = new(
                """
                INSERT INTO public.project_job (kind, dedup_key, repository_id, project_id, payload)
                SELECT @kind, @dedup, @repositoryId, @projectId, CAST(@payload AS jsonb)
                WHERE @dedup IS NULL OR NOT EXISTS (
                    SELECT 1 FROM public.project_job
                    WHERE dedup_key = @dedup AND status = 'queued')
                RETURNING id
                """,
                connection);
            command.Parameters.AddWithValue("kind", kind);
            command.Parameters.AddWithValue("dedup", (object?)dedupKey ?? DBNull.Value);
            command.Parameters.AddWithValue("repositoryId", (object?)repositoryId ?? DBNull.Value);
            command.Parameters.AddWithValue("projectId", (object?)projectId ?? DBNull.Value);
            command.Parameters.AddWithValue("payload", string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            try
            {
                object? id = await command.ExecuteScalarAsync(cancellationToken);
                if (id == null || id is DBNull)
                {
                    return null;
                }

                return Convert.ToInt64(id);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return null;
            }
        }

        public async Task<ProjectJob?> ClaimAsync(CancellationToken cancellationToken)
        {
            if (BackupService.IsRestoring)
            {
                return null;
            }

            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            NpgsqlConnection connection = await OpenAsync(db, cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using NpgsqlCommand command = new(ClaimSql, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            ProjectJob job = ReadJob(reader);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return job;
        }

        public async Task CompleteAsync(long jobId, CancellationToken cancellationToken, string? warning = null)
        {
            await ExecuteAsync(
                """
                UPDATE public.project_job
                SET status = 'done', finished_at = now(), error = @error
                WHERE id = @id
                """,
                cancellationToken,
                new NpgsqlParameter("id", jobId),
                new NpgsqlParameter("error", string.IsNullOrWhiteSpace(warning) ? DBNull.Value : TrimError(warning)));
        }

        public async Task RetryAsync(ProjectJob job, string error, CancellationToken cancellationToken)
        {
            string message = TrimError(error);
            if (job.Attempts >= MaxAttempts)
            {
                await FailAsync(job.Id, message, cancellationToken);
                return;
            }

            int index = Math.Clamp(job.Attempts - 1, 0, RetryDelays.Length - 1);
            DateTime runAfter = DateTime.UtcNow.Add(RetryDelays[index]);
            await ExecuteAsync(
                """
                UPDATE public.project_job
                SET status = 'queued', run_after = @runAfter, started_at = NULL, error = @error
                WHERE id = @id AND status = 'running'
                """,
                cancellationToken,
                new NpgsqlParameter("id", job.Id),
                new NpgsqlParameter("runAfter", runAfter),
                new NpgsqlParameter("error", message));
        }

        /// <summary>
        /// Unknown kinds fail immediately. They are not put back on the queue.
        /// </summary>
        public async Task FailUnknownAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            await FailAsync(job.Id, "Unknown job kind '" + job.Kind + "'.", cancellationToken);
        }

        public async Task FailAsync(long jobId, string error, CancellationToken cancellationToken)
        {
            await ExecuteAsync(
                """
                UPDATE public.project_job
                SET status = 'failed', finished_at = now(), error = @error
                WHERE id = @id
                """,
                cancellationToken,
                new NpgsqlParameter("id", jobId),
                new NpgsqlParameter("error", TrimError(error)));
        }

        public async Task ResetStuckAsync(CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            NpgsqlConnection connection = await OpenAsync(db, cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (NpgsqlCommand cancelDuplicates = new(
                """
                WITH ranked AS (
                    SELECT id, ROW_NUMBER() OVER (PARTITION BY dedup_key ORDER BY id) AS n
                    FROM public.project_job
                    WHERE status = 'running' AND dedup_key IS NOT NULL
                )
                UPDATE public.project_job
                SET status = 'cancelled', finished_at = now(),
                    error = 'Cancelled on startup because another running job had the same dedup key'
                WHERE id IN (SELECT id FROM ranked WHERE n > 1)
                """,
                connection,
                transaction))
            {
                await cancelDuplicates.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (NpgsqlCommand cancelSuperseded = new(
                """
                UPDATE public.project_job AS stuck
                SET status = 'cancelled', finished_at = now(),
                    error = 'Cancelled on startup because a queued job with the same dedup key exists'
                WHERE stuck.status = 'running'
                  AND stuck.dedup_key IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM public.project_job queued
                      WHERE queued.status = 'queued'
                        AND queued.dedup_key = stuck.dedup_key
                        AND queued.id <> stuck.id)
                """,
                connection,
                transaction))
            {
                await cancelSuperseded.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (NpgsqlCommand requeue = new(
                """
                UPDATE public.project_job
                SET status = 'queued', started_at = NULL, run_after = now()
                WHERE status = 'running'
                """,
                connection,
                transaction))
            {
                await requeue.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        public async Task DeleteExpiredAsync(CancellationToken cancellationToken)
        {
            await ExecuteAsync(
                """
                DELETE FROM public.project_job
                WHERE status = 'done' AND finished_at < now() - interval '14 days'
                """,
                cancellationToken);
        }

        private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            NpgsqlConnection connection = await OpenAsync(db, cancellationToken);
            await using NpgsqlCommand command = new(sql, connection);
            foreach (NpgsqlParameter parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await ExecuteAsync(
                    """
                    UPDATE public.project_job
                    SET status = 'cancelled', finished_at = now(),
                        error = 'Cancelled because a queued job with the same dedup key already exists'
                    WHERE id = @id
                    """,
                    cancellationToken,
                    new NpgsqlParameter("id", parameters.First(p => p.ParameterName == "id").Value!));
            }
        }

        private static async Task<NpgsqlConnection> OpenAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            return connection;
        }

        private static ProjectJob ReadJob(NpgsqlDataReader reader)
        {
            return new ProjectJob
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Kind = reader.GetString(reader.GetOrdinal("kind")),
                DedupKey = reader.IsDBNull(reader.GetOrdinal("dedup_key")) ? null : reader.GetString(reader.GetOrdinal("dedup_key")),
                RepositoryId = reader.IsDBNull(reader.GetOrdinal("repository_id")) ? null : reader.GetInt32(reader.GetOrdinal("repository_id")),
                ProjectId = reader.IsDBNull(reader.GetOrdinal("project_id")) ? null : reader.GetInt32(reader.GetOrdinal("project_id")),
                Payload = reader.IsDBNull(reader.GetOrdinal("payload")) ? "{}" : reader.GetString(reader.GetOrdinal("payload")),
                Status = reader.GetString(reader.GetOrdinal("status")),
                Attempts = reader.GetInt32(reader.GetOrdinal("attempts")),
                RunAfter = reader.GetFieldValue<DateTime>(reader.GetOrdinal("run_after")),
                StartedAt = reader.IsDBNull(reader.GetOrdinal("started_at")) ? null : reader.GetFieldValue<DateTime>(reader.GetOrdinal("started_at")),
                FinishedAt = reader.IsDBNull(reader.GetOrdinal("finished_at")) ? null : reader.GetFieldValue<DateTime>(reader.GetOrdinal("finished_at")),
                Error = reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error")),
                CreatedAt = reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at"))
            };
        }

        private static string TrimError(string error)
        {
            string trimmed = (error ?? "").Trim();
            if (trimmed.Length > 4000)
            {
                trimmed = trimmed[..4000];
            }

            return trimmed.Length == 0 ? "The job failed." : trimmed;
        }
    }
}
