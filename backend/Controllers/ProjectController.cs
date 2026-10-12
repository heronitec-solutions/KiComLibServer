using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public class ProjectWriteRequest
    {
        public int RepositoryId { get; set; }

        public string? ProjectFile { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? Status { get; set; }

        public string? Customer { get; set; }

        public int? OwnerUserId { get; set; }

        public string? ExportScope { get; set; }

        public bool? IsArchived { get; set; }
    }

    public class ProjectAccessWriteRequest
    {
        public List<ProjectAccessEntry>? Access { get; set; }
    }

    public class ProjectAccessEntry
    {
        public int GroupId { get; set; }

        public bool CanWrite { get; set; }
    }

    [Route("api/project")]
    [ApiController]
    public class ProjectController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectJobQueue _queue;
        private readonly IEnumerable<IGitProviderAdapter> _adapters;
        private readonly ILogger<ProjectController> _logger;

        public ProjectController(
            ApplicationDbContext db,
            ProjectAccessService access,
            ProjectJobQueue queue,
            IEnumerable<IGitProviderAdapter> adapters,
            ILogger<ProjectController> logger)
        {
            _db = db;
            _access = access;
            _queue = queue;
            _adapters = adapters;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q, CancellationToken cancellationToken)
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (!caller.IsAuthenticated)
            {
                return Unauthorized(new { error = "Authentication required" });
            }

            IQueryable<Project> query = _access.Readable(caller);
            if (!string.IsNullOrWhiteSpace(status))
            {
                string wanted = status.Trim();
                query = query.Where(p => p.Status == wanted);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                string term = q.Trim().ToLowerInvariant();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term)
                    || (p.Description != null && p.Description.ToLower().Contains(term))
                    || (p.Customer != null && p.Customer.ToLower().Contains(term))
                    || p.ProjectFile.ToLower().Contains(term));
            }

            List<Project> projects = await query
                .Include(p => p.Repository)
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);
            List<int> ids = projects.Select(p => p.Id).ToList();
            Dictionary<int, int> revisionCounts = await _db.ProjectRevisions
                .Where(r => ids.Contains(r.ProjectId))
                .GroupBy(r => r.ProjectId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
            List<int> heads = projects.Where(p => p.HeadRevisionId != null).Select(p => p.HeadRevisionId!.Value).ToList();
            Dictionary<int, int> componentCounts = heads.Count == 0
                ? new Dictionary<int, int>()
                : await _db.ProjectComponents
                    .Where(c => heads.Contains(c.RevisionId))
                    .GroupBy(c => c.RevisionId)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
            Dictionary<int, string> owners = await OwnerNamesAsync(projects.Select(p => p.OwnerUserId), cancellationToken);

            return Ok(projects.Select(project => new
            {
                id = project.Id,
                uuid = project.Uuid,
                name = project.Name,
                status = project.Status,
                customer = project.Customer,
                ownerUserId = project.OwnerUserId,
                ownerName = project.OwnerUserId != null && owners.TryGetValue(project.OwnerUserId.Value, out string? ownerName) ? ownerName : null,
                repositoryId = project.RepositoryId,
                projectFile = project.ProjectFile,
                isArchived = project.IsArchived,
                counts = new
                {
                    revisions = revisionCounts.GetValueOrDefault(project.Id),
                    components = project.HeadRevisionId != null && componentCounts.TryGetValue(project.HeadRevisionId.Value, out int components) ? components : 0,
                    warnings = 0
                }
            }));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProjectWriteRequest? request, CancellationToken cancellationToken)
        {
            if (!CallerContext.Current(HttpContext).IsAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator permission required" });
            }

            if (request == null || string.IsNullOrWhiteSpace(request.ProjectFile))
            {
                return BadRequest(new { error = "projectFile is required" });
            }

            string? fileError = ValidateProjectFile(request.ProjectFile);
            if (fileError != null)
            {
                return BadRequest(new { error = fileError });
            }

            string status = string.IsNullOrWhiteSpace(request.Status) ? "development" : request.Status.Trim();
            string? statusError = ValidateStatus(status);
            if (statusError != null)
            {
                return BadRequest(new { error = statusError });
            }

            string exportScope = string.IsNullOrWhiteSpace(request.ExportScope) ? "project_dir" : request.ExportScope.Trim();
            if (exportScope is not ("project_dir" or "repository"))
            {
                return BadRequest(new { error = "exportScope must be project_dir or repository" });
            }

            GitRepository? repository = await _db.GitRepositories.FirstOrDefaultAsync(r => r.Id == request.RepositoryId, cancellationToken);
            if (repository == null)
            {
                return BadRequest(new { error = "Git repository not found" });
            }

            string projectFile = request.ProjectFile.Trim().Replace('\\', '/');
            string name = string.IsNullOrWhiteSpace(request.Name)
                ? Path.GetFileNameWithoutExtension(projectFile)
                : request.Name.Trim();
            Project project = new()
            {
                Uuid = Guid.NewGuid(),
                RepositoryId = repository.Id,
                ProjectFile = projectFile,
                ExportScope = exportScope,
                Name = name,
                Description = NullIfEmpty(request.Description),
                Status = status,
                Customer = NullIfEmpty(request.Customer),
                OwnerUserId = request.OwnerUserId,
                IsArchived = request.IsArchived ?? false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Projects.Add(project);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return Conflict(new { error = "This project file is already registered for the repository" });
            }

            await _queue.EnqueueAsync("fetch", ProjectJobQueue.FetchKey(repository.Id), repository.Id, project.Id, "{}", cancellationToken);
            _logger.LogInformation("Created project {ProjectId} by user {UserId}.", project.Id, CallerContext.Current(HttpContext).UserId);
            return StatusCode(StatusCodes.Status201Created, await DetailAsync(project.Id, cancellationToken));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            object? detail = await DetailAsync(id, cancellationToken);
            return detail == null ? NotFound(new { error = "Project not found" }) : Ok(detail);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProjectWriteRequest? request, CancellationToken cancellationToken)
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (project == null)
            {
                return NotFound(new { error = "Project not found" });
            }

            if (!await _access.CanWriteAsync(caller, id, cancellationToken))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Write permission required" });
            }

            if (request == null)
            {
                return BadRequest(new { error = "Request body is required" });
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                project.Name = request.Name.Trim();
            }

            if (request.Description != null)
            {
                project.Description = NullIfEmpty(request.Description);
            }

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                string? statusError = ValidateStatus(request.Status.Trim());
                if (statusError != null)
                {
                    return BadRequest(new { error = statusError });
                }

                project.Status = request.Status.Trim();
            }

            if (request.Customer != null)
            {
                project.Customer = NullIfEmpty(request.Customer);
            }

            if (request.OwnerUserId != null)
            {
                project.OwnerUserId = request.OwnerUserId;
            }

            if (!string.IsNullOrWhiteSpace(request.ExportScope))
            {
                string scope = request.ExportScope.Trim();
                if (scope is not ("project_dir" or "repository"))
                {
                    return BadRequest(new { error = "exportScope must be project_dir or repository" });
                }

                project.ExportScope = scope;
            }

            if (request.IsArchived != null)
            {
                project.IsArchived = request.IsArchived.Value;
            }

            project.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(await DetailAsync(id, cancellationToken));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            if (!CallerContext.Current(HttpContext).IsAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator permission required" });
            }

            Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (project == null)
            {
                return NotFound(new { error = "Project not found" });
            }

            project.HeadRevisionId = null;
            await _db.SaveChangesAsync(cancellationToken);
            _db.Projects.Remove(project);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deleted project {ProjectId} by user {UserId}. The git mirror was kept.", id, CallerContext.Current(HttpContext).UserId);
            return NoContent();
        }

        [HttpGet("{id:int}/access")]
        public async Task<IActionResult> GetAccess(int id, CancellationToken cancellationToken)
        {
            if (!CallerContext.Current(HttpContext).IsAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator permission required" });
            }

            if (!await _db.Projects.AnyAsync(p => p.Id == id, cancellationToken))
            {
                return NotFound(new { error = "Project not found" });
            }

            List<ProjectGroupAccess> rows = await _db.ProjectGroupAccess
                .Include(a => a.Group)
                .Where(a => a.ProjectId == id)
                .ToListAsync(cancellationToken);
            return Ok(rows.Select(row => new
            {
                groupId = row.GroupId,
                groupName = row.Group?.Name,
                canWrite = row.CanWrite
            }));
        }

        [HttpPut("{id:int}/access")]
        public async Task<IActionResult> PutAccess(int id, [FromBody] List<ProjectAccessEntry>? access, CancellationToken cancellationToken)
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (!caller.IsAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator permission required" });
            }

            if (!await _db.Projects.AnyAsync(p => p.Id == id, cancellationToken))
            {
                return NotFound(new { error = "Project not found" });
            }

            List<ProjectAccessEntry> entries = access ?? new List<ProjectAccessEntry>();
            List<int> groupIds = entries.Select(entry => entry.GroupId).Distinct().ToList();
            int found = await _db.AppGroups.CountAsync(g => groupIds.Contains(g.Id), cancellationToken);
            if (found != groupIds.Count)
            {
                return BadRequest(new { error = "One or more groups were not found" });
            }

            List<ProjectGroupAccess> existing = await _db.ProjectGroupAccess.Where(a => a.ProjectId == id).ToListAsync(cancellationToken);
            _db.ProjectGroupAccess.RemoveRange(existing);
            foreach (int groupId in groupIds)
            {
                ProjectAccessEntry entry = entries.First(item => item.GroupId == groupId);
                _db.ProjectGroupAccess.Add(new ProjectGroupAccess
                {
                    ProjectId = id,
                    GroupId = groupId,
                    CanWrite = entry.CanWrite
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Updated access for project {ProjectId} by user {UserId}.", id, caller.UserId);
            return await GetAccess(id, cancellationToken);
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

        private async Task<object?> DetailAsync(int id, CancellationToken cancellationToken)
        {
            Project? project = await _db.Projects
                .Include(p => p.Repository)
            .ThenInclude(r => r!.Host)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (project == null)
            {
                return null;
            }

            CallerContext caller = CallerContext.Current(HttpContext);
            int revisions = await _db.ProjectRevisions.CountAsync(r => r.ProjectId == id, cancellationToken);
            int components = 0;
            if (project.HeadRevisionId != null)
            {
                components = await _db.ProjectComponents.CountAsync(c => c.RevisionId == project.HeadRevisionId, cancellationToken);
            }

            IGitProviderAdapter? adapter = project.Repository?.Host == null
                ? null
                : _adapters.FirstOrDefault(item => item.Kind == project.Repository.Host.Kind);
            Dictionary<int, string> owners = await OwnerNamesAsync(new int?[] { project.OwnerUserId }, cancellationToken);
            return new
            {
                id = project.Id,
                uuid = project.Uuid,
                repositoryId = project.RepositoryId,
                projectFile = project.ProjectFile,
                exportScope = project.ExportScope,
                name = project.Name,
                description = project.Description,
                status = project.Status,
                customer = project.Customer,
                ownerUserId = project.OwnerUserId,
                ownerName = project.OwnerUserId != null && owners.TryGetValue(project.OwnerUserId.Value, out string? ownerName) ? ownerName : null,
                headRevisionId = project.HeadRevisionId,
                isArchived = project.IsArchived,
                permissions = new
                {
                    canWrite = await _access.CanWriteAsync(caller, id, cancellationToken),
                    canAdmin = caller.IsAdmin
                },
                counts = new { revisions, components, warnings = 0 },
                host = project.Repository?.Host == null ? null : new
                {
                    id = project.Repository.Host.Id,
                    name = project.Repository.Host.Name,
                    kind = project.Repository.Host.Kind,
                    webUrl = project.Repository.Host.WebUrl
                },
                links = adapter == null || project.Repository == null ? null : adapter.Links(project.Repository)
            };
        }

        private async Task<Dictionary<int, string>> OwnerNamesAsync(IEnumerable<int?> ownerIds, CancellationToken cancellationToken)
        {
            List<int> ids = ownerIds.Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, string>();
            }

            return await _db.AppUsers
                .Where(user => ids.Contains(user.Id))
                .ToDictionaryAsync(
                    user => user.Id,
                    user => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName.Trim(),
                    cancellationToken);
        }

        private static string? ValidateProjectFile(string projectFile)
        {
            string normalized = projectFile.Trim().Replace('\\', '/');
            if (normalized.Length < 1 || normalized.StartsWith('/') || normalized.Contains("..", StringComparison.Ordinal))
            {
                return "projectFile must be a relative path inside the repository";
            }

            if (!normalized.EndsWith(".kicad_pro", StringComparison.OrdinalIgnoreCase))
            {
                return "projectFile must be a .kicad_pro file";
            }

            return null;
        }

        private static string? ValidateStatus(string status)
        {
            if (status is not ("development" or "released" or "obsolete"))
            {
                return "status must be development, released, or obsolete";
            }

            return null;
        }

        private static string? NullIfEmpty(string? value)
        {
            string trimmed = (value ?? "").Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
