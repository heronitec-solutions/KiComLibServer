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
    public class Model3DController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly DiffService _diffService;

        public Model3DController(ApplicationDbContext context, DiffService diffService)
        {
            _context = context;
            _diffService = diffService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Model3DDTO>>> GetModel3Ds(
            [FromQuery] string _sort = "id",
            [FromQuery] string _order = "asc",
            [FromQuery] string? _filterby = null,
            [FromQuery] string? _filter = null)
        {
            IQueryable<Model3DDTO> query = _context.GetModel3DList();

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
        public async Task<ActionResult<Model3DDTO>> GetModel3D(int id)
        {
            Model3DDTO? model = await _context.GetModel3DList().FirstOrDefaultAsync(m => m.Id == id);
            return model == null ? NotFound() : model;
        }

        [HttpGet("searchbyfilename")]
        public async Task<ActionResult<IEnumerable<Model3DDTO>>> GetModelByFilename([FromQuery] string _filename, [FromQuery] string? _path = null)
        {
            if (string.IsNullOrEmpty(_filename))
            {
                return BadRequest("Filename is required.");
            }

            IQueryable<Model3DDTO> query = _context.GetModel3DList()
                .Where(m => m.Filename == HttpUtility.UrlDecode(_filename));

            if (!string.IsNullOrEmpty(_path))
            {
                query = query.Where(m => m.Directory == HttpUtility.UrlDecode(_path));
            }
            else
            {
                query = query.Where(m => m.Directory == "" || m.Directory == null);
            }

            List<Model3DDTO> models = await query.ToListAsync();
            if (models.Count == 0)
            {
                return NotFound(string.IsNullOrEmpty(_path)
                    ? $"No models found with the filename '{_filename}'."
                    : $"No models found with the filename '{_filename}' and the path '{_path}'.");
            }

            return models;
        }

        [HttpGet("{id:int}/history")]
        public async Task<IActionResult> History(int id, CancellationToken cancellationToken)
        {
            if (!await _context.Model3D.AnyAsync(s => s.Id == id, cancellationToken))
            {
                return NotFound();
            }

            var rows = await (
                from r in _context.Model3DRevisions
                join c in _context.Commits on r.CommitId equals c.Id
                where r.Model3dId == id
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
            object? diff = await _diffService.GetBinaryDiffAsync("model3d", id, from, to, cancellationToken);
            return diff == null ? NotFound() : Ok(diff);
        }

        [HttpPost]
        public IActionResult PostModel3D() => Gone();

        [HttpPut("{id:int}")]
        public IActionResult PutModel3D(int id) => Gone();

        [HttpDelete("{id:int}")]
        public IActionResult DeleteModel3D(int id) => Gone();

        private ObjectResult Gone() =>
            StatusCode(StatusCodes.Status410Gone, new { message = "Direct writes are disabled. Use POST /api/commit." });
    }
}
