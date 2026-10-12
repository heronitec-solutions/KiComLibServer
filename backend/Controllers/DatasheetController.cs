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
    public class DatasheetController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlobStore _blobStore;
        private readonly DiffService _diffService;

        public DatasheetController(ApplicationDbContext context, IBlobStore blobStore, DiffService diffService)
        {
            _context = context;
            _blobStore = blobStore;
            _diffService = diffService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DatasheetDTO>>> GetDatasheets(
            [FromQuery] string _sort = "id",
            [FromQuery] string _order = "asc",
            [FromQuery] string? _filterby = null,
            [FromQuery] string? _filter = null)
        {
            IQueryable<DatasheetDTO> query = _context.GetDatasheetList();

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
        public async Task<ActionResult<DatasheetDTO>> GetDatasheet(int id)
        {
            DatasheetDTO? datasheet = await _context.GetDatasheetList().FirstOrDefaultAsync(m => m.Id == id);
            return datasheet == null ? NotFound() : datasheet;
        }

        [HttpGet("searchbyfilename")]
        public async Task<ActionResult<IEnumerable<DatasheetDTO>>> GetModelByFilename([FromQuery] string _filename, [FromQuery] string? _path = null)
        {
            if (string.IsNullOrEmpty(_filename))
            {
                return BadRequest("Filename is required.");
            }

            IQueryable<DatasheetDTO> query = _context.GetDatasheetList().Where(m => m.Filename == _filename);
            if (!string.IsNullOrEmpty(_path))
            {
                query = query.Where(m => m.Directory == _path);
            }

            List<DatasheetDTO> datasheets = await query.ToListAsync();
            if (datasheets.Count == 0)
            {
                return NotFound(string.IsNullOrEmpty(_path)
                    ? $"No datasheets found with the filename '{_filename}'."
                    : $"No datasheets found with the filename '{_filename}' and the path '{_path}'.");
            }

            return datasheets;
        }

        [HttpGet("{id:int}/file")]
        public async Task<IActionResult> GetFile(int id, [FromQuery] bool download, CancellationToken cancellationToken)
        {
            DatasheetDTO? datasheet = await _context.GetDatasheetList().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
            if (datasheet == null)
            {
                return NotFound();
            }

            string? hash = datasheet.Hash ?? datasheet.FileHash;
            if (string.IsNullOrWhiteSpace(hash))
            {
                return NotFound();
            }

            Blob? blob = await _blobStore.GetAsync(hash, cancellationToken);
            if (blob?.Data == null)
            {
                return NotFound();
            }

            return BlobMedia.File(this, blob.Data, blob.MediaType, SafeFileName(datasheet.Filename), download);
        }

        [HttpGet("{id:int}/history")]
        public async Task<IActionResult> History(int id, CancellationToken cancellationToken)
        {
            if (!await _context.Datasheet.AnyAsync(s => s.Id == id, cancellationToken))
            {
                return NotFound();
            }

            var rows = await (
                from r in _context.DatasheetRevisions
                join c in _context.Commits on r.CommitId equals c.Id
                where r.DatasheetId == id
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
            object? diff = await _diffService.GetBinaryDiffAsync("datasheet", id, from, to, cancellationToken);
            return diff == null ? NotFound() : Ok(diff);
        }

        [HttpPost]
        public IActionResult PostDatasheet() => Gone();

        [HttpPut("{id:int}")]
        public IActionResult PutDatasheet(int id) => Gone();

        [HttpDelete("{id:int}")]
        public IActionResult DeleteDatasheet(int id) => Gone();

        private ObjectResult Gone() =>
            StatusCode(StatusCodes.Status410Gone, new { message = "Direct writes are disabled. Use POST /api/commit." });

        private static string SafeFileName(string? filename)
        {
            string name = Path.GetFileName((filename ?? "").Replace('\\', '/').Trim());
            if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            {
                return "datasheet.pdf";
            }

            return name;
        }
    }
}
