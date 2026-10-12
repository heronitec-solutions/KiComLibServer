using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public sealed class RenderJobHandler : IProjectJobHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly GitMirrorService _mirror;
        private readonly IKiCadRunnerClient _runner;
        private readonly ProjectCache _cache;

        public RenderJobHandler(
            ApplicationDbContext db,
            GitMirrorService mirror,
            IKiCadRunnerClient runner,
            ProjectCache cache)
        {
            _db = db;
            _mirror = mirror;
            _runner = runner;
            _cache = cache;
        }

        public string Kind => "render";

        public async Task RunAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            (int? revisionId, string? sha) = AnalyzeJobHandler.ReadPayload(job.Payload);
            ProjectRevision? revision = null;
            if (revisionId != null)
            {
                revision = await _db.ProjectRevisions
                    .Include(r => r.Project)!.ThenInclude(p => p!.Repository)
                    .FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken);
            }

            if (revision == null && job.ProjectId != null && !string.IsNullOrWhiteSpace(sha))
            {
                revision = await _db.ProjectRevisions
                    .Include(r => r.Project)!.ThenInclude(p => p!.Repository)
                    .FirstOrDefaultAsync(r => r.ProjectId == job.ProjectId && r.CommitSha == sha, cancellationToken);
            }

            if (revision?.Project?.Repository == null)
            {
                throw new InvalidOperationException("render job did not match a project revision.");
            }

            byte[] tar = await _mirror.ArchiveAsync(
                revision.Project.Repository,
                revision.CommitSha,
                ProjectExport.TreePath(revision.Project),
                ProjectExport.ArchiveMaxMb(),
                cancellationToken);
            byte[] zip = await _runner.RenderAsync(tar, revision.Project.ProjectFile, cancellationToken);
            _cache.Unpack(_cache.RenderRelative(revision.ProjectId, revision.CommitSha), zip, protect: false);
        }
    }
}
