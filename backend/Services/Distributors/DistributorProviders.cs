namespace KiComLibBackend.Services.Distributors
{
    public static class DistributorNames
    {
        public const string Mouser = "Mouser Electronics";
        public const string Arrow = "Arrow Electronics";
        public const string DigiKey = "DigiKey";
        public const string Farnell = "Farnell";
        public const string Tme = "TME";
    }

    public sealed class DistributorProviderInfo
    {
        public DistributorProviderInfo(string id, string name, string distributorName)
        {
            Id = id;
            Name = name;
            DistributorName = distributorName;
        }

        public string Id { get; }

        /// <summary>Short name shown in the provider list.</summary>
        public string Name { get; }

        /// <summary>Catalog distributor name the UI matches against.</summary>
        public string DistributorName { get; }
    }

    public static class DistributorProviders
    {
        public const string Mouser = "mouser";
        public const string DigiKey = "digikey";
        public const string Farnell = "farnell";
        public const string Arrow = "arrow";
        public const string Tme = "tme";

        public static readonly IReadOnlyList<DistributorProviderInfo> All = new DistributorProviderInfo[]
        {
            new(Mouser, "Mouser", DistributorNames.Mouser),
            new(DigiKey, "DigiKey", DistributorNames.DigiKey),
            new(Farnell, "Farnell", DistributorNames.Farnell),
            new(Arrow, "Arrow", DistributorNames.Arrow),
            new(Tme, "TME", DistributorNames.Tme)
        };

        public static bool IsKnown(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            foreach (DistributorProviderInfo provider in All)
            {
                if (provider.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string? Canonical(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            foreach (DistributorProviderInfo provider in All)
            {
                if (provider.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return provider.Id;
                }
            }

            return null;
        }
    }
}
