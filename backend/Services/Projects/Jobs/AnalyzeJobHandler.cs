using System.Text;
using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public sealed class AnalyzeJobHandler : IProjectJobHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly GitMirrorService _mirror;
        private readonly IKiCadRunnerClient _runner;
        private readonly ProjectAnalysisApplier _applier;
        private readonly ILogger<AnalyzeJobHandler> _logger;

        public AnalyzeJobHandler(
            ApplicationDbContext db,
            GitMirrorService mirror,
            IKiCadRunnerClient runner,
            ProjectAnalysisApplier applier,
            ILogger<AnalyzeJobHandler> logger)
        {
            _db = db;
            _mirror = mirror;
            _runner = runner;
            _applier = applier;
            _logger = logger;
        }

        public string Kind => "analyze";

        public async Task RunAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            (Project project, ProjectRevision revision) = await LoadAsync(job, cancellationToken);
            revision.AnalysisStatus = "running";
            revision.AnalysisError = null;
            await _db.SaveChangesAsync(cancellationToken);
            try
            {
                GitRepository repository = project.Repository ?? throw new InvalidOperationException("Project has no git repository.");
                byte[] tar = await _mirror.ArchiveAsync(repository, revision.CommitSha, ProjectExport.TreePath(project), ProjectExport.ArchiveMaxMb(), cancellationToken);
                string json = await _runner.AnalyzeAsync(tar, project.ProjectFile, null, cancellationToken);
                string hash = await _applier.StoreBlobAsync(Encoding.UTF8.GetBytes(json), "application/json", cancellationToken);
                revision.NetlistBlobHash = hash;
                revision.KicadVersion = ProjectExport.KicadVersion(json);
                revision.AnalysisStatus = "done";
                revision.AnalysisError = null;
                await _applier.ApplyAsync(revision, json, cancellationToken);
                _logger.LogInformation("Analyzed project {ProjectId} at {Sha}.", project.Id, revision.CommitSha);
            }
            catch (RunnerCallException exception)
            {
                revision.AnalysisStatus = exception.Code == "unsupported_version" ? "unsupported" : "failed";
                revision.AnalysisError = Trim(exception.Message);
                revision.AnalyzedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                if (exception.Retryable)
                {
                    throw;
                }
            }
            catch (Exception exception) when (exception is GitCommandException or InvalidOperationException or JsonException)
            {
                revision.AnalysisStatus = "failed";
                revision.AnalysisError = Trim(exception.Message);
                revision.AnalyzedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        private async Task<(Project Project, ProjectRevision Revision)> LoadAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            (int? revisionId, string? sha) = ReadPayload(job.Payload);
            ProjectRevision? revision = null;
            if (revisionId != null)
            {
                revision = await _db.ProjectRevisions
                    .Include(r => r.Project)!.ThenInclude(p => p!.Repository)
                    .FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken);
            }

            if (revision == null && job.ProjectId != null && !string.IsNullOrWhiteSpace(sha))
            {
                string wanted = sha.Trim();
                revision = await _db.ProjectRevisions
                    .Include(r => r.Project)!.ThenInclude(p => p!.Repository)
                    .FirstOrDefaultAsync(r => r.ProjectId == job.ProjectId && r.CommitSha == wanted, cancellationToken);
            }

            if (revision?.Project == null)
            {
                throw new InvalidOperationException("analyze job did not match a project revision.");
            }

            return (revision.Project, revision);
        }

        internal static (int? RevisionId, string? Sha) ReadPayload(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return (null, null);
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                int? revisionId = null;
                string? sha = null;
                if (doc.RootElement.TryGetProperty("revisionId", out JsonElement id) && id.TryGetInt32(out int parsed))
                {
                    revisionId = parsed;
                }

                if (doc.RootElement.TryGetProperty("commitSha", out JsonElement shaElement) && shaElement.ValueKind == JsonValueKind.String)
                {
                    sha = shaElement.GetString();
                }

                return (revisionId, sha);
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }

        private static string Trim(string message)
        {
            string text = (message ?? "").Trim();
            if (text.Length > 500)
            {
                text = text[..500];
            }

            return text.Length == 0 ? "Analysis failed." : text;
        }
    }
}
