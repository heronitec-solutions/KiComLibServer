using KiComLibBackend.Models;
using KiComLibBackend.Services;

namespace KiComLibBackend.Tests
{
    public class WarehouseStockTests
    {
        [Fact]
        public void MissingBatchesBecomeEmpty()
        {
            (IReadOnlyList<WarehouseStock.BatchDraft> batches, string? error) = WarehouseStock.Normalize(null);

            Assert.Null(error);
            Assert.Single(batches);
            Assert.Equal(WarehouseStock.EmptyBatch, batches[0].Name);
            Assert.Equal(0, batches[0].Quantity);
        }

        [Fact]
        public void ZeroQuantityIsRenamedToEmpty()
        {
            (IReadOnlyList<WarehouseStock.BatchDraft> batches, string? error) = WarehouseStock.Normalize(new[]
            {
                Draft("Incoming", 0, "Dock")
            });

            Assert.Null(error);
            Assert.Single(batches);
            Assert.Equal(WarehouseStock.EmptyBatch, batches[0].Name);
            Assert.Equal("Dock", batches[0].Location);
        }

        [Fact]
        public void OrderedStaysBesideAnEmptyBatch()
        {
            (IReadOnlyList<WarehouseStock.BatchDraft> batches, string? error) = WarehouseStock.Normalize(new[]
            {
                Draft("Ordered", 12, "PO-14"),
                Draft("Shelf", 0, "A-3")
            });

            Assert.Null(error);
            Assert.Equal(2, batches.Count);
            Assert.Equal("Ordered", batches[0].Name);
            Assert.Equal(12, batches[0].Quantity);
            Assert.Equal(WarehouseStock.EmptyBatch, batches[1].Name);
            Assert.Equal("A-3", batches[1].Location);
        }

        [Fact]
        public void PositiveStockDoesNotInventAnEmptyBatch()
        {
            (IReadOnlyList<WarehouseStock.BatchDraft> batches, string? error) = WarehouseStock.Normalize(new[]
            {
                Draft("Ordered", 4, "PO-2"),
                Draft("Received", 8, "B-1")
            });

            Assert.Null(error);
            Assert.Equal(2, batches.Count);
            Assert.DoesNotContain(batches, batch => batch.Name == WarehouseStock.EmptyBatch);
        }

        [Fact]
        public void SameNameAndLocationAreMerged()
        {
            (IReadOnlyList<WarehouseStock.BatchDraft> batches, string? error) = WarehouseStock.Normalize(new[]
            {
                Draft("Ordered", 2, "PO-1"),
                Draft("ordered", 3, "PO-1")
            });

            Assert.Null(error);
            Assert.Single(batches);
            Assert.Equal(5, batches[0].Quantity);
        }

        [Fact]
        public void EmptyNameCannotHoldQuantity()
        {
            (IReadOnlyList<WarehouseStock.BatchDraft> _, string? error) = WarehouseStock.Normalize(new[]
            {
                Draft("Empty", 3, "")
            });

            Assert.Equal("The batch Empty is reserved for a quantity of zero", error);
        }

        [Fact]
        public void WarehouseRightsStayIndependentOfTheCatalog()
        {
            AccessRights rights = AccessService.RightsOf(new[]
            {
                new AppGroup
                {
                    CanRead = true,
                    CanWrite = false,
                    CanReadWarehouse = false,
                    CanWriteWarehouse = true
                },
                new AppGroup
                {
                    CanRead = false,
                    CanWrite = false,
                    CanReadWarehouse = true,
                    CanWriteWarehouse = false
                }
            });

            Assert.True(rights.CanRead);
            Assert.False(rights.CanWrite);
            Assert.False(rights.CanAdmin);
            Assert.True(rights.CanReadWarehouse);
            Assert.True(rights.CanWriteWarehouse);
        }

        private static WarehouseStock.BatchDraft Draft(string name, int quantity, string location)
        {
            return new WarehouseStock.BatchDraft
            {
                Name = name,
                Quantity = quantity,
                Location = location
            };
        }
    }
}
