using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public sealed class GitPollingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ProjectJobQueue _queue;
        private readonly ILogger<GitPollingService> _logger;

        public GitPollingService(IServiceScopeFactory scopes, ProjectJobQueue queue, ILogger<GitPollingService> logger)
        {
            _scopes = scopes;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!BackupService.IsRestoring)
                    {
                        await PollOnceAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Git polling failed.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            if (BackupService.IsRestoring)
            {
                return;
            }

            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            AppSettingsService settings = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
            int pollMinutes = await settings.GetIntAsync(AppSettingsService.PollIntervalKey, 5, 1, 10080, cancellationToken);
            int webhookMinutes = await settings.GetIntAsync(AppSettingsService.WebhookPollIntervalKey, 60, 1, 10080, cancellationToken);
            List<GitRepository> repositories = await db.GitRepositories
                .Where(r => r.Status != "disabled")
                .ToListAsync(cancellationToken);
            DateTime now = DateTime.UtcNow;
            foreach (GitRepository repository in repositories)
            {
                int interval = repository.PollIntervalMinutes
                    ?? (repository.SyncMode == "webhook" ? webhookMinutes : pollMinutes);
                if (repository.LastFetchAt != null && repository.LastFetchAt > now.AddMinutes(-interval))
                {
                    continue;
                }

                await _queue.EnqueueAsync("fetch", ProjectJobQueue.FetchKey(repository.Id), repository.Id, null, "{}", cancellationToken);
            }
        }
    }
}
