using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace KiComLibBackend.Controllers
{
    public class BackupCreateRequest
    {
        public string? Name { get; set; }

        public string? Passphrase { get; set; }
    }

    public class BackupRestoreRequest
    {
        public string? Passphrase { get; set; }
    }

    [Route("api/backup")]
    [ApiController]
    public class BackupController : ControllerBase
    {
        private readonly BackupService _backups;

        public BackupController(BackupService backups)
        {
            _backups = backups;
        }

        [HttpGet]
        public IActionResult List()
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            return Ok(_backups.List().Select(Map));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] BackupCreateRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            try
            {
                BackupInfo info = await _backups.CreateAsync(request?.Name, request?.Passphrase, cancellationToken);
                return StatusCode(StatusCodes.Status201Created, Map(info));
            }
            catch (InvalidOperationException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
        }

        [HttpPost("upload")]
        [RequestSizeLimit(BackupService.DefaultMaxBytes)]
        public async Task<IActionResult> Upload(IFormFile? file, [FromForm] string? name, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "Choose a backup file" });
            }

            try
            {
                await using Stream stream = file.OpenReadStream();
                BackupInfo info = await _backups.ImportAsync(stream, name, file.FileName, cancellationToken);
                return StatusCode(StatusCodes.Status201Created, Map(info));
            }
            catch (InvalidOperationException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
        }

        [HttpGet("{id}/file")]
        public IActionResult Download(string id)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            (BackupInfo Info, string Path)? opened = _backups.Open(id);
            if (opened == null)
            {
                return NotFound(new { error = "Backup not found" });
            }

            FileStream stream = System.IO.File.OpenRead(opened.Value.Path);
            return File(stream, "application/octet-stream", opened.Value.Info.FileName);
        }

        [HttpPost("{id}/restore")]
        public async Task<IActionResult> Restore(string id, [FromBody] BackupRestoreRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            try
            {
                BackupRestoreResult result = await _backups.RestoreAsync(id, request?.Passphrase, cancellationToken);
                return Ok(new { restored = id, restarting = result.Restarting, keysRestored = result.KeysRestored });
            }
            catch (InvalidOperationException exception)
            {
                int status = exception.Message == "Unknown backup"
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status400BadRequest;
                return StatusCode(status, new { error = exception.Message });
            }
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            try
            {
                _backups.Delete(id);
                return NoContent();
            }
            catch (InvalidOperationException exception)
            {
                return NotFound(new { error = exception.Message });
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

        private static object Map(BackupInfo info)
        {
            return new
            {
                id = info.Id,
                name = info.Name,
                fileName = info.FileName,
                createdAt = info.CreatedAt,
                sizeBytes = info.SizeBytes,
                source = info.Source,
                encrypted = info.Encrypted
            };
        }
    }
}
