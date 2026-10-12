using KiComLibBackend.Models;
using KiComLibBackend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LibraryFileController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LibraryFileController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/libraryfile
        [HttpGet]
        public async Task<ActionResult<IEnumerable<LibraryFile>>> GetLibraryFiles(
            [FromQuery] string _sort = "id",
            [FromQuery] string _order = "asc",
            [FromQuery] string? _filterby = null,
            [FromQuery] string? _filter = null)
        {
            IQueryable<LibraryFile> query = _context.LibraryFile;

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
                case "type":
                    query = _order == "asc" ? query.OrderBy(m => m.Type) : query.OrderByDescending(m => m.Type);
                    break;
                case "comment":
                    query = _order == "asc" ? query.OrderBy(m => m.Comment) : query.OrderByDescending(m => m.Comment);
                    break;
                case "filename":
                    query = _order == "asc" ? query.OrderBy(m => m.Filename) : query.OrderByDescending(m => m.Filename);
                    break;
                case "description":
                    query = _order == "asc" ? query.OrderBy(m => m.Description) : query.OrderByDescending(m => m.Description);
                    break;
                case "version":
                    query = _order == "asc" ? query.OrderBy(m => m.Version) : query.OrderByDescending(m => m.Version);
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
                switch (_filterby?.ToLower())
                {
                    case "type":
                        if (_filter.ToLower() == "0" || _filter.ToLower() == "footprint")
                            query = query.Where(m => m.Type.Equals(0));
                        else if (_filter.ToLower() == "1" || _filter.ToLower() == "symbol")
                            query = query.Where(m => m.Type.Equals(1));
                        break;
                    default:
                        break; // No filtering applied
                }
            }

            return await query.ToListAsync();
        }

        // GET: api/libraryfile/5
        [HttpGet("{id}")]
        public async Task<ActionResult<LibraryFile>> GetLibraryFile(int id)
        {
            var libraryFile = await _context.LibraryFile.FindAsync(id);

            if (libraryFile == null)
            {
                return NotFound();
            }

            return libraryFile;
        }

        // POST: api/libraryfile
        [HttpPost]
        public async Task<ActionResult<LibraryFile>> PostLibraryFile(LibraryFile libraryFile)
        {
            libraryFile.Id = null; // Set the ID to 0 to indicate a new          

            if (libraryFile.Type < 0 || libraryFile.Type > 1)
            {
                return BadRequest("Invalid type. Type must be 0 (Footprint) or 1 (Symbol).");
            }

            _context.LibraryFile.Add(libraryFile);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetLibraryFile", new { id = libraryFile.Id }, libraryFile);
        }

        // PUT: api/libraryfile/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutLibraryFile(int id, LibraryFile libraryFile)
        {
            if (id != libraryFile.Id)
            {
                return BadRequest("IDs do not match.");
            }

            if (libraryFile.Type < 0 || libraryFile.Type > 1)
            {
                return BadRequest("Invalid type. Type must be 0 (Footprint) or 1 (Symbol).");
            }

            _context.Entry(libraryFile).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.LibraryFile.Any(e => e.Id == id))
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

        // DELETE: api/libraryfile/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLibraryFile(int id)
        {
            var libraryFile = await _context.LibraryFile.FindAsync(id);
            if (libraryFile == null)
            {
                return NotFound();
            }

            _context.LibraryFile.Remove(libraryFile);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
