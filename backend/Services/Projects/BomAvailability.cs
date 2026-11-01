namespace KiComLibBackend.Services
{
    public enum BomSupplyKind
    {
        Primary,
        AlternativeMpn,
        AlternativePart
    }

    public sealed class BomDistributorInfo
    {
        public int Id { get; init; }

        public required string Name { get; init; }

        public bool Favorite { get; init; }

        public string? Provider { get; init; }
    }

    public sealed class BomOrderRow
    {
        public int Id { get; init; }

        public int DistributorId { get; init; }

        public required string OrderNumber { get; init; }
    }

    public sealed class BomPartNumberSupply
    {
        public int Id { get; init; }

        public required string Manufacturer { get; init; }

        public required string Mpn { get; init; }

        public required IReadOnlyList<BomOrderRow> Orders { get; init; }
    }

    public sealed class BomPartSupply
    {
        public int PartId { get; init; }

        public required IReadOnlyList<BomPartNumberSupply> PartNumbers { get; init; }

        public required IReadOnlyList<int> AlternativePartIds { get; init; }
    }

    public sealed class BomSupplyProbe
    {
        public BomSupplyKind Kind { get; init; }

        public required string Provider { get; init; }

        public required string DistributorName { get; init; }

        public required string OrderNumber { get; init; }

        public bool StoredOrder { get; init; }

        public required string Manufacturer { get; init; }

        public required string Mpn { get; init; }
    }

    public sealed class BomStockOffer
    {
        public required string OrderNumber { get; init; }

        public string? Mpn { get; init; }

        public int? QuantityAvailable { get; init; }
    }

    public sealed class BomAvailabilityHit
    {
        public BomSupplyKind Kind { get; init; }

        public required string Distributor { get; init; }

        public required string OrderNumber { get; init; }

        public required string Manufacturer { get; init; }

        public required string Mpn { get; init; }

        public int QuantityAvailable { get; init; }

        public bool Limited { get; init; }

        public string Status => Kind switch
        {
            BomSupplyKind.AlternativeMpn => "alternative_mpn",
            BomSupplyKind.AlternativePart => "alternative_part",
            _ => "ok"
        };
    }

    /// <summary>
    /// Order in which a BOM line asks distributors for stock, and how a reply becomes a status.
    /// </summary>
    public static class BomAvailability
    {
        public const double LimitedFactor = 1.5;

        public static bool IsLimited(int available, int needed)
        {
            if (needed <= 0)
            {
                return false;
            }

            return available < needed * LimitedFactor;
        }

        public static BomPartNumberSupply? Primary(BomPartSupply part, string? schematicMpn)
        {
            if (!string.IsNullOrWhiteSpace(schematicMpn))
            {
                string wanted = schematicMpn.Trim();
                BomPartNumberSupply? match = part.PartNumbers.FirstOrDefault(row =>
                    row.Mpn.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            return part.PartNumbers.OrderBy(row => row.Id).FirstOrDefault();
        }

        public static (string Manufacturer, string Mpn) Identity(
            string? schematicManufacturer,
            string? schematicMpn,
            BomPartSupply? supply,
            BomAvailabilityHit? hit)
        {
            BomPartNumberSupply? primary = supply == null ? null : Primary(supply, schematicMpn);
            string manufacturer = First(schematicManufacturer, primary?.Manufacturer);
            string mpn = First(schematicMpn, primary?.Mpn);
            if (hit != null && hit.Kind != BomSupplyKind.Primary)
            {
                if (!string.IsNullOrWhiteSpace(hit.Manufacturer))
                {
                    manufacturer = hit.Manufacturer.Trim();
                }

                if (!string.IsNullOrWhiteSpace(hit.Mpn))
                {
                    mpn = hit.Mpn.Trim();
                }
            }

            return (manufacturer, mpn);
        }

        public static IReadOnlyList<BomSupplyProbe> Probes(
            BomPartSupply part,
            string? schematicMpn,
            IReadOnlyDictionary<int, BomPartSupply> catalog,
            IReadOnlyList<BomDistributorInfo> distributors,
            IReadOnlySet<string> usableProviders)
        {
            List<BomSupplyProbe> probes = new();
            AddPart(probes, part, schematicMpn, distributors, usableProviders, alternativePart: false);
            HashSet<int> seen = new() { part.PartId };
            foreach (int alternativeId in part.AlternativePartIds)
            {
                if (!seen.Add(alternativeId) || !catalog.TryGetValue(alternativeId, out BomPartSupply? alternative))
                {
                    continue;
                }

                AddPart(probes, alternative, null, distributors, usableProviders, alternativePart: true);
            }

            return probes;
        }

        public static BomAvailabilityHit? Match(BomSupplyProbe probe, IReadOnlyList<BomStockOffer> offers, int needed)
        {
            if (needed <= 0)
            {
                return null;
            }

            if (probe.StoredOrder)
            {
                BomStockOffer? stored = offers
                    .Where(offer => Same(offer.OrderNumber, probe.OrderNumber) && offer.QuantityAvailable != null)
                    .OrderByDescending(offer => offer.QuantityAvailable)
                    .FirstOrDefault();
                if (stored?.QuantityAvailable == null || stored.QuantityAvailable.Value < needed)
                {
                    return null;
                }

                return Hit(probe, probe.OrderNumber, stored.QuantityAvailable.Value, needed);
            }

            BomStockOffer? open = offers
                .Where(offer => !string.IsNullOrWhiteSpace(offer.OrderNumber)
                    && Same(offer.Mpn, probe.Mpn)
                    && offer.QuantityAvailable != null
                    && offer.QuantityAvailable.Value >= needed)
                .OrderByDescending(offer => offer.QuantityAvailable)
                .FirstOrDefault();
            if (open?.QuantityAvailable == null)
            {
                return null;
            }

            return Hit(probe, open.OrderNumber.Trim(), open.QuantityAvailable.Value, needed);
        }

        private static void AddPart(
            List<BomSupplyProbe> probes,
            BomPartSupply part,
            string? schematicMpn,
            IReadOnlyList<BomDistributorInfo> distributors,
            IReadOnlySet<string> usableProviders,
            bool alternativePart)
        {
            BomPartNumberSupply? primary = Primary(part, schematicMpn);
            if (primary == null)
            {
                return;
            }

            BomSupplyKind primaryKind = alternativePart ? BomSupplyKind.AlternativePart : BomSupplyKind.Primary;
            AddMpn(probes, primary, primaryKind, distributors, usableProviders);
            foreach (BomPartNumberSupply other in part.PartNumbers.Where(row => row.Id != primary.Id).OrderBy(row => row.Id))
            {
                BomSupplyKind kind = alternativePart ? BomSupplyKind.AlternativePart : BomSupplyKind.AlternativeMpn;
                AddMpn(probes, other, kind, distributors, usableProviders);
            }
        }

        private static void AddMpn(
            List<BomSupplyProbe> probes,
            BomPartNumberSupply partNumber,
            BomSupplyKind kind,
            IReadOnlyList<BomDistributorInfo> distributors,
            IReadOnlySet<string> usableProviders)
        {
            if (string.IsNullOrWhiteSpace(partNumber.Mpn))
            {
                return;
            }

            List<BomOrderRow> stored = partNumber.Orders
                .Where(row => !string.IsNullOrWhiteSpace(row.OrderNumber))
                .OrderBy(row => row.Id)
                .ToList();
            Dictionary<int, BomDistributorInfo> byId = new();
            foreach (BomDistributorInfo distributor in distributors)
            {
                byId.TryAdd(distributor.Id, distributor);
            }

            HashSet<int> seen = new();
            if (stored.Count > 0)
            {
                BomOrderRow first = stored[0];
                seen.Add(first.DistributorId);
                if (byId.TryGetValue(first.DistributorId, out BomDistributorInfo? distributor) && Usable(distributor, usableProviders))
                {
                    probes.Add(Stored(first, distributor, partNumber, kind));
                }
            }

            foreach (BomDistributorInfo distributor in Ordered(distributors, usableProviders, favorite: true))
            {
                if (!seen.Add(distributor.Id))
                {
                    continue;
                }

                probes.Add(ForDistributor(distributor, stored, partNumber, kind));
            }

            foreach (BomDistributorInfo distributor in Ordered(distributors, usableProviders, favorite: false))
            {
                if (!seen.Add(distributor.Id))
                {
                    continue;
                }

                probes.Add(ForDistributor(distributor, stored, partNumber, kind));
            }
        }

        private static IEnumerable<BomDistributorInfo> Ordered(
            IReadOnlyList<BomDistributorInfo> distributors,
            IReadOnlySet<string> usableProviders,
            bool favorite)
        {
            return distributors
                .Where(distributor => distributor.Favorite == favorite && Usable(distributor, usableProviders))
                .OrderBy(distributor => distributor.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static bool Usable(BomDistributorInfo distributor, IReadOnlySet<string> usableProviders)
        {
            return distributor.Provider != null && usableProviders.Contains(distributor.Provider);
        }

        private static BomSupplyProbe ForDistributor(
            BomDistributorInfo distributor,
            IReadOnlyList<BomOrderRow> stored,
            BomPartNumberSupply partNumber,
            BomSupplyKind kind)
        {
            BomOrderRow? row = stored.FirstOrDefault(order => order.DistributorId == distributor.Id);
            if (row != null)
            {
                return Stored(row, distributor, partNumber, kind);
            }

            return new BomSupplyProbe
            {
                Kind = kind,
                Provider = distributor.Provider ?? "",
                DistributorName = distributor.Name,
                OrderNumber = "",
                StoredOrder = false,
                Manufacturer = partNumber.Manufacturer,
                Mpn = partNumber.Mpn
            };
        }

        private static BomSupplyProbe Stored(
            BomOrderRow row,
            BomDistributorInfo distributor,
            BomPartNumberSupply partNumber,
            BomSupplyKind kind)
        {
            return new BomSupplyProbe
            {
                Kind = kind,
                Provider = distributor.Provider ?? "",
                DistributorName = distributor.Name,
                OrderNumber = row.OrderNumber.Trim(),
                StoredOrder = true,
                Manufacturer = partNumber.Manufacturer,
                Mpn = partNumber.Mpn
            };
        }

        private static BomAvailabilityHit Hit(BomSupplyProbe probe, string orderNumber, int available, int needed)
        {
            return new BomAvailabilityHit
            {
                Kind = probe.Kind,
                Distributor = probe.DistributorName,
                OrderNumber = orderNumber,
                Manufacturer = probe.Manufacturer,
                Mpn = probe.Mpn,
                QuantityAvailable = available,
                Limited = IsLimited(available, needed)
            };
        }

        private static bool Same(string? left, string? right)
        {
            return string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string First(string? preferred, string? fallback)
        {
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                return preferred.Trim();
            }

            return string.IsNullOrWhiteSpace(fallback) ? "" : fallback.Trim();
        }
    }
}
