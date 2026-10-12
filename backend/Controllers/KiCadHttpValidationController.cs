using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KiComLibBackend.Data;
using KiComLibBackend.Models;

namespace KiComLibBackend.Controllers
{
    [Route("api/v1")]
    [ApiController]
    public class KiCadHttpValidationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public KiCadHttpValidationController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/v1
        [HttpGet]
        public async Task<ActionResult<KiCadHttpValidationData>> GetEndpointValidation()
        {
            var validationData = new KiCadHttpValidationData
            {
                Categories = "api/v1/categories",
                Parts = "api/v1/parts"
            };

            return validationData;
        }
    }
}
