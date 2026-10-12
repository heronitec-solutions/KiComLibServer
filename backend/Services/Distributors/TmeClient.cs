using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class TmeClient
    {
        public const string HttpClientName = "tme";
        public const string TokenUrl = "https://api.tme.eu/auth/token";
        public const string SearchUrl = "https://api.tme.eu/products/search";
        public const string FilesUrl = "https://api.tme.eu/products/files";
        public const int DetailLimit = 10;

        private readonly IHttpClientFactory _http;
        private readonly ILogger<TmeClient> _logger;
        private string? _accessToken;
        private string? _tokenKey;
        private DateTimeOffset _tokenExpiresAt;

        public TmeClient(IHttpClientFactory http, ILogger<TmeClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DistributorProduct>> SearchAsync(
            TmeAccess access,
            string query,
            CancellationToken cancellationToken)
        {
            string token = await AccessTokenAsync(access, cancellationToken);
            string response = await GetAsync(SearchAddress(access, query), token, access.Language, cancellationToken);
            EnsureOk(response);
            List<DistributorProduct> products;
            try
            {
                products = ParseSearch(response, access.Country, access.Language).ToList();
            }
            catch (JsonException)
            {
                _logger.LogWarning("Distributor {Provider} returned a response that could not be read", DistributorProviders.Tme);
                throw new DistributorUpstreamException(DistributorProviders.Tme, "The distributor request failed");
            }

            List<string> symbols = products
                .Select(product => product.OrderNumber)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(DetailLimit)
                .ToList();
            if (symbols.Count > 0)
            {
                products = await MergeFilesAsync(access, token, products, symbols, cancellationToken);
            }

            return products;
        }

        public static IReadOnlyList<DistributorProduct> ParseSearch(string json, string? country = null, string? language = null)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement owner = document.RootElement;
            if (DistributorJson.Try(owner, "data", out JsonElement data) && data.ValueKind == JsonValueKind.Object)
            {
                owner = DistributorJson.Try(data, "products", out JsonElement products) && products.ValueKind == JsonValueKind.Object
                    ? products
                    : data;
            }

            JsonElement? list = DistributorJson.Array(owner, "elements");
            if (list == null)
            {
                return Array.Empty<DistributorProduct>();
            }

            List<DistributorProduct> found = new();
            foreach (JsonElement item in list.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                DistributorProduct? product = Map(item, country, language);
                if (product != null)
                {
                    found.Add(product);
                }
            }

            return found;
        }

        public static bool IsError(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                string? status = DistributorJson.Text(document.RootElement, "status");
                return status == null || !status.Equals("OK", StringComparison.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        public static IReadOnlyDictionary<string, string> ParseDatasheetUrls(string json, string? language = null)
        {
            Dictionary<string, string> urls = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> scores = new(StringComparer.OrdinalIgnoreCase);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement owner = document.RootElement;
            if (DistributorJson.Try(owner, "data", out JsonElement data) && data.ValueKind == JsonValueKind.Object)
            {
                owner = data;
            }

            JsonElement? elements = DistributorJson.Array(owner, "elements");
            if (elements == null)
            {
                return urls;
            }

            foreach (JsonElement item in elements.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? symbol = DistributorJson.Text(item, "symbol");
                if (symbol == null
                    || !DistributorJson.Try(item, "documents", out JsonElement documents)
                    || documents.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                JsonElement? docs = DistributorJson.Array(documents, "elements");
                if (docs == null)
                {
                    continue;
                }

                foreach (JsonElement doc in docs.Value.EnumerateArray())
                {
                    if (doc.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string? absolute = AbsoluteUrl(DistributorJson.Text(doc, "url"));
                    if (absolute == null || !IsPdf(absolute))
                    {
                        continue;
                    }

                    int score = DatasheetScore(doc, language);
                    if (!scores.TryGetValue(symbol, out int current) || score > current)
                    {
                        scores[symbol] = score;
                        urls[symbol] = absolute;
                    }
                }
            }

            return urls;
        }

        private async Task<List<DistributorProduct>> MergeFilesAsync(
            TmeAccess access,
            string token,
            List<DistributorProduct> products,
            List<string> symbols,
            CancellationToken cancellationToken)
        {
            try
            {
                string response = await GetAsync(FilesAddress(access, symbols), token, access.Language, cancellationToken);
                if (IsError(response))
                {
                    return products;
                }

                return ApplyDatasheets(products, ParseDatasheetUrls(response, access.Language));
            }
            catch (DistributorUpstreamException)
            {
                _logger.LogWarning("Distributor {Provider} datasheet lookup was not available", DistributorProviders.Tme);
                return products;
            }
            catch (JsonException)
            {
                return products;
            }
        }

        private async Task<string> AccessTokenAsync(TmeAccess access, CancellationToken cancellationToken)
        {
            string key = access.Token + "\n" + access.AppSecret;
            if (_accessToken != null && _tokenKey == key && _tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(15))
            {
                return _accessToken;
            }

            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(access.Token + ":" + access.AppSecret));
            using FormUrlEncodedContent content = new(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });
            using HttpRequestMessage request = new(HttpMethod.Post, TokenUrl) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            string response = await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.Tme,
                request,
                _logger,
                cancellationToken);
            try
            {
                using JsonDocument document = JsonDocument.Parse(response);
                string? token = DistributorJson.Text(document.RootElement, "access_token");
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new DistributorUpstreamException(DistributorProviders.Tme, "The distributor request failed");
                }

                int lifetime = DistributorJson.Int(document.RootElement, "expires_in") ?? 60;
                _accessToken = token;
                _tokenKey = key;
                _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(lifetime, 30));
                return token;
            }
            catch (DistributorUpstreamException)
            {
                throw;
            }
            catch (JsonException)
            {
                throw new DistributorUpstreamException(DistributorProviders.Tme, "The distributor request failed");
            }
        }

        private async Task<string> GetAsync(
            string url,
            string bearer,
            string? language,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            string? tag = LanguageTag(language);
            if (tag != null)
            {
                request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(tag));
            }

            return await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.Tme,
                request,
                _logger,
                cancellationToken);
        }

        private static string SearchAddress(TmeAccess access, string query)
        {
            return SearchUrl
                + "?country=" + Uri.EscapeDataString(access.Country)
                + "&phrase=" + Uri.EscapeDataString(query.Trim())
                + "&scope[]=products"
                + "&page=1"
                + "&limit=20";
        }

        private static string FilesAddress(TmeAccess access, IReadOnlyList<string> symbols)
        {
            StringBuilder builder = new(FilesUrl);
            builder.Append("?country=");
            builder.Append(Uri.EscapeDataString(access.Country));
            foreach (string symbol in symbols)
            {
                builder.Append("&symbols[]=");
                builder.Append(Uri.EscapeDataString(symbol));
            }

            return builder.ToString();
        }

        private void EnsureOk(string json)
        {
            if (IsError(json))
            {
                _logger.LogWarning("Distributor {Provider} rejected the search", DistributorProviders.Tme);
                throw new DistributorUpstreamException(DistributorProviders.Tme, "The distributor request failed");
            }
        }

        private static List<DistributorProduct> ApplyDatasheets(
            IReadOnlyList<DistributorProduct> products,
            IReadOnlyDictionary<string, string> datasheets)
        {
            List<DistributorProduct> merged = new(products.Count);
            foreach (DistributorProduct product in products)
            {
                if (product.OrderNumber != null
                    && product.DatasheetUrl == null
                    && datasheets.TryGetValue(product.OrderNumber, out string? datasheet))
                {
                    merged.Add(DistributorProduct.Create(
                        DistributorProviders.Tme,
                        DistributorNames.Tme,
                        product.Manufacturer,
                        product.ManufacturerPartNumber,
                        product.Description,
                        datasheet,
                        product.ProductUrl,
                        product.LifecycleStatus,
                        product.Package,
                        product.OrderNumber,
                        product.SourceQuantityMin,
                        product.SourceQuantitySteps));
                }
                else
                {
                    merged.Add(product);
                }
            }

            return merged;
        }

        private static DistributorProduct? Map(JsonElement item, string? country, string? language)
        {
            string? symbol = DistributorJson.Text(item, "symbol");
            string? mpn = FirstText(item, "manufacturer_symbols");
            if (symbol == null && mpn == null)
            {
                return null;
            }

            return DistributorProduct.Create(
                DistributorProviders.Tme,
                DistributorNames.Tme,
                ManufacturerName(item),
                mpn,
                DistributorJson.Text(item, "description"),
                null,
                ProductUrl(country, language, symbol),
                null,
                null,
                symbol,
                DistributorJson.Int(item, "minimal_amount"),
                DistributorJson.Int(item, "multiples"));
        }

        private static string? ManufacturerName(JsonElement item)
        {
            if (!DistributorJson.Try(item, "manufacturer", out JsonElement manufacturer)
                || manufacturer.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return DistributorJson.Text(manufacturer, "name");
        }

        private static string? FirstText(JsonElement item, string name)
        {
            JsonElement? values = DistributorJson.Array(item, name);
            if (values == null)
            {
                return null;
            }

            foreach (JsonElement value in values.Value.EnumerateArray())
            {
                string? text = DistributorJson.AsText(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }

            return null;
        }

        private static string? ProductUrl(string? country, string? language, string? symbol)
        {
            if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(symbol))
            {
                return null;
            }

            return "https://www.tme.eu/"
                + Uri.EscapeDataString(country.Trim().ToLowerInvariant())
                + "/"
                + Uri.EscapeDataString(language.Trim().ToLowerInvariant())
                + "/details/"
                + Uri.EscapeDataString(symbol.Trim().ToLowerInvariant())
                + "/";
        }

        private static int DatasheetScore(JsonElement document, string? language)
        {
            int score = 1;
            string? type = DistributorJson.Text(document, "type");
            if (type != null && type.Equals("DTE", StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            string? documentLanguage = DistributorJson.Text(document, "language");
            if (!string.IsNullOrWhiteSpace(language)
                && documentLanguage != null
                && documentLanguage.Equals(language, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }

            return score;
        }

        private static string? AbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            string trimmed = url.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + trimmed;
            }

            if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return null;
        }

        private static string? LanguageTag(string? language)
        {
            string tag = (language ?? "").Trim().ToLowerInvariant();
            if (tag.Length is < 2 or > 16)
            {
                return null;
            }

            foreach (char character in tag)
            {
                if (!char.IsAsciiLetter(character) && character != '-')
                {
                    return null;
                }
            }

            return tag;
        }

        private static bool IsPdf(string url)
        {
            string path = url;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                path = uri.AbsolutePath;
            }

            int query = path.IndexOf('?', StringComparison.Ordinal);
            if (query >= 0)
            {
                path = path[..query];
            }

            return path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        }
    }
}
