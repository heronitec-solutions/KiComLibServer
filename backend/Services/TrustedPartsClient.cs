using System.Security.Cryptography;

namespace KiComLibBackend.Services
{
    public sealed class TrustedPartsWriteRequest
    {
        public bool Enabled { get; set; }

        public string? CompanyId { get; set; }

        public string? ApiKey { get; set; }

        public bool ClearApiKey { get; set; }
    }

    /// <summary>
    /// Values the admin UI may see. The API key itself is never included.
    /// </summary>
    public sealed class TrustedPartsAccount
    {
        public bool Enabled { get; init; }

        public string CompanyId { get; init; } = "";

        public bool ApiKeySet { get; init; }
    }

    /// <summary>
    /// Decrypted credentials for later Inventory API calls.
    /// </summary>
    public sealed class TrustedPartsCredentials
    {
        public string CompanyId { get; init; } = "";

        public string ApiKey { get; init; } = "";
    }

    /// <summary>
    /// Holds the TrustedParts Inventory API connection.
    /// Search and catalog calls that use these credentials are added separately.
    /// </summary>
    public sealed class TrustedPartsClient
    {
        public const string HttpClientName = "trustedparts";
        public const string SearchEndpoint = "https://api.trustedparts.com/v2/search";

        private readonly AppSettingsService _settings;
        private readonly SecretProtector _secrets;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<TrustedPartsClient> _logger;

        public TrustedPartsClient(
            AppSettingsService settings,
            SecretProtector secrets,
            IHttpClientFactory http,
            ILogger<TrustedPartsClient> logger)
        {
            _settings = settings;
            _secrets = secrets;
            _http = http;
            _logger = logger;
        }

        public HttpClient CreateHttpClient()
        {
            return _http.CreateClient(HttpClientName);
        }

        public async Task<TrustedPartsAccount> AccountAsync(CancellationToken cancellationToken)
        {
            string? companyId = await _settings.FindAsync(AppSettingsService.TrustedPartsCompanyIdKey, cancellationToken);
            string? protectedKey = await _settings.FindAsync(AppSettingsService.TrustedPartsApiKeyKey, cancellationToken);
            return new TrustedPartsAccount
            {
                Enabled = await _settings.GetBoolAsync(AppSettingsService.TrustedPartsEnabledKey, false, cancellationToken),
                CompanyId = (companyId ?? "").Trim(),
                ApiKeySet = !string.IsNullOrWhiteSpace(protectedKey)
            };
        }

        /// <summary>
        /// Returns credentials only when the integration is turned on and both values are stored.
        /// </summary>
        public async Task<TrustedPartsCredentials?> CredentialsAsync(CancellationToken cancellationToken)
        {
            TrustedPartsAccount account = await AccountAsync(cancellationToken);
            if (!account.Enabled || account.CompanyId.Length == 0 || !account.ApiKeySet)
            {
                return null;
            }

            string? apiKey = await ReadApiKeyAsync(cancellationToken);
            if (string.IsNullOrEmpty(apiKey))
            {
                return null;
            }

            return new TrustedPartsCredentials
            {
                CompanyId = account.CompanyId,
                ApiKey = apiKey
            };
        }

        public async Task<string?> SaveAsync(TrustedPartsWriteRequest? request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return "TrustedParts settings are required";
            }

            string companyId = (request.CompanyId ?? "").Trim();
            string apiKey = (request.ApiKey ?? "").Trim();
            if (companyId.Length > 100)
            {
                return "Company ID must be at most 100 characters";
            }

            if (apiKey.Length > 500)
            {
                return "API key must be at most 500 characters";
            }

            if (companyId.Any(char.IsControl) || apiKey.Any(char.IsControl))
            {
                return "Company ID and API key cannot contain control characters";
            }

            bool keyStored = !string.IsNullOrWhiteSpace(
                await _settings.FindAsync(AppSettingsService.TrustedPartsApiKeyKey, cancellationToken));
            bool willHaveKey = apiKey.Length > 0 || (keyStored && !request.ClearApiKey);
            if (request.Enabled && (companyId.Length == 0 || !willHaveKey))
            {
                return "Enter a company ID and API key before turning TrustedParts on";
            }

            await _settings.SetAsync(AppSettingsService.TrustedPartsEnabledKey, request.Enabled ? "true" : "false", cancellationToken);
            await _settings.SetAsync(AppSettingsService.TrustedPartsCompanyIdKey, companyId, cancellationToken);
            if (apiKey.Length > 0)
            {
                await _settings.SetAsync(AppSettingsService.TrustedPartsApiKeyKey, _secrets.Protect(apiKey), cancellationToken);
            }
            else if (request.ClearApiKey)
            {
                await _settings.DeleteAsync(AppSettingsService.TrustedPartsApiKeyKey, cancellationToken);
            }

            return null;
        }

        private async Task<string?> ReadApiKeyAsync(CancellationToken cancellationToken)
        {
            string? protectedKey = await _settings.FindAsync(AppSettingsService.TrustedPartsApiKeyKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(protectedKey))
            {
                return null;
            }

            try
            {
                string apiKey = _secrets.Unprotect(protectedKey).Trim();
                return apiKey.Length == 0 ? null : apiKey;
            }
            catch (CryptographicException exception)
            {
                _logger.LogWarning(exception, "Stored TrustedParts API key could not be decrypted");
                return null;
            }
        }
    }
}
