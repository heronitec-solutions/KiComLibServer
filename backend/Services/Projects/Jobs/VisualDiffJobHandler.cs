using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public sealed class VisualDiffJobHandler : IProjectJobHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly GitMirrorService _mirror;
        private readonly IKiCadRunnerClient _runner;
        private readonly ProjectCache _cache;
        private readonly ILogger<VisualDiffJobHandler> _logger;

        public VisualDiffJobHandler(
            ApplicationDbContext db,
            GitMirrorService mirror,
            IKiCadRunnerClient runner,
            ProjectCache cache,
            ILogger<VisualDiffJobHandler> logger)
        {
            _db = db;
            _mirror = mirror;
            _runner = runner;
            _cache = cache;
            _logger = logger;
        }

        public string Kind => "visual_diff";

        public async Task RunAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            int? diffId = ReadDiffId(job.Payload);
            if (diffId == null && job.DedupKey != null && job.DedupKey.StartsWith("visual_diff:", StringComparison.Ordinal))
            {
                if (int.TryParse(job.DedupKey["visual_diff:".Length..], out int parsed))
                {
                    diffId = parsed;
                }
            }

            ProjectDiff? diff = diffId == null
                ? null
                : await _db.ProjectDiffs.FirstOrDefaultAsync(d => d.Id == diffId, cancellationToken);
            if (diff == null)
            {
                throw new InvalidOperationException("visual_diff job did not match a project diff.");
            }

            diff.Status = "running";
            diff.Error = null;
            await _db.SaveChangesAsync(cancellationToken);
            try
            {
                Project? project = await _db.Projects.Include(p => p.Repository).FirstOrDefaultAsync(p => p.Id == diff.ProjectId, cancellationToken);
                ProjectRevision? baseRevision = await _db.ProjectRevisions.FirstOrDefaultAsync(r => r.Id == diff.BaseRevisionId, cancellationToken);
                ProjectRevision? targetRevision = await _db.ProjectRevisions.FirstOrDefaultAsync(r => r.Id == diff.TargetRevisionId, cancellationToken);
                if (project?.Repository == null || baseRevision == null || targetRevision == null)
                {
                    throw new InvalidOperationException("visual_diff is missing a project revision.");
                }

                string? tree = ProjectExport.TreePath(project);
                int maxMb = ProjectExport.ArchiveMaxMb();
                byte[] baseTar = await _mirror.ArchiveAsync(project.Repository, baseRevision.CommitSha, tree, maxMb, cancellationToken);
                byte[] targetTar = await _mirror.ArchiveAsync(project.Repository, targetRevision.CommitSha, tree, maxMb, cancellationToken);
                byte[] zip = await _runner.VisualDiffAsync(baseTar, targetTar, project.ProjectFile, cancellationToken);
                string relative = _cache.DiffRelative(diff.Uuid);
                _cache.Unpack(relative, zip, protect: true);
                byte[]? manifestBytes = _cache.ReadFile(relative, "manifest.json");
                if (manifestBytes == null)
                {
                    throw new InvalidOperationException("The runner zip has no manifest.json.");
                }

                using JsonDocument manifest = JsonDocument.Parse(manifestBytes);
                diff.KicadVersion = ProjectExport.TryProperty(manifest.RootElement, "kicad", out JsonElement kicad) && kicad.ValueKind == JsonValueKind.String
                    ? kicad.GetString()
                    : null;
                diff.Summary = ProjectExport.TryProperty(manifest.RootElement, "summary", out JsonElement summary)
                    ? summary.GetRawText()
                    : null;
                diff.Status = "done";
                diff.Error = null;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Visual diff {DiffId} for project {ProjectId} is ready.", diff.Id, diff.ProjectId);
            }
            catch (RunnerCallException exception)
            {
                diff.Status = "failed";
                diff.Error = Trim(exception.Message);
                await _db.SaveChangesAsync(cancellationToken);
                if (exception.Retryable)
                {
                    throw;
                }
            }
            catch (Exception exception) when (exception is GitCommandException or InvalidOperationException or JsonException)
            {
                diff.Status = "failed";
                diff.Error = Trim(exception.Message);
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        private static int? ReadDiffId(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("diffId", out JsonElement id) && id.TryGetInt32(out int parsed))
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        private static string Trim(string message)
        {
            string text = (message ?? "").Trim();
            if (text.Length > 500)
            {
                text = text[..500];
            }

            return text.Length == 0 ? "Visual diff failed." : text;
        }
    }
}
