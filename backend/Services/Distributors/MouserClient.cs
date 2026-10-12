using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class MouserClient
    {
        public const string HttpClientName = "mouser";
        public const string KeywordUrl = "https://api.mouser.com/api/v1/search/keyword";
        public const string PartNumberUrl = "https://api.mouser.com/api/v1/search/partnumber";

        private readonly IHttpClientFactory _http;
        private readonly ILogger<MouserClient> _logger;

        public MouserClient(IHttpClientFactory http, ILogger<MouserClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        public Task<IReadOnlyList<DistributorProduct>> SearchKeywordAsync(string apiKey, string query, CancellationToken cancellationToken)
        {
            string url = KeywordUrl + "?apiKey=" + Uri.EscapeDataString(apiKey);
            var payload = new Dictionary<string, object>
            {
                ["SearchByKeywordRequest"] = new Dictionary<string, object>
                {
                    ["keyword"] = query,
                    ["records"] = 25,
                    ["startingRecord"] = 0,
                    ["searchOptions"] = "",
                    ["searchWithYourSignUpLanguage"] = "false"
                }
            };
            return PostAsync(url, payload, cancellationToken);
        }

        public Task<IReadOnlyList<DistributorProduct>> SearchPartAsync(string apiKey, string manufacturerPartNumber, CancellationToken cancellationToken)
        {
            string url = PartNumberUrl + "?apiKey=" + Uri.EscapeDataString(apiKey);
            var payload = new Dictionary<string, object>
            {
                ["SearchByPartRequest"] = new Dictionary<string, object>
                {
                    ["mouserPartNumber"] = manufacturerPartNumber,
                    ["partSearchOptions"] = ""
                }
            };
            return PostAsync(url, payload, cancellationToken);
        }

        public static IReadOnlyList<DistributorProduct> Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            List<DistributorProduct> products = new();
            CollectParts(root, products);
            if (products.Count == 0 && HasErrors(root))
            {
                throw new DistributorUpstreamException(DistributorProviders.Mouser, "Mouser rejected the search");
            }

            return products;
        }

        private async Task<IReadOnlyList<DistributorProduct>> PostAsync(
            string url,
            Dictionary<string, object> payload,
            CancellationToken cancellationToken)
        {
            string body = JsonSerializer.Serialize(payload);
            using HttpRequestMessage request = new(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            string response = await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.Mouser,
                request,
                _logger,
                cancellationToken);
            try
            {
                return Parse(response);
            }
            catch (DistributorUpstreamException)
            {
                throw;
            }
            catch (JsonException)
            {
                _logger.LogWarning("Distributor {Provider} returned a response that could not be read", DistributorProviders.Mouser);
                throw new DistributorUpstreamException(DistributorProviders.Mouser, "The distributor request failed");
            }
        }

        private static void CollectParts(JsonElement element, List<DistributorProduct> products)
        {
            JsonElement? parts = null;
            if (DistributorJson.Try(element, "SearchResults", out JsonElement results))
            {
                parts = DistributorJson.Array(results, "Parts");
            }

            parts ??= DistributorJson.Array(element, "Parts");
            if (parts == null)
            {
                return;
            }

            foreach (JsonElement part in parts.Value.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                MapPart(part, products);
            }
        }

        private static void MapPart(JsonElement part, List<DistributorProduct> products)
        {
            string? mpn = DistributorJson.Text(part, "ManufacturerPartNumber");
            string? lifecycle = LifecycleMapper.Map(
                DistributorJson.Text(part, "LifecycleStatus"),
                DistributorJson.Flag(part, "IsDiscontinued"));
            // Datasheet and product photo stay empty. Mouser does not offer a download for them.
            products.Add(DistributorProduct.Create(
                DistributorProviders.Mouser,
                DistributorNames.Mouser,
                DistributorJson.Text(part, "Manufacturer"),
                mpn,
                DistributorJson.Text(part, "Description"),
                null,
                DistributorJson.Text(part, "ProductDetailUrl"),
                lifecycle,
                null,
                DistributorJson.Text(part, "MouserPartNumber"),
                DistributorJson.Int(part, "Min"),
                DistributorJson.Int(part, "Mult")));

            string? replacement = ReplacementMpn(part);
            if (replacement != null && !Same(replacement, mpn))
            {
                products.Add(DistributorProduct.Create(
                    DistributorProviders.Mouser,
                    DistributorNames.Mouser,
                    null,
                    replacement,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null));
            }

            JsonElement? alternates = DistributorJson.Array(part, "AlternatePackagings");
            if (alternates == null)
            {
                return;
            }

            foreach (JsonElement alternate in alternates.Value.EnumerateArray())
            {
                if (alternate.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? alternateMpn = DistributorJson.Text(alternate, "APMfrPN", "ManufacturerPartNumber");
                if (alternateMpn == null || Same(alternateMpn, mpn))
                {
                    continue;
                }

                products.Add(DistributorProduct.Create(
                    DistributorProviders.Mouser,
                    DistributorNames.Mouser,
                    DistributorJson.Text(alternate, "Manufacturer"),
                    alternateMpn,
                    null,
                    null,
                    null,
                    null,
                    DistributorJson.Text(alternate, "AlternatePackaging"),
                    null,
                    null,
                    null));
            }
        }

        private static string? ReplacementMpn(JsonElement part)
        {
            if (!DistributorJson.Try(part, "SuggestedReplacement", out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                return DistributorJson.Text(value, "ManufacturerPartNumber", "MouserPartNumber");
            }

            return null;
        }

        private static bool HasErrors(JsonElement root)
        {
            JsonElement? errors = DistributorJson.Array(root, "Errors");
            return errors != null && errors.Value.GetArrayLength() > 0;
        }

        private static bool Same(string left, string? right)
        {
            return right != null && left.Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
