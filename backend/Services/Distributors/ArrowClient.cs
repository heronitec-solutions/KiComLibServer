using System.Text.Json;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class ArrowClient
    {
        public const string HttpClientName = "arrow";
        public const string TokenUrl = "https://api.arrow.com/itemservice/v4/en/search/token";
        public const string ListUrl = "https://api.arrow.com/itemservice/v4/en/search/list";

        private readonly IHttpClientFactory _http;
        private readonly ILogger<ArrowClient> _logger;
        private string? _searchToken;
        private string? _tokenLogin;

        public ArrowClient(IHttpClientFactory http, ILogger<ArrowClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DistributorProduct>> SearchAsync(
            ArrowAccess access,
            string query,
            CancellationToken cancellationToken)
        {
            string token = await SearchTokenAsync(access, cancellationToken);
            string url = ListUrl
                + "?search_token=" + Uri.EscapeDataString(token)
                + "&q=" + Uri.EscapeDataString(query)
                + "&page_size=25&page_number=1";
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            string response = await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.Arrow,
                request,
                _logger,
                cancellationToken);
            return ParseProducts(response);
        }

        public static string? ParseToken(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return FindToken(document.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static IReadOnlyList<DistributorProduct> ParseProducts(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                List<DistributorProduct> found = new();
                Walk(document.RootElement, found);
                return Dedup(found);
            }
            catch (JsonException)
            {
                return Array.Empty<DistributorProduct>();
            }
        }

        private async Task<string> SearchTokenAsync(ArrowAccess access, CancellationToken cancellationToken)
        {
            if (_searchToken != null && _tokenLogin == access.Login)
            {
                return _searchToken;
            }

            string url = TokenUrl
                + "?login=" + Uri.EscapeDataString(access.Login)
                + "&apikey=" + Uri.EscapeDataString(access.ApiKey);
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            string response = await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.Arrow,
                request,
                _logger,
                cancellationToken);
            string? token = ParseToken(response);
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Distributor {Provider} did not return a search token", DistributorProviders.Arrow);
                throw new DistributorUpstreamException(DistributorProviders.Arrow, "The distributor request failed");
            }

            _searchToken = token;
            _tokenLogin = access.Login;
            return token;
        }

        private static string? FindToken(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String || !IsTokenName(property.Name))
                    {
                        continue;
                    }

                    string? value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string? nested = FindToken(property.Value);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? nested = FindToken(item);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private static bool IsTokenName(string name)
        {
            return name.Equals("searchToken", StringComparison.OrdinalIgnoreCase)
                || name.Equals("search_token", StringComparison.OrdinalIgnoreCase)
                || name.Equals("token", StringComparison.OrdinalIgnoreCase);
        }

        private static void Walk(JsonElement element, List<DistributorProduct> found)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (IsPart(element))
                {
                    found.Add(Map(element));
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Walk(property.Value, found);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(item, found);
                }
            }
        }

        private static bool IsPart(JsonElement element)
        {
            return DistributorJson.Text(element, "partNum", "mfrPartNum", "manufacturerPartNumber", "originalPartNum") != null;
        }

        private static DistributorProduct Map(JsonElement part)
        {
            string? datasheet = Datasheet(part);
            return DistributorProduct.Create(
                DistributorProviders.Arrow,
                DistributorNames.Arrow,
                Manufacturer(part),
                DistributorJson.Text(part, "mfrPartNum", "manufacturerPartNumber", "originalPartNum"),
                DistributorJson.Text(part, "desc", "description"),
                datasheet,
                ProductUrl(part, datasheet),
                LifecycleMapper.Map(DistributorJson.Text(part, "lifeCycleStatus", "lifecycleStatus", "status", "partStatus")),
                Package(part),
                DistributorJson.Text(part, "partNum"),
                DistributorJson.Int(part, "minOrderQty", "minOrderQuantity", "minimumOrderQuantity", "minimumOrderQty"),
                DistributorJson.Int(part, "orderMultiple", "orderMultiples", "quantityMultiple", "mult"));
        }

        private static string? Manufacturer(JsonElement part)
        {
            if (DistributorJson.Try(part, "manufacturer", out JsonElement manufacturer))
            {
                if (manufacturer.ValueKind == JsonValueKind.String)
                {
                    return DistributorJson.AsText(manufacturer);
                }

                if (manufacturer.ValueKind == JsonValueKind.Object)
                {
                    return DistributorJson.Text(manufacturer, "mfrName", "name");
                }
            }

            return DistributorJson.Text(part, "mfrName", "manufacturerName");
        }

        private static string? Datasheet(JsonElement part)
        {
            foreach (JsonProperty property in part.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || IsPart(item))
                    {
                        continue;
                    }

                    string? type = DistributorJson.Text(item, "type", "docType", "documentType", "resourceType");
                    string? url = DistributorJson.Text(item, "uri", "url", "href");
                    if (url != null && type != null && type.Contains("datasheet", StringComparison.OrdinalIgnoreCase))
                    {
                        return url;
                    }
                }
            }

            return null;
        }

        private static string? ProductUrl(JsonElement part, string? datasheet)
        {
            string? url = DistributorJson.Text(part, "productUrl", "productURL", "buyUrl", "partUrl", "url");
            if (url == null || string.Equals(url, datasheet, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return url;
        }

        private static string? Package(JsonElement part)
        {
            if (DistributorJson.Try(part, "packageType", out JsonElement package) && package.ValueKind == JsonValueKind.Object)
            {
                return DistributorJson.Text(package, "name");
            }

            return DistributorJson.Text(part, "package", "pkg");
        }

        private static List<DistributorProduct> Dedup(List<DistributorProduct> products)
        {
            List<DistributorProduct> unique = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (DistributorProduct product in products)
            {
                string key = (product.OrderNumber ?? "") + "\n" + (product.ManufacturerPartNumber ?? "");
                if (key == "\n" || !seen.Add(key))
                {
                    continue;
                }

                unique.Add(product);
            }

            return unique;
        }
    }
}
