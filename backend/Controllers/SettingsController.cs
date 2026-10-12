using System.Net.Http.Headers;
using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Services;
using KiComLibBackend.Services.Distributors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Controllers
{
    public class SettingsWriteRequest
    {
        public int SessionLifetimeDays { get; set; }

        public int? GitPollIntervalMinutes { get; set; }

        public int? GitWebhookPollIntervalMinutes { get; set; }

        public int? ProjectMaxCommitsPerSync { get; set; }

        public int? ProjectInitialTags { get; set; }

        public string? KicomlibLibraryNicknames { get; set; }
    }

    [Route("api/settings")]
    [ApiController]
    public class SettingsController : ControllerBase
    {
        private readonly AppSettingsService _settings;
        private readonly TrustedPartsClient _trustedParts;
        private readonly DistributorSettingsService _distributors;
        private readonly DistributorSearchService _distributorSearch;
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _environment;
        private readonly IHttpClientFactory _http;

        public SettingsController(
            AppSettingsService settings,
            TrustedPartsClient trustedParts,
            DistributorSettingsService distributors,
            DistributorSearchService distributorSearch,
            ApplicationDbContext db,
            IWebHostEnvironment environment,
            IHttpClientFactory http)
        {
            _settings = settings;
            _trustedParts = trustedParts;
            _distributors = distributors;
            _distributorSearch = distributorSearch;
            _db = db;
            _environment = environment;
            _http = http;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            return Ok(await PayloadAsync(cancellationToken));
        }

        [HttpPut]
        public async Task<IActionResult> Put([FromBody] SettingsWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            int days = request?.SessionLifetimeDays ?? 0;
            if (days < AppSettingsService.MinSessionLifetimeDays || days > AppSettingsService.MaxSessionLifetimeDays)
            {
                return BadRequest(new
                {
                    error = $"Session lifetime must be between {AppSettingsService.MinSessionLifetimeDays} and {AppSettingsService.MaxSessionLifetimeDays} days"
                });
            }

            string? rangeError = ValidateProjectSettings(request);
            if (rangeError != null)
            {
                return BadRequest(new { error = rangeError });
            }

            await _settings.SetSessionLifetimeDaysAsync(days, cancellationToken);
            if (request?.GitPollIntervalMinutes != null)
            {
                await _settings.SetAsync(AppSettingsService.PollIntervalKey, request.GitPollIntervalMinutes.Value.ToString(), cancellationToken);
            }

            if (request?.GitWebhookPollIntervalMinutes != null)
            {
                await _settings.SetAsync(AppSettingsService.WebhookPollIntervalKey, request.GitWebhookPollIntervalMinutes.Value.ToString(), cancellationToken);
            }

            if (request?.ProjectMaxCommitsPerSync != null)
            {
                await _settings.SetAsync(AppSettingsService.MaxCommitsKey, request.ProjectMaxCommitsPerSync.Value.ToString(), cancellationToken);
            }

            if (request?.ProjectInitialTags != null)
            {
                await _settings.SetAsync(AppSettingsService.InitialTagsKey, request.ProjectInitialTags.Value.ToString(), cancellationToken);
            }

            if (request?.KicomlibLibraryNicknames != null)
            {
                await _settings.SetAsync(AppSettingsService.LibraryNicknamesKey, request.KicomlibLibraryNicknames.Trim(), cancellationToken);
            }

            return Ok(await PayloadAsync(cancellationToken));
        }

        [HttpPut("trustedparts")]
        public async Task<IActionResult> PutTrustedParts([FromBody] TrustedPartsWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            string? error = await _trustedParts.SaveAsync(request, cancellationToken);
            if (error != null)
            {
                return BadRequest(new { error });
            }

            return Ok(await PayloadAsync(cancellationToken));
        }

        [HttpPut("distributors")]
        public async Task<IActionResult> PutDistributors([FromBody] DistributorSettingsWriteRequest? request, CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            string? error = await _distributors.SaveAsync(request, cancellationToken);
            if (error != null)
            {
                return BadRequest(new { error });
            }

            return Ok(await PayloadAsync(cancellationToken));
        }

        [HttpPost("distributors/test/{provider}")]
        public async Task<IActionResult> TestDistributor(
            string provider,
            [FromBody] DistributorSettingsWriteRequest? request,
            CancellationToken cancellationToken)
        {
            IActionResult? denied = RequireAdmin();
            if (denied != null)
            {
                return denied;
            }

            DistributorTestOutcome outcome = await _distributorSearch.TestAsync(provider, request, cancellationToken);
            if (outcome.StatusCode != 200)
            {
                return BadRequest(new { error = outcome.Error });
            }

            return Ok(new { ok = true, provider = outcome.Provider, message = outcome.Message });
        }

        private async Task<object> PayloadAsync(CancellationToken cancellationToken)
        {
            int schemaVersion = await _db.SchemaVersions.MaxAsync(s => (int?)s.Version, cancellationToken) ?? 0;
            (bool reachable, string? kicad) = await RunnerStatusAsync(cancellationToken);
            return new
            {
                sessionLifetimeDays = await _settings.SessionLifetimeDaysAsync(cancellationToken),
                version = ProductVersion.Read(_environment.ContentRootPath),
                httpLibraryVersion = ProductVersion.HttpLibraryApiVersion,
                apiVersion = ProductVersion.ApiVersion,
                schemaVersion,
                gitPollIntervalMinutes = await _settings.GetIntAsync(AppSettingsService.PollIntervalKey, 5, 1, 10080, cancellationToken),
                gitWebhookPollIntervalMinutes = await _settings.GetIntAsync(AppSettingsService.WebhookPollIntervalKey, 60, 1, 10080, cancellationToken),
                projectMaxCommitsPerSync = await _settings.GetIntAsync(AppSettingsService.MaxCommitsKey, 20, 1, 5000, cancellationToken),
                projectInitialTags = await _settings.GetIntAsync(AppSettingsService.InitialTagsKey, 50, 0, 5000, cancellationToken),
                kicomlibLibraryNicknames = await _settings.GetStringAsync(AppSettingsService.LibraryNicknamesKey, "KiComLib", cancellationToken),
                trustedParts = await _trustedParts.AccountAsync(cancellationToken),
                distributors = await _distributors.ViewAsync(cancellationToken),
                runner = new { reachable, kicad }
            };
        }

        private static string? ValidateProjectSettings(SettingsWriteRequest? request)
        {
            if (request == null)
            {
                return null;
            }

            if (request.GitPollIntervalMinutes != null && request.GitPollIntervalMinutes is < 1 or > 10080)
            {
                return "gitPollIntervalMinutes must be between 1 and 10080";
            }

            if (request.GitWebhookPollIntervalMinutes != null && request.GitWebhookPollIntervalMinutes is < 1 or > 10080)
            {
                return "gitWebhookPollIntervalMinutes must be between 1 and 10080";
            }

            if (request.ProjectMaxCommitsPerSync != null && request.ProjectMaxCommitsPerSync is < 1 or > 5000)
            {
                return "projectMaxCommitsPerSync must be between 1 and 5000";
            }

            if (request.ProjectInitialTags != null && request.ProjectInitialTags is < 0 or > 5000)
            {
                return "projectInitialTags must be between 0 and 5000";
            }

            if (request.KicomlibLibraryNicknames != null && (request.KicomlibLibraryNicknames.Trim().Length < 1 || request.KicomlibLibraryNicknames.Length > 200))
            {
                return "kicomlibLibraryNicknames must be between 1 and 200 characters";
            }

            return null;
        }

        private async Task<(bool Reachable, string? Kicad)> RunnerStatusAsync(CancellationToken cancellationToken)
        {
            string? url = Environment.GetEnvironmentVariable("KICAD_RUNNER_URL");
            if (string.IsNullOrWhiteSpace(url))
            {
                return (false, null);
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                HttpClient client = _http.CreateClient();
                using HttpRequestMessage request = new(HttpMethod.Get, url.TrimEnd('/') + "/health");
                string? token = Environment.GetEnvironmentVariable("KICAD_RUNNER_TOKEN");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                using HttpResponseMessage response = await client.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return (false, null);
                }

                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                string? kicad = document.RootElement.TryGetProperty("kicad", out JsonElement value) ? value.GetString() : null;
                return (true, kicad);
            }
            catch (Exception)
            {
                return (false, null);
            }
        }

        private IActionResult? RequireAdmin()
        {
            CallerContext caller = CallerContext.Current(HttpContext);
            if (caller.TokenRejected || !caller.IsAuthenticated)
            {
                return Unauthorized(new { error = caller.TokenRejected ? "Invalid API token" : "Authentication required" });
            }

            if (!caller.IsAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator permission required" });
            }

            return null;
        }
    }
}
