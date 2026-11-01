using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class ArrowClient
    {
        public const string HttpClientName = "arrow";
        public const string TokenUrl = "https://api.arrow.com/itemservice/v4/en/search/token";
        public const string ListUrl = "https://api.arrow.com/itemservice/v4/en/search/list";

        /// <summary>
        /// Search By List accepts many parts. The response itself returns at most <see cref="ListRowLimit"/> rows,
        /// so each availability call stays inside that page.
        /// </summary>
        public const int PartBatchLimit = 10;

        public const int ListRowLimit = 25;

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
                DescribeFailure,
                cancellationToken);
            return ParseProducts(response);
        }

        /// <summary>
        /// Looks up many manufacturer part numbers with Search By List. Each call carries up to
        /// <see cref="PartBatchLimit"/> numbers so a rate limit covers more of the BOM.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>>> SearchPartsAsync(
            ArrowAccess access,
            IReadOnlyList<string> partNumbers,
            CancellationToken cancellationToken)
        {
            Dictionary<string, IReadOnlyList<DistributorProduct>> found = new(StringComparer.OrdinalIgnoreCase);
            foreach (IReadOnlyList<string> batch in PackQueries(partNumbers))
            {
                using HttpRequestMessage request = new(HttpMethod.Get, ListRequestUrl(access, batch));
                string response = await DistributorHttp.SendAsync(
                    _http,
                    HttpClientName,
                    DistributorProviders.Arrow,
                    request,
                    _logger,
                    DescribeFailure,
                    cancellationToken);
                foreach (KeyValuePair<string, IReadOnlyList<DistributorProduct>> pair in Assign(response, batch))
                {
                    found[pair.Key] = pair.Value;
                }
            }

            return found;
        }

        public static IReadOnlyList<IReadOnlyList<string>> PackQueries(IReadOnlyList<string> partNumbers)
        {
            return PartBatches.Chunk(partNumbers, PartBatchLimit);
        }

        public static string ListRequestUrl(ArrowAccess access, IReadOnlyList<string> partNumbers)
        {
            var payload = new Dictionary<string, object>
            {
                ["request"] = new Dictionary<string, object>
                {
                    ["login"] = access.Login,
                    ["apikey"] = access.ApiKey,
                    ["remoteIp"] = RemoteAddress(),
                    ["useExact"] = true,
                    ["parts"] = partNumbers
                        .Select(part => new Dictionary<string, string> { ["partNum"] = part.Trim() })
                        .ToList()
                }
            };
            return ListUrl
                + "?req=" + Uri.EscapeDataString(JsonSerializer.Serialize(payload))
                + "&fmt=json&rows=" + ListRowLimit;
        }

        public static IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>> Assign(
            string json,
            IReadOnlyList<string> queries)
        {
            if (IsError(json))
            {
                throw new DistributorUpstreamException(
                    DistributorProviders.Arrow,
                    DescribeFailure(200, json) ?? "The distributor request failed");
            }

            Dictionary<string, List<DistributorProduct>> assigned = new(StringComparer.OrdinalIgnoreCase);
            foreach (string query in queries)
            {
                string trimmed = query.Trim();
                if (trimmed.Length > 0)
                {
                    assigned.TryAdd(trimmed, new List<DistributorProduct>());
                }
            }

            foreach (DistributorProduct product in ParseProducts(json))
            {
                string? key = queries.FirstOrDefault(query =>
                    Same(query, product.ManufacturerPartNumber) || Same(query, product.OrderNumber));
                if (key == null || !assigned.TryGetValue(key.Trim(), out List<DistributorProduct>? list))
                {
                    continue;
                }

                list.Add(product);
            }

            Dictionary<string, IReadOnlyList<DistributorProduct>> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<DistributorProduct>> pair in assigned)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        public static string? DescribeFailure(int statusCode, string? body)
        {
            return DistributorFailure.Describe(statusCode, DistributorFailure.ReadMessage(body));
        }

        public static bool IsError(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (ParseProducts(json).Count > 0)
                {
                    return false;
                }

                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (string name in new[] { "error", "Error", "fault", "Fault", "errorMessage", "ErrorMessage" })
                {
                    if (DistributorJson.Try(root, name, out _))
                    {
                        return true;
                    }
                }

                return DistributorFailure.IsLimit(200, DistributorFailure.ReadMessage(json));
            }
            catch (JsonException)
            {
                return true;
            }
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
                DescribeFailure,
                cancellationToken);
            string? token = ParseToken(response);
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Distributor {Provider} did not return a search token", DistributorProviders.Arrow);
                throw new DistributorUpstreamException(
                    DistributorProviders.Arrow,
                    DescribeFailure(200, response) ?? "The distributor request failed");
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
                DistributorJson.Int(part, "orderMultiple", "orderMultiples", "quantityMultiple", "mult"),
                quantityAvailable: AvailableQuantity(part));
        }

        /// <summary>
        /// Sums Arrow <c>fohQty</c> values under this part. Nested objects that are a different
        /// manufacturer part are left out so related parts are not counted as this part's stock.
        /// </summary>
        private static int? AvailableQuantity(JsonElement part)
        {
            string? own = DistributorJson.Text(part, "mfrPartNum", "manufacturerPartNumber", "originalPartNum");
            long total = 0;
            bool any = false;
            CollectStock(part, own, isRoot: true, ref total, ref any);
            if (!any)
            {
                return null;
            }

            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private static void CollectStock(JsonElement element, string? ownMpn, bool isRoot, ref long total, ref bool any)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CollectStock(item, ownMpn, false, ref total, ref any);
                }

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (!isRoot)
            {
                string? nested = DistributorJson.Text(element, "mfrPartNum", "manufacturerPartNumber", "originalPartNum");
                if (nested != null && ownMpn != null && !nested.Equals(ownMpn, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals("fohQty", StringComparison.OrdinalIgnoreCase))
                {
                    int? count = DistributorJson.Int(element, property.Name);
                    if (count is >= 0)
                    {
                        total += count.Value;
                        any = true;
                    }

                    continue;
                }

                CollectStock(property.Value, ownMpn, false, ref total, ref any);
            }
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

        private static string RemoteAddress()
        {
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    {
                        return address.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // Arrow only checks that remoteIp looks like an address.
            }

            return "127.0.0.1";
        }

        private static bool Same(string? left, string? right)
        {
            return string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
