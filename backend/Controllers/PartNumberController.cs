using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;

namespace KiComLibBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PartNumberController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CatalogRevisionService _catalog;

        public PartNumberController(ApplicationDbContext context, CatalogRevisionService catalog)
        {
            _context = context;
            _catalog = catalog;
        }

        // GET: api/partnumber
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PartNumber>>> GetPartNumbers(
            [FromQuery] string _sort = "id",
            [FromQuery] string _order = "asc",
            [FromQuery] uint? _part_id = null,
            [FromQuery] uint? _manufacturer_id = null)
        {
            IQueryable<PartNumber> query = _context.PartNumber;

            // Filter by Part if provided
            if (_part_id.HasValue)
            {
                query = query.Where(m => m.PartId == _part_id.Value);
            }

            // Filter by Manufacturer if provided
            if (_manufacturer_id.HasValue)
            {
                query = query.Where(m => m.ManufacturerId == _manufacturer_id.Value);
            }

            switch (_sort?.ToLower())
            {
                case "id":
                    query = _order == "asc" ? query.OrderBy(m => m.Id) : query.OrderByDescending(m => m.Id);
                    break;
                case "uuid":
                    query = _order == "asc" ? query.OrderBy(m => m.Uuid) : query.OrderByDescending(m => m.Uuid);
                    break;
                case "part_id":
                    query = _order == "asc" ? query.OrderBy(m => m.PartId) : query.OrderByDescending(m => m.PartId);
                    break;
                case "manufacturer_id":
                    query = _order == "asc" ? query.OrderBy(m => m.ManufacturerId) : query.OrderByDescending(m => m.ManufacturerId);
                    break;
                case "manufacturer_part_number":
                    query = _order == "asc" ? query.OrderBy(m => m.ManufacturerPartNumber) : query.OrderByDescending(m => m.ManufacturerPartNumber);
                    break;
                case "quantity":
                    query = _order == "asc" ? query.OrderBy(m => m.Quantity) : query.OrderByDescending(m => m.Quantity);
                    break;
                case "package_type":
                    query = _order == "asc" ? query.OrderBy(m => m.PackageType) : query.OrderByDescending(m => m.PackageType);
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

            query.Include(p => p.Part)
                 .Include(p => p.Manufacturer);

            return await query.ToListAsync();
        }

        // GET: api/partnumber/5
        [HttpGet("{id}")]
        public async Task<ActionResult<PartNumber>> GetPartNumber(int id)
        {
            var partNumber = await _context.PartNumber
                                        .Include(p => p.Part)
                                        .Include(p => p.Manufacturer)
                                        .FirstOrDefaultAsync(p => p.Id == id);

            if (partNumber == null)
            {
                return NotFound();
            }

            return partNumber;
        }

        // POST: api/partnumber
        [HttpPost]
        public async Task<ActionResult<PartNumber>> PostPartNumber(PartNumber partNumber, CancellationToken cancellationToken)
        {
            partNumber.Id = null;

            return await _catalog.ExecuteAsync(async () =>
            {
                _context.PartNumber.Add(partNumber);
                await _context.SaveChangesAsync(cancellationToken);
                await _catalog.RecordAsync(
                    "partnumber",
                    partNumber.Id!.Value,
                    "create",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Create part number {partNumber.ManufacturerPartNumber}"),
                    "web-ui",
                    cancellationToken);
                return CreatedAtAction("GetPartNumber", new { id = partNumber.Id }, partNumber);
            }, cancellationToken);
        }

        // PUT: api/partnumber/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutPartNumber(int id, PartNumber partNumber, CancellationToken cancellationToken)
        {
            if (id != partNumber.Id)
            {
                return BadRequest();
            }

            return await _catalog.ExecuteAsync<IActionResult>(async () =>
            {
                _context.Entry(partNumber).State = EntityState.Modified;
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.PartNumber.Any(e => e.Id == id))
                    {
                        return NotFound();
                    }

                    throw;
                }

                await _catalog.RecordAsync(
                    "partnumber",
                    id,
                    "update",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Update part number {partNumber.ManufacturerPartNumber}"),
                    "web-ui",
                    cancellationToken);
                return NoContent();
            }, cancellationToken);
        }

        // DELETE: api/partnumber/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePartNumber(int id, CancellationToken cancellationToken)
        {
            bool found = false;
            await _catalog.ExecuteAsync(async () =>
            {
                PartNumber? partNumber = await _context.PartNumber.FindAsync(new object[] { id }, cancellationToken);
                if (partNumber == null)
                {
                    return 0;
                }

                found = true;
                await _catalog.RecordDeleteAsync(
                    "partnumber",
                    id,
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Delete part number {partNumber.ManufacturerPartNumber}"),
                    "web-ui",
                    cancellationToken);
                _context.PartNumber.Remove(partNumber);
                await _context.SaveChangesAsync(cancellationToken);
                return 0;
            }, cancellationToken);

            return found ? NoContent() : NotFound();
        }
    }
}
