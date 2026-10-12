using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public class ReleaseWriteRequest
    {
        public string? Sha { get; set; }

        public string? Tag { get; set; }

        public string? Name { get; set; }

        public string? Notes { get; set; }
    }

    public class ReleaseUpdateRequest
    {
        public string? Notes { get; set; }

        public string? State { get; set; }
    }

    [Route("api/project")]
    [ApiController]
    public class ProjectReleaseController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectJobQueue _queue;
        private readonly ProjectBlobStore _blobs;

        public ProjectReleaseController(
            ApplicationDbContext db,
            ProjectAccessService access,
            ProjectJobQueue queue,
            ProjectBlobStore blobs)
        {
            _db = db;
            _access = access;
            _queue = queue;
            _blobs = blobs;
        }

        [HttpGet("{id:int}/release")]
        public async Task<IActionResult> List(int id, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            List<ProjectRelease> releases = await _db.ProjectReleases
                .Where(r => r.ProjectId == id)
                .OrderByDescending(r => r.ReleasedAt)
                .ThenByDescending(r => r.Id)
                .ToListAsync(cancellationToken);
            return Ok(await EnvelopesAsync(id, releases, cancellationToken));
        }

        [HttpPost("{id:int}/release")]
        public async Task<IActionResult> Create(int id, [FromBody] ReleaseWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireWriteAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            if (request == null)
            {
                return BadRequest(new { error = "sha or tag is required" });
            }

            string name = (request.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { error = "name is required" });
            }

            if (name.Length > 200)
            {
                return BadRequest(new { error = "name is too long" });
            }

            string sha = (request.Sha ?? "").Trim();
            string tag = (request.Tag ?? "").Trim();
            if (string.IsNullOrEmpty(sha) && string.IsNullOrEmpty(tag))
            {
                return BadRequest(new { error = "sha or tag is required" });
            }

            Project project = await _db.Projects.FirstAsync(p => p.Id == id, cancellationToken);
            string? storedTag = null;
            if (!string.IsNullOrEmpty(tag))
            {
                string? resolved = await ShaForTagAsync(project.RepositoryId, id, tag, cancellationToken);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    return NotFound(new { error = "Tag was not found on this repository" });
                }

                if (!string.IsNullOrEmpty(sha) && !sha.Equals(resolved, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { error = "The commit does not match the tag" });
                }

                sha = resolved;
                storedTag = ShortTag(tag);
            }

            ProjectRevision? revision = await FindRevisionAsync(id, sha, cancellationToken);
            if (revision == null)
            {
                return NotFound(new { error = "Revision not found" });
            }

            if (await _db.ProjectReleases.AnyAsync(r => r.ProjectId == id && r.Name == name, cancellationToken))
            {
                return Conflict(new { error = "A release with this name already exists" });
            }

            CallerContext caller = CallerContext.Current(HttpContext);
            ProjectRelease release = new()
            {
                Uuid = Guid.NewGuid(),
                ProjectId = id,
                RevisionId = revision.Id,
                Tag = storedTag,
                Name = name,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                State = "released",
                ReleasedBy = caller.UserId,
                ReleasedAt = DateTime.UtcNow
            };
            _db.ProjectReleases.Add(release);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return Conflict(new { error = "A release with this name already exists" });
            }

            await _queue.EnqueueAsync(
                "outputs",
                ProjectJobQueue.OutputsKey(release.Id),
                project.RepositoryId,
                project.Id,
                JsonSerializer.Serialize(new { releaseId = release.Id, revisionId = revision.Id, commitSha = revision.CommitSha }),
                cancellationToken);

            List<object> created = await EnvelopesAsync(id, new List<ProjectRelease> { release }, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, created[0]);
        }

        [HttpPut("{id:int}/release/{releaseId:int}")]
        public async Task<IActionResult> Update(int id, int releaseId, [FromBody] ReleaseUpdateRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireWriteAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            ProjectRelease? release = await _db.ProjectReleases.FirstOrDefaultAsync(r => r.Id == releaseId && r.ProjectId == id, cancellationToken);
            if (release == null)
            {
                return NotFound(new { error = "Release not found" });
            }

            if (request == null)
            {
                return BadRequest(new { error = "notes or state is required" });
            }

            if (request.Notes != null)
            {
                release.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            }

            if (request.State != null)
            {
                string state = request.State.Trim();
                if (state != "released" && state != "withdrawn")
                {
                    return BadRequest(new { error = "state must be released or withdrawn" });
                }

                release.State = state;
            }

            await _db.SaveChangesAsync(cancellationToken);
            List<object> updated = await EnvelopesAsync(id, new List<ProjectRelease> { release }, cancellationToken);
            return Ok(updated[0]);
        }

        [HttpGet("{id:int}/artifact/{artifactId:int}")]
        public async Task<IActionResult> Download(int id, int artifactId, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            ProjectArtifact? artifact = await _db.ProjectArtifacts.FirstOrDefaultAsync(a => a.Id == artifactId, cancellationToken);
            if (artifact == null)
            {
                return NotFound(new { error = "Artifact not found" });
            }

            bool owned = await _db.ProjectRevisions.AnyAsync(r => r.Id == artifact.RevisionId && r.ProjectId == id, cancellationToken);
            if (!owned)
            {
                return NotFound(new { error = "Artifact not found" });
            }

            ProjectBlob? blob = await _blobs.GetAsync(artifact.BlobHash, cancellationToken);
            if (blob?.Data == null)
            {
                return NotFound(new { error = "Artifact not found" });
            }

            return File(blob.Data, string.IsNullOrWhiteSpace(blob.MediaType) ? "application/octet-stream" : blob.MediaType, artifact.FileName);
        }

        private async Task<string?> ShaForTagAsync(int repositoryId, int projectId, string tag, CancellationToken cancellationToken)
        {
            string shortName = ShortTag(tag);
            string full = "refs/tags/" + shortName;
            GitRef? gitRef = await _db.GitRefs.FirstOrDefaultAsync(
                r => r.RepositoryId == repositoryId && r.Name == full,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(gitRef?.Sha))
            {
                return gitRef.Sha;
            }

            ProjectRevision? byRef = await _db.ProjectRevisions.FirstOrDefaultAsync(
                r => r.ProjectId == projectId && (r.Refs.Contains(full) || r.Refs.Contains(shortName)),
                cancellationToken);
            return byRef?.CommitSha;
        }

        private static string ShortTag(string tag)
        {
            string name = tag.Trim();
            if (name.StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                name = name["refs/tags/".Length..];
            }

            if (name.EndsWith("^{}", StringComparison.Ordinal))
            {
                name = name[..^3];
            }

            return name;
        }

        private async Task<ProjectRevision?> FindRevisionAsync(int projectId, string sha, CancellationToken cancellationToken)
        {
            ProjectRevision? revision = await _db.ProjectRevisions.FirstOrDefaultAsync(
                r => r.ProjectId == projectId && r.CommitSha == sha,
                cancellationToken);
            if (revision != null)
            {
                return revision;
            }

            string lowered = sha.ToLowerInvariant();
            return await _db.ProjectRevisions.FirstOrDefaultAsync(
                r => r.ProjectId == projectId && r.CommitSha.ToLower() == lowered,
                cancellationToken);
        }

        private async Task<List<object>> EnvelopesAsync(int projectId, List<ProjectRelease> releases, CancellationToken cancellationToken)
        {
            if (releases.Count == 0)
            {
                return new List<object>();
            }

            int[] revisionIds = releases.Select(r => r.RevisionId).Distinct().ToArray();
            List<ProjectRevision> revisions = await _db.ProjectRevisions
                .Where(r => revisionIds.Contains(r.Id))
                .ToListAsync(cancellationToken);
            Dictionary<int, ProjectRevision> revisionById = revisions.ToDictionary(r => r.Id);
            List<ProjectArtifact> artifacts = await _db.ProjectArtifacts
                .Where(a => revisionIds.Contains(a.RevisionId))
                .OrderBy(a => a.Id)
                .ToListAsync(cancellationToken);
            string[] reportHashes = artifacts
                .Where(a => a.Kind == "erc_json" || a.Kind == "drc_json")
                .Select(a => a.BlobHash)
                .Distinct()
                .ToArray();
            Dictionary<string, (int Errors, int Warnings)?> summaries = new(StringComparer.OrdinalIgnoreCase);
            if (reportHashes.Length > 0)
            {
                List<ProjectBlob> blobs = await _db.ProjectBlobs
                    .Where(b => reportHashes.Contains(b.Hash))
                    .ToListAsync(cancellationToken);
                foreach (ProjectBlob blob in blobs)
                {
                    summaries[blob.Hash] = blob.Data == null ? null : ReadCheckSummary(blob.Data);
                }
            }

            string[] keys = releases.Select(r => ProjectJobQueue.OutputsKey(r.Id)).ToArray();
            List<ProjectJob> jobs = await _db.ProjectJobs
                .Where(j => j.ProjectId == projectId && j.DedupKey != null && keys.Contains(j.DedupKey))
                .ToListAsync(cancellationToken);
            Dictionary<string, ProjectJob> latestJob = jobs
                .GroupBy(j => j.DedupKey!)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.Id).First());

            List<object> result = new();
            foreach (ProjectRelease release in releases)
            {
                revisionById.TryGetValue(release.RevisionId, out ProjectRevision? revision);
                List<ProjectArtifact> own = artifacts.Where(a => a.RevisionId == release.RevisionId).ToList();
                latestJob.TryGetValue(ProjectJobQueue.OutputsKey(release.Id), out ProjectJob? job);
                result.Add(new
                {
                    id = release.Id,
                    uuid = release.Uuid,
                    name = release.Name,
                    tag = release.Tag,
                    sha = revision?.CommitSha,
                    revisionId = release.RevisionId,
                    notes = release.Notes,
                    state = release.State,
                    releasedAt = release.ReleasedAt,
                    releasedBy = release.ReleasedBy,
                    erc = SummaryFor(own, "erc_json", summaries),
                    drc = SummaryFor(own, "drc_json", summaries),
                    outputsStatus = job?.Status,
                    outputsError = job?.Error,
                    artifacts = own.Select(a => new
                    {
                        id = a.Id,
                        kind = a.Kind,
                        variant = a.Variant,
                        fileName = a.FileName,
                        kicadVersion = a.KicadVersion
                    })
                });
            }

            return result;
        }

        private static object? SummaryFor(List<ProjectArtifact> artifacts, string kind, Dictionary<string, (int Errors, int Warnings)?> summaries)
        {
            ProjectArtifact? artifact = artifacts.FirstOrDefault(a => a.Kind == kind && a.Variant == "");
            if (artifact == null || !summaries.TryGetValue(artifact.BlobHash, out (int Errors, int Warnings)? summary) || summary == null)
            {
                return null;
            }

            return new { errors = summary.Value.Errors, warnings = summary.Value.Warnings };
        }

        internal static (int Errors, int Warnings)? ReadCheckSummary(byte[] data)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(data);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                bool hasErrorCount = TryInt(root, "error_count", out int errors);
                bool hasWarningCount = TryInt(root, "warning_count", out int warnings);
                if (hasErrorCount || hasWarningCount)
                {
                    return (errors, warnings);
                }

                if (!TryArray(root, "violations", out JsonElement violations) && !TryArray(root, "items", out violations))
                {
                    return null;
                }

                int errorCount = 0;
                int warningCount = 0;
                foreach (JsonElement item in violations.EnumerateArray())
                {
                    string severity = "";
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("severity", out JsonElement severityElement)
                        && severityElement.ValueKind == JsonValueKind.String)
                    {
                        severity = severityElement.GetString() ?? "";
                    }

                    if (severity.Contains("warn", StringComparison.OrdinalIgnoreCase))
                    {
                        warningCount++;
                    }
                    else
                    {
                        errorCount++;
                    }
                }

                return (errorCount, warningCount);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool TryInt(JsonElement element, string name, out int value)
        {
            value = 0;
            return element.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out value);
        }

        private static bool TryArray(JsonElement element, string name, out JsonElement value)
        {
            if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            value = default;
            return false;
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

        private async Task<IActionResult?> RequireWriteAsync(int id, CancellationToken cancellationToken)
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

            if (!await _access.CanWriteAsync(caller, id, cancellationToken))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Write permission required" });
            }

            return null;
        }
    }
}
