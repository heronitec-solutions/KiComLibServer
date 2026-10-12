using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Re-matches components from the stored analysis JSON. Does not call the runner.
    /// </summary>
    public sealed class RematchJobHandler : IProjectJobHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly ProjectAnalysisApplier _applier;

        public RematchJobHandler(ApplicationDbContext db, ProjectAnalysisApplier applier)
        {
            _db = db;
            _applier = applier;
        }

        public string Kind => "rematch";

        public async Task RunAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            (int? revisionId, string? sha) = AnalyzeJobHandler.ReadPayload(job.Payload);
            ProjectRevision? revision = null;
            if (revisionId != null)
            {
                revision = await _db.ProjectRevisions.FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken);
            }

            if (revision == null && job.ProjectId != null && !string.IsNullOrWhiteSpace(sha))
            {
                revision = await _db.ProjectRevisions.FirstOrDefaultAsync(
                    r => r.ProjectId == job.ProjectId && r.CommitSha == sha,
                    cancellationToken);
            }

            if (revision == null)
            {
                throw new InvalidOperationException("rematch job did not match a project revision.");
            }

            string? json = await _applier.ReadBlobTextAsync(revision.NetlistBlobHash, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                revision.AnalysisStatus = "failed";
                revision.AnalysisError = "No stored netlist is available to rematch.";
                await _db.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(revision.AnalysisError);
            }

            if (revision.AnalysisStatus is "pending" or "failed" or "running")
            {
                revision.AnalysisStatus = "done";
                revision.AnalysisError = null;
            }

            await _applier.ApplyAsync(revision, json, cancellationToken);
        }
    }
}
