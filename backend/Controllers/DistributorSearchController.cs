using KiComLibBackend.Services;
using KiComLibBackend.Services.Distributors;
using Microsoft.AspNetCore.Mvc;

namespace KiComLibBackend.Controllers
{
    public sealed class DistributorKeywordRequest
    {
        public string? Query { get; set; }

        public string? Provider { get; set; }
    }

    public sealed class DistributorAlternativesRequest
    {
        public string? ManufacturerPartNumber { get; set; }

        public string? Manufacturer { get; set; }
    }

    public sealed class DistributorOrdersRequest
    {
        public List<DistributorPartQuery>? PartNumbers { get; set; }
    }

    public sealed class DistributorDatasheetRequest
    {
        public string? Url { get; set; }

        public string? Filename { get; set; }
    }

    [Route("api/distributor-search")]
    [ApiController]
    public class DistributorSearchController : ControllerBase
    {
        private readonly DistributorSettingsService _settings;
        private readonly DistributorSearchService _search;
        private readonly DistributorDatasheetImporter _datasheets;
        private readonly ProductImageStore _images;

        public DistributorSearchController(
            DistributorSettingsService settings,
            DistributorSearchService search,
            DistributorDatasheetImporter datasheets,
            ProductImageStore images)
        {
            _settings = settings;
            _search = search;
            _datasheets = datasheets;
            _images = images;
        }

        [HttpGet("providers")]
        public async Task<IActionResult> Providers(CancellationToken cancellationToken)
        {
            DistributorAvailability availability = await _settings.AvailabilityAsync(cancellationToken);
            var providers = DistributorProviders.All.Select(provider => new
            {
                id = provider.Id,
                name = provider.Name,
                enabled = availability.IsUsable(provider.Id)
            });
            return Ok(new { providers, lookupProvider = availability.LookupProvider });
        }

        [HttpPost("keyword")]
        public async Task<IActionResult> Keyword([FromBody] DistributorKeywordRequest? request, CancellationToken cancellationToken)
        {
            KeywordSearchOutcome outcome = await _search.KeywordAsync(request?.Query, request?.Provider, cancellationToken);
            if (outcome.StatusCode == 400)
            {
                return BadRequest(new { error = outcome.Error });
            }

            if (outcome.StatusCode == 502)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = outcome.Error, provider = outcome.Provider });
            }

            return Ok(new { provider = outcome.Provider, results = outcome.Results });
        }

        [HttpPost("alternatives")]
        public async Task<IActionResult> Alternatives([FromBody] DistributorAlternativesRequest? request, CancellationToken cancellationToken)
        {
            MultiSearchOutcome outcome = await _search.AlternativesAsync(
                request?.ManufacturerPartNumber,
                request?.Manufacturer,
                cancellationToken);
            if (outcome.StatusCode == 400)
            {
                return BadRequest(new { error = outcome.Error });
            }

            return Ok(new { results = outcome.Results, errors = outcome.Errors });
        }

        [HttpPost("orders")]
        public async Task<IActionResult> Orders([FromBody] DistributorOrdersRequest? request, CancellationToken cancellationToken)
        {
            MultiSearchOutcome outcome = await _search.OrdersAsync(request?.PartNumbers, cancellationToken);
            if (outcome.StatusCode == 400)
            {
                return BadRequest(new { error = outcome.Error });
            }

            return Ok(new { results = outcome.Results, errors = outcome.Errors });
        }

        [HttpPost("datasheet")]
        public async Task<IActionResult> Datasheet([FromBody] DistributorDatasheetRequest? request, CancellationToken cancellationToken)
        {
            DatasheetImportOutcome outcome = await _datasheets.ImportAsync(
                request?.Url,
                request?.Filename,
                CallerContext.Current(HttpContext).Username,
                cancellationToken);
            if (outcome.StatusCode == 201)
            {
                return StatusCode(StatusCodes.Status201Created, new
                {
                    id = outcome.Id,
                    uuid = outcome.Uuid,
                    filename = outcome.Filename,
                    directory = outcome.Directory
                });
            }

            return BadRequest(new { error = outcome.Error });
        }

        [HttpPost("image")]
        public async Task<IActionResult> Image([FromBody] DistributorDatasheetRequest? request, CancellationToken cancellationToken)
        {
            ProductImageResult outcome = await _images.ImportAsync(request?.Url, cancellationToken);
            if (outcome.StatusCode == 201 && outcome.Hash != null)
            {
                return StatusCode(StatusCodes.Status201Created, new { hash = outcome.Hash, mediaType = outcome.MediaType });
            }

            return BadRequest(new { error = outcome.Error ?? "The product image could not be stored" });
        }
    }
}
