namespace KiComLibBackend.Services
{
    /// <summary>
    /// Turns submitted stock rows into stored batches. A quantity of zero is the
    /// batch "Empty". Any other name, including "Ordered", keeps a positive quantity.
    /// </summary>
    public static class WarehouseStock
    {
        public const string EmptyBatch = "Empty";

        public const int MaxQuantity = 1_000_000_000;

        public sealed class BatchDraft
        {
            public string Name { get; set; } = "";

            public int Quantity { get; set; }

            public string Location { get; set; } = "";
        }

        public static (IReadOnlyList<BatchDraft> Batches, string? Error) Normalize(IEnumerable<BatchDraft>? batches)
        {
            List<BatchDraft> source = batches?.ToList() ?? new List<BatchDraft>();
            if (source.Count == 0)
            {
                return (new[] { Empty("") }, null);
            }

            List<BatchDraft> stock = new();
            string emptyLocation = "";
            bool sawZero = false;

            foreach (BatchDraft batch in source)
            {
                if (batch.Quantity < 0)
                {
                    return (Array.Empty<BatchDraft>(), "Quantity cannot be negative");
                }

                if (batch.Quantity > MaxQuantity)
                {
                    return (Array.Empty<BatchDraft>(), "Quantity is too large");
                }

                string location = (batch.Location ?? "").Trim();
                if (location.Length > 200)
                {
                    return (Array.Empty<BatchDraft>(), "Location must be at most 200 characters");
                }

                string name = (batch.Name ?? "").Trim();
                bool namedEmpty = name.Equals(EmptyBatch, StringComparison.OrdinalIgnoreCase);
                if (batch.Quantity == 0 || namedEmpty)
                {
                    if (batch.Quantity > 0)
                    {
                        return (Array.Empty<BatchDraft>(), "The batch Empty is reserved for a quantity of zero");
                    }

                    sawZero = true;
                    if (emptyLocation.Length == 0 && location.Length > 0)
                    {
                        emptyLocation = location;
                    }

                    continue;
                }

                if (name.Length == 0)
                {
                    return (Array.Empty<BatchDraft>(), "A batch with a quantity needs a name, for example Ordered");
                }

                if (name.Length > 80)
                {
                    return (Array.Empty<BatchDraft>(), "Batch name must be at most 80 characters");
                }

                BatchDraft? existing = stock.FirstOrDefault(row =>
                    row.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.Location, location, StringComparison.Ordinal));
                if (existing == null)
                {
                    stock.Add(new BatchDraft
                    {
                        Name = name,
                        Quantity = batch.Quantity,
                        Location = location
                    });
                    continue;
                }

                long sum = (long)existing.Quantity + batch.Quantity;
                if (sum > MaxQuantity)
                {
                    return (Array.Empty<BatchDraft>(), "Quantity is too large");
                }

                existing.Quantity = (int)sum;
            }

            if (sawZero || stock.Count == 0)
            {
                stock.Add(Empty(emptyLocation));
            }

            return (stock, null);
        }

        private static BatchDraft Empty(string location)
        {
            return new BatchDraft
            {
                Name = EmptyBatch,
                Quantity = 0,
                Location = location
            };
        }
    }
}
