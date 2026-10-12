using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using KiComLibBackend.Services.Distributors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KiComLibBackend.Controllers
{
    public class WarehouseWriteRequest
    {
        public int? PartId { get; set; }

        public string? Name { get; set; }

        public string? Value { get; set; }

        public string? Comment { get; set; }

        /// <summary>Photo for a manual entry. Null leaves the current photo, empty removes it.</summary>
        public string? ImageHash { get; set; }

        public List<WarehouseBatchWrite>? Batches { get; set; }
    }

    public class WarehouseBatchWrite
    {
        public string? Name { get; set; }

        public int Quantity { get; set; }

        public string? Location { get; set; }
    }

    [Route("api/warehouse")]
    [ApiController]
    public class WarehouseController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IBlobStore _blobs;
        private readonly ProductImageStore _images;

        public WarehouseController(ApplicationDbContext db, IBlobStore blobs, ProductImageStore images)
        {
            _db = db;
            _blobs = blobs;
            _images = images;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string _sort = "id", [FromQuery] string _order = "asc", CancellationToken cancellationToken = default)
        {
            List<WarehouseItem> items = await _db.WarehouseItems
                .AsNoTracking()
                .Include(item => item.Part)
                .Include(item => item.Batches)
                .ToListAsync(cancellationToken);

            List<WarehouseRow> rows = items.Select(item => Map(item, item.Part)).ToList();
            return Ok(Sort(rows, _sort, _order));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
        {
            WarehouseItem? item = await LoadAsync(id, tracking: false, cancellationToken);
            if (item == null)
            {
                return NotFound(new { error = "Warehouse item not found" });
            }

            return Ok(Map(item, item.Part));
        }

        [HttpGet("{id:int}/image")]
        public async Task<IActionResult> Image(int id, CancellationToken cancellationToken)
        {
            var stored = await _db.WarehouseItems
                .AsNoTracking()
                .Where(item => item.Id == id)
                .Select(item => new
                {
                    item.ImageHash,
                    PartImageHash = item.Part != null ? item.Part.ImageHash : null
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (stored == null)
            {
                return NotFound();
            }

            string? hash = ProductImageStore.NormalizeHash(stored.ImageHash)
                ?? ProductImageStore.NormalizeHash(stored.PartImageHash);
            if (hash == null)
            {
                return NotFound();
            }

            return await ImageFileAsync(hash, cancellationToken);
        }

        [HttpGet("image/{hash}")]
        public async Task<IActionResult> ImageByHash(string hash, CancellationToken cancellationToken)
        {
            string? normalized = ProductImageStore.NormalizeHash(hash);
            if (normalized == null)
            {
                return NotFound();
            }

            return await ImageFileAsync(normalized, cancellationToken);
        }

        [HttpPost("image")]
        [RequestSizeLimit(ProductImageStore.MaxBytes + (64 * 1024))]
        [RequestFormLimits(MultipartBodyLengthLimit = ProductImageStore.MaxBytes + (64 * 1024))]
        public async Task<IActionResult> PostImage(IFormFile? file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "An image file is required" });
            }

            if (file.Length > ProductImageStore.MaxBytes)
            {
                return BadRequest(new { error = "The product image is larger than 5 MB" });
            }

            using MemoryStream buffer = new();
            await file.CopyToAsync(buffer, cancellationToken);
            ProductImageResult stored = await _images.StoreAsync(buffer.ToArray(), cancellationToken);
            if (stored.StatusCode != 201 || stored.Hash == null)
            {
                return BadRequest(new { error = stored.Error ?? "The product image could not be stored" });
            }

            return StatusCode(StatusCodes.Status201Created, new { hash = stored.Hash, mediaType = stored.MediaType });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WarehouseWriteRequest? request, CancellationToken cancellationToken)
        {
            (Part? part, IActionResult? invalid) = await ResolvePartAsync(request, cancellationToken);
            if (invalid != null)
            {
                return invalid;
            }

            string? identityError = ApplyIdentity(new WarehouseItem(), part, request, out WarehouseItem item);
            if (identityError != null)
            {
                return BadRequest(new { error = identityError });
            }

            string? imageError = await ApplyImageAsync(item, part, request, cancellationToken);
            if (imageError != null)
            {
                return BadRequest(new { error = imageError });
            }

            (IReadOnlyList<WarehouseStock.BatchDraft>? batches, string? batchError) = ReadBatches(request);
            if (batchError != null || batches == null)
            {
                return BadRequest(new { error = batchError ?? "Batches are invalid" });
            }

            ReplaceBatches(item, batches);
            _db.WarehouseItems.Add(item);
            IActionResult? conflict = await SaveAsync(cancellationToken);
            if (conflict != null)
            {
                return conflict;
            }

            return StatusCode(StatusCodes.Status201Created, Map(item, part));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] WarehouseWriteRequest? request, CancellationToken cancellationToken)
        {
            WarehouseItem? item = await LoadAsync(id, tracking: true, cancellationToken);
            if (item == null)
            {
                return NotFound(new { error = "Warehouse item not found" });
            }

            (Part? part, IActionResult? invalid) = await ResolvePartAsync(request, cancellationToken);
            if (invalid != null)
            {
                return invalid;
            }

            string? identityError = ApplyIdentity(item, part, request, out _);
            if (identityError != null)
            {
                return BadRequest(new { error = identityError });
            }

            string? imageError = await ApplyImageAsync(item, part, request, cancellationToken);
            if (imageError != null)
            {
                return BadRequest(new { error = imageError });
            }

            (IReadOnlyList<WarehouseStock.BatchDraft>? batches, string? batchError) = ReadBatches(request);
            if (batchError != null || batches == null)
            {
                return BadRequest(new { error = batchError ?? "Batches are invalid" });
            }

            ReplaceBatches(item, batches);
            IActionResult? conflict = await SaveAsync(cancellationToken);
            if (conflict != null)
            {
                return conflict;
            }

            return Ok(Map(item, part));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            WarehouseItem? item = await _db.WarehouseItems.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (item == null)
            {
                return NotFound(new { error = "Warehouse item not found" });
            }

            _db.WarehouseItems.Remove(item);
            await _db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        private async Task<WarehouseItem?> LoadAsync(int id, bool tracking, CancellationToken cancellationToken)
        {
            IQueryable<WarehouseItem> query = _db.WarehouseItems
                .Include(item => item.Part)
                .Include(item => item.Batches);
            if (!tracking)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        }

        private async Task<(Part? Part, IActionResult? Error)> ResolvePartAsync(WarehouseWriteRequest? request, CancellationToken cancellationToken)
        {
            if (request?.PartId is not int partId || partId <= 0)
            {
                return (null, null);
            }

            Part? part = await _db.Part.AsNoTracking().FirstOrDefaultAsync(row => row.Id == partId, cancellationToken);
            if (part == null)
            {
                return (null, BadRequest(new { error = "Catalog part not found" }));
            }

            return (part, null);
        }

        private static string? ApplyIdentity(WarehouseItem target, Part? part, WarehouseWriteRequest? request, out WarehouseItem item)
        {
            item = target;
            string comment = (request?.Comment ?? "").Trim();
            if (comment.Length > 4000)
            {
                return "Comment must be at most 4000 characters";
            }

            if (part != null)
            {
                if (part.Id is not int partId || partId <= 0)
                {
                    return "Catalog part not found";
                }

                string partName = (part.Name ?? "").Trim();
                if (partName.Length == 0)
                {
                    return "Catalog part has no name";
                }

                item.PartId = partId;
                item.Name = partName;
                item.Value = (part.Value ?? "").Trim();
            }
            else
            {
                string name = (request?.Name ?? "").Trim();
                string value = (request?.Value ?? "").Trim();
                if (name.Length < 1 || name.Length > 200)
                {
                    return "Name must be between 1 and 200 characters";
                }

                if (value.Length > 200)
                {
                    return "Value must be at most 200 characters";
                }

                item.PartId = null;
                item.Name = name;
                item.Value = value;
            }

            item.Comment = comment;
            return null;
        }

        private async Task<string?> ApplyImageAsync(WarehouseItem item, Part? part, WarehouseWriteRequest? request, CancellationToken cancellationToken)
        {
            if (part != null)
            {
                item.ImageHash = null;
                return null;
            }

            if (request?.ImageHash == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.ImageHash))
            {
                item.ImageHash = null;
                return null;
            }

            string? hash = ProductImageStore.NormalizeHash(request.ImageHash);
            if (hash == null)
            {
                return "The product image is not valid";
            }

            Blob? blob = await _blobs.GetAsync(hash, cancellationToken);
            if (blob?.Data == null
                || blob.Data.Length > ProductImageStore.MaxBytes
                || ProductImageStore.DetectMediaType(blob.Data) == null)
            {
                return "The product image does not exist";
            }

            item.ImageHash = hash;
            return null;
        }

        private async Task<IActionResult> ImageFileAsync(string hash, CancellationToken cancellationToken)
        {
            Blob? blob = await _blobs.GetAsync(hash, cancellationToken);
            if (blob?.Data == null
                || blob.Data.Length > ProductImageStore.MaxBytes
                || ProductImageStore.DetectMediaType(blob.Data) is not string mediaType)
            {
                return NotFound();
            }

            return File(blob.Data, mediaType);
        }

        private static (IReadOnlyList<WarehouseStock.BatchDraft>? Batches, string? Error) ReadBatches(WarehouseWriteRequest? request)
        {
            IEnumerable<WarehouseStock.BatchDraft>? drafts = request?.Batches?.Select(batch => new WarehouseStock.BatchDraft
            {
                Name = batch.Name ?? "",
                Quantity = batch.Quantity,
                Location = batch.Location ?? ""
            });
            return WarehouseStock.Normalize(drafts);
        }

        private void ReplaceBatches(WarehouseItem item, IReadOnlyList<WarehouseStock.BatchDraft> batches)
        {
            List<WarehouseBatch> previous = item.Batches.ToList();
            if (previous.Count > 0)
            {
                _db.WarehouseBatches.RemoveRange(previous);
                item.Batches.Clear();
            }

            foreach (WarehouseStock.BatchDraft batch in batches)
            {
                item.Batches.Add(new WarehouseBatch
                {
                    Name = batch.Name,
                    Quantity = batch.Quantity,
                    Location = batch.Location
                });
            }
        }

        private async Task<IActionResult?> SaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return null;
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Conflict(new { error = "This catalog part is already in the warehouse" });
            }
        }

        private static List<WarehouseRow> Sort(List<WarehouseRow> rows, string sort, string order)
        {
            bool descending = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
            StringComparer text = StringComparer.OrdinalIgnoreCase;
            IEnumerable<WarehouseRow> ordered = sort.ToLowerInvariant() switch
            {
                "name" => descending ? rows.OrderByDescending(row => row.Name, text) : rows.OrderBy(row => row.Name, text),
                "value" => descending ? rows.OrderByDescending(row => row.Value, text) : rows.OrderBy(row => row.Value, text),
                "quantity" => descending ? rows.OrderByDescending(row => row.Quantity) : rows.OrderBy(row => row.Quantity),
                "location" or "locations" => descending ? rows.OrderByDescending(row => row.Locations, text) : rows.OrderBy(row => row.Locations, text),
                "updatedat" => descending ? rows.OrderByDescending(row => row.UpdatedAt) : rows.OrderBy(row => row.UpdatedAt),
                _ => descending ? rows.OrderByDescending(row => row.Id) : rows.OrderBy(row => row.Id)
            };

            return ordered.ToList();
        }

        private static WarehouseRow Map(WarehouseItem item, Part? part)
        {
            bool linked = item.PartId != null && part != null;
            string? ownImage = ProductImageStore.NormalizeHash(item.ImageHash);
            string? partImage = linked ? ProductImageStore.NormalizeHash(part!.ImageHash) : null;
            List<WarehouseBatch> batches = (item.Batches ?? new List<WarehouseBatch>())
                .OrderBy(batch => batch.Name == WarehouseStock.EmptyBatch ? 1 : 0)
                .ThenBy(batch => batch.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(batch => batch.Location, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string locations = string.Join(", ", batches
                .Select(batch => batch.Location)
                .Where(location => location.Length > 0)
                .Distinct(StringComparer.Ordinal));

            return new WarehouseRow
            {
                Id = item.Id,
                PartId = linked ? item.PartId : null,
                Name = linked ? part!.Name : item.Name,
                Value = linked ? (part!.Value ?? "") : item.Value,
                Comment = item.Comment,
                HasImage = ownImage != null || partImage != null,
                ImageHash = ownImage,
                Quantity = batches.Sum(batch => batch.Quantity),
                Locations = locations,
                Batches = batches.Select(batch => new
                {
                    id = batch.Id,
                    name = batch.Name,
                    quantity = batch.Quantity,
                    location = batch.Location
                }),
                UpdatedAt = item.UpdatedAt,
                CreatedAt = item.CreatedAt
            };
        }

        private sealed class WarehouseRow
        {
            public int Id { get; init; }

            public int? PartId { get; init; }

            public string Name { get; init; } = "";

            public string Value { get; init; } = "";

            public string Comment { get; init; } = "";

            public bool HasImage { get; init; }

            public string? ImageHash { get; init; }

            public int Quantity { get; init; }

            public string Locations { get; init; } = "";

            public IEnumerable<object> Batches { get; init; } = Array.Empty<object>();

            public DateTime UpdatedAt { get; init; }

            public DateTime CreatedAt { get; init; }
        }
    }
}
