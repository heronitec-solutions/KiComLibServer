using System.Web;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TemplateController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly DiffService _diffService;

        public TemplateController(ApplicationDbContext context, DiffService diffService)
        {
            _context = context;
            _diffService = diffService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TemplateDTO>>> GetTemplates(
            [FromQuery] string _sort = "id",
            [FromQuery] string _order = "asc",
            [FromQuery] string? _filterby = null,
            [FromQuery] string? _filter = null)
        {
            IQueryable<TemplateDTO> query = _context.GetTemplateList();

            if (_filterby != null && _filter != null)
            {
                switch (_filterby.ToLowerInvariant())
                {
                    case "id":
                        query = query.Where(m => m.Id == Convert.ToInt32(_filter));
                        break;
                    case "path":
                        {
                            string decoded = HttpUtility.UrlDecode(_filter) ?? _filter;
                            int slash = decoded.LastIndexOf('/');
                            string directory = slash >= 0 ? decoded[..slash] : "";
                            string filename = slash >= 0 ? decoded[(slash + 1)..] : decoded;
                            query = query.Where(m => m.Directory == directory && m.Filename == filename);
                            break;
                        }
                }
            }

            query = _sort?.ToLowerInvariant() switch
            {
                "id" => _order == "asc" ? query.OrderBy(m => m.Id) : query.OrderByDescending(m => m.Id),
                "uuid" => _order == "asc" ? query.OrderBy(m => m.Uuid) : query.OrderByDescending(m => m.Uuid),
                "filename" => _order == "asc" ? query.OrderBy(m => m.Filename) : query.OrderByDescending(m => m.Filename),
                "file_size" => _order == "asc" ? query.OrderBy(m => m.FileSize) : query.OrderByDescending(m => m.FileSize),
                "createdat" => _order == "asc" ? query.OrderBy(m => m.CreatedAt) : query.OrderByDescending(m => m.CreatedAt),
                "updatedat" => _order == "asc" ? query.OrderBy(m => m.UpdatedAt) : query.OrderByDescending(m => m.UpdatedAt),
                _ => query
            };

            return await query.ToListAsync();
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TemplateDTO>> GetTemplate(int id)
        {
            TemplateDTO? template = await _context.GetTemplateList().FirstOrDefaultAsync(m => m.Id == id);
            return template == null ? NotFound() : template;
        }

        [HttpGet("{id:int}/history")]
        public async Task<IActionResult> History(int id, CancellationToken cancellationToken)
        {
            if (!await _context.Template.AnyAsync(s => s.Id == id, cancellationToken))
            {
                return NotFound();
            }

            var rows = await (
                from r in _context.TemplateRevisions
                join c in _context.Commits on r.CommitId equals c.Id
                where r.TemplateId == id
                orderby r.RevisionNo
                select new
                {
                    revisionId = r.Id,
                    revisionNo = r.RevisionNo,
                    isDeleted = r.IsDeleted,
                    directory = r.Directory,
                    filename = r.Filename,
                    hash = r.BlobHash,
                    size = r.Size,
                    commitId = c.Id,
                    message = c.Message,
                    author = c.Author,
                    createdAt = r.CreatedAt
                }).ToListAsync(cancellationToken);

            return Ok(rows);
        }

        [HttpGet("{id:int}/diff")]
        public async Task<IActionResult> Diff(int id, [FromQuery] int from, [FromQuery] int to, CancellationToken cancellationToken)
        {
            object? diff = await _diffService.GetBinaryDiffAsync("template", id, from, to, cancellationToken);
            return diff == null ? NotFound() : Ok(diff);
        }

        [HttpPost]
        public IActionResult PostTemplate() => Gone();

        [HttpPut("{id:int}")]
        public IActionResult PutTemplate(int id) => Gone();

        [HttpDelete("{id:int}")]
        public IActionResult DeleteTemplate(int id) => Gone();

        private ObjectResult Gone() =>
            StatusCode(StatusCodes.Status410Gone, new { message = "Direct writes are disabled. Use POST /api/commit." });
    }
}
