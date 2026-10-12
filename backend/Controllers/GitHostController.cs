using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public class GitHostWriteRequest
    {
        public string? Kind { get; set; }

        public string? Name { get; set; }

        public string? WebUrl { get; set; }

        public string? SshHost { get; set; }

        public string? CaCertificate { get; set; }

        public bool AllowHttp { get; set; }
    }

    public class GitHostKnownHostsRequest
    {
        public string? KnownHosts { get; set; }
    }

    [Route("api/git-host")]
    [ApiController]
    public class GitHostController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly GitCli _git;
        private readonly ILogger<GitHostController> _logger;

        public GitHostController(ApplicationDbContext db, GitCli git, ILogger<GitHostController> logger)
        {
            _db = db;
            _git = git;
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

            List<GitHost> hosts = await _db.GitHosts.OrderBy(h => h.Name).ToListAsync(cancellationToken);
            return Ok(hosts.Select(Map));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GitHostWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            string? error = Validate(request, creating: true);
            if (error != null)
            {
                return BadRequest(new { error });
            }

            if (await _db.GitHosts.AnyAsync(h => h.Name == request!.Name!.Trim(), cancellationToken))
            {
                return Conflict(new { error = "A git host with this name already exists" });
            }

            GitHost host = new()
            {
                Uuid = Guid.NewGuid(),
                Kind = request!.Kind!.Trim(),
                Name = request.Name!.Trim(),
                WebUrl = request.WebUrl!.Trim().TrimEnd('/'),
                SshHost = NullIfEmpty(request.SshHost),
                CaCertificate = NullIfEmpty(request.CaCertificate),
                AllowHttp = request.AllowHttp,
                IsSystem = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.GitHosts.Add(host);
            await _db.SaveChangesAsync(cancellationToken);
            CallerContext caller = CallerContext.Current(HttpContext);
            _logger.LogInformation("Created git host {HostId} ({Name}) by user {UserId}.", host.Id, host.Name, caller.UserId);
            return StatusCode(StatusCodes.Status201Created, Map(host));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] GitHostWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitHost? host = await _db.GitHosts.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
            if (host == null)
            {
                return NotFound(new { error = "Git host not found" });
            }

            string? error = Validate(request, creating: false);
            if (error != null)
            {
                return BadRequest(new { error });
            }

            if (host.IsSystem && !string.Equals(request!.Kind, host.Kind, StringComparison.Ordinal))
            {
                return BadRequest(new { error = "The kind of a system git host cannot be changed" });
            }

            string name = request!.Name!.Trim();
            if (await _db.GitHosts.AnyAsync(h => h.Name == name && h.Id != id, cancellationToken))
            {
                return Conflict(new { error = "A git host with this name already exists" });
            }

            host.Kind = request.Kind!.Trim();
            host.Name = name;
            host.WebUrl = request.WebUrl!.Trim().TrimEnd('/');
            host.SshHost = NullIfEmpty(request.SshHost);
            host.CaCertificate = NullIfEmpty(request.CaCertificate);
            host.AllowHttp = request.AllowHttp;
            host.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Updated git host {HostId} by user {UserId}.", host.Id, CallerContext.Current(HttpContext).UserId);
            return Ok(Map(host));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitHost? host = await _db.GitHosts.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
            if (host == null)
            {
                return NotFound(new { error = "Git host not found" });
            }

            if (host.IsSystem)
            {
                return Conflict(new { error = "System git hosts cannot be deleted" });
            }

            bool used = await _db.GitCredentials.AnyAsync(c => c.HostId == id, cancellationToken)
                || await _db.GitRepositories.AnyAsync(r => r.HostId == id, cancellationToken);
            if (used)
            {
                return Conflict(new { error = "Git host is in use" });
            }

            _db.GitHosts.Remove(host);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deleted git host {HostId} by user {UserId}.", id, CallerContext.Current(HttpContext).UserId);
            return NoContent();
        }

        [HttpPost("{id:int}/ssh-host-key")]
        public async Task<IActionResult> ScanHostKey(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitHost? host = await _db.GitHosts.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
            if (host == null)
            {
                return NotFound(new { error = "Git host not found" });
            }

            try
            {
                (string hostname, int port) = GitUrlPolicy.ParseSshEndpoint(host.SshHost);
                string knownHosts = await _git.KeyScanAsync(hostname, port, cancellationToken);
                return Ok(new
                {
                    knownHosts,
                    fingerprints = GitCli.FingerprintsOfKnownHosts(knownHosts)
                });
            }
            catch (Exception exception) when (exception is GitAccessException or GitCommandException)
            {
                return BadRequest(new { error = exception.Message });
            }
        }

        [HttpPut("{id:int}/ssh-host-key")]
        public async Task<IActionResult> ConfirmHostKey(int id, [FromBody] GitHostKnownHostsRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitHost? host = await _db.GitHosts.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
            if (host == null)
            {
                return NotFound(new { error = "Git host not found" });
            }

            if (string.IsNullOrWhiteSpace(request?.KnownHosts))
            {
                return BadRequest(new { error = "knownHosts is required" });
            }

            host.SshKnownHosts = request.KnownHosts.Trim();
            host.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Confirmed SSH host key for git host {HostId} by user {UserId}.", host.Id, CallerContext.Current(HttpContext).UserId);
            return Ok(Map(host));
        }

        private static string? Validate(GitHostWriteRequest? request, bool creating)
        {
            if (request == null)
            {
                return "Request body is required";
            }

            string kind = (request.Kind ?? "").Trim();
            if (kind is not ("gitlab" or "github" or "generic"))
            {
                return "kind must be gitlab, github, or generic";
            }

            string name = (request.Name ?? "").Trim();
            if (name.Length < 1 || name.Length > 200)
            {
                return "name must be between 1 and 200 characters";
            }

            if (!Uri.TryCreate((request.WebUrl ?? "").Trim(), UriKind.Absolute, out Uri? web)
                || (web.Scheme != "https" && web.Scheme != "http"))
            {
                return "webUrl must be an absolute http or https URL";
            }

            if (web.Scheme == "http" && !request.AllowHttp)
            {
                return "allowHttp must be true when webUrl uses http";
            }

            return null;
        }

        private static object Map(GitHost host)
        {
            return new
            {
                id = host.Id,
                uuid = host.Uuid,
                kind = host.Kind,
                name = host.Name,
                webUrl = host.WebUrl,
                sshHost = host.SshHost,
                caCertificate = host.CaCertificate,
                sshKnownHosts = host.SshKnownHosts,
                allowHttp = host.AllowHttp,
                isSystem = host.IsSystem,
                createdAt = host.CreatedAt,
                updatedAt = host.UpdatedAt
            };
        }

        private static string? NullIfEmpty(string? value)
        {
            string trimmed = (value ?? "").Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
