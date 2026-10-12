using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;

namespace KiComLibBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderNumberController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CatalogRevisionService _catalog;

        public OrderNumberController(ApplicationDbContext context, CatalogRevisionService catalog)
        {
            _context = context;
            _catalog = catalog;
        }

        // GET: api/ordernumber
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderNumber>>> GetOrderNumbers(
            [FromQuery] string _sort = "id",
            [FromQuery] string _order = "asc",
            [FromQuery] int? _part_number_id = null,
            [FromQuery] int? _distributor_id = null)
        {
            IQueryable<OrderNumber> query = _context.OrderNumber;

            // Filter by Part if provided
            if (_part_number_id.HasValue)
            {
                query = query.Where(m => m.PartNumberId == _part_number_id.Value);
            }

            // Filter by Manufacturer if provided
            if (_distributor_id.HasValue)
            {
                query = query.Where(m => m.DistributorId == _distributor_id.Value);
            }

            switch (_sort?.ToLower())
            {
                case "id":
                    query = _order == "asc" ? query.OrderBy(m => m.Id) : query.OrderByDescending(m => m.Id);
                    break;
                case "uuid":
                    query = _order == "asc" ? query.OrderBy(m => m.Uuid) : query.OrderByDescending(m => m.Uuid);
                    break;
                case "order_number":
                    query = _order == "asc" ? query.OrderBy(m => m.OrderNumberValue) : query.OrderByDescending(m => m.OrderNumberValue);
                    break;
                case "part_number_id":
                    query = _order == "asc" ? query.OrderBy(m => m.PartNumberId) : query.OrderByDescending(m => m.PartNumberId);
                    break;
                case "distributor_id":
                    query = _order == "asc" ? query.OrderBy(m => m.DistributorId) : query.OrderByDescending(m => m.DistributorId);
                    break;
                case "quantity_min":
                    query = _order == "asc" ? query.OrderBy(m => m.QuantityMin) : query.OrderByDescending(m => m.QuantityMin);
                    break;
                case "quantity_steps":
                    query = _order == "asc" ? query.OrderBy(m => m.QuantitySteps) : query.OrderByDescending(m => m.QuantitySteps);
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

            query.Include(o => o.PartNumber)
                 .Include(o => o.Distributor);

            return await query.ToListAsync();
        }

        // GET: api/ordernumber/5
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderNumber>> GetOrderNumber(int id)
        {
            var orderNumber = await _context.OrderNumber
                                        .Include(o => o.PartNumber)
                                        .Include(o => o.Distributor)
                                        .FirstOrDefaultAsync(o => o.Id == id);

            if (orderNumber == null)
            {
                return NotFound();
            }

            return orderNumber;
        }

        // POST: api/ordernumber
        [HttpPost]
        public async Task<ActionResult<OrderNumber>> PostOrderNumber(OrderNumber orderNumber, CancellationToken cancellationToken)
        {
            orderNumber.Id = null;

            return await _catalog.ExecuteAsync(async () =>
            {
                _context.OrderNumber.Add(orderNumber);
                await _context.SaveChangesAsync(cancellationToken);
                await _catalog.RecordAsync(
                    "ordernumber",
                    orderNumber.Id!.Value,
                    "create",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Create order number {orderNumber.OrderNumberValue}"),
                    "web-ui",
                    cancellationToken);
                return CreatedAtAction("GetOrderNumber", new { id = orderNumber.Id }, orderNumber);
            }, cancellationToken);
        }

        // PUT: api/ordernumber/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrderNumber(int id, OrderNumber orderNumber, CancellationToken cancellationToken)
        {
            if (id != orderNumber.Id)
            {
                return BadRequest();
            }

            return await _catalog.ExecuteAsync<IActionResult>(async () =>
            {
                _context.Entry(orderNumber).State = EntityState.Modified;
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.OrderNumber.Any(e => e.Id == id))
                    {
                        return NotFound();
                    }

                    throw;
                }

                await _catalog.RecordAsync(
                    "ordernumber",
                    id,
                    "update",
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Update order number {orderNumber.OrderNumberValue}"),
                    "web-ui",
                    cancellationToken);
                return NoContent();
            }, cancellationToken);
        }

        // DELETE: api/ordernumber/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrderNumber(int id, CancellationToken cancellationToken)
        {
            bool found = false;
            await _catalog.ExecuteAsync(async () =>
            {
                OrderNumber? orderNumber = await _context.OrderNumber.FindAsync(new object[] { id }, cancellationToken);
                if (orderNumber == null)
                {
                    return 0;
                }

                found = true;
                await _catalog.RecordDeleteAsync(
                    "ordernumber",
                    id,
                    CatalogKinds.Author(Request),
                    CatalogKinds.Message(Request, $"Delete order number {orderNumber.OrderNumberValue}"),
                    "web-ui",
                    cancellationToken);
                _context.OrderNumber.Remove(orderNumber);
                await _context.SaveChangesAsync(cancellationToken);
                return 0;
            }, cancellationToken);

            return found ? NoContent() : NotFound();
        }
    }
}
