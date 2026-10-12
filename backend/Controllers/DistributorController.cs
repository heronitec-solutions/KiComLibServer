using KiComLibBackend.Models;
using KiComLibBackend.Data;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DistributorController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CatalogRevisionService _catalog;

        public DistributorController(ApplicationDbContext context, CatalogRevisionService catalog)
        {
            _context = context;
            _catalog = catalog;
        }

        // GET: api/distributor
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Distributor>>> GetDistributors([FromQuery] string _sort = "id", [FromQuery] string _order = "asc")
        {
            IQueryable<Distributor> query = _context.Distributor;

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
                case "isfavorite":
                    query = _order == "asc" ? query.OrderBy(m => m.IsFavorite) : query.OrderByDescending(m => m.IsFavorite);
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

        // GET: api/distributor/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Distributor>> GetDistributor(int id)
        {
            var distributor = await _context.Distributor.FindAsync(id);

            if (distributor == null)
            {
                return NotFound();
            }

            return distributor;
        }

        // PUT: api/distributor/5
        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateDistributor(int id, Distributor distributor, CancellationToken cancellationToken)
        {
            if (id != distributor.Id)
            {
                return BadRequest();
            }

            distributor.TrustedPartsId = NormalizeTrustedPartsId(distributor.TrustedPartsId);

            return await _catalog.ExecuteAsync<IActionResult>(async () =>
            {
                _context.Entry(distributor).State = EntityState.Modified;
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DistributorExists(id))
                    {
                        return NotFound();
                    }

                    throw;
                }

                await _catalog.RecordAsync(
                    "distributor",
                    id,
                    "update",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Update distributor {distributor.Name}"),
                    "web-ui",
                    cancellationToken);
                return NoContent();
            }, cancellationToken);
        }

        // POST: api/distributor
        [HttpPost]
        public async Task<ActionResult<Distributor>> CreateDistributor(Distributor distributor, CancellationToken cancellationToken)
        {
            distributor.Id = null;
            distributor.TrustedPartsId = NormalizeTrustedPartsId(distributor.TrustedPartsId);

            return await _catalog.ExecuteAsync(async () =>
            {
                _context.Distributor.Add(distributor);
                await _context.SaveChangesAsync(cancellationToken);
                await _catalog.RecordAsync(
                    "distributor",
                    distributor.Id!.Value,
                    "create",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Create distributor {distributor.Name}"),
                    "web-ui",
                    cancellationToken);
                return CreatedAtAction("GetDistributor", new { id = distributor.Id }, distributor);
            }, cancellationToken);
        }

        // DELETE: api/distributor/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDistributor(int id, CancellationToken cancellationToken)
        {
            bool found = false;
            await _catalog.ExecuteAsync(async () =>
            {
                Distributor? distributor = await _context.Distributor.FindAsync(new object[] { id }, cancellationToken);
                if (distributor == null)
                {
                    return 0;
                }

                found = true;
                await _catalog.RecordDeleteAsync(
                    "distributor",
                    id,
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Delete distributor {distributor.Name}"),
                    "web-ui",
                    cancellationToken);
                _context.Distributor.Remove(distributor);
                await _context.SaveChangesAsync(cancellationToken);
                return 0;
            }, cancellationToken);

            return found ? NoContent() : NotFound();
        }

        private bool DistributorExists(int id)
        {
            return _context.Distributor.Any(e => e.Id == id);
        }

        private static string? NormalizeTrustedPartsId(string? value)
        {
            string trimmed = (value ?? "").Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
