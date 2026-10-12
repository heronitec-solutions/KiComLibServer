using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class DigiKeyClient
    {
        public const string HttpClientName = "digikey";
        public const string TokenUrl = "https://api.digikey.com/v1/oauth2/token";
        public const string KeywordUrl = "https://api.digikey.com/products/v4/search/keyword";

        private readonly IHttpClientFactory _http;
        private readonly ILogger<DigiKeyClient> _logger;
        private string? _accessToken;
        private string? _tokenClientId;

        public DigiKeyClient(IHttpClientFactory http, ILogger<DigiKeyClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DistributorProduct>> SearchKeywordAsync(
            DigiKeyAccess access,
            string query,
            CancellationToken cancellationToken)
        {
            string token = await AccessTokenAsync(access, cancellationToken);
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["Keywords"] = query,
                ["Limit"] = 25,
                ["Offset"] = 0
            });
            using HttpRequestMessage request = new(HttpMethod.Post, KeywordUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("X-DIGIKEY-Client-Id", access.ClientId);
            request.Headers.TryAddWithoutValidation("X-DIGIKEY-Locale", string.IsNullOrWhiteSpace(access.Locale) ? DistributorSettingsService.DefaultDigiKeyLocale : access.Locale);
            string response = await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.DigiKey,
                request,
                _logger,
                cancellationToken);
            try
            {
                return Parse(response);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Distributor {Provider} returned a response that could not be read", DistributorProviders.DigiKey);
                throw new DistributorUpstreamException(DistributorProviders.DigiKey, "The distributor request failed");
            }
        }

        public Task<IReadOnlyList<DistributorProduct>> SearchPartAsync(
            DigiKeyAccess access,
            string manufacturerPartNumber,
            CancellationToken cancellationToken)
        {
            return SearchKeywordAsync(access, manufacturerPartNumber, cancellationToken);
        }

        public static IReadOnlyList<DistributorProduct> Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement? products = DistributorJson.Array(document.RootElement, "Products");
            if (products == null)
            {
                return Array.Empty<DistributorProduct>();
            }

            List<DistributorProduct> found = new();
            foreach (JsonElement product in products.Value.EnumerateArray())
            {
                if (product.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                MapProduct(product, found);
            }

            return found;
        }

        private async Task<string> AccessTokenAsync(DigiKeyAccess access, CancellationToken cancellationToken)
        {
            if (_accessToken != null && _tokenClientId == access.ClientId)
            {
                return _accessToken;
            }

            using FormUrlEncodedContent content = new(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = access.ClientId,
                ["client_secret"] = access.ClientSecret
            });
            using HttpRequestMessage request = new(HttpMethod.Post, TokenUrl) { Content = content };
            string response = await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.DigiKey,
                request,
                _logger,
                cancellationToken);
            try
            {
                using JsonDocument document = JsonDocument.Parse(response);
                string? token = DistributorJson.Text(document.RootElement, "access_token");
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new DistributorUpstreamException(DistributorProviders.DigiKey, "The distributor request failed");
                }

                _accessToken = token;
                _tokenClientId = access.ClientId;
                return token;
            }
            catch (DistributorUpstreamException)
            {
                throw;
            }
            catch (JsonException)
            {
                throw new DistributorUpstreamException(DistributorProviders.DigiKey, "The distributor request failed");
            }
        }

        private static void MapProduct(JsonElement product, List<DistributorProduct> found)
        {
            JsonElement? variations = DistributorJson.Array(product, "ProductVariations");
            if (variations == null || variations.Value.GetArrayLength() == 0)
            {
                found.Add(MapOne(product, null));
            }
            else
            {
                foreach (JsonElement variation in variations.Value.EnumerateArray())
                {
                    found.Add(MapOne(product, variation.ValueKind == JsonValueKind.Object ? variation : null));
                }
            }

            foreach (string name in new[] { "AlternateProducts", "ProductAlternates", "Substitutions" })
            {
                JsonElement? alternates = DistributorJson.Array(product, name);
                if (alternates == null)
                {
                    continue;
                }

                foreach (JsonElement alternate in alternates.Value.EnumerateArray())
                {
                    if (alternate.ValueKind == JsonValueKind.Object)
                    {
                        found.Add(MapOne(alternate, null));
                    }
                }
            }
        }

        private static DistributorProduct MapOne(JsonElement product, JsonElement? variation)
        {
            string? status = Status(product);
            return DistributorProduct.Create(
                DistributorProviders.DigiKey,
                DistributorNames.DigiKey,
                Manufacturer(product),
                DistributorJson.Text(product, "ManufacturerProductNumber", "ManufacturerPartNumber"),
                Description(product),
                DistributorJson.Text(product, "DatasheetUrl"),
                DistributorJson.Text(product, "ProductUrl"),
                LifecycleMapper.Map(status),
                Package(product, variation),
                OrderNumber(product, variation),
                Quantity(product, variation, "MinimumOrderQuantity"),
                Steps(product, variation),
                DistributorJson.Text(product, "PhotoUrl"));
        }

        private static string? Manufacturer(JsonElement product)
        {
            if (DistributorJson.Try(product, "Manufacturer", out JsonElement manufacturer))
            {
                if (manufacturer.ValueKind == JsonValueKind.String)
                {
                    return DistributorJson.AsText(manufacturer);
                }

                if (manufacturer.ValueKind == JsonValueKind.Object)
                {
                    return DistributorJson.Text(manufacturer, "Name");
                }
            }

            return DistributorJson.Text(product, "ManufacturerName");
        }

        private static string? Description(JsonElement product)
        {
            if (!DistributorJson.Try(product, "Description", out JsonElement description))
            {
                return DistributorJson.Text(product, "ProductDescription", "DetailedDescription");
            }

            if (description.ValueKind == JsonValueKind.String)
            {
                return DistributorJson.AsText(description);
            }

            if (description.ValueKind == JsonValueKind.Object)
            {
                return DistributorJson.Text(description, "ProductDescription", "DetailedDescription");
            }

            return null;
        }

        private static string? Status(JsonElement product)
        {
            if (!DistributorJson.Try(product, "ProductStatus", out JsonElement status))
            {
                return DistributorJson.Text(product, "Status");
            }

            if (status.ValueKind == JsonValueKind.String)
            {
                return DistributorJson.AsText(status);
            }

            if (status.ValueKind == JsonValueKind.Object)
            {
                return DistributorJson.Text(status, "Status");
            }

            return null;
        }

        private static string? OrderNumber(JsonElement product, JsonElement? variation)
        {
            if (variation != null)
            {
                string? fromVariation = DistributorJson.Text(variation.Value, "DigiKeyProductNumber");
                if (fromVariation != null)
                {
                    return fromVariation;
                }
            }

            return DistributorJson.Text(product, "DigiKeyProductNumber");
        }

        private static int? Quantity(JsonElement product, JsonElement? variation, string name)
        {
            if (variation != null)
            {
                int? fromVariation = DistributorJson.Int(variation.Value, name);
                if (fromVariation != null)
                {
                    return fromVariation;
                }
            }

            return DistributorJson.Int(product, name);
        }

        private static int? Steps(JsonElement product, JsonElement? variation)
        {
            return Quantity(product, variation, "QuantityMultiple") ?? Quantity(product, variation, "StandardPackage");
        }

        private static string? Package(JsonElement product, JsonElement? variation)
        {
            string? named = PackageTypeName(variation) ?? PackageTypeName(product);
            if (named != null)
            {
                return named;
            }

            JsonElement? parameters = DistributorJson.Array(product, "Parameters");
            if (parameters == null)
            {
                return null;
            }

            foreach (JsonElement parameter in parameters.Value.EnumerateArray())
            {
                if (parameter.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? label = DistributorJson.Text(parameter, "ParameterText", "Parameter");
                if (label != null && label.Contains("package", StringComparison.OrdinalIgnoreCase))
                {
                    return DistributorJson.Text(parameter, "ValueText", "Value");
                }
            }

            return null;
        }

        private static string? PackageTypeName(JsonElement? element)
        {
            if (element == null || !DistributorJson.Try(element.Value, "PackageType", out JsonElement package))
            {
                return null;
            }

            if (package.ValueKind == JsonValueKind.String)
            {
                return DistributorJson.AsText(package);
            }

            if (package.ValueKind == JsonValueKind.Object)
            {
                return DistributorJson.Text(package, "Name");
            }

            return null;
        }
    }
}
