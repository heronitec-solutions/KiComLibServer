using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public class LoginRequest
    {
        public string? Username { get; set; }

        public string? Password { get; set; }
    }

    public class PasswordChangeRequest
    {
        public string? CurrentPassword { get; set; }

        public string? NewPassword { get; set; }
    }

    public class UserWriteRequest
    {
        public string? Username { get; set; }

        public string? Password { get; set; }

        public bool? IsActive { get; set; }

        public List<int>? GroupIds { get; set; }
    }

    public class GroupWriteRequest
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public bool CanRead { get; set; } = true;

        public bool CanWrite { get; set; }

        public bool CanAdmin { get; set; }

        public bool CanReadWarehouse { get; set; }

        public bool CanWriteWarehouse { get; set; }
    }

    public class TokenCreateRequest
    {
        public string? Name { get; set; }

        public bool? CanWrite { get; set; }
    }

    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly AccessService _access;
        private readonly DirectoryAuthService _directoryAuth;
        private readonly LoginRateLimiter _limiter;

        public AuthController(
            ApplicationDbContext db,
            AccessService access,
            DirectoryAuthService directoryAuth,
            LoginRateLimiter limiter)
        {
            _db = db;
            _access = access;
            _directoryAuth = directoryAuth;
            _limiter = limiter;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken cancellationToken)
        {
            string username = (request?.Username ?? "").Trim();
            string limitKey = LoginRateLimiter.Key(HttpContext.Connection.RemoteIpAddress?.ToString(), username);
            if (_limiter.IsBlocked(limitKey))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many sign-in attempts. Try again later." });
            }

            AppUser? user = await _directoryAuth.AuthenticateAsync(username, request?.Password ?? "", cancellationToken);
            if (user == null)
            {
                _limiter.RecordFailure(limitKey);
                return Unauthorized(new { error = "Unknown username or password" });
            }

            _limiter.Clear(limitKey);
            IssuedSession session = await _access.CreateSessionAsync(user, cancellationToken);
            SessionCookies.Set(Response, Request, session.Token, session.ExpiresAt);
            user.Groups = await _db.AppGroups.AsNoTracking().Where(g => g.Users.Any(member => member.Id == user.Id)).ToListAsync(cancellationToken);
            CallerContext caller = AccessService.FromUser(user, "session");
            return Ok(new { user = caller.ToProfile() });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (!caller.IsAuthenticated)
            {
                return Unauthorized(new { error = "Authentication required" });
            }

            await _access.RevokeCurrentSessionAsync(caller, cancellationToken);
            SessionCookies.Clear(Response, Request);
            return NoContent();
        }

        [HttpGet("me")]
        public IActionResult Me()
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (caller.TokenRejected)
            {
                return Unauthorized(new { error = "Invalid API token" });
            }

            if (!caller.IsAuthenticated)
            {
                return Ok(new
                {
                    authenticated = false,
                    username = (string?)null,
                    isAdmin = false,
                    canRead = false,
                    canWrite = false,
                    canReadWarehouse = false,
                    canWriteWarehouse = false,
                    mustChangePassword = false,
                    authType = "anonymous",
                    groups = Array.Empty<object>()
                });
            }

            return Ok(caller.ToProfile());
        }

        [HttpPost("password")]
        public async Task<IActionResult> ChangePassword([FromBody] PasswordChangeRequest? request, CancellationToken cancellationToken)
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (!caller.IsAuthenticated || caller.UserId == null)
            {
                return Unauthorized(new { error = "Authentication required" });
            }

            string? policy = PasswordPolicy.ValidateNew(request?.NewPassword);
            if (policy != null)
            {
                return BadRequest(new { error = policy });
            }

            AppUser? user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == caller.UserId, cancellationToken);
            if (user != null && user.Source == "directory")
            {
                return BadRequest(new { error = "This account uses the directory password" });
            }

            if (user == null || !PasswordHasher.Verify(request?.CurrentPassword ?? "", user.PasswordHash))
            {
                return BadRequest(new { error = "Current password is wrong" });
            }

            user.PasswordHash = PasswordHasher.Hash(request!.NewPassword!);
            List<UserSession> sessions = await _db.UserSessions.Where(s => s.UserId == user.Id).ToListAsync(cancellationToken);
            _db.UserSessions.RemoveRange(sessions);
            await _db.SaveChangesAsync(cancellationToken);
            IssuedSession session = await _access.CreateSessionAsync(user, cancellationToken);
            SessionCookies.Set(Response, Request, session.Token, session.ExpiresAt);
            user.Groups = await _db.AppGroups.AsNoTracking().Where(g => g.Users.Any(member => member.Id == user.Id)).ToListAsync(cancellationToken);
            return Ok(new { user = AccessService.FromUser(user, "session").ToProfile() });
        }
    }

    [Route("api/user")]
    [ApiController]
    public class UserAdminController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public UserAdminController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            List<AppUser> users = await _db.AppUsers
                .Include(u => u.Groups)
                .OrderBy(u => u.Username)
                .ToListAsync(cancellationToken);
            Dictionary<int, Dictionary<int, string>> origins = await OriginsByUserAsync(users.Select(u => u.Id), cancellationToken);
            return Ok(users.Select(user => MapUser(user, origins.GetValueOrDefault(user.Id))));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            string? usernameError = await ValidateUsernameAsync(request?.Username, null, cancellationToken);
            if (usernameError != null)
            {
                return BadRequest(new { error = usernameError });
            }

            string? passwordError = PasswordPolicy.ValidateNew(request?.Password);
            if (passwordError != null)
            {
                return BadRequest(new { error = passwordError });
            }

            DateTime now = DateTime.UtcNow;
            AppUser user = new()
            {
                Username = request!.Username!.Trim(),
                PasswordHash = PasswordHasher.Hash(request.Password!),
                IsActive = request.IsActive != false,
                CreatedAt = now,
                UpdatedAt = now
            };

            string? groupError = await AssignGroupsAsync(user, request.GroupIds ?? new List<int>(), cancellationToken);
            if (groupError != null)
            {
                return BadRequest(new { error = groupError });
            }

            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status201Created, MapUser(user));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UserWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            AppUser? user = await _db.AppUsers.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            if (user.Source == "directory")
            {
                if (!string.IsNullOrEmpty(request?.Password))
                {
                    return BadRequest(new { error = "This account uses the directory password" });
                }

                if (request?.Username != null && request.Username.Trim() != user.Username)
                {
                    return BadRequest(new { error = "The username of a directory account comes from the directory" });
                }

                if (request?.GroupIds != null)
                {
                    List<int> directoryIds = await _db.UserGroupLinks
                        .Where(l => l.UserId == user.Id && l.Origin == "directory")
                        .Select(l => l.GroupId)
                        .ToListAsync(cancellationToken);
                    request.GroupIds = request.GroupIds.Concat(directoryIds).Distinct().ToList();
                }
            }

            string? usernameError = await ValidateUsernameAsync(request?.Username, id, cancellationToken);
            if (usernameError != null)
            {
                return BadRequest(new { error = usernameError });
            }

            bool nextActive = request?.IsActive ?? user.IsActive;
            bool currentlyAdmin = user.IsActive && user.Groups.Any(g => g.CanAdmin);
            bool nextAdmin = nextActive && await WouldBeAdminAsync(request?.GroupIds, user, cancellationToken);
            if (currentlyAdmin && !nextAdmin && await IsLastActiveAdminAsync(id, cancellationToken))
            {
                return BadRequest(new { error = "The last active administrator cannot be removed from administration or deactivated" });
            }

            if (!string.IsNullOrEmpty(request?.Password))
            {
                string? passwordError = PasswordPolicy.ValidateNew(request.Password);
                if (passwordError != null)
                {
                    return BadRequest(new { error = passwordError });
                }

                user.PasswordHash = PasswordHasher.Hash(request.Password);
                List<UserSession> sessions = await _db.UserSessions.Where(s => s.UserId == user.Id).ToListAsync(cancellationToken);
                _db.UserSessions.RemoveRange(sessions);
            }

            user.Username = request!.Username!.Trim();
            user.IsActive = nextActive;

            if (request.GroupIds != null)
            {
                string? groupError = await AssignGroupsAsync(user, request.GroupIds, cancellationToken);
                if (groupError != null)
                {
                    return BadRequest(new { error = groupError });
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            Dictionary<int, string> origins = await _db.UserGroupLinks
                .Where(l => l.UserId == user.Id)
                .ToDictionaryAsync(l => l.GroupId, l => l.Origin, cancellationToken);
            return Ok(MapUser(user, origins));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            CallerContext caller = CallerContext.Current(HttpContext);
            if (caller.UserId == id)
            {
                return BadRequest(new { error = "You cannot delete the account you are signed in with" });
            }

            AppUser? user = await _db.AppUsers.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            if (user.IsActive && user.Groups.Any(g => g.CanAdmin) && await IsLastActiveAdminAsync(id, cancellationToken))
            {
                return BadRequest(new { error = "The last active administrator cannot be deleted" });
            }

            _db.AppUsers.Remove(user);
            await _db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        private IActionResult? RequireAdmin()
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (caller.TokenRejected || !caller.IsAuthenticated)
            {
                return Unauthorized(new { error = caller.TokenRejected ? "Invalid API token" : "Authentication required" });
            }

            if (!caller.IsAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator permission required" });
            }

            return null;
        }

        private async Task<string?> ValidateUsernameAsync(string? username, int? exceptId, CancellationToken cancellationToken)
        {
            string name = (username ?? "").Trim();
            if (name.Length < 1 || name.Length > 100)
            {
                return "Username must be between 1 and 100 characters";
            }

            bool taken = await _db.AppUsers.AnyAsync(
                u => u.Username.ToLower() == name.ToLower() && u.Id != exceptId,
                cancellationToken);
            return taken ? "A user with this name already exists" : null;
        }

        private async Task<bool> IsLastActiveAdminAsync(int userId, CancellationToken cancellationToken)
        {
            return !await _db.AppUsers.AnyAsync(
                u => u.IsActive && u.Id != userId && u.Groups.Any(g => g.CanAdmin),
                cancellationToken);
        }

        private async Task<bool> WouldBeAdminAsync(List<int>? groupIds, AppUser user, CancellationToken cancellationToken)
        {
            if (groupIds == null)
            {
                return user.Groups.Any(g => g.CanAdmin);
            }

            List<int> ids = groupIds.Distinct().ToList();
            return await _db.AppGroups.AnyAsync(g => ids.Contains(g.Id) && g.CanAdmin, cancellationToken);
        }

        private async Task<string?> AssignGroupsAsync(AppUser user, List<int> groupIds, CancellationToken cancellationToken)
        {
            List<int> ids = groupIds.Distinct().ToList();
            List<AppGroup> groups = await _db.AppGroups.Where(g => ids.Contains(g.Id)).ToListAsync(cancellationToken);
            if (groups.Count != ids.Count)
            {
                return "Unknown group";
            }

            if (user.Source == "directory" && user.Id != 0)
            {
                List<UserGroupLink> links = await _db.UserGroupLinks.Where(l => l.UserId == user.Id).ToListAsync(cancellationToken);
                HashSet<int> directoryIds = links.Where(l => l.Origin == "directory").Select(l => l.GroupId).ToHashSet();
                HashSet<int> keep = ids.Where(id => !directoryIds.Contains(id)).Concat(directoryIds).ToHashSet();
                foreach (AppGroup existing in user.Groups.ToList())
                {
                    if (!keep.Contains(existing.Id))
                    {
                        user.Groups.Remove(existing);
                    }
                }

                foreach (AppGroup group in groups)
                {
                    if (keep.Contains(group.Id) && user.Groups.All(existing => existing.Id != group.Id))
                    {
                        user.Groups.Add(group);
                    }
                }

                return null;
            }

            user.Groups.Clear();
            foreach (AppGroup group in groups)
            {
                user.Groups.Add(group);
            }

            return null;
        }

        internal static object MapUser(AppUser user, IReadOnlyDictionary<int, string>? origins = null)
        {
            AccessRights rights = AccessService.RightsOf(user.Groups);
            return new
            {
                id = user.Id,
                username = user.Username,
                displayName = user.DisplayName,
                source = user.Source,
                isAdmin = rights.CanAdmin,
                isActive = user.IsActive,
                canRead = rights.CanRead,
                canWrite = rights.CanWrite,
                canReadWarehouse = rights.CanReadWarehouse,
                canWriteWarehouse = rights.CanWriteWarehouse,
                createdAt = user.CreatedAt,
                groups = user.Groups
                    .OrderBy(g => g.Name)
                    .Select(g => new
                    {
                        id = g.Id,
                        name = g.Name,
                        canRead = g.CanRead,
                        canWrite = g.CanWrite,
                        canAdmin = g.CanAdmin,
                        canReadWarehouse = g.CanReadWarehouse,
                        canWriteWarehouse = g.CanWriteWarehouse,
                        source = g.Source,
                        origin = origins != null && origins.TryGetValue(g.Id, out string? origin) ? origin : "manual"
                    })
            };
        }

        private async Task<Dictionary<int, Dictionary<int, string>>> OriginsByUserAsync(IEnumerable<int> userIds, CancellationToken cancellationToken)
        {
            List<int> ids = userIds.ToList();
            List<UserGroupLink> links = await _db.UserGroupLinks.Where(l => ids.Contains(l.UserId)).ToListAsync(cancellationToken);
            return links
                .GroupBy(l => l.UserId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(link => link.GroupId, link => link.Origin));
        }
    }

    [Route("api/group")]
    [ApiController]
    public class GroupController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public GroupController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            List<AppGroup> groups = await _db.AppGroups
                .Include(g => g.Users)
                .OrderBy(g => g.Name)
                .ToListAsync(cancellationToken);
            return Ok(groups.Select(MapGroup));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GroupWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            string? error = await ValidateAsync(request, null, cancellationToken);
            if (error != null)
            {
                return BadRequest(new { error });
            }

            DateTime now = DateTime.UtcNow;
            AppGroup group = new()
            {
                Name = request!.Name!.Trim(),
                Description = (request.Description ?? "").Trim(),
                CanRead = request.CanRead || request.CanWrite,
                CanWrite = request.CanWrite,
                CanAdmin = request.CanAdmin,
                CanReadWarehouse = request.CanReadWarehouse || request.CanWriteWarehouse,
                CanWriteWarehouse = request.CanWriteWarehouse,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.AppGroups.Add(group);
            await _db.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status201Created, MapGroup(group));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] GroupWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            AppGroup? group = await _db.AppGroups.Include(g => g.Users).FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
            if (group == null)
            {
                return NotFound(new { error = "Group not found" });
            }

            if (request != null && (group.IsSystem || group.Source == "directory"))
            {
                request.Name = group.Name;
            }

            string? error = await ValidateAsync(request, id, cancellationToken);
            if (error != null)
            {
                return BadRequest(new { error });
            }

            if (group.CanAdmin && !request!.CanAdmin)
            {
                bool keepsAnotherAdmin = await _db.AppUsers.AnyAsync(
                    u => u.IsActive && u.Groups.Any(g => g.CanAdmin && g.Id != id),
                    cancellationToken);
                if (group.Users.Any(u => u.IsActive) && !keepsAnotherAdmin)
                {
                    return BadRequest(new { error = "The last group that can manage users and settings cannot lose that permission" });
                }
            }

            if (!group.IsSystem && group.Source != "directory")
            {
                group.Name = request!.Name!.Trim();
            }

            group.Description = (request!.Description ?? "").Trim();
            group.CanRead = request.CanRead || request.CanWrite;
            group.CanWrite = request.CanWrite;
            group.CanAdmin = request.CanAdmin;
            group.CanReadWarehouse = request.CanReadWarehouse || request.CanWriteWarehouse;
            group.CanWriteWarehouse = request.CanWriteWarehouse;
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(MapGroup(group));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            AppGroup? group = await _db.AppGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
            if (group == null)
            {
                return NotFound(new { error = "Group not found" });
            }

            if (string.Equals(group.Name, "Admins", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "The Admins group cannot be deleted" });
            }

            _db.AppGroups.Remove(group);
            await _db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        private IActionResult? RequireAdmin()
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (caller.TokenRejected || !caller.IsAuthenticated)
            {
                return Unauthorized(new { error = caller.TokenRejected ? "Invalid API token" : "Authentication required" });
            }

            if (!caller.IsAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator permission required" });
            }

            return null;
        }

        private async Task<string?> ValidateAsync(GroupWriteRequest? request, int? exceptId, CancellationToken cancellationToken)
        {
            string name = (request?.Name ?? "").Trim();
            if (name.Length < 1 || name.Length > 100)
            {
                return "Group name must be between 1 and 100 characters";
            }

            bool taken = await _db.AppGroups.AnyAsync(
                g => g.Name.ToLower() == name.ToLower() && g.Id != exceptId,
                cancellationToken);
            return taken ? "A group with this name already exists" : null;
        }

        private static object MapGroup(AppGroup group)
        {
            return new
            {
                id = group.Id,
                name = group.Name,
                description = group.Description,
                canRead = group.CanRead || group.CanWrite,
                canWrite = group.CanWrite,
                canAdmin = group.CanAdmin,
                canReadWarehouse = group.CanReadWarehouse || group.CanWriteWarehouse,
                canWriteWarehouse = group.CanWriteWarehouse,
                isSystem = group.IsSystem,
                source = group.Source,
                directoryDn = group.DirectoryDn,
                memberCount = group.Users?.Count ?? 0
            };
        }
    }

    [Route("api/token")]
    [ApiController]
    public class ApiTokenController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ApiTokenController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            CallerContext? caller = RequireUser(out IActionResult? denied);
            if (caller == null)
            {
                return denied!;
            }

            List<ApiToken> tokens = await _db.ApiTokens
                .Where(t => t.UserId == caller.UserId && t.RevokedAt == null)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
            return Ok(tokens.Select(MapToken));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TokenCreateRequest? request, CancellationToken cancellationToken)
        {
            CallerContext? caller = RequireUser(out IActionResult? denied);
            if (caller == null)
            {
                return denied!;
            }

            string name = (request?.Name ?? "").Trim();
            if (name.Length < 1 || name.Length > 100)
            {
                return BadRequest(new { error = "Token name must be between 1 and 100 characters" });
            }

            bool canWrite = request?.CanWrite ?? caller.CanWrite;
            if (canWrite && !caller.CanWrite)
            {
                return BadRequest(new { error = "This account cannot create a token with write permission" });
            }

            string secret = SecretToken.Create("kpt_");
            ApiToken token = new()
            {
                UserId = caller.UserId!.Value,
                Name = name,
                TokenHash = SecretToken.Hash(secret),
                TokenPrefix = SecretToken.PrefixOf(secret),
                CanWrite = canWrite,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(ApiToken.LifetimeDays)
            };
            _db.ApiTokens.Add(token);
            await _db.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status201Created, new
            {
                id = token.Id,
                name = token.Name,
                prefix = token.TokenPrefix,
                canWrite = token.CanWrite,
                createdAt = token.CreatedAt,
                expiresAt = token.ExpiresAt,
                token = secret
            });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Revoke(int id, CancellationToken cancellationToken)
        {
            CallerContext? caller = RequireUser(out IActionResult? denied);
            if (caller == null)
            {
                return denied!;
            }

            ApiToken? token = await _db.ApiTokens.FirstOrDefaultAsync(
                t => t.Id == id && t.UserId == caller.UserId && t.RevokedAt == null,
                cancellationToken);
            if (token == null)
            {
                return NotFound(new { error = "Token not found" });
            }

            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        private CallerContext? RequireUser(out IActionResult? denied)
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (caller.TokenRejected || !caller.IsAuthenticated || caller.UserId == null)
            {
                denied = Unauthorized(new { error = caller.TokenRejected ? "Invalid API token" : "Authentication required" });
                return null;
            }

            denied = null;
            return caller;
        }

        private static object MapToken(ApiToken token)
        {
            return new
            {
                id = token.Id,
                name = token.Name,
                prefix = token.TokenPrefix,
                canWrite = token.CanWrite,
                createdAt = token.CreatedAt,
                expiresAt = token.ExpiresAt,
                lastUsedAt = token.LastUsedAt
            };
        }
    }
}
