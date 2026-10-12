using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using KiComLibBackend.Services.Distributors;

namespace KiComLibBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PartController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CatalogRevisionService _catalog;
        private readonly IBlobStore _blobs;
        private readonly ProductImageStore _images;

        public PartController(
            ApplicationDbContext context,
            CatalogRevisionService catalog,
            IBlobStore blobs,
            ProductImageStore images)
        {
            _context = context;
            _catalog = catalog;
            _blobs = blobs;
            _images = images;
        }

        // GET: api/part
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Part>>> GetParts([FromQuery] string _sort = "id", [FromQuery] string _order = "asc")
        {
            IQueryable<Part> query = _context.Part;

            switch (_sort?.ToLower())
            {
                case "id":
                    query = _order == "asc" ? query.OrderBy(m => m.Id) : query.OrderByDescending(m => m.Id);
                    break;
                case "uuid":
                    query = _order == "asc" ? query.OrderBy(m => m.Uuid) : query.OrderByDescending(m => m.Uuid);
                    break;
                case "name":
                    query = _order == "asc" ? query.OrderBy(m => m.Name) : query.OrderByDescending(m => m.Name);
                    break;
                case "comment":
                    query = _order == "asc" ? query.OrderBy(m => m.Comment) : query.OrderByDescending(m => m.Comment);
                    break;
                case "datasheet_id":
                    query = _order == "asc" ? query.OrderBy(m => m.DatasheetId) : query.OrderByDescending(m => m.DatasheetId);
                    break;
                case "library_id":
                    query = _order == "asc" ? query.OrderBy(m => m.LibraryId) : query.OrderByDescending(m => m.LibraryId);
                    break;
                case "symbol_id":
                    query = _order == "asc" ? query.OrderBy(m => m.SymbolId) : query.OrderByDescending(m => m.SymbolId);
                    break;
                case "footprint_id":
                    query = _order == "asc" ? query.OrderBy(m => m.FootprintId) : query.OrderByDescending(m => m.FootprintId);
                    break;
                case "lifecycle_status":
                case "lifecyclestatus":
                    query = _order == "asc" ? query.OrderBy(m => m.LifecycleStatus) : query.OrderByDescending(m => m.LifecycleStatus);
                    break;
                case "keywords":
                    query = _order == "asc" ? query.OrderBy(m => m.Keywords) : query.OrderByDescending(m => m.Keywords);
                    break;
                case "value":
                    query = _order == "asc" ? query.OrderBy(m => m.Value) : query.OrderByDescending(m => m.Value);
                    break;
                case "properties":
                    query = _order == "asc" ? query.OrderBy(m => m.Properties) : query.OrderByDescending(m => m.Properties);
                    break;
                case "alternatives":
                    query = _order == "asc" ? query.OrderBy(m => m.Alternatives) : query.OrderByDescending(m => m.Alternatives);
                    break;
                case "createdat":
                    query = _order == "asc" ? query.OrderBy(m => m.CreatedAt) : query.OrderByDescending(m => m.CreatedAt);
                    break;
                case "updatedat":
                    query = _order == "asc" ? query.OrderBy(m => m.UpdatedAt) : query.OrderByDescending(m => m.UpdatedAt);
                    break;
                default:
                    break; // No sorting applied
            }

            query.Include(p => p.DatasheetNavigation)
                 .Include(p => p.Library)
                 .Include(p => p.Symbol)
                 .Include(p => p.Footprint);

            return await query.ToListAsync();
        }

        // GET: api/part/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Part>> GetPart(int id)
        {
            var part = await _context.Part
                                    .Include(p => p.DatasheetNavigation)
                                    .Include(p => p.Library)
                                    .Include(p => p.Symbol)
                                    .Include(p => p.Footprint)
                                    .FirstOrDefaultAsync(p => p.Id == id);

            if (part == null)
            {
                return NotFound();
            }

            return part;
        }

        // POST: api/part
        [HttpPost]
        public async Task<ActionResult<Part>> PostPart(Part part, CancellationToken cancellationToken)
        {
            part.Id = null;
            ActionResult? invalid = await PrepareDatasheetsAsync(part, cancellationToken)
                ?? await PrepareImageAsync(part, cancellationToken)
                ?? InvalidLinks(part)
                ?? NormalizeLifecycle(part);
            if (invalid != null)
            {
                return invalid;
            }

            return await _catalog.ExecuteAsync(async () =>
            {
                part.Alternatives = NormalizeAlternatives(part.Alternatives, part.Id);
                _context.Part.Add(part);
                await _context.SaveChangesAsync(cancellationToken);
                await _catalog.RecordAsync(
                    "part",
                    part.Id!.Value,
                    "create",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Create part {part.Name}"),
                    "web-ui",
                    cancellationToken);
                return CreatedAtAction("GetPart", new { id = part.Id }, part);
            }, cancellationToken);
        }

        // PUT: api/part/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutPart(int id, Part part, CancellationToken cancellationToken)
        {
            if (id != part.Id)
            {
                return BadRequest();
            }

            ActionResult? invalid = await PrepareDatasheetsAsync(part, cancellationToken)
                ?? await PrepareImageAsync(part, cancellationToken)
                ?? InvalidLinks(part)
                ?? NormalizeLifecycle(part);
            if (invalid != null)
            {
                return invalid;
            }

            return await _catalog.ExecuteAsync<IActionResult>(async () =>
            {
                part.Alternatives = NormalizeAlternatives(part.Alternatives, part.Id);
                _context.Entry(part).State = EntityState.Modified;
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Part.Any(e => e.Id == id))
                    {
                        return NotFound();
                    }

                    throw;
                }

                await _catalog.RecordAsync(
                    "part",
                    id,
                    "update",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Update part {part.Name}"),
                    "web-ui",
                    cancellationToken);
                return NoContent();
            }, cancellationToken);
        }

        // DELETE: api/part/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePart(int id, CancellationToken cancellationToken)
        {
            bool found = false;
            await _catalog.ExecuteAsync(async () =>
            {
                Part? part = await _context.Part.FindAsync(new object[] { id }, cancellationToken);
                if (part == null)
                {
                    return 0;
                }

                found = true;
                await _catalog.RecordDeleteAsync(
                    "part",
                    id,
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Delete part {part.Name}"),
                    "web-ui",
                    cancellationToken);
                _context.Part.Remove(part);
                await _context.SaveChangesAsync(cancellationToken);
                return 0;
            }, cancellationToken);

            return found ? NoContent() : NotFound();
        }

        [HttpPost("image")]
        [RequestSizeLimit(ProductImageStore.MaxBytes + (64 * 1024))]
        [RequestFormLimits(MultipartBodyLengthLimit = ProductImageStore.MaxBytes + (64 * 1024))]
        public async Task<IActionResult> PostImage(IFormFile? file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
            {
                return ImageError("An image file is required");
            }

            if (file.Length > ProductImageStore.MaxBytes)
            {
                return ImageError("The product image is larger than 5 MB");
            }

            using MemoryStream buffer = new();
            await file.CopyToAsync(buffer, cancellationToken);
            ProductImageResult stored = await _images.StoreAsync(buffer.ToArray(), cancellationToken);
            if (stored.StatusCode != 201 || stored.Hash == null)
            {
                return ImageError(stored.Error ?? "The product image could not be stored");
            }

            return StatusCode(StatusCodes.Status201Created, new { hash = stored.Hash, mediaType = stored.MediaType });
        }

        [HttpGet("image/{hash}")]
        public async Task<IActionResult> GetImageByHash(string hash, CancellationToken cancellationToken)
        {
            string? normalized = ProductImageStore.NormalizeHash(hash);
            if (normalized == null)
            {
                return NotFound();
            }

            return await ImageFileAsync(normalized, cancellationToken);
        }

        [HttpGet("{id:int}/image")]
        public async Task<IActionResult> GetImage(int id, CancellationToken cancellationToken)
        {
            string? hash = await _context.Part
                .Where(part => part.Id == id)
                .Select(part => part.ImageHash)
                .FirstOrDefaultAsync(cancellationToken);
            string? normalized = ProductImageStore.NormalizeHash(hash);
            if (normalized == null)
            {
                return NotFound();
            }

            return await ImageFileAsync(normalized, cancellationToken);
        }

        /// <summary>
        /// Stores the datasheet list and keeps datasheet_id equal to its first entry.
        /// A request that only sets datasheet_id still becomes a one-item list.
        /// </summary>
        private async Task<ActionResult?> PrepareDatasheetsAsync(Part part, CancellationToken cancellationToken)
        {
            part.DatasheetIds = PartDatasheetUsage.Normalize(part.DatasheetIds, part.DatasheetId);
            part.DatasheetId = part.DatasheetIds.Count == 0 ? null : part.DatasheetIds[0];
            if (part.DatasheetIds.Count == 0)
            {
                return null;
            }

            List<int?> wanted = part.DatasheetIds.Select(id => (int?)id).ToList();
            List<int> found = await _context.Datasheet
                .Where(d => d.DeletedAt == null && wanted.Contains(d.Id))
                .Select(d => d.Id!.Value)
                .ToListAsync(cancellationToken);
            if (part.DatasheetIds.All(found.Contains))
            {
                return null;
            }

            return BadRequest(new
            {
                errors = new Dictionary<string, string[]>
                {
                    ["datasheetIds"] = new[] { "The selected datasheet does not exist." }
                }
            });
        }

        private async Task<ActionResult?> PrepareImageAsync(Part part, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(part.ImageHash))
            {
                part.ImageHash = null;
                return null;
            }

            string? hash = ProductImageStore.NormalizeHash(part.ImageHash);
            if (hash == null)
            {
                return ImageError("The product image is not valid.");
            }

            Blob? blob = await _blobs.GetAsync(hash, cancellationToken);
            if (blob?.Data == null
                || blob.Data.Length > ProductImageStore.MaxBytes
                || ProductImageStore.DetectMediaType(blob.Data) == null)
            {
                return ImageError("The product image does not exist.");
            }

            part.ImageHash = hash;
            return null;
        }

        private async Task<IActionResult> ImageFileAsync(string hash, CancellationToken cancellationToken)
        {
            Blob? blob = await _blobs.GetAsync(hash, cancellationToken);
            if (blob?.Data == null || blob.Data.Length > ProductImageStore.MaxBytes)
            {
                return NotFound();
            }

            string? mediaType = ProductImageStore.DetectMediaType(blob.Data);
            if (mediaType == null)
            {
                return NotFound();
            }

            return File(blob.Data, mediaType);
        }

        private static BadRequestObjectResult ImageError(string message)
        {
            return new BadRequestObjectResult(new
            {
                error = message,
                errors = new Dictionary<string, string[]>
                {
                    ["imageHash"] = new[] { message }
                }
            });
        }

        private static ActionResult? InvalidLinks(Part part)
        {
            string? error = PartLinks.Normalize(part.Links, out List<PartLink> links);
            part.Links = links;
            if (error == null)
            {
                return null;
            }

            return new BadRequestObjectResult(new
            {
                errors = new Dictionary<string, string[]>
                {
                    ["links"] = new[] { error }
                }
            });
        }

        private static readonly string[] LifecycleStatuses = { "active", "nrnd", "ltb", "eol" };

        /// <summary>Stores a lowercase lifecycle status. Blank values become active.</summary>
        private static ActionResult? NormalizeLifecycle(Part part)
        {
            string status = (part.LifecycleStatus ?? "").Trim().ToLowerInvariant();
            if (status.Length == 0)
            {
                part.LifecycleStatus = "active";
                return null;
            }

            if (Array.IndexOf(LifecycleStatuses, status) < 0)
            {
                return new BadRequestObjectResult(new
                {
                    errors = new Dictionary<string, string[]>
                    {
                        ["lifecycleStatus"] = new[] { "Lifecycle status must be active, nrnd, ltb, or eol." }
                    }
                });
            }

            part.LifecycleStatus = status;
            return null;
        }

        /// <summary>Keeps positive, distinct part ids and drops a part's own id.</summary>
        private static List<int> NormalizeAlternatives(List<int>? ids, int? selfId)
        {
            return (ids ?? new List<int>())
                .Where(id => id > 0 && id != selfId)
                .Distinct()
                .ToList();
        }
    }
}
