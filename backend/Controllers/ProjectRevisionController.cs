using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    [Route("api/project")]
    [ApiController]
    public class ProjectRevisionController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectJobQueue _queue;
        private readonly BomService _bom;
        private readonly ProjectAnalysisApplier _applier;

        public ProjectRevisionController(
            ApplicationDbContext db,
            ProjectAccessService access,
            ProjectJobQueue queue,
            BomService bom,
            ProjectAnalysisApplier applier)
        {
            _db = db;
            _access = access;
            _queue = queue;
            _bom = bom;
            _applier = applier;
        }

        [HttpGet("usage/part/{partId:int}")]
        public async Task<IActionResult> Usage(int partId, CancellationToken cancellationToken)
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (!caller.IsAuthenticated)
            {
                return Unauthorized(new { error = "Authentication required" });
            }

            bool exists = await _db.Part.AnyAsync(p => p.Id == partId, cancellationToken);
            if (!exists)
            {
                return NotFound(new { error = "Part not found" });
            }

            return Ok(await _bom.UsageAsync(caller, partId, cancellationToken));
        }

        [HttpGet("{id:int}/revision")]
        public async Task<IActionResult> List(
            int id,
            [FromQuery] int _limit = 50,
            [FromQuery] int _offset = 0,
            [FromQuery(Name = "ref")] string? refName = null,
            [FromQuery] bool includeUnreachable = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            if (_limit < 1)
            {
                _limit = 50;
            }

            if (_limit > 200)
            {
                _limit = 200;
            }

            if (_offset < 0)
            {
                _offset = 0;
            }

            IQueryable<ProjectRevision> query = _db.ProjectRevisions.Where(r => r.ProjectId == id);
            if (!includeUnreachable)
            {
                query = query.Where(r => r.Reachable);
            }

            if (!string.IsNullOrWhiteSpace(refName))
            {
                string wanted = refName.Trim();
                string branch = "refs/heads/" + wanted;
                string tag = "refs/tags/" + wanted;
                query = query.Where(r => r.Refs.Contains(wanted) || r.Refs.Contains(branch) || r.Refs.Contains(tag));
            }

            int total = await query.CountAsync(cancellationToken);
            List<ProjectRevision> revisions = await query
                .OrderByDescending(r => r.CommittedAt)
                .ThenByDescending(r => r.Id)
                .Skip(_offset)
                .Take(_limit)
                .ToListAsync(cancellationToken);
            Dictionary<int, int> components = await ComponentCountsAsync(revisions.Select(r => r.Id).ToList(), cancellationToken);
            Dictionary<string, string> blobs = await BlobTextAsync(revisions, cancellationToken);
            Response.Headers["X-Total-Count"] = total.ToString();
            return Ok(new
            {
                total,
                items = revisions.Select(revision => RevisionItem(revision, components, blobs))
            });
        }

        [HttpGet("{id:int}/revision/{sha}")]
        public async Task<IActionResult> Get(int id, string sha, CancellationToken cancellationToken)
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

            Dictionary<int, int> components = await ComponentCountsAsync(new List<int> { revision.Id }, cancellationToken);
            string? json = await _applier.ReadBlobTextAsync(revision.NetlistBlobHash, cancellationToken);

            return Ok(new
            {
                id = revision.Id,
                commitSha = revision.CommitSha,
                message = revision.Message,
                authorName = revision.AuthorName,
                authorEmail = revision.AuthorEmail,
                committedAt = revision.CommittedAt,
                refs = revision.Refs,
                reachable = revision.Reachable,
                analysisStatus = revision.AnalysisStatus,
                analysisError = revision.AnalysisError,
                kicadVersion = revision.KicadVersion,
                analyzedAt = revision.AnalyzedAt,
                catalogCommitId = revision.CatalogCommitId,
                counts = new
                {
                    components = components.GetValueOrDefault(revision.Id),
                    warnings = json == null ? 0 : AnalysisDocument.WarningCount(json)
                },
                titleBlock = AnalysisDocument.Section(json, "titleBlock"),
                pages = AnalysisDocument.Section(json, "pages"),
                warnings = WarningList(json)
            });
        }

        [HttpPost("{id:int}/revision/{sha}/analyze")]
        public async Task<IActionResult> Analyze(int id, string sha, CancellationToken cancellationToken)
        {
            return await EnqueueRevisionJobAsync(id, sha, "analyze", ProjectJobQueue.AnalyzeKey, markPending: true, cancellationToken);
        }

        [HttpPost("{id:int}/revision/{sha}/rematch")]
        public async Task<IActionResult> Rematch(int id, string sha, CancellationToken cancellationToken)
        {
            return await EnqueueRevisionJobAsync(id, sha, "rematch", ProjectJobQueue.RematchKey, markPending: false, cancellationToken);
        }

        [HttpGet("{id:int}/revision/{sha}/bom")]
        public async Task<IActionResult> Bom(int id, string sha, [FromQuery] string? group, CancellationToken cancellationToken)
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

            return Ok(await _bom.LinesAsync(revision.Id, group, cancellationToken));
        }

        [HttpGet("{id:int}/diff")]
        public async Task<IActionResult> Diff(int id, [FromQuery] string? @base, [FromQuery] string? target, CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireReadAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(target))
            {
                return BadRequest(new { error = "base and target are required" });
            }

            ProjectRevision? baseRevision = await FindRevisionAsync(id, @base, cancellationToken);
            ProjectRevision? targetRevision = await FindRevisionAsync(id, target, cancellationToken);
            if (baseRevision == null || targetRevision == null)
            {
                return NotFound(new { error = "Revision not found" });
            }

            return Ok(await _bom.DiffAsync(baseRevision, targetRevision, cancellationToken));
        }

        private async Task<IActionResult> EnqueueRevisionJobAsync(
            int id,
            string sha,
            string kind,
            Func<int, string, string> dedupKey,
            bool markPending,
            CancellationToken cancellationToken)
        {
            IActionResult? denied = await RequireWriteAsync(id, cancellationToken);
            if (denied != null)
            {
                return denied;
            }

            Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (project == null)
            {
                return NotFound(new { error = "Project not found" });
            }

            ProjectRevision? revision = await FindRevisionAsync(id, sha, cancellationToken);
            if (revision == null)
            {
                return NotFound(new { error = "Revision not found" });
            }

            if (markPending)
            {
                revision.AnalysisStatus = "pending";
                revision.AnalysisError = null;
                await _db.SaveChangesAsync(cancellationToken);
            }

            string payload = JsonSerializer.Serialize(new { revisionId = revision.Id, commitSha = revision.CommitSha });
            long? jobId = await _queue.EnqueueAsync(kind, dedupKey(project.Id, revision.CommitSha), project.RepositoryId, project.Id, payload, cancellationToken);
            return Accepted(new { id = jobId, status = "queued" });
        }

        private async Task<ProjectRevision?> FindRevisionAsync(int projectId, string sha, CancellationToken cancellationToken)
        {
            string wanted = sha.Trim().ToLowerInvariant();
            return await _db.ProjectRevisions.FirstOrDefaultAsync(
                r => r.ProjectId == projectId && r.CommitSha.ToLower() == wanted,
                cancellationToken);
        }

        private async Task<Dictionary<int, int>> ComponentCountsAsync(List<int> revisionIds, CancellationToken cancellationToken)
        {
            if (revisionIds.Count == 0)
            {
                return new Dictionary<int, int>();
            }

            return await _db.ProjectComponents
                .Where(c => revisionIds.Contains(c.RevisionId))
                .GroupBy(c => c.RevisionId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        }

        private async Task<Dictionary<string, string>> BlobTextAsync(List<ProjectRevision> revisions, CancellationToken cancellationToken)
        {
            List<string> hashes = revisions.Where(r => r.NetlistBlobHash != null).Select(r => r.NetlistBlobHash!).Distinct().ToList();
            Dictionary<string, string> texts = new(StringComparer.OrdinalIgnoreCase);
            if (hashes.Count == 0)
            {
                return texts;
            }

            List<ProjectBlob> blobs = await _db.ProjectBlobs.Where(b => hashes.Contains(b.Hash)).ToListAsync(cancellationToken);
            foreach (ProjectBlob blob in blobs)
            {
                if (blob.Data != null)
                {
                    texts[blob.Hash] = System.Text.Encoding.UTF8.GetString(blob.Data);
                }
            }

            return texts;
        }

        private static object RevisionItem(ProjectRevision revision, Dictionary<int, int> components, Dictionary<string, string> blobs)
        {
            int warnings = 0;
            if (revision.NetlistBlobHash != null && blobs.TryGetValue(revision.NetlistBlobHash, out string? json))
            {
                warnings = AnalysisDocument.WarningCount(json);
            }

            return new
            {
                id = revision.Id,
                commitSha = revision.CommitSha,
                message = revision.Message,
                authorName = revision.AuthorName,
                committedAt = revision.CommittedAt,
                refs = revision.Refs,
                reachable = revision.Reachable,
                analysisStatus = revision.AnalysisStatus,
                analysisError = revision.AnalysisError,
                kicadVersion = revision.KicadVersion,
                counts = new
                {
                    components = components.GetValueOrDefault(revision.Id),
                    warnings
                }
            };
        }

        private static List<string> WarningList(string? json)
        {
            JsonElement? section = AnalysisDocument.Section(json, "warnings");
            if (section == null || section.Value.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            List<string> warnings = new();
            foreach (JsonElement item in section.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        warnings.Add(text);
                    }
                }
                else
                {
                    warnings.Add(item.GetRawText());
                }
            }

            return warnings;
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
