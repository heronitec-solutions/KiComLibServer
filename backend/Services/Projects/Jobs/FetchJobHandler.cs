using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public sealed class FetchJobHandler : IProjectJobHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly GitMirrorService _mirror;
        private readonly RevisionDiscovery _discovery;
        private readonly SecretProtector _secrets;
        private readonly ProjectJobQueue _queue;
        private readonly AppSettingsService _settings;
        private readonly ILogger<FetchJobHandler> _logger;

        public FetchJobHandler(
            ApplicationDbContext db,
            GitMirrorService mirror,
            RevisionDiscovery discovery,
            SecretProtector secrets,
            ProjectJobQueue queue,
            AppSettingsService settings,
            ILogger<FetchJobHandler> logger)
        {
            _db = db;
            _mirror = mirror;
            _discovery = discovery;
            _secrets = secrets;
            _queue = queue;
            _settings = settings;
            _logger = logger;
        }

        public string Kind => "fetch";

        public async Task RunAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            if (job.RepositoryId == null)
            {
                throw new InvalidOperationException("fetch job is missing repository_id.");
            }

            int repositoryId = job.RepositoryId.Value;
            await _mirror.LockAsync(repositoryId, async () =>
            {
                GitRepository? repository = await _db.GitRepositories
                    .Include(r => r.Host)
                    .Include(r => r.Credential)
                    .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);
                if (repository == null || repository.Host == null)
                {
                    throw new InvalidOperationException("Repository " + repositoryId.ToString() + " was not found.");
                }

                if (repository.Status == "disabled")
                {
                    return;
                }

                Dictionary<string, string> previous = await _db.GitRefs
                    .Where(r => r.RepositoryId == repository.Id)
                    .ToDictionaryAsync(r => r.Name, r => r.Sha, cancellationToken);

                GitAuth auth = BuildAuth(repository, _secrets);
                try
                {
                    MirrorSyncResult sync = await _mirror.SyncAsync(repository, auth, previous, cancellationToken);
                    List<MovedTagNotice> moved = MergeMovedTags(repository.LastError, previous, sync.Refs);
                    repository.Status = "ok";
                    repository.LastFetchAt = DateTime.UtcNow;
                    repository.DefaultBranch = string.IsNullOrWhiteSpace(sync.DefaultBranch) ? repository.DefaultBranch : sync.DefaultBranch;
                    repository.LastError = RepositoryNotices.Pack(moved, null);
                    if (repository.Credential != null)
                    {
                        repository.Credential.LastUsedAt = DateTime.UtcNow;
                        repository.Credential.LastError = null;
                    }

                    await ReplaceRefsAsync(repository.Id, sync.Refs, cancellationToken);
                    await _db.SaveChangesAsync(cancellationToken);
                    int maxCommits = await _settings.GetIntAsync(AppSettingsService.MaxCommitsKey, 20, 1, 5000, cancellationToken);
                    int initialTags = await _settings.GetIntAsync(AppSettingsService.InitialTagsKey, 50, 0, 5000, cancellationToken);
                    await _discovery.ImportAsync(_db, repository, previous, sync.Refs, _queue, maxCommits, initialTags, cancellationToken);
                    await _queue.EnqueueAsync("discover", ProjectJobQueue.DiscoverKey(repository.Id), repository.Id, null, "{}", cancellationToken);
                    _logger.LogInformation("Fetched repository {RepositoryId} ({Changed}).", repository.Id, sync.Fetched ? "updated" : "unchanged");
                }
                catch (Exception exception) when (exception is GitCommandException or GitAccessException or System.Security.Cryptography.CryptographicException)
                {
                    string message = GitText.Scrub(exception.Message, SecretValues(repository, _secrets));
                    bool authentication = GitText.IsAuthenticationFailure(exception.Message);
                    (_, List<MovedTagNotice> tags) = RepositoryNotices.Unpack(repository.LastError);
                    repository.Status = "error";
                    repository.LastError = RepositoryNotices.Pack(tags, authentication
                        ? "Authentication failed. Check the git credential."
                        : message);
                    if (repository.Credential != null && authentication)
                    {
                        repository.Credential.LastError = "Authentication failed";
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                    throw new InvalidOperationException(repository.LastError ?? message, exception);
                }
            }, cancellationToken);
        }

        internal static GitAuth BuildAuth(GitRepository repository, SecretProtector secrets)
        {
            GitHost host = repository.Host ?? throw new InvalidOperationException("Repository has no host.");
            string? username = null;
            string? token = null;
            string? sshKey = null;
            if (repository.Credential != null)
            {
                string secret = secrets.Unprotect(repository.Credential.SecretProtected);
                if (repository.Credential.Kind == "ssh_key")
                {
                    sshKey = secret;
                }
                else
                {
                    username = string.IsNullOrWhiteSpace(repository.Credential.Username)
                        ? GitUrlPolicy.DefaultHttpsUsername(host.Kind)
                        : repository.Credential.Username.Trim();
                    token = secret;
                }
            }

            return new GitAuth
            {
                Username = username,
                HttpsToken = token,
                SshPrivateKey = sshKey,
                KnownHosts = host.SshKnownHosts,
                CaCertificate = host.CaCertificate,
                AllowHttp = host.AllowHttp,
                AllowFile = repository.CloneUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static IEnumerable<string?> SecretValues(GitRepository repository, SecretProtector secrets)
        {
            if (repository.Credential == null)
            {
                yield break;
            }

            yield return repository.Credential.Username;
            string unprotected;
            try
            {
                unprotected = secrets.Unprotect(repository.Credential.SecretProtected);
            }
            catch (Exception)
            {
                yield break;
            }

            yield return unprotected;
        }

        private static List<MovedTagNotice> MergeMovedTags(
            string? lastError,
            IReadOnlyDictionary<string, string> previous,
            IReadOnlyDictionary<string, string> current)
        {
            (_, List<MovedTagNotice> tags) = RepositoryNotices.Unpack(lastError);
            foreach (KeyValuePair<string, string> pair in current)
            {
                if (!pair.Key.StartsWith("refs/tags/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!previous.TryGetValue(pair.Key, out string? from) || from.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = pair.Key["refs/tags/".Length..];
                MovedTagNotice? existing = tags.FirstOrDefault(tag => tag.Name == name);
                if (existing == null)
                {
                    tags.Add(new MovedTagNotice { Name = name, From = from, To = pair.Value });
                }
                else
                {
                    existing.To = pair.Value;
                }
            }

            return tags;
        }

        private async Task ReplaceRefsAsync(int repositoryId, IReadOnlyDictionary<string, string> current, CancellationToken cancellationToken)
        {
            List<GitRef> existing = await _db.GitRefs.Where(r => r.RepositoryId == repositoryId).ToListAsync(cancellationToken);
            foreach (GitRef row in existing)
            {
                if (!current.TryGetValue(row.Name, out string? sha))
                {
                    _db.GitRefs.Remove(row);
                }
                else if (!row.Sha.Equals(sha, StringComparison.OrdinalIgnoreCase))
                {
                    row.Sha = sha;
                    row.UpdatedAt = DateTime.UtcNow;
                }
            }

            foreach (KeyValuePair<string, string> pair in current)
            {
                if (existing.All(row => row.Name != pair.Key))
                {
                    _db.GitRefs.Add(new GitRef
                    {
                        RepositoryId = repositoryId,
                        Name = pair.Key,
                        Sha = pair.Value,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }
        }
    }

    public sealed class DiscoverJobHandler : IProjectJobHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly GitMirrorService _mirror;
        private readonly ILogger<DiscoverJobHandler> _logger;

        public DiscoverJobHandler(ApplicationDbContext db, GitMirrorService mirror, ILogger<DiscoverJobHandler> logger)
        {
            _db = db;
            _mirror = mirror;
            _logger = logger;
        }

        public string Kind => "discover";

        public async Task RunAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            if (job.RepositoryId == null)
            {
                throw new InvalidOperationException("discover job is missing repository_id.");
            }

            int repositoryId = job.RepositoryId.Value;
            await _mirror.LockAsync(repositoryId, async () =>
            {
                GitRepository? repository = await _db.GitRepositories.FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);
                if (repository == null)
                {
                    throw new InvalidOperationException("Repository " + repositoryId.ToString() + " was not found.");
                }

                IReadOnlyList<string> files = await _mirror.ListProjectFilesAsync(repository, cancellationToken);
                _logger.LogInformation("Discovered {Count} KiCad projects in repository {RepositoryId}. Nothing was created automatically.", files.Count, repository.Id);
            }, cancellationToken);
        }
    }
}
