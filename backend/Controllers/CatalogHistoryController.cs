using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace KiComLibBackend.Controllers
{
    [ApiController]
    public class CatalogHistoryController : ControllerBase
    {
        private readonly CatalogRevisionService _catalog;
        private readonly RevertService _revert;

        public CatalogHistoryController(CatalogRevisionService catalog, RevertService revert)
        {
            _catalog = catalog;
            _revert = revert;
        }

        [HttpGet("api/part/{id:int}/history")]
        public Task<IActionResult> PartHistory(int id, CancellationToken cancellationToken) => History("part", id, cancellationToken);

        [HttpGet("api/manufacturer/{id:int}/history")]
        public Task<IActionResult> ManufacturerHistory(int id, CancellationToken cancellationToken) => History("manufacturer", id, cancellationToken);

        [HttpGet("api/distributor/{id:int}/history")]
        public Task<IActionResult> DistributorHistory(int id, CancellationToken cancellationToken) => History("distributor", id, cancellationToken);

        [HttpGet("api/partnumber/{id:int}/history")]
        public Task<IActionResult> PartNumberHistory(int id, CancellationToken cancellationToken) => History("partnumber", id, cancellationToken);

        [HttpGet("api/ordernumber/{id:int}/history")]
        public Task<IActionResult> OrderNumberHistory(int id, CancellationToken cancellationToken) => History("ordernumber", id, cancellationToken);

        [HttpGet("api/part/{id:int}/revision/{revisionNo:int}")]
        public Task<IActionResult> PartRevision(int id, int revisionNo, CancellationToken cancellationToken) => Revision("part", id, revisionNo, cancellationToken);

        [HttpGet("api/manufacturer/{id:int}/revision/{revisionNo:int}")]
        public Task<IActionResult> ManufacturerRevision(int id, int revisionNo, CancellationToken cancellationToken) => Revision("manufacturer", id, revisionNo, cancellationToken);

        [HttpGet("api/distributor/{id:int}/revision/{revisionNo:int}")]
        public Task<IActionResult> DistributorRevision(int id, int revisionNo, CancellationToken cancellationToken) => Revision("distributor", id, revisionNo, cancellationToken);

        [HttpGet("api/partnumber/{id:int}/revision/{revisionNo:int}")]
        public Task<IActionResult> PartNumberRevision(int id, int revisionNo, CancellationToken cancellationToken) => Revision("partnumber", id, revisionNo, cancellationToken);

        [HttpGet("api/ordernumber/{id:int}/revision/{revisionNo:int}")]
        public Task<IActionResult> OrderNumberRevision(int id, int revisionNo, CancellationToken cancellationToken) => Revision("ordernumber", id, revisionNo, cancellationToken);

        [HttpGet("api/part/{id:int}/diff")]
        public Task<IActionResult> PartDiff(int id, [FromQuery] int from, [FromQuery] int to, CancellationToken cancellationToken) => Diff("part", id, from, to, cancellationToken);

        [HttpGet("api/manufacturer/{id:int}/diff")]
        public Task<IActionResult> ManufacturerDiff(int id, [FromQuery] int from, [FromQuery] int to, CancellationToken cancellationToken) => Diff("manufacturer", id, from, to, cancellationToken);

        [HttpGet("api/distributor/{id:int}/diff")]
        public Task<IActionResult> DistributorDiff(int id, [FromQuery] int from, [FromQuery] int to, CancellationToken cancellationToken) => Diff("distributor", id, from, to, cancellationToken);

        [HttpGet("api/partnumber/{id:int}/diff")]
        public Task<IActionResult> PartNumberDiff(int id, [FromQuery] int from, [FromQuery] int to, CancellationToken cancellationToken) => Diff("partnumber", id, from, to, cancellationToken);

        [HttpGet("api/ordernumber/{id:int}/diff")]
        public Task<IActionResult> OrderNumberDiff(int id, [FromQuery] int from, [FromQuery] int to, CancellationToken cancellationToken) => Diff("ordernumber", id, from, to, cancellationToken);

        [HttpPost("api/{entity:regex(^(part|manufacturer|distributor|partnumber|ordernumber|symbol|footprint|model3d|datasheet|template)$)}/{id:int}/revert")]
        public async Task<IActionResult> Revert(string entity, int id, [FromBody] KiComLibBackend.Models.RevertRequest? request, CancellationToken cancellationToken)
        {
            if (request?.RevisionNo == null)
            {
                return BadRequest(new { error = "revisionNo is required. Use 0 to undo a create that is still current." });
            }

            int revisionNo = request.RevisionNo.Value;
            request = CommitController.WithAccountAuthor(HttpContext, request);
            RevertOutcome outcome = await _revert.RestoreAsync(entity, id, revisionNo, request, cancellationToken);
            return CommitController.RevertResult(outcome);
        }

        private async Task<IActionResult> History(string entity, int id, CancellationToken cancellationToken)
        {
            if (!await _catalog.KnownAsync(entity, id, cancellationToken))
            {
                return NotFound();
            }

            return Ok(await _catalog.HistoryAsync(entity, id, cancellationToken));
        }

        private async Task<IActionResult> Revision(string entity, int id, int revisionNo, CancellationToken cancellationToken)
        {
            object? revision = await _catalog.RevisionAsync(entity, id, revisionNo, cancellationToken);
            return revision == null ? NotFound() : Ok(revision);
        }

        private async Task<IActionResult> Diff(string entity, int id, int from, int to, CancellationToken cancellationToken)
        {
            object? diff = await _catalog.DiffAsync(entity, id, from, to, cancellationToken);
            return diff == null ? NotFound() : Ok(diff);
        }
    }
}
