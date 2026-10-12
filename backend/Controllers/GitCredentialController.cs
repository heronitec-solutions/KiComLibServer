using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public class GitCredentialWriteRequest
    {
        public int HostId { get; set; }

        public string? Name { get; set; }

        public string? Kind { get; set; }

        public string? Username { get; set; }

        public string? Secret { get; set; }

        public string? PublicKey { get; set; }

        public bool Generate { get; set; }

        public DateTime? ExpiresAt { get; set; }
    }

    [Route("api/git-credential")]
    [ApiController]
    public class GitCredentialController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly SecretProtector _secrets;
        private readonly GitCli _git;
        private readonly ILogger<GitCredentialController> _logger;

        public GitCredentialController(ApplicationDbContext db, SecretProtector secrets, GitCli git, ILogger<GitCredentialController> logger)
        {
            _db = db;
            _secrets = secrets;
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

            List<GitCredential> credentials = await _db.GitCredentials.OrderBy(c => c.Name).ToListAsync(cancellationToken);
            return Ok(credentials.Select(credential => Map(credential)));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GitCredentialWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            if (request == null || !await _db.GitHosts.AnyAsync(h => h.Id == request.HostId, cancellationToken))
            {
                return BadRequest(new { error = "hostId is required" });
            }

            string name = (request.Name ?? "").Trim();
            if (name.Length < 1 || name.Length > 200)
            {
                return BadRequest(new { error = "name must be between 1 and 200 characters" });
            }

            string kind = (request.Kind ?? "").Trim();
            if (kind is not ("https_token" or "ssh_key"))
            {
                return BadRequest(new { error = "kind must be https_token or ssh_key" });
            }

            string? publicKey = null;
            string secret;
            string hint;
            try
            {
                (secret, publicKey, hint) = await MaterializeSecretAsync(request, kind, cancellationToken);
            }
            catch (GitCommandException exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return BadRequest(new { error = exception.Message });
            }

            if (await _db.GitCredentials.AnyAsync(c => c.HostId == request.HostId && c.Name == name, cancellationToken))
            {
                return Conflict(new { error = "A credential with this name already exists on the host" });
            }

            GitCredential credential = new()
            {
                Uuid = Guid.NewGuid(),
                HostId = request.HostId,
                Name = name,
                Kind = kind,
                Username = kind == "https_token" ? NullIfEmpty(request.Username) : null,
                SecretProtected = _secrets.Protect(secret),
                SecretHint = hint,
                PublicKey = publicKey,
                ExpiresAt = request.ExpiresAt,
                CreatedBy = CallerContext.Current(HttpContext).UserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.GitCredentials.Add(credential);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Created git credential {CredentialId} on host {HostId} by user {UserId}.", credential.Id, credential.HostId, credential.CreatedBy);
            return StatusCode(StatusCodes.Status201Created, Map(credential, publicKey));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] GitCredentialWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitCredential? credential = await _db.GitCredentials.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (credential == null)
            {
                return NotFound(new { error = "Git credential not found" });
            }

            if (request == null)
            {
                return BadRequest(new { error = "Request body is required" });
            }

            string name = string.IsNullOrWhiteSpace(request.Name) ? credential.Name : request.Name.Trim();
            if (await _db.GitCredentials.AnyAsync(c => c.HostId == credential.HostId && c.Name == name && c.Id != id, cancellationToken))
            {
                return Conflict(new { error = "A credential with this name already exists on the host" });
            }

            credential.Name = name;
            credential.ExpiresAt = request.ExpiresAt ?? credential.ExpiresAt;
            if (request.ExpiresAt == null && request.Name != null)
            {
                // Keep the stored expiry when the client omits it.
            }

            if (!string.IsNullOrWhiteSpace(request.Secret) || request.Generate)
            {
                try
                {
                    (string secret, string? publicKey, string hint) = await MaterializeSecretAsync(
                        request,
                        credential.Kind,
                        cancellationToken);
                    credential.SecretProtected = _secrets.Protect(secret);
                    credential.SecretHint = hint;
                    if (publicKey != null)
                    {
                        credential.PublicKey = publicKey;
                    }

                    if (credential.Kind == "https_token")
                    {
                        credential.Username = NullIfEmpty(request.Username) ?? credential.Username;
                    }
                }
                catch (GitCommandException exception)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new { error = exception.Message });
                }
                catch (InvalidOperationException exception)
                {
                    return BadRequest(new { error = exception.Message });
                }
            }
            else if (credential.Kind == "https_token" && request.Username != null)
            {
                credential.Username = NullIfEmpty(request.Username);
            }

            credential.UpdatedAt = DateTime.UtcNow;
            credential.LastError = null;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Updated git credential {CredentialId} by user {UserId}.", credential.Id, CallerContext.Current(HttpContext).UserId);
            return Ok(Map(credential));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = ApiAuth.RequireAdmin(HttpContext);
            if (denied != null)
            {
                return denied;
            }

            GitCredential? credential = await _db.GitCredentials.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (credential == null)
            {
                return NotFound(new { error = "Git credential not found" });
            }

            if (await _db.GitRepositories.AnyAsync(r => r.CredentialId == id, cancellationToken))
            {
                return Conflict(new { error = "Git credential is in use" });
            }

            _db.GitCredentials.Remove(credential);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deleted git credential {CredentialId} by user {UserId}.", id, CallerContext.Current(HttpContext).UserId);
            return NoContent();
        }

        private async Task<(string Secret, string? PublicKey, string Hint)> MaterializeSecretAsync(
            GitCredentialWriteRequest request,
            string kind,
            CancellationToken cancellationToken)
        {
            if (kind == "ssh_key" && request.Generate)
            {
                (string privateKey, string publicKey) = await _git.GenerateEd25519Async(cancellationToken);
                return (privateKey, publicKey, GitText.OpenSshFingerprint(publicKey));
            }

            string secret = (request.Secret ?? "").Trim();
            if (secret.Length < 1)
            {
                throw new InvalidOperationException("secret is required");
            }

            if (kind == "ssh_key")
            {
                string? publicKey = NullIfEmpty(request.PublicKey);
                string hint = publicKey == null ? "SSH key" : GitText.OpenSshFingerprint(publicKey);
                return (secret, publicKey, hint);
            }

            return (secret, null, GitText.TokenHint(secret));
        }

        internal static object Map(GitCredential credential, string? publicKeyOverride = null)
        {
            bool expiresSoon = credential.ExpiresAt != null && credential.ExpiresAt <= DateTime.UtcNow.AddDays(14);
            return new
            {
                id = credential.Id,
                uuid = credential.Uuid,
                hostId = credential.HostId,
                name = credential.Name,
                kind = credential.Kind,
                username = credential.Username,
                secretHint = credential.SecretHint,
                publicKey = publicKeyOverride ?? credential.PublicKey,
                expiresAt = credential.ExpiresAt,
                expiresSoon,
                lastUsedAt = credential.LastUsedAt,
                lastError = credential.LastError,
                createdBy = credential.CreatedBy,
                createdAt = credential.CreatedAt,
                updatedAt = credential.UpdatedAt
            };
        }

        private static string? NullIfEmpty(string? value)
        {
            string trimmed = (value ?? "").Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
