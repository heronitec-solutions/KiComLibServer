using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public sealed class ProjectJobProcessor
    {
        private readonly ProjectJobQueue _queue;

        public ProjectJobProcessor(ProjectJobQueue queue)
        {
            _queue = queue;
        }

        public async Task ExecuteAsync(ProjectJob job, IEnumerable<IProjectJobHandler> handlers, CancellationToken cancellationToken)
        {
            IProjectJobHandler? handler = handlers.FirstOrDefault(h => h.Kind == job.Kind);
            if (handler == null)
            {
                await _queue.FailUnknownAsync(job, cancellationToken);
                return;
            }

            try
            {
                await handler.RunAsync(job, cancellationToken);
                await _queue.CompleteAsync(job.Id, cancellationToken, handler.CompletionNote);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await _queue.RetryAsync(job, exception.Message, cancellationToken);
            }
        }
    }

    public sealed class ProjectWorker : BackgroundService
    {
        private readonly ProjectJobQueue _queue;
        private readonly ProjectJobProcessor _processor;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ProjectWorker> _logger;

        public ProjectWorker(
            ProjectJobQueue queue,
            ProjectJobProcessor processor,
            IServiceScopeFactory scopes,
            ILogger<ProjectWorker> logger)
        {
            _queue = queue;
            _processor = processor;
            _scopes = scopes;
            _logger = logger;
        }

        public static bool Enabled
        {
            get
            {
                string? value = Environment.GetEnvironmentVariable("PROJECT_WORKER_ENABLED");
                return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static int Parallelism
        {
            get
            {
                if (int.TryParse(Environment.GetEnvironmentVariable("PROJECT_WORKER_PARALLEL"), out int parallel) && parallel > 0)
                {
                    return Math.Min(parallel, 32);
                }

                return 2;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _queue.ResetStuckAsync(stoppingToken);
                await _queue.DeleteExpiredAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Could not reset project jobs on startup.");
            }

            int parallel = Parallelism;
            Task[] loops = new Task[parallel];
            for (int i = 0; i < parallel; i++)
            {
                loops[i] = LoopAsync(stoppingToken);
            }

            await Task.WhenAll(loops);
        }

        private async Task LoopAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (BackupService.IsRestoring)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    ProjectJob? job = await _queue.ClaimAsync(stoppingToken);
                    if (job == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        continue;
                    }

                    await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
                    IEnumerable<IProjectJobHandler> handlers = scope.ServiceProvider.GetServices<IProjectJobHandler>();
                    await _processor.ExecuteAsync(job, handlers, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Project worker loop failed.");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }
}
