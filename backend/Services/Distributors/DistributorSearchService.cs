namespace KiComLibBackend.Services.Distributors
{
    public sealed class DistributorPartQuery
    {
        public string? ManufacturerPartNumber { get; set; }

        public string? Manufacturer { get; set; }
    }

    public sealed class KeywordSearchOutcome
    {
        public int StatusCode { get; init; }

        public string? Error { get; init; }

        public string? Provider { get; init; }

        public IReadOnlyList<DistributorProduct> Results { get; init; } = Array.Empty<DistributorProduct>();

        public static KeywordSearchOutcome BadRequest(string error) => new() { StatusCode = 400, Error = error };

        public static KeywordSearchOutcome BadGateway(string provider, string error) =>
            new() { StatusCode = 502, Error = error, Provider = provider };

        public static KeywordSearchOutcome Ok(string provider, IReadOnlyList<DistributorProduct> results) =>
            new() { StatusCode = 200, Provider = provider, Results = results };
    }

    public sealed class DistributorTestOutcome
    {
        public int StatusCode { get; init; }

        public string? Error { get; init; }

        public string Provider { get; init; } = "";

        public string Message { get; init; } = "";

        public static DistributorTestOutcome BadRequest(string error) => new() { StatusCode = 400, Error = error };

        public static DistributorTestOutcome Ok(string provider, string name, int count)
        {
            string detail = count == 1
                ? "The test search returned 1 part."
                : $"The test search returned {count} parts.";
            return new DistributorTestOutcome
            {
                StatusCode = 200,
                Provider = provider,
                Message = $"{name} is reachable. {detail}"
            };
        }
    }

    public sealed class MultiSearchOutcome
    {
        public IReadOnlyList<DistributorProduct> Results { get; init; } = Array.Empty<DistributorProduct>();

        public IReadOnlyList<DistributorProviderError> Errors { get; init; } = Array.Empty<DistributorProviderError>();

        public string? Error { get; init; }

        public int StatusCode { get; init; } = 200;
    }

    public sealed class DistributorSearchService
    {
        public const string TestQuery = "1N4148";
        public const int KeywordLimit = 25;
        public const int AlternativesLimit = 50;
        public const int OrderLimit = 100;
        public const int PartLimit = 20;
        public const int TextLimit = 200;

        private readonly DistributorSettingsService _settings;
        private readonly MouserClient _mouser;
        private readonly DigiKeyClient _digikey;
        private readonly FarnellClient _farnell;
        private readonly ArrowClient _arrow;
        private readonly TmeClient _tme;
        private readonly ILogger<DistributorSearchService> _logger;

        public DistributorSearchService(
            DistributorSettingsService settings,
            MouserClient mouser,
            DigiKeyClient digikey,
            FarnellClient farnell,
            ArrowClient arrow,
            TmeClient tme,
            ILogger<DistributorSearchService> logger)
        {
            _settings = settings;
            _mouser = mouser;
            _digikey = digikey;
            _farnell = farnell;
            _arrow = arrow;
            _tme = tme;
            _logger = logger;
        }

        public async Task<KeywordSearchOutcome> KeywordAsync(string? query, string? provider, CancellationToken cancellationToken)
        {
            string text = (query ?? "").Trim();
            if (text.Length == 0)
            {
                return KeywordSearchOutcome.BadRequest("query is required");
            }

            if (text.Length > TextLimit)
            {
                return KeywordSearchOutcome.BadRequest("query must be at most 200 characters");
            }

            DistributorAvailability availability = await _settings.AvailabilityAsync(cancellationToken);
            string? requested = string.IsNullOrWhiteSpace(provider) ? availability.LookupProvider : provider.Trim();
            string? id = DistributorProviders.Canonical(requested);
            if (string.IsNullOrWhiteSpace(requested))
            {
                return KeywordSearchOutcome.BadRequest("No distributor lookup provider is configured");
            }

            if (id == null)
            {
                return KeywordSearchOutcome.BadRequest("Unknown distributor provider");
            }

            if (!availability.IsUsable(id))
            {
                return KeywordSearchOutcome.BadRequest($"Distributor '{id}' is not enabled");
            }

            try
            {
                IReadOnlyList<DistributorProduct> results = await SearchKeywordAsync(id, availability, text, cancellationToken);
                return KeywordSearchOutcome.Ok(id, results.Take(KeywordLimit).ToList());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogFailure(id, exception);
                return KeywordSearchOutcome.BadGateway(id, "The distributor request failed");
            }
        }

        public async Task<DistributorTestOutcome> TestAsync(
            string? provider,
            DistributorSettingsWriteRequest? request,
            CancellationToken cancellationToken)
        {
            DistributorTestAccess access = await _settings.AccessForTestAsync(provider, request, cancellationToken);
            if (access.Error != null)
            {
                return DistributorTestOutcome.BadRequest(access.Error);
            }

            try
            {
                IReadOnlyList<DistributorProduct> results = access.Provider switch
                {
                    DistributorProviders.Mouser => await _mouser.SearchKeywordAsync(access.Mouser!.ApiKey, TestQuery, cancellationToken),
                    DistributorProviders.DigiKey => await _digikey.SearchKeywordAsync(access.DigiKey!, TestQuery, cancellationToken),
                    DistributorProviders.Farnell => await _farnell.SearchAsync(access.Farnell!, TestQuery, cancellationToken),
                    DistributorProviders.Arrow => await _arrow.SearchAsync(access.Arrow!, TestQuery, cancellationToken),
                    DistributorProviders.Tme => await _tme.SearchAsync(access.Tme!, TestQuery, cancellationToken),
                    _ => throw new DistributorUpstreamException(access.Provider, "Unknown distributor provider")
                };
                return DistributorTestOutcome.Ok(access.Provider, access.Name, results.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DistributorUpstreamException exception)
            {
                LogFailure(access.Provider, exception);
                return DistributorTestOutcome.BadRequest(exception.Message);
            }
            catch (Exception exception)
            {
                LogFailure(access.Provider, exception);
                return DistributorTestOutcome.BadRequest("The distributor request failed");
            }
        }

        public async Task<MultiSearchOutcome> AlternativesAsync(
            string? manufacturerPartNumber,
            string? manufacturer,
            CancellationToken cancellationToken)
        {
            string mpn = (manufacturerPartNumber ?? "").Trim();
            string? maker = string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer.Trim();
            if (mpn.Length == 0)
            {
                return new MultiSearchOutcome { StatusCode = 400, Error = "manufacturerPartNumber is required" };
            }

            if (mpn.Length > TextLimit)
            {
                return new MultiSearchOutcome { StatusCode = 400, Error = "manufacturerPartNumber must be at most 200 characters" };
            }

            if (maker != null && maker.Length > TextLimit)
            {
                return new MultiSearchOutcome { StatusCode = 400, Error = "manufacturer must be at most 200 characters" };
            }

            DistributorAvailability availability = await _settings.AvailabilityAsync(cancellationToken);
            List<Task<ProviderBatch>> tasks = new();
            foreach (DistributorProviderInfo provider in DistributorProviders.All)
            {
                if (!availability.IsUsable(provider.Id))
                {
                    continue;
                }

                tasks.Add(SearchPartSafeAsync(provider.Id, availability, mpn, cancellationToken));
            }

            ProviderBatch[] batches = await Task.WhenAll(tasks);
            List<DistributorProduct> results = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<DistributorProviderError> errors = ErrorsFrom(batches);
            foreach (ProviderBatch batch in batches)
            {
                if (batch.Error != null || results.Count >= AlternativesLimit)
                {
                    continue;
                }

                foreach (DistributorProduct product in batch.Results)
                {
                    if (results.Count >= AlternativesLimit || IsQueriedPart(product, mpn, maker))
                    {
                        continue;
                    }

                    string key = (product.Manufacturer ?? "") + "\n" + product.ManufacturerPartNumber;
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    results.Add(product);
                }
            }

            return new MultiSearchOutcome { Results = results, Errors = errors };
        }

        public async Task<MultiSearchOutcome> OrdersAsync(
            IReadOnlyList<DistributorPartQuery>? partNumbers,
            CancellationToken cancellationToken)
        {
            if (partNumbers == null || partNumbers.Count == 0)
            {
                return new MultiSearchOutcome { StatusCode = 400, Error = "partNumbers is required" };
            }

            if (partNumbers.Count > PartLimit)
            {
                return new MultiSearchOutcome { StatusCode = 400, Error = "partNumbers accepts at most 20 items" };
            }

            List<(string Mpn, string? Manufacturer)> parts = new();
            HashSet<string> requested = new(StringComparer.OrdinalIgnoreCase);
            foreach (DistributorPartQuery part in partNumbers)
            {
                string mpn = (part.ManufacturerPartNumber ?? "").Trim();
                string? maker = string.IsNullOrWhiteSpace(part.Manufacturer) ? null : part.Manufacturer.Trim();
                if (mpn.Length == 0)
                {
                    return new MultiSearchOutcome { StatusCode = 400, Error = "manufacturerPartNumber is required" };
                }

                if (mpn.Length > TextLimit)
                {
                    return new MultiSearchOutcome { StatusCode = 400, Error = "manufacturerPartNumber must be at most 200 characters" };
                }

                if (maker != null && maker.Length > TextLimit)
                {
                    return new MultiSearchOutcome { StatusCode = 400, Error = "manufacturer must be at most 200 characters" };
                }

                if (requested.Add(mpn))
                {
                    parts.Add((mpn, maker));
                }
            }

            DistributorAvailability availability = await _settings.AvailabilityAsync(cancellationToken);
            List<Task<ProviderBatch>> tasks = new();
            foreach ((string mpn, _) in parts)
            {
                foreach (DistributorProviderInfo provider in DistributorProviders.All)
                {
                    if (!availability.IsUsable(provider.Id))
                    {
                        continue;
                    }

                    tasks.Add(SearchPartSafeAsync(provider.Id, availability, mpn, cancellationToken));
                }
            }

            ProviderBatch[] batches = await Task.WhenAll(tasks);
            List<DistributorProduct> results = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<DistributorProviderError> errors = ErrorsFrom(batches);
            foreach (ProviderBatch batch in batches)
            {
                if (batch.Error != null || results.Count >= OrderLimit)
                {
                    continue;
                }

                foreach (DistributorProduct product in batch.Results)
                {
                    if (results.Count >= OrderLimit || string.IsNullOrWhiteSpace(product.OrderNumber))
                    {
                        continue;
                    }

                    DistributorProduct row = product;
                    if (string.IsNullOrWhiteSpace(product.ManufacturerPartNumber))
                    {
                        row = product.WithManufacturerPartNumber(batch.QueriedMpn);
                    }
                    else if (!product.ManufacturerPartNumber.Equals(batch.QueriedMpn, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string key = (row.DistributorName ?? "") + "\n" + row.OrderNumber + "\n" + row.ManufacturerPartNumber;
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    results.Add(row);
                }
            }

            return new MultiSearchOutcome { Results = results, Errors = errors };
        }

        private async Task<IReadOnlyList<DistributorProduct>> SearchKeywordAsync(
            string provider,
            DistributorAvailability availability,
            string query,
            CancellationToken cancellationToken)
        {
            return provider switch
            {
                DistributorProviders.Mouser => await _mouser.SearchKeywordAsync(availability.Mouser!.ApiKey, query, cancellationToken),
                DistributorProviders.DigiKey => await _digikey.SearchKeywordAsync(availability.DigiKey!, query, cancellationToken),
                DistributorProviders.Farnell => await _farnell.SearchAsync(availability.Farnell!, query, cancellationToken),
                DistributorProviders.Arrow => await _arrow.SearchAsync(availability.Arrow!, query, cancellationToken),
                DistributorProviders.Tme => await _tme.SearchAsync(availability.Tme!, query, cancellationToken),
                _ => Array.Empty<DistributorProduct>()
            };
        }

        private async Task<IReadOnlyList<DistributorProduct>> SearchPartAsync(
            string provider,
            DistributorAvailability availability,
            string manufacturerPartNumber,
            CancellationToken cancellationToken)
        {
            return provider switch
            {
                DistributorProviders.Mouser => await _mouser.SearchPartAsync(availability.Mouser!.ApiKey, manufacturerPartNumber, cancellationToken),
                DistributorProviders.DigiKey => await _digikey.SearchPartAsync(availability.DigiKey!, manufacturerPartNumber, cancellationToken),
                DistributorProviders.Farnell => await _farnell.SearchAsync(availability.Farnell!, manufacturerPartNumber, cancellationToken),
                DistributorProviders.Arrow => await _arrow.SearchAsync(availability.Arrow!, manufacturerPartNumber, cancellationToken),
                DistributorProviders.Tme => await _tme.SearchAsync(availability.Tme!, manufacturerPartNumber, cancellationToken),
                _ => Array.Empty<DistributorProduct>()
            };
        }

        private async Task<ProviderBatch> SearchPartSafeAsync(
            string provider,
            DistributorAvailability availability,
            string manufacturerPartNumber,
            CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<DistributorProduct> results = await SearchPartAsync(provider, availability, manufacturerPartNumber, cancellationToken);
                return new ProviderBatch(provider, manufacturerPartNumber, results, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogFailure(provider, exception);
                return new ProviderBatch(provider, manufacturerPartNumber, Array.Empty<DistributorProduct>(), "The distributor request failed");
            }
        }

        private static List<DistributorProviderError> ErrorsFrom(IEnumerable<ProviderBatch> batches)
        {
            List<DistributorProviderError> errors = new();
            HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);
            foreach (ProviderBatch batch in batches)
            {
                if (batch.Error == null || !reported.Add(batch.Provider + "\n" + batch.Error))
                {
                    continue;
                }

                errors.Add(new DistributorProviderError { Provider = batch.Provider, Message = batch.Error });
            }

            return errors;
        }

        private static bool IsQueriedPart(DistributorProduct product, string mpn, string? manufacturer)
        {
            if (string.IsNullOrWhiteSpace(product.ManufacturerPartNumber))
            {
                return true;
            }

            if (!product.ManufacturerPartNumber.Equals(mpn, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(product.Manufacturer))
            {
                return true;
            }

            return product.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase);
        }

        private void LogFailure(string provider, Exception exception)
        {
            if (exception is DistributorUpstreamException)
            {
                return;
            }

            _logger.LogWarning(
                "Distributor {Provider} search failed: {Detail}",
                provider,
                DistributorLog.Sanitize(exception.Message));
        }

        private sealed record ProviderBatch(
            string Provider,
            string QueriedMpn,
            IReadOnlyList<DistributorProduct> Results,
            string? Error);
    }
}
