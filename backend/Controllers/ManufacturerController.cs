using KiComLibBackend.Models;
using KiComLibBackend.Data;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ManufacturerController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CatalogRevisionService _catalog;

        public ManufacturerController(ApplicationDbContext context, CatalogRevisionService catalog)
        {
            _context = context;
            _catalog = catalog;
        }

        // GET: api/manufacturer
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Manufacturer>>> GetManufacturers([FromQuery] string _sort = "id", [FromQuery] string _order = "asc")
        {
            IQueryable<Manufacturer> query = _context.Manufacturer;

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
                case "url":
                    query = _order == "asc" ? query.OrderBy(m => m.Url) : query.OrderByDescending(m => m.Url);
                    break;
                case "trustedpartsid":
                    query = _order == "asc" ? query.OrderBy(m => m.TrustedPartsId) : query.OrderByDescending(m => m.TrustedPartsId);
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

            return await query.ToListAsync();
        }

        // GET: api/manufacturer/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Manufacturer>> GetManufacturer(int id)
        {
            var manufacturer = await _context.Manufacturer.FindAsync(id);

            if (manufacturer == null)
            {
                return NotFound();
            }

            return manufacturer;
        }

        // PUT: api/manufacturer/5
        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateManufacturer(int id, Manufacturer manufacturer, CancellationToken cancellationToken)
        {
            if (id != manufacturer.Id)
            {
                return BadRequest();
            }

            manufacturer.TrustedPartsId = NormalizeTrustedPartsId(manufacturer.TrustedPartsId);

            return await _catalog.ExecuteAsync<IActionResult>(async () =>
            {
                _context.Entry(manufacturer).State = EntityState.Modified;
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ManufacturerExists(id))
                    {
                        return NotFound();
                    }

                    throw;
                }

                await _catalog.RecordAsync(
                    "manufacturer",
                    id,
                    "update",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Update manufacturer {manufacturer.Name}"),
                    "web-ui",
                    cancellationToken);
                return NoContent();
            }, cancellationToken);
        }

        // POST: api/manufacturer
        [HttpPost]
        public async Task<ActionResult<Manufacturer>> CreateManufacturer(Manufacturer manufacturer, CancellationToken cancellationToken)
        {
            manufacturer.Id = null;
            manufacturer.TrustedPartsId = NormalizeTrustedPartsId(manufacturer.TrustedPartsId);

            return await _catalog.ExecuteAsync(async () =>
            {
                _context.Manufacturer.Add(manufacturer);
                await _context.SaveChangesAsync(cancellationToken);
                await _catalog.RecordAsync(
                    "manufacturer",
                    manufacturer.Id!.Value,
                    "create",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Create manufacturer {manufacturer.Name}"),
                    "web-ui",
                    cancellationToken);
                return CreatedAtAction("GetManufacturer", new { id = manufacturer.Id }, manufacturer);
            }, cancellationToken);
        }

        // DELETE: api/manufacturer/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteManufacturer(int id, CancellationToken cancellationToken)
        {
            bool found = false;
            await _catalog.ExecuteAsync(async () =>
            {
                Manufacturer? manufacturer = await _context.Manufacturer.FindAsync(new object[] { id }, cancellationToken);
                if (manufacturer == null)
                {
                    return 0;
                }

                found = true;
                await _catalog.RecordDeleteAsync(
                    "manufacturer",
                    id,
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Delete manufacturer {manufacturer.Name}"),
                    "web-ui",
                    cancellationToken);
                _context.Manufacturer.Remove(manufacturer);
                await _context.SaveChangesAsync(cancellationToken);
                return 0;
            }, cancellationToken);

            return found ? NoContent() : NotFound();
        }

        private bool ManufacturerExists(int id)
        {
            return _context.Manufacturer.Any(e => e.Id == id);
        }

        private static string? NormalizeTrustedPartsId(string? value)
        {
            string trimmed = (value ?? "").Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
