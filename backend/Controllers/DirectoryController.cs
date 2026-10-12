using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace KiComLibBackend.Controllers
{
    public class DirectoryImportRequest
    {
        public List<string>? DistinguishedNames { get; set; }

        public bool CanRead { get; set; } = true;

        public bool CanWrite { get; set; }

        public bool CanAdmin { get; set; }

        public bool CanReadWarehouse { get; set; }

        public bool CanWriteWarehouse { get; set; }
    }

    [Route("api/directory")]
    [ApiController]
    public class DirectoryController : ControllerBase
    {
        private readonly DirectoryAuthService _directory;

        public DirectoryController(DirectoryAuthService directory)
        {
            _directory = directory;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            return Ok(await _directory.PayloadAsync(cancellationToken));
        }

        [HttpPut]
        public async Task<IActionResult> Put([FromBody] DirectoryWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            string? error = await _directory.SaveAsync(request, cancellationToken);
            if (error != null)
            {
                return BadRequest(new { error });
            }

            return Ok(await _directory.PayloadAsync(cancellationToken));
        }

        [HttpPost("test")]
        public async Task<IActionResult> Test([FromBody] DirectoryWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            try
            {
                await _directory.TestAsync(request, cancellationToken);
                return Ok(new { ok = true, message = "Connected to the directory" });
            }
            catch (DirectoryFailureException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
        }

        [HttpGet("users")]
        public async Task<IActionResult> SearchUsers([FromQuery] string? q, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            try
            {
                IReadOnlyList<DirectoryAccount> users = await _directory.SearchUsersAsync(q, cancellationToken);
                return Ok(users.Select(user => new
                {
                    id = user.Id,
                    username = user.Username,
                    displayName = user.DisplayName,
                    distinguishedName = user.DistinguishedName
                }));
            }
            catch (DirectoryFailureException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
        }

        [HttpGet("groups")]
        public async Task<IActionResult> SearchGroups([FromQuery] string? q, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            try
            {
                IReadOnlyList<DirectoryGroupRecord> groups = await _directory.SearchGroupsAsync(q, cancellationToken);
                return Ok(groups.Select(group => new
                {
                    id = group.Id,
                    name = group.Name,
                    description = group.Description,
                    distinguishedName = group.DistinguishedName
                }));
            }
            catch (DirectoryFailureException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
        }

        [HttpPost("users/import")]
        public async Task<IActionResult> ImportUsers([FromBody] DirectoryImportRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            try
            {
                return Ok(await _directory.ImportUsersAsync(request?.DistinguishedNames, cancellationToken));
            }
            catch (DirectoryFailureException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
        }

        [HttpPost("groups/import")]
        public async Task<IActionResult> ImportGroups([FromBody] DirectoryImportRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            try
            {
                return Ok(await _directory.ImportGroupsAsync(
                    request?.DistinguishedNames,
                    request?.CanRead ?? true,
                    request?.CanWrite ?? false,
                    request?.CanAdmin ?? false,
                    request?.CanReadWarehouse ?? false,
                    request?.CanWriteWarehouse ?? false,
                    cancellationToken));
            }
            catch (DirectoryFailureException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
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
    }
}
