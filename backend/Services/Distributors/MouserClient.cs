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

        /// <summary>Mouser accepts at most this many part numbers, separated by a pipe, in one search.</summary>
        public const int PartBatchLimit = 10;

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

        /// <summary>
        /// Looks up many manufacturer part numbers. Each Mouser call carries up to
        /// <see cref="PartBatchLimit"/> numbers so a rate limit covers more of the BOM.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>>> SearchPartsAsync(
            string apiKey,
            IReadOnlyList<string> partNumbers,
            CancellationToken cancellationToken)
        {
            Dictionary<string, IReadOnlyList<DistributorProduct>> found = new(StringComparer.OrdinalIgnoreCase);
            foreach (string packed in PackQueries(partNumbers))
            {
                string[] queries = packed.Split('|');
                string url = PartNumberUrl + "?apiKey=" + Uri.EscapeDataString(apiKey);
                var payload = new Dictionary<string, object>
                {
                    ["SearchByPartRequest"] = new Dictionary<string, object>
                    {
                        ["mouserPartNumber"] = packed,
                        ["partSearchOptions"] = "Exact"
                    }
                };
                string response = await PostRawAsync(url, payload, cancellationToken);
                foreach (KeyValuePair<string, IReadOnlyList<DistributorProduct>> pair in Assign(response, queries))
                {
                    found[pair.Key] = pair.Value;
                }
            }

            return found;
        }

        /// <summary>Packs part numbers into pipe-separated Mouser queries of at most <see cref="PartBatchLimit"/>.</summary>
        public static IReadOnlyList<string> PackQueries(IReadOnlyList<string> partNumbers)
        {
            List<string> packed = new();
            List<string> batch = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in partNumbers)
            {
                string part = (raw ?? "").Trim();
                if (part.Length == 0 || !seen.Add(part))
                {
                    continue;
                }

                if (part.Contains('|'))
                {
                    Flush();
                    packed.Add(part);
                    continue;
                }

                batch.Add(part);
                if (batch.Count == PartBatchLimit)
                {
                    Flush();
                }
            }

            Flush();
            return packed;

            void Flush()
            {
                if (batch.Count == 0)
                {
                    return;
                }

                packed.Add(string.Join("|", batch));
                batch.Clear();
            }
        }

        public static IReadOnlyList<DistributorProduct> Parse(string json)
        {
            return ParseBuckets(json).SelectMany(bucket => bucket.Products).ToList();
        }

        public static IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>> Assign(
            string json,
            IReadOnlyList<string> queries)
        {
            Dictionary<string, List<DistributorProduct>> assigned = new(StringComparer.OrdinalIgnoreCase);
            foreach (string query in queries)
            {
                string trimmed = query.Trim();
                if (trimmed.Length > 0)
                {
                    assigned.TryAdd(trimmed, new List<DistributorProduct>());
                }
            }

            foreach ((string? source, List<DistributorProduct> products) in ParseBuckets(json))
            {
                string? key = queries.FirstOrDefault(query => Same(query.Trim(), source));
                if (key == null || !assigned.TryGetValue(key.Trim(), out List<DistributorProduct>? list))
                {
                    continue;
                }

                list.AddRange(products);
            }

            Dictionary<string, IReadOnlyList<DistributorProduct>> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<DistributorProduct>> pair in assigned)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        /// <summary>
        /// Mouser states the reason in the body. A rate limit becomes a sentence that says the request limit is exhausted.
        /// </summary>
        public static string? DescribeFailure(int statusCode, string? body)
        {
            return DistributorFailure.Describe(statusCode, ReadErrorMessage(body));
        }

        private async Task<IReadOnlyList<DistributorProduct>> PostAsync(
            string url,
            Dictionary<string, object> payload,
            CancellationToken cancellationToken)
        {
            string response = await PostRawAsync(url, payload, cancellationToken);
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

        private async Task<string> PostRawAsync(
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
            return await DistributorHttp.SendAsync(
                _http,
                HttpClientName,
                DistributorProviders.Mouser,
                request,
                _logger,
                DescribeFailure,
                cancellationToken);
        }

        private static List<(string? SourceMpn, List<DistributorProduct> Products)> ParseBuckets(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            List<(string? SourceMpn, List<DistributorProduct> Products)> buckets = new();
            JsonElement? parts = null;
            if (DistributorJson.Try(root, "SearchResults", out JsonElement results))
            {
                parts = DistributorJson.Array(results, "Parts");
            }

            parts ??= DistributorJson.Array(root, "Parts");
            if (parts != null)
            {
                foreach (JsonElement part in parts.Value.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    List<DistributorProduct> mapped = new();
                    MapPart(part, mapped);
                    buckets.Add((DistributorJson.Text(part, "ManufacturerPartNumber"), mapped));
                }
            }

            if (buckets.Sum(bucket => bucket.Products.Count) == 0)
            {
                string? reason = DescribeFailure(200, json);
                if (reason != null)
                {
                    throw new DistributorUpstreamException(DistributorProviders.Mouser, reason);
                }

                if (HasErrors(root))
                {
                    throw new DistributorUpstreamException(DistributorProviders.Mouser, "Mouser rejected the search");
                }
            }

            return buckets;
        }

        private static string? ReadErrorMessage(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;
                JsonElement? errors = DistributorJson.Array(root, "Errors");
                if (errors != null)
                {
                    foreach (JsonElement error in errors.Value.EnumerateArray())
                    {
                        if (error.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        string? message = DistributorJson.Text(error, "Message", "message", "Code");
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            return TrimMessage(message);
                        }
                    }
                }

                return TrimMessage(DistributorJson.Text(root, "ErrorMessage", "Message", "message", "error"));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? TrimMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            string text = message.Trim().Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= 300 ? text : text[..300];
        }

        private static void MapPart(JsonElement part, List<DistributorProduct> products)
        {
            string? mpn = DistributorJson.Text(part, "ManufacturerPartNumber");
            string? lifecycle = LifecycleMapper.Map(
                DistributorJson.Text(part, "LifecycleStatus"),
                DistributorJson.Flag(part, "IsDiscontinued"));
            int? stock = DistributorJson.Int(part, "AvailabilityInStock")
                ?? DistributorJson.CountInText(DistributorJson.Text(part, "Availability"));
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
                DistributorJson.Int(part, "Mult"),
                quantityAvailable: stock));

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
