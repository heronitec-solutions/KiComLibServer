using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KiComLibBackend.Data;
using KiComLibBackend.Models;

namespace KiComLibBackend.Controllers
{
    [Route("api/v1/categories.json")]
    [ApiController]
    public class KiCadHttpCategoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public KiCadHttpCategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/v1/categories.json
        [HttpGet]
        public async Task<ActionResult<IEnumerable<KiCadHttpCategory>>> GetLibraries()
        {
            IQueryable<KiCadHttpCategory> query = _context.GetKiCadHttpCategoryList();

            return await query.ToListAsync();
        }
    }
}
