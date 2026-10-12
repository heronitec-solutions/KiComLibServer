using System.Text.Json;
using KiComLibBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// After a catalog commit, enqueue rematch for every project head.
    /// At most one wave every 10 minutes.
    /// </summary>
    public class ProjectRematchScheduler
    {
        private static readonly object Gate = new();
        private static DateTime _lastUtc = DateTime.MinValue;

        private readonly IServiceScopeFactory _scopes;
        private readonly ProjectJobQueue _queue;
        private readonly ILogger<ProjectRematchScheduler> _logger;

        public ProjectRematchScheduler(IServiceScopeFactory scopes, ProjectJobQueue queue, ILogger<ProjectRematchScheduler> logger)
        {
            _scopes = scopes;
            _queue = queue;
            _logger = logger;
        }

        public async Task NotifyCatalogChangedAsync(CancellationToken cancellationToken)
        {
            lock (Gate)
            {
                if (DateTime.UtcNow - _lastUtc < TimeSpan.FromMinutes(10))
                {
                    return;
                }

                _lastUtc = DateTime.UtcNow;
            }

            try
            {
                await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
                ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var heads = await db.Projects
                    .Where(p => p.HeadRevisionId != null && p.HeadRevision != null)
                    .Select(p => new { p.Id, RevisionId = p.HeadRevisionId!.Value, p.HeadRevision!.CommitSha })
                    .ToListAsync(cancellationToken);
                foreach (var head in heads)
                {
                    string payload = JsonSerializer.Serialize(new { revisionId = head.RevisionId, commitSha = head.CommitSha });
                    await _queue.EnqueueAsync(
                        "rematch",
                        ProjectJobQueue.RematchKey(head.Id, head.CommitSha),
                        null,
                        head.Id,
                        payload,
                        cancellationToken);
                }

                if (heads.Count > 0)
                {
                    _logger.LogInformation("Queued catalog rematch for {Count} project heads.", heads.Count);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Catalog rematch could not be queued.");
            }
        }
    }
}
