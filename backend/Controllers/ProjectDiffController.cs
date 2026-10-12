using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public class VisualDiffRequest
    {
        public string? Base { get; set; }

        public string? Target { get; set; }
    }

    [Route("api/project")]
    [ApiController]
    public class ProjectDiffController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectJobQueue _queue;
        private readonly ProjectCache _cache;

        public ProjectDiffController(
            ApplicationDbContext db,
            ProjectAccessService access,
            ProjectJobQueue queue,
            ProjectCache cache)
        {
            _db = db;
            _access = access;
            _queue = queue;
            _cache = cache;
        }

        [HttpPost("{id:int}/visual-diff")]
        public async Task<IActionResult> Create(int id, [FromBody] VisualDiffRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Base) || string.IsNullOrWhiteSpace(request.Target))
            {
                return BadRequest(new { error = "base and target are required" });
            }

            ProjectRevision? baseRevision = await FindRevisionAsync(id, request.Base, cancellationToken);
            ProjectRevision? targetRevision = await FindRevisionAsync(id, request.Target, cancellationToken);
            if (baseRevision == null || targetRevision == null)
            {
                return NotFound(new { error = "Revision not found" });
            }

            ProjectDiff? diff = await _db.ProjectDiffs.FirstOrDefaultAsync(
                d => d.BaseRevisionId == baseRevision.Id && d.TargetRevisionId == targetRevision.Id,
                cancellationToken);
            if (diff == null)
            {
                diff = new ProjectDiff
                {
                    Uuid = Guid.NewGuid(),
                    ProjectId = id,
                    BaseRevisionId = baseRevision.Id,
                    TargetRevisionId = targetRevision.Id,
                    Status = "queued",
                    CreatedAt = DateTime.UtcNow
                };
                _db.ProjectDiffs.Add(diff);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    _db.Entry(diff).State = EntityState.Detached;
                    diff = await _db.ProjectDiffs.FirstOrDefaultAsync(
                        d => d.BaseRevisionId == baseRevision.Id && d.TargetRevisionId == targetRevision.Id,
                        cancellationToken);
                    if (diff == null)
                    {
                        throw;
                    }
                }
            }

            if (diff.Status == "done")
            {
                return Ok(Envelope(diff));
            }

            if (diff.Status == "failed")
            {
                diff.Status = "queued";
                diff.Error = null;
                await _db.SaveChangesAsync(cancellationToken);
            }

            await _queue.EnqueueAsync(
                "visual_diff",
                ProjectJobQueue.VisualDiffKey(diff.Id),
                null,
                id,
                JsonSerializer.Serialize(new { diffId = diff.Id }),
                cancellationToken);
            return Accepted(new { id = diff.Id, uuid = diff.Uuid, status = diff.Status == "running" ? "running" : "queued" });
        }

        [HttpGet("{id:int}/visual-diff/{diffId}")]
        public async Task<IActionResult> Get(int id, string diffId, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            ProjectDiff? diff = await FindDiffAsync(id, diffId, cancellationToken);
            if (diff == null)
            {
                return NotFound(new { error = "Visual diff not found" });
            }

            return Ok(Envelope(diff));
        }

        [HttpGet("{id:int}/visual-diff/{diffId}/file/{*path}")]
        public async Task<IActionResult> Download(int id, string diffId, string path, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            if (!ProjectCache.IsSafeRelative(path))
            {
                return BadRequest(new { error = "Invalid cache path" });
            }

            ProjectDiff? diff = await FindDiffAsync(id, diffId, cancellationToken);
            if (diff == null)
            {
                return NotFound(new { error = "Visual diff not found" });
            }

            byte[]? bytes = _cache.ReadFile(_cache.DiffRelative(diff.Uuid), path);
            if (bytes != null)
            {
                diff.LastViewedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return SvgOrBytes(bytes, path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
            }

            if (diff.Status == "done")
            {
                diff.Status = "queued";
                diff.Error = null;
                await _db.SaveChangesAsync(cancellationToken);
                await _queue.EnqueueAsync(
                    "visual_diff",
                    ProjectJobQueue.VisualDiffKey(diff.Id),
                    null,
                    id,
                    JsonSerializer.Serialize(new { diffId = diff.Id }),
                    cancellationToken);
            }

            return Accepted(new { status = "queued" });
        }

        [HttpGet("{id:int}/revision/{sha}/page/{fileName}")]
        public async Task<IActionResult> Page(int id, string sha, string fileName, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            ProjectRevision? revision = await FindRevisionAsync(id, sha, cancellationToken);
            if (revision == null)
            {
                return NotFound(new { error = "Revision not found" });
            }

            string page = fileName;
            if (page.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                page = page[..^4];
            }

            string? relativeFile = _cache.PageFile(id, revision.CommitSha, page);
            if (relativeFile != null)
            {
                byte[]? bytes = _cache.ReadFile(_cache.RenderRelative(id, revision.CommitSha), relativeFile);
                if (bytes != null)
                {
                    return SvgOrBytes(bytes, svg: true);
                }
            }

            await _queue.EnqueueAsync(
                "render",
                ProjectJobQueue.RenderKey(id, revision.CommitSha),
                null,
                id,
                JsonSerializer.Serialize(new { revisionId = revision.Id, commitSha = revision.CommitSha }),
                cancellationToken);
            return Accepted(new { status = "queued" });
        }

        private object Envelope(ProjectDiff diff)
        {
            JsonElement? manifest = null;
            byte[]? bytes = _cache.ReadFile(_cache.DiffRelative(diff.Uuid), "manifest.json");
            if (bytes != null && bytes.Length > 0)
            {
                try
                {
                    manifest = JsonSerializer.Deserialize<JsonElement>(bytes);
                }
                catch (JsonException)
                {
                    manifest = null;
                }
            }

            return new
            {
                id = diff.Id,
                uuid = diff.Uuid,
                status = diff.Status,
                error = diff.Error,
                kicad = diff.KicadVersion,
                summary = diff.Summary == null ? null : Parse(diff.Summary),
                manifest
            };
        }

        private static JsonElement? Parse(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task<ProjectDiff?> FindDiffAsync(int projectId, string diffId, CancellationToken cancellationToken)
        {
            if (int.TryParse(diffId, out int numeric))
            {
                ProjectDiff? byId = await _db.ProjectDiffs.FirstOrDefaultAsync(d => d.Id == numeric && d.ProjectId == projectId, cancellationToken);
                if (byId != null)
                {
                    return byId;
                }
            }

            if (Guid.TryParse(diffId, out Guid uuid))
            {
                return await _db.ProjectDiffs.FirstOrDefaultAsync(d => d.Uuid == uuid && d.ProjectId == projectId, cancellationToken);
            }

            return null;
        }

        private async Task<ProjectRevision?> FindRevisionAsync(int projectId, string sha, CancellationToken cancellationToken)
        {
            string wanted = sha.Trim().ToLowerInvariant();
            return await _db.ProjectRevisions.FirstOrDefaultAsync(
                r => r.ProjectId == projectId && r.CommitSha.ToLower() == wanted,
                cancellationToken);
        }

        private IActionResult SvgOrBytes(byte[] bytes, bool svg)
        {
            if (!svg)
            {
                return File(bytes, "application/octet-stream");
            }

            byte[]? clean = SvgSanitizer.SanitizeBytes(bytes);
            if (clean == null)
            {
                return File(bytes, "application/octet-stream");
            }

            return File(clean, "image/svg+xml");
        }

        private async Task<IActionResult?> RequireReadAsync(int id, CancellationToken cancellationToken)
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (!caller.IsAuthenticated)
            {
                return Unauthorized(new { error = "Authentication required" });
            }

            if (!await _db.Projects.AnyAsync(p => p.Id == id, cancellationToken))
            {
                return NotFound(new { error = "Project not found" });
            }

            if (!await _access.CanReadAsync(caller, id, cancellationToken))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Project access required" });
            }

            return null;
        }
    }
}
