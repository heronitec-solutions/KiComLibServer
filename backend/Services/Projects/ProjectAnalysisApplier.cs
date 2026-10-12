using System.Text;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Stores the runner analysis JSON (including netlistXml) and replaces component rows for one revision.
    /// </summary>
    public class ProjectAnalysisApplier
    {
        private readonly ApplicationDbContext _db;
        private readonly CatalogMatcher _matcher;
        private readonly GitMirrorService _mirror;
        private readonly ILogger<ProjectAnalysisApplier> _logger;

        public ProjectAnalysisApplier(
            ApplicationDbContext db,
            CatalogMatcher matcher,
            GitMirrorService mirror,
            ILogger<ProjectAnalysisApplier> logger)
        {
            _db = db;
            _matcher = matcher;
            _mirror = mirror;
            _logger = logger;
        }

        public async Task<string> StoreBlobAsync(byte[] data, string mediaType, CancellationToken cancellationToken)
        {
            string hash = ProjectExport.Sha256Hex(data);
            bool exists = await _db.ProjectBlobs.AnyAsync(b => b.Hash == hash, cancellationToken);
            if (!exists)
            {
                _db.ProjectBlobs.Add(new ProjectBlob
                {
                    Hash = hash,
                    Size = data.LongLength,
                    MediaType = mediaType,
                    Data = data,
                    Storage = 0,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            return hash;
        }

        public async Task<string?> ReadBlobTextAsync(string? hash, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return null;
            }

            ProjectBlob? blob = await _db.ProjectBlobs.FirstOrDefaultAsync(b => b.Hash == hash, cancellationToken);
            if (blob?.Data == null || blob.Data.Length == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(blob.Data);
        }

        public async Task ApplyAsync(ProjectRevision revision, string json, CancellationToken cancellationToken)
        {
            List<ProjectComponent> components = AnalysisDocument.ReadComponents(json, revision.Id);
            await _matcher.ApplyAsync(components, cancellationToken);
            List<ProjectComponent> existing = await _db.ProjectComponents
                .Where(c => c.RevisionId == revision.Id && c.Variant == "")
                .ToListAsync(cancellationToken);
            _db.ProjectComponents.RemoveRange(existing);
            await _db.SaveChangesAsync(cancellationToken);
            if (components.Count > 0)
            {
                _db.ProjectComponents.AddRange(components);
            }

            int? catalogHead = await _db.Commits
                .OrderByDescending(c => c.Id)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);
            revision.CatalogCommitId = catalogHead;
            revision.AnalysisStatus = "done";
            revision.AnalysisError = null;
            revision.AnalyzedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await UpdateHeadAsync(revision.ProjectId, cancellationToken);
        }

        public async Task UpdateHeadAsync(int projectId, CancellationToken cancellationToken)
        {
            try
            {
                Project? project = await _db.Projects
                    .Include(p => p.Repository)
                    .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
                if (project?.Repository == null)
                {
                    return;
                }

                string branch = project.Repository.DefaultBranch ?? "";
                HashSet<string> reachable = new(StringComparer.OrdinalIgnoreCase);
                if (branch.Length > 0)
                {
                    reachable = await _mirror.ReachableAsync(project.Repository, new[] { "refs/heads/" + branch }, cancellationToken);
                }

                List<ProjectRevision> done = await _db.ProjectRevisions
                    .Where(r => r.ProjectId == projectId && r.AnalysisStatus == "done")
                    .ToListAsync(cancellationToken);
                IEnumerable<ProjectRevision> candidates = reachable.Count > 0
                    ? done.Where(r => reachable.Contains(r.CommitSha))
                    : done.Where(r => OnDefaultBranch(r, branch));
                ProjectRevision? newest = candidates
                    .OrderByDescending(r => r.CommittedAt)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefault();
                if (newest == null || project.HeadRevisionId == newest.Id)
                {
                    return;
                }

                project.HeadRevisionId = newest.Id;
                project.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not update the head revision of project {ProjectId}.", projectId);
            }
        }

        private static bool OnDefaultBranch(ProjectRevision revision, string branch)
        {
            if (branch.Length == 0)
            {
                return false;
            }

            string full = "refs/heads/" + branch;
            return revision.Refs.Any(name =>
                name.Equals(full, StringComparison.Ordinal)
                || name.Equals(branch, StringComparison.Ordinal));
        }
    }
}
