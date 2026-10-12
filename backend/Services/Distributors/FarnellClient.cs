using System.Text.Json;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class FarnellClient
    {
        public const string HttpClientName = "farnell";
        public const string CatalogUrl = "https://api.element14.com/catalog/products";

        private readonly IHttpClientFactory _http;
        private readonly ILogger<FarnellClient> _logger;

        public FarnellClient(IHttpClientFactory http, ILogger<FarnellClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DistributorProduct>> SearchAsync(
            FarnellAccess access,
            string query,
            CancellationToken cancellationToken)
        {
            string url = SearchUrl(access.Store, access.ApiKey, query);
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            string response = await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.Farnell,
                request,
                _logger,
                cancellationToken);
            try
            {
                IReadOnlyList<DistributorProduct> products = Parse(response);
                if (products.Count == 0 && IsError(response))
                {
                    throw new DistributorUpstreamException(DistributorProviders.Farnell, "The distributor request failed");
                }

                return products;
            }
            catch (DistributorUpstreamException)
            {
                throw;
            }
            catch (JsonException)
            {
                _logger.LogWarning("Distributor {Provider} returned a response that could not be read", DistributorProviders.Farnell);
                throw new DistributorUpstreamException(DistributorProviders.Farnell, "The distributor request failed");
            }
        }

        public static string SearchUrl(string store, string apiKey, string query)
        {
            return CatalogUrl
                + "?term=any:" + Uri.EscapeDataString(query)
                + "&storeInfo.id=" + Uri.EscapeDataString(store)
                + "&resultsSettings.offset=0"
                + "&resultsSettings.numberOfResults=25"
                + "&resultsSettings.responseGroup=medium"
                + "&callInfo.omitXmlSchema=false"
                + "&callInfo.responseDataFormat=JSON"
                + "&callInfo.apiKey=" + Uri.EscapeDataString(apiKey);
        }

        public static IReadOnlyList<DistributorProduct> Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement? products = Products(document.RootElement);
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

                found.Add(Map(product));
            }

            return found;
        }

        public static bool IsError(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (Products(root) != null)
                {
                    return false;
                }

                return DistributorJson.Try(root, "ErrorMessage", out _)
                    || DistributorJson.Try(root, "Error", out _)
                    || DistributorJson.Try(root, "errorMessage", out _);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        private static JsonElement? Products(JsonElement root)
        {
            foreach (string wrapper in new[] { "manufacturerPartNumberSearchReturn", "keywordSearchReturn" })
            {
                if (DistributorJson.Try(root, wrapper, out JsonElement container))
                {
                    JsonElement? products = DistributorJson.Array(container, "products");
                    if (products != null)
                    {
                        return products;
                    }
                }
            }

            return DistributorJson.Array(root, "products");
        }

        private static DistributorProduct Map(JsonElement product)
        {
            string? datasheet = null;
            JsonElement? sheets = DistributorJson.Array(product, "datasheets");
            if (sheets != null)
            {
                foreach (JsonElement sheet in sheets.Value.EnumerateArray())
                {
                    if (sheet.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    datasheet = DistributorJson.Text(sheet, "url");
                    if (datasheet != null)
                    {
                        break;
                    }
                }
            }

            int? steps = DistributorJson.Int(product, "packSize");
            if (steps is null or <= 0)
            {
                steps = DistributorJson.Int(product, "orderMultiples");
            }

            return DistributorProduct.Create(
                DistributorProviders.Farnell,
                DistributorNames.Farnell,
                DistributorJson.Text(product, "brandName", "vendorName"),
                DistributorJson.Text(product, "translatedManufacturerPartNumber", "manufacturerPartNumber"),
                DistributorJson.Text(product, "displayName"),
                datasheet,
                ProductUrl(product),
                Lifecycle(product),
                null,
                DistributorJson.Text(product, "sku"),
                DistributorJson.Int(product, "translatedMinimumOrderQuality", "translatedMinimumOrderQuantity"),
                steps);
        }

        private static string? ProductUrl(JsonElement product)
        {
            return DistributorJson.Text(product, "productURL", "productUrl", "url");
        }

        private static string? Lifecycle(JsonElement product)
        {
            JsonElement? attributes = DistributorJson.Array(product, "attributes");
            if (attributes == null)
            {
                return null;
            }

            foreach (JsonElement attribute in attributes.Value.EnumerateArray())
            {
                if (attribute.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? label = DistributorJson.Text(attribute, "attributeLabel", "label", "name");
                if (label == null)
                {
                    continue;
                }

                if (!label.Contains("lifecycle", StringComparison.OrdinalIgnoreCase)
                    && !label.Contains("product status", StringComparison.OrdinalIgnoreCase)
                    && !label.Contains("part status", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return LifecycleMapper.Map(DistributorJson.Text(attribute, "attributeValue", "value"));
            }

            return null;
        }
    }
}
