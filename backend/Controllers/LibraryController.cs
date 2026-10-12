using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KiComLibBackend.Data;
using KiComLibBackend.Models;

namespace KiComLibBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LibraryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LibraryController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/library
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Library>>> GetLibraries(
            [FromQuery] string _sort = "id",
            [FromQuery] string _order = "asc",
            [FromQuery] string? _filterby = null,
            [FromQuery] string? _filter = null)
        {
            IQueryable<Library> query = _context.Library;

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
                case "filename":
                    query = _order == "asc" ? query.OrderBy(m => m.Filename) : query.OrderByDescending(m => m.Filename);
                    break;
                case "description":
                    query = _order == "asc" ? query.OrderBy(m => m.Description) : query.OrderByDescending(m => m.Description);
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

            if (_filterby != null && _filter != null)
            {
                query = query.Where(m => EF.Property<string>(m, _filterby) == _filter);
            }

            return await query.ToListAsync();
        }

        // GET: api/library/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Library>> GetLibrary(int id)
        {
            var library = await _context.Library.FindAsync(id);

            if (library == null)
            {
                return NotFound();
            }

            return library;
        }

        // POST: api/library
        [HttpPost]
        public async Task<ActionResult<Library>> PostLibrary(Library library)
        {
            library.Id = null; // Set the ID to 0 to indicate a new            

            _context.Library.Add(library);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetLibrary", new { id = library.Id }, library);
        }

        // PUT: api/library/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutLibrary(int id, Library library)
        {
            if (id != library.Id)
            {
                return BadRequest();
            }

            _context.Entry(library).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Library.Any(e => e.Id == id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/library/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLibrary(int id)
        {
            var library = await _context.Library.FindAsync(id);
            if (library == null)
            {
                return NotFound();
            }

            _context.Library.Remove(library);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
