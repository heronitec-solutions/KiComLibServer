using System.Text.Json.Serialization;

namespace KiComLibBackend.Services.Distributors
{
    /// <summary>
    /// One distributor hit. Order quantities are set only when an order number is present.
    /// </summary>
    public sealed class DistributorProduct
    {
        public string Provider { get; init; } = "";

        public string? Manufacturer { get; init; }

        public string? ManufacturerPartNumber { get; init; }

        public string? Description { get; init; }

        public string? DatasheetUrl { get; init; }

        public string? ProductUrl { get; init; }

        /// <summary>Absolute http(s) URL of a distributor product photo, when the search returned one.</summary>
        public string? ImageUrl { get; init; }

        public string? LifecycleStatus { get; init; }

        public string? Package { get; init; }

        public string? DistributorName { get; init; }

        public string? OrderNumber { get; init; }

        public int? QuantityMin { get; init; }

        public int? QuantitySteps { get; init; }

        [JsonIgnore]
        public int? SourceQuantityMin { get; init; }

        [JsonIgnore]
        public int? SourceQuantitySteps { get; init; }

        public static DistributorProduct Create(
            string provider,
            string distributorName,
            string? manufacturer,
            string? manufacturerPartNumber,
            string? description,
            string? datasheetUrl,
            string? productUrl,
            string? lifecycleStatus,
            string? package,
            string? orderNumber,
            int? quantityMin,
            int? quantitySteps,
            string? imageUrl = null)
        {
            string? order = Clean(orderNumber);
            int? sourceMin = quantityMin is > 0 ? quantityMin : null;
            int? sourceSteps = quantitySteps is > 0 ? quantitySteps : null;
            int? min = null;
            int? steps = null;
            if (order != null)
            {
                min = sourceMin ?? 1;
                steps = sourceSteps ?? 1;
            }

            return new DistributorProduct
            {
                Provider = provider,
                DistributorName = distributorName,
                Manufacturer = Clean(manufacturer),
                ManufacturerPartNumber = Clean(manufacturerPartNumber),
                Description = Clean(description),
                DatasheetUrl = Clean(datasheetUrl),
                ProductUrl = Clean(productUrl),
                ImageUrl = AbsoluteImageUrl(imageUrl),
                LifecycleStatus = NormalizeLifecycle(lifecycleStatus),
                Package = Clean(package),
                OrderNumber = order,
                QuantityMin = min,
                QuantitySteps = steps,
                SourceQuantityMin = sourceMin,
                SourceQuantitySteps = sourceSteps
            };
        }

        public DistributorProduct WithManufacturerPartNumber(string manufacturerPartNumber)
        {
            return Create(
                Provider,
                DistributorName ?? "",
                Manufacturer,
                manufacturerPartNumber,
                Description,
                DatasheetUrl,
                ProductUrl,
                LifecycleStatus,
                Package,
                OrderNumber,
                QuantityMin,
                QuantitySteps,
                ImageUrl);
        }

        /// <summary>
        /// Keeps an absolute http(s) photo URL. Protocol-relative URLs become https.
        /// A path is joined to <paramref name="siteOrigin"/> when that origin is http(s).
        /// </summary>
        public static string? AbsoluteImageUrl(string? value, string? siteOrigin = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value.Trim();
            if (text.StartsWith("//", StringComparison.Ordinal))
            {
                text = "https:" + text;
            }
            else if (text.StartsWith('/') && !string.IsNullOrWhiteSpace(siteOrigin))
            {
                text = siteOrigin.TrimEnd('/') + text;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
            {
                return null;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            return uri.AbsoluteUri;
        }

        private static string? NormalizeLifecycle(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value.Trim().ToLowerInvariant();
            if (text is "active" or "nrnd" or "ltb" or "eol")
            {
                return text;
            }

            return LifecycleMapper.Map(value);
        }

        private static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }
    }

    public sealed class DistributorProviderError
    {
        public required string Provider { get; init; }

        public required string Message { get; init; }
    }
}
