using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;

namespace KiComLibBackend.Controllers
{
    [Route("api/v1/parts")]
    [ApiController]
    public class KiCadHttpPartController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly KiCadFieldBuilder _fields;

        public KiCadHttpPartController(ApplicationDbContext context, KiCadFieldBuilder fields)
        {
            _context = context;
            _fields = fields;
        }

        // GET: api/v1/parts/category/{categoryId}.json
        [HttpGet("category/{categoryId}.json")]
        public async Task<ActionResult<IEnumerable<KiCadHttpListPart>>> GetParts(int categoryId)
        {
            IQueryable<KiCadHttpListPart> query = _context.GetKiCadHttpListPartList();

            query = query.Where(m => m.LibraryId.Equals(categoryId));

            return await query.ToListAsync();
        }

        // GET: api/v1/parts/{partId}.json
        [HttpGet("{partId}.json")]
        public async Task<ActionResult<KiCadHttpPart>> GetPart(int partId)
        {
            var part = await _context.Part
                                    .Include(p => p.DatasheetNavigation)
                                    .Include(p => p.Library)
                                    .Include(p => p.Symbol)!.ThenInclude(s => s!.HeadRevision)
                                    .Include(p => p.Footprint)!.ThenInclude(f => f!.HeadRevision)
                                    .FirstOrDefaultAsync(p => p.Id == partId);

            if (part == null)
            {
                return NotFound();
            }

            return await _fields.BuildAsync(part, HttpContext.RequestAborted);
        }
    }
}
