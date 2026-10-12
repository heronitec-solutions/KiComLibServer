using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public class GitRepositoryWriteRequest
    {
        public int HostId { get; set; }

        public int? CredentialId { get; set; }

        public string? CloneUrl { get; set; }

        public string[]? TrackedBranches { get; set; }

        public string? TagPattern { get; set; }

        public string? SyncMode { get; set; }

        public int? PollIntervalMinutes { get; set; }

        public bool LfsEnabled { get; set; }

        public string? Status { get; set; }
    }

    public class GitRepositoryTestRequest
    {
        public int HostId { get; set; }

        public int? CredentialId { get; set; }

        public string? CloneUrl { get; set; }
    }

    [Route("api/git-repository")]
    [ApiController]
    public class GitRepositoryController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly SecretProtector _secrets;
        private readonly GitCli _git;
        private readonly GitMirrorService _mirror;
        private readonly ProjectJobQueue _queue;
        private readonly IEnumerable<IGitProviderAdapter> _adapters;
        private readonly ILogger<GitRepositoryController> _logger;

        public GitRepositoryController(
            ApplicationDbContext db,
            SecretProtector secrets,
            GitCli git,
            GitMirrorService mirror,
            ProjectJobQueue queue,
            IEnumerable<IGitProviderAdapter> adapters,
            ILogger<GitRepositoryController> logger)
        {
            _db = db;
            _secrets = secrets;
            _git = git;
            _mirror = mirror;
            _queue = queue;
            _adapters = adapters;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            List<GitRepository> repositories = await _db.GitRepositories
                .Include(r => r.Host)
                .OrderBy(r => r.ProviderPath)
                .ToListAsync(cancellationToken);
            return Ok(repositories.Select(repository => Map(repository, includeSecret: false)));
        }

        [HttpPost("test")]
        public async Task<IActionResult> Test([FromBody] GitRepositoryTestRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.CloneUrl))
            {
                return BadRequest(new { error = "cloneUrl is required" });
            }

            GitHost? host = await _db.GitHosts.FirstOrDefaultAsync(h => h.Id == request.HostId, cancellationToken);
            if (host == null)
            {
                return BadRequest(new { error = "Git host not found" });
            }

            GitCredential? credential = null;
            if (request.CredentialId != null)
            {
                credential = await _db.GitCredentials.FirstOrDefaultAsync(c => c.Id == request.CredentialId && c.HostId == host.Id, cancellationToken);
                if (credential == null)
                {
                    return BadRequest(new { error = "Git credential not found on this host" });
                }
            }

            RepositoryAddress address;
            try
            {
                address = Adapter(host.Kind).Parse(host, request.CloneUrl);
                string? mismatch = GitUrlPolicy.CredentialMismatch(address.CloneUrl, credential?.Kind);
                if (mismatch != null)
                {
                    return BadRequest(new { error = mismatch });
                }
            }
            catch (GitAccessException exception)
            {
                return BadRequest(new { error = exception.Message });
            }

            GitRepository probe = new()
            {
                CloneUrl = address.CloneUrl,
                Host = host,
                Credential = credential
            };
            try
            {
                LsRemoteResult remote = await _git.LsRemoteAsync(address.CloneUrl, FetchJobHandler.BuildAuth(probe, _secrets), cancellationToken);
                return Ok(new
                {
                    ok = true,
                    defaultBranch = remote.HeadTarget,
                    branches = remote.Refs.Keys.Where(name => name.StartsWith("refs/heads/", StringComparison.Ordinal)).Select(name => name["refs/heads/".Length..]).OrderBy(name => name),
                    tags = remote.Refs.Keys.Where(name => name.StartsWith("refs/tags/", StringComparison.Ordinal)).Select(name => name["refs/tags/".Length..]).OrderBy(name => name),
                    error = (string?)null
                });
            }
            catch (Exception exception) when (exception is GitCommandException or GitAccessException or System.Security.Cryptography.CryptographicException)
            {
                return Ok(new
                {
                    ok = false,
                    defaultBranch = (string?)null,
                    branches = Array.Empty<string>(),
                    tags = Array.Empty<string>(),
                    error = GitText.Scrub(exception.Message, new string?[] { credential == null ? null : SafeUnprotect(credential) })
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GitRepositoryWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            (GitRepository? repository, IActionResult? error) = await BuildAsync(request, null, cancellationToken);
            if (error != null || repository == null)
            {
                return error!;
            }

            string webhookSecret = GitText.NewWebhookSecret();
            repository.WebhookSecretProtected = _secrets.Protect(webhookSecret);
            repository.CreatedBy = CallerContext.Current(HttpContext).UserId;
            repository.Uuid = Guid.NewGuid();
            repository.CreatedAt = DateTime.UtcNow;
            repository.UpdatedAt = DateTime.UtcNow;
            _db.GitRepositories.Add(repository);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return Conflict(new { error = "A repository with this path already exists on the host" });
            }

            await _db.Entry(repository).Reference(r => r.Host).LoadAsync(cancellationToken);
            _logger.LogInformation("Created git repository {RepositoryId} by user {UserId}.", repository.Id, repository.CreatedBy);
            string webhookUrl = ApiAuth.WebhookUrl(repository.Uuid) ?? "";
            return StatusCode(StatusCodes.Status201Created, Map(repository, includeSecret: true, webhookSecret: webhookSecret, webhookUrl: webhookUrl));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitRepository? repository = await LoadAsync(id, cancellationToken);
            if (repository == null)
            {
                return NotFound(new { error = "Git repository not found" });
            }

            return Ok(Map(repository, includeSecret: false));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] GitRepositoryWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitRepository? existing = await LoadAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { error = "Git repository not found" });
            }

            (GitRepository? updated, IActionResult? error) = await BuildAsync(request, existing, cancellationToken);
            if (error != null || updated == null)
            {
                return error!;
            }

            existing.CredentialId = updated.CredentialId;
            existing.CloneUrl = updated.CloneUrl;
            existing.WebUrl = updated.WebUrl;
            existing.ProviderPath = updated.ProviderPath;
            existing.TrackedBranches = updated.TrackedBranches;
            existing.TagPattern = updated.TagPattern;
            existing.SyncMode = updated.SyncMode;
            existing.PollIntervalMinutes = updated.PollIntervalMinutes;
            existing.LfsEnabled = updated.LfsEnabled;
            if (!string.IsNullOrWhiteSpace(request?.Status))
            {
                existing.Status = updated.Status;
            }

            existing.UpdatedAt = DateTime.UtcNow;
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return Conflict(new { error = "A repository with this path already exists on the host" });
            }

            _logger.LogInformation("Updated git repository {RepositoryId} by user {UserId}.", existing.Id, CallerContext.Current(HttpContext).UserId);
            return Ok(Map(existing, includeSecret: false));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, [FromQuery] bool deleteProjects, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitRepository? repository = await _db.GitRepositories.Include(r => r.Projects).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (repository == null)
            {
                return NotFound(new { error = "Git repository not found" });
            }

            if (repository.Projects.Count > 0 && !deleteProjects)
            {
                return Conflict(new { error = "Repository has projects. Pass deleteProjects=true to delete them as well." });
            }

            foreach (Project project in repository.Projects.ToList())
            {
                project.HeadRevisionId = null;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _db.Projects.RemoveRange(repository.Projects);
            await _db.SaveChangesAsync(cancellationToken);
            Guid uuid = repository.Uuid;
            _db.GitRepositories.Remove(repository);
            await _db.SaveChangesAsync(cancellationToken);
            TryDeleteMirror(uuid);
            _logger.LogInformation("Deleted git repository {RepositoryId} by user {UserId}.", id, CallerContext.Current(HttpContext).UserId);
            return NoContent();
        }

        [HttpPost("{id:int}/sync")]
        public async Task<IActionResult> Sync(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitRepository? repository = await _db.GitRepositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (repository == null)
            {
                return NotFound(new { error = "Git repository not found" });
            }

            await _queue.EnqueueAsync("fetch", ProjectJobQueue.FetchKey(repository.Id), repository.Id, null, "{}", cancellationToken);
            return Accepted(new { status = "queued", dedupKey = ProjectJobQueue.FetchKey(repository.Id) });
        }

        [HttpPost("{id:int}/webhook-secret")]
        public async Task<IActionResult> RotateSecret(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitRepository? repository = await LoadAsync(id, cancellationToken);
            if (repository == null)
            {
                return NotFound(new { error = "Git repository not found" });
            }

            string secret = GitText.NewWebhookSecret();
            repository.WebhookSecretProtected = _secrets.Protect(secret);
            repository.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Rotated webhook secret for repository {RepositoryId} by user {UserId}.", id, CallerContext.Current(HttpContext).UserId);
            string webhookUrl = ApiAuth.WebhookUrl(repository.Uuid) ?? "";
            return Ok(new
            {
                webhook = WebhookPayload(repository, webhookUrl, secret)
            });
        }

        [HttpGet("{id:int}/discovered")]
        public async Task<IActionResult> Discovered(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitRepository? repository = await _db.GitRepositories.Include(r => r.Projects).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (repository == null)
            {
                return NotFound(new { error = "Git repository not found" });
            }

            IReadOnlyList<string> files = await _mirror.ListProjectFilesAsync(repository, cancellationToken);
            return Ok(new
            {
                defaultBranch = repository.DefaultBranch,
                files = files.Select(file =>
                {
                    Project? project = repository.Projects.FirstOrDefault(p => p.ProjectFile == file);
                    return new { projectFile = file, projectId = project?.Id, isProject = project != null };
                })
            });
        }

        private async Task<(GitRepository? Repository, IActionResult? Error)> BuildAsync(
            GitRepositoryWriteRequest? request,
            GitRepository? existing,
            CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CloneUrl))
            {
                return (null, BadRequest(new { error = "cloneUrl is required" }));
            }

            int hostId = existing?.HostId ?? request.HostId;
            GitHost? host = await _db.GitHosts.FirstOrDefaultAsync(h => h.Id == hostId, cancellationToken);
            if (host == null)
            {
                return (null, BadRequest(new { error = "Git host not found" }));
            }

            GitCredential? credential = null;
            if (request.CredentialId != null)
            {
                credential = await _db.GitCredentials.FirstOrDefaultAsync(c => c.Id == request.CredentialId && c.HostId == host.Id, cancellationToken);
                if (credential == null)
                {
                    return (null, BadRequest(new { error = "Git credential not found on this host" }));
                }
            }

            RepositoryAddress address;
            try
            {
                address = Adapter(host.Kind).Parse(host, request.CloneUrl);
                string? mismatch = GitUrlPolicy.CredentialMismatch(address.CloneUrl, credential?.Kind);
                if (mismatch != null)
                {
                    return (null, BadRequest(new { error = mismatch }));
                }
            }
            catch (GitAccessException exception)
            {
                return (null, BadRequest(new { error = exception.Message }));
            }

            string syncMode = string.IsNullOrWhiteSpace(request.SyncMode) ? existing?.SyncMode ?? "poll" : request.SyncMode.Trim();
            if (syncMode is not ("poll" or "webhook"))
            {
                return (null, BadRequest(new { error = "syncMode must be poll or webhook" }));
            }

            string status = string.IsNullOrWhiteSpace(request.Status) ? existing?.Status ?? "new" : request.Status.Trim();
            if (status is not ("new" or "ok" or "error" or "disabled"))
            {
                return (null, BadRequest(new { error = "status is invalid" }));
            }

            GitRepository repository = existing ?? new GitRepository { HostId = host.Id, Status = "new" };
            repository.HostId = host.Id;
            repository.CredentialId = request.CredentialId;
            repository.CloneUrl = address.CloneUrl;
            repository.WebUrl = address.WebUrl;
            repository.ProviderPath = address.ProviderPath;
            repository.TrackedBranches = request.TrackedBranches ?? existing?.TrackedBranches ?? Array.Empty<string>();
            repository.TagPattern = string.IsNullOrWhiteSpace(request.TagPattern) ? existing?.TagPattern ?? "*" : request.TagPattern.Trim();
            repository.SyncMode = syncMode;
            repository.PollIntervalMinutes = request.PollIntervalMinutes;
            repository.LfsEnabled = request.LfsEnabled;
            repository.Status = status;
            repository.Host = host;
            return (repository, null);
        }

        private async Task<GitRepository?> LoadAsync(int id, CancellationToken cancellationToken)
        {
            return await _db.GitRepositories
                .Include(r => r.Host)
                .Include(r => r.Refs)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        private IGitProviderAdapter Adapter(string kind)
        {
            return _adapters.FirstOrDefault(adapter => adapter.Kind == kind)
                ?? throw new GitAccessException("Unknown git host kind '" + kind + "'.");
        }

        private object Map(GitRepository repository, bool includeSecret, string? webhookSecret = null, string? webhookUrl = null)
        {
            (string? error, List<MovedTagNotice> moved) = RepositoryNotices.Unpack(repository.LastError);
            string url = webhookUrl ?? ApiAuth.WebhookUrl(repository.Uuid) ?? "";
            IGitProviderAdapter? adapter = repository.Host == null ? null : _adapters.FirstOrDefault(item => item.Kind == repository.Host.Kind);
            return new
            {
                id = repository.Id,
                uuid = repository.Uuid,
                hostId = repository.HostId,
                credentialId = repository.CredentialId,
                cloneUrl = repository.CloneUrl,
                webUrl = repository.WebUrl,
                providerPath = repository.ProviderPath,
                defaultBranch = repository.DefaultBranch,
                trackedBranches = repository.TrackedBranches,
                tagPattern = repository.TagPattern,
                syncMode = repository.SyncMode,
                pollIntervalMinutes = repository.PollIntervalMinutes,
                lfsEnabled = repository.LfsEnabled,
                status = repository.Status,
                lastFetchAt = repository.LastFetchAt,
                lastError = error,
                movedTags = moved.Select(tag => new { name = tag.Name, from = tag.From, to = tag.To }),
                createdBy = repository.CreatedBy,
                refs = repository.Refs.OrderBy(r => r.Name).Select(r => new { name = r.Name, sha = r.Sha, updatedAt = r.UpdatedAt }),
                links = adapter == null ? null : adapter.Links(repository),
                webhook = new
                {
                    url,
                    secret = includeSecret ? webhookSecret : null,
                    instructions = adapter?.Instructions(repository, url)
                }
            };
        }

        private object WebhookPayload(GitRepository repository, string webhookUrl, string secret)
        {
            IGitProviderAdapter? adapter = repository.Host == null ? null : _adapters.FirstOrDefault(item => item.Kind == repository.Host.Kind);
            return new
            {
                url = webhookUrl,
                secret,
                instructions = adapter?.Instructions(repository, webhookUrl)
            };
        }

        private string? SafeUnprotect(GitCredential credential)
        {
            try
            {
                return _secrets.Unprotect(credential.SecretProtected);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void TryDeleteMirror(Guid uuid)
        {
            string path = _git.MirrorPath(uuid);
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not delete the git mirror for repository {Uuid}.", uuid);
            }
        }
    }
}
