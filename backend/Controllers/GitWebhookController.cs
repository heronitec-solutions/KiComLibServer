using System.Collections.Concurrent;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public sealed class WebhookRateLimiter
    {
        private readonly ConcurrentDictionary<Guid, Queue<DateTime>> _windows = new();

        public bool Allow(Guid repositoryUuid, int limitPerMinute = 30)
        {
            Queue<DateTime> window = _windows.GetOrAdd(repositoryUuid, _ => new Queue<DateTime>());
            lock (window)
            {
                DateTime now = DateTime.UtcNow;
                while (window.Count > 0 && now - window.Peek() >= TimeSpan.FromMinutes(1))
                {
                    window.Dequeue();
                }

                if (window.Count >= limitPerMinute)
                {
                    return false;
                }

                window.Enqueue(now);
                return true;
            }
        }
    }

    [Route("api/hooks/git")]
    [ApiController]
    public class GitWebhookController : ControllerBase
    {
        public const long MaxBodyBytes = 25L * 1024L * 1024L;

        private readonly ApplicationDbContext _db;
        private readonly SecretProtector _secrets;
        private readonly ProjectJobQueue _queue;
        private readonly WebhookRateLimiter _limiter;
        private readonly IEnumerable<IGitProviderAdapter> _adapters;

        public GitWebhookController(
            ApplicationDbContext db,
            SecretProtector secrets,
            ProjectJobQueue queue,
            WebhookRateLimiter limiter,
            IEnumerable<IGitProviderAdapter> adapters)
        {
            _db = db;
            _secrets = secrets;
            _queue = queue;
            _limiter = limiter;
            _adapters = adapters;
        }

        [HttpPost("{repositoryUuid:guid}")]
        [RequestSizeLimit(MaxBodyBytes)]
        public async Task<IActionResult> Receive(Guid repositoryUuid, CancellationToken cancellationToken)
        {
            GitRepository? repository = await _db.GitRepositories
                .Include(r => r.Host)
                .FirstOrDefaultAsync(r => r.Uuid == repositoryUuid, cancellationToken);
            if (repository == null || repository.Host == null)
            {
                return NotFound(new { error = "Git repository not found" });
            }

            if (!_limiter.Allow(repositoryUuid))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many webhook requests" });
            }

            Request.EnableBuffering();
            using MemoryStream buffer = new();
            await Request.Body.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length > MaxBodyBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "Webhook body is too large" });
            }

            byte[] body = buffer.ToArray();
            string? secret = Unprotect(repository.WebhookSecretProtected);
            if (secret == null)
            {
                return Unauthorized(new { error = "Invalid webhook signature" });
            }

            IGitProviderAdapter? adapter = _adapters.FirstOrDefault(item => item.Kind == repository.Host.Kind);
            if (adapter == null || !adapter.VerifyWebhook(Request, body, secret))
            {
                return Unauthorized(new { error = "Invalid webhook signature" });
            }

            WebhookEvent parsed = adapter.ParseWebhook(Request, body);
            if (parsed.Kind == "ping")
            {
                return Ok(new { status = "ping" });
            }

            if (parsed.Kind == "ignored")
            {
                return NoContent();
            }

            await _queue.EnqueueAsync("fetch", ProjectJobQueue.FetchKey(repository.Id), repository.Id, null, "{}", cancellationToken);
            return Accepted(new { status = "queued", dedupKey = ProjectJobQueue.FetchKey(repository.Id) });
        }

        private string? Unprotect(string? protectedSecret)
        {
            if (string.IsNullOrWhiteSpace(protectedSecret))
            {
                return null;
            }

            try
            {
                return _secrets.Unprotect(protectedSecret);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
