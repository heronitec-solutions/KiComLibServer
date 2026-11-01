using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services.Distributors;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public class BomService
    {
        private readonly ApplicationDbContext _db;
        private readonly CatalogMatcher _matcher;
        private readonly KiCadFieldBuilder _fields;
        private readonly ProjectAccessService _access;
        private readonly DistributorSearchService _search;
        private readonly ILogger<BomService> _logger;

        public BomService(
            ApplicationDbContext db,
            CatalogMatcher matcher,
            KiCadFieldBuilder fields,
            ProjectAccessService access,
            DistributorSearchService search,
            ILogger<BomService> logger)
        {
            _db = db;
            _matcher = matcher;
            _fields = fields;
            _access = access;
            _search = search;
            _logger = logger;
        }

        public async Task<List<object>> LinesAsync(int revisionId, string? group, CancellationToken cancellationToken)
        {
            List<List<DescribedComponent>> groups = await GroupsAsync(revisionId, group, cancellationToken);
            return groups.Select(Line).ToList();
        }

        public async Task<object> AvailabilityAsync(int revisionId, string? group, int pieces, CancellationToken cancellationToken)
        {
            List<List<DescribedComponent>> groups = await GroupsAsync(revisionId, group, cancellationToken);
            SupplyCatalog catalog = await LoadSupplyAsync(groups, cancellationToken);
            IReadOnlySet<string> usable = await _search.UsableProvidersAsync(cancellationToken);
            PreparedLine[] prepared = groups.Select(members => Prepare(members, pieces, catalog, usable)).ToArray();
            object[] rows = new object[prepared.Length];
            OfferLoad load = new();
            int[] cursor = new int[prepared.Length];
            BomAvailabilityHit?[] hits = new BomAvailabilityHit?[prepared.Length];
            bool[] settled = new bool[prepared.Length];

            while (true)
            {
                List<(int Index, BomSupplyProbe Probe)> wave = new();
                bool pending = false;
                for (int index = 0; index < prepared.Length; index++)
                {
                    if (settled[index])
                    {
                        continue;
                    }

                    if (prepared[index].TotalQuantity <= 0 || cursor[index] >= prepared[index].Probes.Count)
                    {
                        settled[index] = true;
                        continue;
                    }

                    pending = true;
                    wave.Add((index, prepared[index].Probes[cursor[index]]));
                }

                if (!pending)
                {
                    break;
                }

                await EnsureOffersAsync(wave.Select(item => item.Probe).ToList(), load, cancellationToken);
                foreach ((int index, BomSupplyProbe probe) in wave)
                {
                    if (!load.Cache.TryGetValue(OfferKey(probe.Provider, probe.Mpn), out IReadOnlyList<DistributorProduct>? products))
                    {
                        products = Array.Empty<DistributorProduct>();
                    }

                    List<BomStockOffer> stock = new(products.Count);
                    foreach (DistributorProduct product in products)
                    {
                        stock.Add(new BomStockOffer
                        {
                            OrderNumber = product.OrderNumber ?? "",
                            Mpn = product.ManufacturerPartNumber,
                            QuantityAvailable = product.QuantityAvailable
                        });
                    }

                    BomAvailabilityHit? hit = BomAvailability.Match(probe, stock, prepared[index].TotalQuantity);
                    if (hit != null)
                    {
                        hits[index] = hit;
                        settled[index] = true;
                    }
                    else
                    {
                        cursor[index]++;
                    }
                }
            }

            List<string> warnings = load.Warnings;
            for (int index = 0; index < prepared.Length; index++)
            {
                rows[index] = AvailabilityRow(prepared[index], hits[index]);
            }

            return new
            {
                pieces,
                warnings,
                lines = rows
            };
        }

        public async Task<object> DiffAsync(ProjectRevision baseRevision, ProjectRevision targetRevision, CancellationToken cancellationToken)
        {
            List<ProjectComponent> baseRows = await LoadAsync(baseRevision.Id, cancellationToken);
            List<ProjectComponent> targetRows = await LoadAsync(targetRevision.Id, cancellationToken);
            Dictionary<string, ProjectComponent> targetByPath = targetRows.ToDictionary(c => c.SymbolPath, StringComparer.Ordinal);
            HashSet<string> basePaths = new(baseRows.Select(c => c.SymbolPath), StringComparer.Ordinal);

            List<object> changed = new();
            List<object> renamed = new();
            List<ProjectComponent> removed = new();
            foreach (ProjectComponent row in baseRows)
            {
                if (!targetByPath.TryGetValue(row.SymbolPath, out ProjectComponent? next))
                {
                    removed.Add(row);
                    continue;
                }

                Dictionary<string, string[]> fields = FieldChanges(row, next);
                bool referenceChanged = !KiCadFieldBuilder.Same(row.Reference, next.Reference);
                if (fields.Count == 0 && !referenceChanged)
                {
                    continue;
                }

                if (referenceChanged && fields.Count == 0)
                {
                    DescribedComponent described = await DescribeAsync(next, cancellationToken);
                    renamed.Add(new
                    {
                        reference = next.Reference,
                        value = next.Value ?? "",
                        status = described.Status,
                        partId = next.PartId,
                        from = new { reference = row.Reference, value = row.Value ?? "" },
                        to = new { reference = next.Reference, value = next.Value ?? "", status = described.Status },
                        changes = new Dictionary<string, string[]>
                        {
                            ["reference"] = new[] { row.Reference, next.Reference }
                        }
                    });
                    continue;
                }

                if (referenceChanged)
                {
                    fields["reference"] = new[] { row.Reference, next.Reference };
                }

                DescribedComponent targetDescribed = await DescribeAsync(next, cancellationToken);
                changed.Add(new
                {
                    reference = next.Reference,
                    value = next.Value ?? "",
                    status = targetDescribed.Status,
                    partId = next.PartId,
                    changes = fields
                });
            }

            List<ProjectComponent> added = targetRows.Where(row => !basePaths.Contains(row.SymbolPath)).ToList();
            List<object> replaced = new();
            HashSet<string> pairedAdded = new(StringComparer.Ordinal);
            List<ProjectComponent> stillRemoved = new();
            foreach (ProjectComponent row in removed)
            {
                ProjectComponent? match = added.FirstOrDefault(candidate =>
                    !pairedAdded.Contains(candidate.SymbolPath)
                    && !string.IsNullOrWhiteSpace(row.Reference)
                    && string.Equals(row.Reference, candidate.Reference, StringComparison.Ordinal));
                if (match == null)
                {
                    stillRemoved.Add(row);
                    continue;
                }

                pairedAdded.Add(match.SymbolPath);
                DescribedComponent to = await DescribeAsync(match, cancellationToken);
                replaced.Add(new
                {
                    reference = row.Reference,
                    from = new { value = row.Value ?? "" },
                    to = new { value = match.Value ?? "", status = to.Status }
                });
            }

            List<object> addedItems = new();
            foreach (ProjectComponent row in added.Where(row => !pairedAdded.Contains(row.SymbolPath)))
            {
                addedItems.Add(await SideItemAsync(row, cancellationToken));
            }

            List<object> removedItems = new();
            foreach (ProjectComponent row in stillRemoved)
            {
                removedItems.Add(await SideItemAsync(row, cancellationToken));
            }

            return new
            {
                @base = baseRevision.CommitSha,
                target = targetRevision.CommitSha,
                summary = new
                {
                    added = addedItems.Count,
                    removed = removedItems.Count,
                    replaced = replaced.Count,
                    changed = changed.Count,
                    renamed = renamed.Count
                },
                replaced,
                changed,
                added = addedItems,
                removed = removedItems,
                renamed
            };
        }

        public async Task<List<object>> UsageAsync(CallerContext caller, int partId, CancellationToken cancellationToken)
        {
            if (!caller.IsAuthenticated)
            {
                return new List<object>();
            }

            List<int> projectIds = await _access.Readable(caller).Select(p => p.Id).ToListAsync(cancellationToken);
            if (projectIds.Count == 0)
            {
                return new List<object>();
            }

            List<Project> projects = await _db.Projects
                .Where(p => projectIds.Contains(p.Id))
                .ToListAsync(cancellationToken);
            HashSet<int> revisionIds = projects.Where(p => p.HeadRevisionId != null).Select(p => p.HeadRevisionId!.Value).ToHashSet();
            List<ProjectRelease> releases = await _db.ProjectReleases
                .Where(r => projectIds.Contains(r.ProjectId) && r.State == "released")
                .ToListAsync(cancellationToken);
            foreach (ProjectRelease release in releases)
            {
                revisionIds.Add(release.RevisionId);
            }

            if (revisionIds.Count == 0)
            {
                return new List<object>();
            }

            List<ProjectRevision> revisions = await _db.ProjectRevisions
                .Where(r => revisionIds.Contains(r.Id))
                .ToListAsync(cancellationToken);
            Dictionary<int, ProjectRevision> revisionById = revisions.ToDictionary(r => r.Id);
            List<ProjectComponent> components = await _db.ProjectComponents
                .Where(c => c.PartId == partId && revisionIds.Contains(c.RevisionId) && c.Variant == "")
                .ToListAsync(cancellationToken);
            Dictionary<int, List<ProjectComponent>> componentsByRevision = components
                .GroupBy(c => c.RevisionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            List<object> rows = new();
            foreach (Project project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                int? countedRevisionId = project.HeadRevisionId != null
                    && componentsByRevision.ContainsKey(project.HeadRevisionId.Value)
                    ? project.HeadRevisionId
                    : releases
                        .Where(r => r.ProjectId == project.Id && componentsByRevision.ContainsKey(r.RevisionId))
                        .Select(r => r.RevisionId)
                        .Distinct()
                        .OrderByDescending(id => componentsByRevision[id].Count)
                        .ThenBy(id => id)
                        .Select(id => (int?)id)
                        .FirstOrDefault();

                if (countedRevisionId == null
                    || !revisionById.TryGetValue(countedRevisionId.Value, out ProjectRevision? revision))
                {
                    continue;
                }

                List<string> releaseNames = releases
                    .Where(r => r.ProjectId == project.Id && r.RevisionId == revision.Id)
                    .Select(r => r.Name)
                    .Distinct()
                    .ToList();
                rows.Add(new
                {
                    projectId = project.Id,
                    projectName = project.Name,
                    revision = revision.CommitSha,
                    release = releaseNames.Count == 0 ? "" : string.Join(", ", releaseNames),
                    quantity = componentsByRevision[revision.Id].Count
                });
            }

            return rows;
        }

        private async Task<List<List<DescribedComponent>>> GroupsAsync(int revisionId, string? group, CancellationToken cancellationToken)
        {
            bool grouped = !string.Equals(group, "none", StringComparison.OrdinalIgnoreCase);
            List<ProjectComponent> components = await _db.ProjectComponents
                .Where(c => c.RevisionId == revisionId && c.Variant == "")
                .OrderBy(c => c.Reference)
                .ThenBy(c => c.SymbolPath)
                .ToListAsync(cancellationToken);
            List<DescribedComponent> described = new();
            foreach (ProjectComponent component in components)
            {
                described.Add(await DescribeAsync(component, cancellationToken));
            }

            if (!grouped)
            {
                return described.Select(item => new List<DescribedComponent> { item }).ToList();
            }

            return described.GroupBy(GroupKey).Select(bucket => bucket.ToList()).ToList();
        }

        private async Task<SupplyCatalog> LoadSupplyAsync(List<List<DescribedComponent>> groups, CancellationToken cancellationToken)
        {
            HashSet<int> direct = new();
            foreach (List<DescribedComponent> group in groups)
            {
                int? partId = group[0].Component.PartId;
                if (partId != null)
                {
                    direct.Add(partId.Value);
                }
            }

            List<Part> directParts = direct.Count == 0
                ? new List<Part>()
                : await _db.Part.Where(part => part.Id != null && direct.Contains(part.Id.Value)).ToListAsync(cancellationToken);
            HashSet<int> alternativeIds = new();
            foreach (Part part in directParts)
            {
                foreach (int alternativeId in part.Alternatives)
                {
                    if (alternativeId > 0 && !direct.Contains(alternativeId))
                    {
                        alternativeIds.Add(alternativeId);
                    }
                }
            }

            List<Part> alternativeParts = alternativeIds.Count == 0
                ? new List<Part>()
                : await _db.Part.Where(part => part.Id != null && alternativeIds.Contains(part.Id.Value)).ToListAsync(cancellationToken);
            List<Part> parts = directParts.Concat(alternativeParts).ToList();
            HashSet<int> partIds = parts.Where(part => part.Id != null).Select(part => part.Id!.Value).ToHashSet();
            List<PartNumber> numbers = partIds.Count == 0
                ? new List<PartNumber>()
                : await _db.PartNumber.Where(number => number.PartId != null && partIds.Contains(number.PartId.Value)).ToListAsync(cancellationToken);
            HashSet<int> numberIds = numbers.Where(number => number.Id != null).Select(number => number.Id!.Value).ToHashSet();
            List<OrderNumber> orders = numberIds.Count == 0
                ? new List<OrderNumber>()
                : await _db.OrderNumber.Where(order => order.PartNumberId != null && numberIds.Contains(order.PartNumberId.Value)).ToListAsync(cancellationToken);
            HashSet<int> manufacturerIds = numbers.Where(number => number.ManufacturerId != null).Select(number => number.ManufacturerId!.Value).ToHashSet();
            List<Manufacturer> manufacturers = manufacturerIds.Count == 0
                ? new List<Manufacturer>()
                : await _db.Manufacturer.Where(maker => maker.Id != null && manufacturerIds.Contains(maker.Id.Value)).ToListAsync(cancellationToken);
            List<Distributor> distributors = await _db.Distributor.AsNoTracking().ToListAsync(cancellationToken);

            Dictionary<int, string> manufacturerNames = manufacturers
                .Where(maker => maker.Id != null)
                .ToDictionary(maker => maker.Id!.Value, maker => maker.Name);
            Dictionary<int, List<OrderNumber>> ordersByNumber = orders
                .Where(order => order.PartNumberId != null)
                .GroupBy(order => order.PartNumberId!.Value)
                .ToDictionary(bucket => bucket.Key, bucket => bucket.ToList());
            Dictionary<int, List<PartNumber>> numbersByPart = numbers
                .Where(number => number.PartId != null)
                .GroupBy(number => number.PartId!.Value)
                .ToDictionary(bucket => bucket.Key, bucket => bucket.ToList());

            Dictionary<int, BomPartSupply> supply = new();
            foreach (Part part in parts)
            {
                if (part.Id == null)
                {
                    continue;
                }

                int partId = part.Id.Value;
                List<BomPartNumberSupply> partNumbers = new();
                if (numbersByPart.TryGetValue(partId, out List<PartNumber>? linkedNumbers))
                {
                    foreach (PartNumber number in linkedNumbers.OrderBy(item => item.Id))
                    {
                        List<BomOrderRow> orderRows = new();
                        if (number.Id != null && ordersByNumber.TryGetValue(number.Id.Value, out List<OrderNumber>? linkedOrders))
                        {
                            foreach (OrderNumber order in linkedOrders)
                            {
                                if (order.Id == null || order.DistributorId == null || string.IsNullOrWhiteSpace(order.OrderNumberValue))
                                {
                                    continue;
                                }

                                orderRows.Add(new BomOrderRow
                                {
                                    Id = order.Id.Value,
                                    DistributorId = order.DistributorId.Value,
                                    OrderNumber = order.OrderNumberValue
                                });
                            }
                        }

                        string manufacturer = "";
                        if (number.ManufacturerId != null && manufacturerNames.TryGetValue(number.ManufacturerId.Value, out string? name))
                        {
                            manufacturer = name;
                        }

                        partNumbers.Add(new BomPartNumberSupply
                        {
                            Id = number.Id ?? 0,
                            Manufacturer = manufacturer,
                            Mpn = number.ManufacturerPartNumber ?? "",
                            Orders = orderRows
                        });
                    }
                }

                List<int> alternatives = new();
                HashSet<int> seenAlternatives = new();
                foreach (int alternativeId in part.Alternatives)
                {
                    if (alternativeId > 0 && alternativeId != partId && seenAlternatives.Add(alternativeId))
                    {
                        alternatives.Add(alternativeId);
                    }
                }

                supply[partId] = new BomPartSupply
                {
                    PartId = partId,
                    PartNumbers = partNumbers,
                    AlternativePartIds = alternatives
                };
            }

            List<BomDistributorInfo> distributorInfos = distributors
                .Where(distributor => distributor.Id != null)
                .Select(distributor => new BomDistributorInfo
                {
                    Id = distributor.Id!.Value,
                    Name = distributor.Name,
                    Favorite = distributor.IsFavorite,
                    Provider = DistributorProviders.ProviderForDistributor(distributor.Name)
                })
                .ToList();
            return new SupplyCatalog(supply, distributorInfos);
        }

        private static PreparedLine Prepare(
            List<DescribedComponent> members,
            int pieces,
            SupplyCatalog catalog,
            IReadOnlySet<string> usable)
        {
            DescribedComponent first = members[0];
            ProjectComponent component = first.Component;
            int quantity = members.Count(item => !item.Component.Dnp && !item.Component.ExcludeFromBom);
            int total = Multiply(quantity, pieces);
            List<string> references = members.Select(item => item.Component.Reference).Where(reference => reference.Length > 0).ToList();
            BomPartSupply? supply = null;
            IReadOnlyList<BomSupplyProbe> probes = Array.Empty<BomSupplyProbe>();
            if (component.PartId != null && catalog.Parts.TryGetValue(component.PartId.Value, out BomPartSupply? found))
            {
                supply = found;
                if (total > 0)
                {
                    probes = BomAvailability.Probes(found, component.Mpn, catalog.Parts, catalog.Distributors, usable);
                }
            }

            return new PreparedLine(
                string.Join(", ", references),
                component.Manufacturer ?? "",
                component.Mpn ?? "",
                total,
                supply,
                probes);
        }

        private static object AvailabilityRow(PreparedLine line, BomAvailabilityHit? hit)
        {
            string status = "";
            if (line.TotalQuantity > 0)
            {
                status = hit == null ? "not_available" : hit.Status;
            }

            (string manufacturer, string mpn) = BomAvailability.Identity(line.Manufacturer, line.Mpn, line.Supply, hit);
            return new
            {
                reference = line.Reference,
                manufacturer,
                mpn,
                totalQuantity = line.TotalQuantity,
                distributor = hit?.Distributor ?? "",
                orderNumber = hit?.OrderNumber ?? "",
                orderStatus = status,
                limited = hit?.Limited ?? false
            };
        }

        private async Task EnsureOffersAsync(
            IReadOnlyList<BomSupplyProbe> probes,
            OfferLoad load,
            CancellationToken cancellationToken)
        {
            List<BomSupplyProbe> missing = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (BomSupplyProbe probe in probes)
            {
                string key = OfferKey(probe.Provider, probe.Mpn);
                if (load.Cache.ContainsKey(key) || !seen.Add(key))
                {
                    continue;
                }

                missing.Add(probe);
            }

            await Task.WhenAll(
                LoadGroupedAsync(
                    DistributorProviders.Mouser,
                    MouserBatches(PartNumbers(missing, DistributorProviders.Mouser)),
                    (batch, token) => _search.MouserOffersAsync(batch, token),
                    load,
                    cancellationToken),
                LoadGroupedAsync(
                    DistributorProviders.DigiKey,
                    PartBatches.Chunk(PartNumbers(missing, DistributorProviders.DigiKey), 1),
                    (batch, token) => OneOfferAsync(DistributorProviders.DigiKey, batch, token),
                    load,
                    cancellationToken),
                LoadGroupedAsync(
                    DistributorProviders.Farnell,
                    FarnellClient.PackQueries(PartNumbers(missing, DistributorProviders.Farnell)),
                    (batch, token) => _search.FarnellOffersAsync(batch, token),
                    load,
                    cancellationToken),
                LoadGroupedAsync(
                    DistributorProviders.Arrow,
                    ArrowClient.PackQueries(PartNumbers(missing, DistributorProviders.Arrow)),
                    (batch, token) => _search.ArrowOffersAsync(batch, token),
                    load,
                    cancellationToken),
                LoadGroupedAsync(
                    DistributorProviders.Tme,
                    PartBatches.Chunk(PartNumbers(missing, DistributorProviders.Tme), 1),
                    (batch, token) => OneOfferAsync(DistributorProviders.Tme, batch, token),
                    load,
                    cancellationToken));
        }

        private static List<string> PartNumbers(IEnumerable<BomSupplyProbe> probes, string provider)
        {
            return probes
                .Where(probe => probe.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase))
                .Select(probe => probe.Mpn)
                .ToList();
        }

        private static IReadOnlyList<IReadOnlyList<string>> MouserBatches(IReadOnlyList<string> partNumbers)
        {
            return MouserClient.PackQueries(partNumbers)
                .Select(packed => (IReadOnlyList<string>)packed.Split('|'))
                .ToList();
        }

        private async Task<IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>>> OneOfferAsync(
            string provider,
            IReadOnlyList<string> batch,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DistributorProduct> products = await _search.OffersAsync(provider, batch[0], cancellationToken);
            return new Dictionary<string, IReadOnlyList<DistributorProduct>>(StringComparer.OrdinalIgnoreCase)
            {
                [batch[0]] = products
            };
        }

        private async Task LoadGroupedAsync(
            string provider,
            IReadOnlyList<IReadOnlyList<string>> batches,
            Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>>>> fetch,
            OfferLoad load,
            CancellationToken cancellationToken)
        {
            foreach (IReadOnlyList<string> batch in batches)
            {
                if (IsStopped(load, provider))
                {
                    RememberEmpty(load, provider, batch);
                    continue;
                }

                try
                {
                    IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>> found =
                        await fetch(batch, cancellationToken);
                    foreach (string query in batch)
                    {
                        if (!found.TryGetValue(query, out IReadOnlyList<DistributorProduct>? products))
                        {
                            products = Array.Empty<DistributorProduct>();
                        }

                        Store(load, provider, query, products);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Stop(load, provider);
                    NoteWarning(provider, exception, load);
                    RememberEmpty(load, provider, batch);
                }
            }
        }

        private static bool IsStopped(OfferLoad load, string provider)
        {
            lock (load.Stopped)
            {
                return load.Stopped.Contains(provider);
            }
        }

        private static void Stop(OfferLoad load, string provider)
        {
            lock (load.Stopped)
            {
                load.Stopped.Add(provider);
            }
        }

        private static void Store(
            OfferLoad load,
            string provider,
            string partNumber,
            IReadOnlyList<DistributorProduct> products)
        {
            lock (load.Cache)
            {
                load.Cache[OfferKey(provider, partNumber)] = products;
            }
        }

        private static void RememberEmpty(OfferLoad load, string provider, IEnumerable<string> partNumbers)
        {
            lock (load.Cache)
            {
                foreach (string partNumber in partNumbers)
                {
                    load.Cache[OfferKey(provider, partNumber)] = Array.Empty<DistributorProduct>();
                }
            }
        }

        private void NoteWarning(string provider, Exception exception, OfferLoad load)
        {
            lock (load.Warnings)
            {
                if (!load.Warned.Add(provider))
                {
                    return;
                }

                string name = DistributorProviders.All.FirstOrDefault(item => item.Id == provider)?.Name ?? provider;
                string detail = exception is DistributorUpstreamException && !string.IsNullOrWhiteSpace(exception.Message)
                    ? exception.Message.Trim()
                    : "The distributor request failed";
                load.Warnings.Add(name + ": " + detail);
            }

            _logger.LogWarning(exception, "Distributor {Provider} availability lookup failed", provider);
        }

        private static string OfferKey(string provider, string mpn)
        {
            return provider.Trim() + "\n" + mpn.Trim();
        }

        private static int Multiply(int quantity, int pieces)
        {
            if (quantity <= 0 || pieces <= 0)
            {
                return 0;
            }

            try
            {
                return checked(quantity * pieces);
            }
            catch (OverflowException)
            {
                return int.MaxValue;
            }
        }

        private async Task<List<ProjectComponent>> LoadAsync(int revisionId, CancellationToken cancellationToken)
        {
            return await _db.ProjectComponents
                .Where(c => c.RevisionId == revisionId && c.Variant == "")
                .OrderBy(c => c.SymbolPath)
                .ToListAsync(cancellationToken);
        }

        private async Task<object> SideItemAsync(ProjectComponent component, CancellationToken cancellationToken)
        {
            DescribedComponent described = await DescribeAsync(component, cancellationToken);
            return new
            {
                reference = component.Reference,
                value = component.Value ?? "",
                status = described.Status,
                partId = component.PartId
            };
        }

        private static Dictionary<string, string[]> FieldChanges(ProjectComponent left, ProjectComponent right)
        {
            Dictionary<string, string[]> changes = new(StringComparer.Ordinal);
            AddChange(changes, "value", left.Value, right.Value);
            AddChange(changes, "footprint", left.Footprint, right.Footprint);
            AddChange(changes, "mpn", left.Mpn, right.Mpn);
            AddChange(changes, "manufacturer", left.Manufacturer, right.Manufacturer);
            if (left.Dnp != right.Dnp)
            {
                changes["dnp"] = new[] { left.Dnp ? "true" : "false", right.Dnp ? "true" : "false" };
            }

            if (left.ExcludeFromBom != right.ExcludeFromBom)
            {
                changes["excludeFromBom"] = new[] { left.ExcludeFromBom ? "true" : "false", right.ExcludeFromBom ? "true" : "false" };
            }

            return changes;
        }

        private static void AddChange(Dictionary<string, string[]> changes, string name, string? left, string? right)
        {
            if (!KiCadFieldBuilder.Same(left, right))
            {
                changes[name] = new[] { left ?? "", right ?? "" };
            }
        }

        private static string GroupKey(DescribedComponent item)
        {
            ProjectComponent component = item.Component;
            if (component.PartId != null)
            {
                return "part:" + component.PartId.Value.ToString();
            }

            return "fields:" + (component.Value ?? "") + "\u001f" + (component.Footprint ?? "") + "\u001f" + (component.Mpn ?? "");
        }

        private static object Line(List<DescribedComponent> members)
        {
            DescribedComponent first = members[0];
            ProjectComponent component = first.Component;
            int quantity = members.Count(item => !item.Component.Dnp && !item.Component.ExcludeFromBom);
            List<string> references = members.Select(item => item.Component.Reference).Where(reference => reference.Length > 0).ToList();
            List<string> outdated = members.SelectMany(item => item.OutdatedFields).Distinct(StringComparer.Ordinal).ToList();
            string status = members.Select(item => item.Status).FirstOrDefault(value => value == "field_outdated")
                ?? members.Select(item => item.Status).FirstOrDefault(value => value == "eol")
                ?? members.Select(item => item.Status).FirstOrDefault(value => value == "ambiguous")
                ?? first.Status;
            return new
            {
                reference = string.Join(", ", references),
                references,
                value = component.Value ?? "",
                footprint = component.Footprint ?? "",
                mpn = component.Mpn ?? "",
                manufacturer = component.Manufacturer ?? "",
                quantity,
                status,
                dnp = members.All(item => item.Component.Dnp),
                excludeFromBom = members.All(item => item.Component.ExcludeFromBom),
                partId = component.PartId,
                partName = first.PartName ?? "",
                outdatedFields = outdated,
                symbolPath = members.Count == 1 ? component.SymbolPath : null
            };
        }

        private async Task<DescribedComponent> DescribeAsync(ProjectComponent component, CancellationToken cancellationToken)
        {
            if (component.PartId == null)
            {
                bool ambiguous = await _matcher.IsAmbiguousMpnAsync(component.Mpn, component.Manufacturer, cancellationToken);
                return new DescribedComponent(component, ambiguous ? "ambiguous" : "not_in_catalog", new List<string>(), null);
            }

            Part? part = await _db.Part.FirstOrDefaultAsync(p => p.Id == component.PartId, cancellationToken);
            if (part == null)
            {
                return new DescribedComponent(component, "not_in_catalog", new List<string>(), null);
            }

            KiCadHttpPart? library = part.Id == null ? null : await _fields.BuildForPartIdAsync(part.Id.Value, cancellationToken);
            List<string> outdated = library == null ? new List<string>() : KiCadFieldBuilder.DifferingFields(component, library);
            if (outdated.Count > 0)
            {
                return new DescribedComponent(component, "field_outdated", outdated, part.Name);
            }

            if (string.Equals(part.LifecycleStatus, "eol", StringComparison.OrdinalIgnoreCase))
            {
                return new DescribedComponent(component, "eol", outdated, part.Name);
            }

            return new DescribedComponent(component, "ok", outdated, part.Name);
        }

        private sealed class DescribedComponent
        {
            public DescribedComponent(ProjectComponent component, string status, List<string> outdatedFields, string? partName)
            {
                Component = component;
                Status = status;
                OutdatedFields = outdatedFields;
                PartName = partName;
            }

            public ProjectComponent Component { get; }

            public string Status { get; }

            public List<string> OutdatedFields { get; }

            public string? PartName { get; }
        }

        private sealed class OfferLoad
        {
            public Dictionary<string, IReadOnlyList<DistributorProduct>> Cache { get; } = new(StringComparer.OrdinalIgnoreCase);

            public List<string> Warnings { get; } = new();

            public HashSet<string> Warned { get; } = new(StringComparer.OrdinalIgnoreCase);

            public HashSet<string> Stopped { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class SupplyCatalog
        {
            public SupplyCatalog(Dictionary<int, BomPartSupply> parts, List<BomDistributorInfo> distributors)
            {
                Parts = parts;
                Distributors = distributors;
            }

            public Dictionary<int, BomPartSupply> Parts { get; }

            public List<BomDistributorInfo> Distributors { get; }
        }

        private sealed class PreparedLine
        {
            public PreparedLine(
                string reference,
                string manufacturer,
                string mpn,
                int totalQuantity,
                BomPartSupply? supply,
                IReadOnlyList<BomSupplyProbe> probes)
            {
                Reference = reference;
                Manufacturer = manufacturer;
                Mpn = mpn;
                TotalQuantity = totalQuantity;
                Supply = supply;
                Probes = probes;
            }

            public string Reference { get; }

            public string Manufacturer { get; }

            public string Mpn { get; }

            public int TotalQuantity { get; }

            public BomPartSupply? Supply { get; }

            public IReadOnlyList<BomSupplyProbe> Probes { get; }
        }
    }
}
