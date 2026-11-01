using KiComLibBackend.Services;
using KiComLibBackend.Services.Distributors;

namespace KiComLibBackend.Tests;

public class BomAvailabilityTests
{
    [Fact]
    public void Probes_CheckStoredRowThenFavoritesThenOthersThenAlternates()
    {
        BomPartSupply part = Part(
            1,
            "RC0805FR-0710KL",
            "Yageo",
            new[] { 9 },
            orders: new (int Id, int DistributorId, string Number)[]
            {
                (1, 2, "DK-1"),
                (2, 1, "MO-1"),
                (3, 3, "FA-1")
            },
            other: new BomPartNumberSupply
            {
                Id = 8,
                Manufacturer = "Vishay",
                Mpn = "CRCW080510K0FKEA",
                Orders = new[]
                {
                    new BomOrderRow { Id = 4, DistributorId = 5, OrderNumber = "TME-1" }
                }
            });
        BomPartSupply alternative = Part(9, "ALT-1", "Panasonic", Array.Empty<int>(), new[]
        {
            (1, 4, "AR-1")
        });
        Dictionary<int, BomPartSupply> catalog = new()
        {
            [1] = part,
            [9] = alternative
        };

        IReadOnlyList<BomSupplyProbe> probes = BomAvailability.Probes(
            part,
            "RC0805FR-0710KL",
            catalog,
            Distributors(),
            Usable());

        string[] sequence = probes.Select(probe => $"{probe.Kind}:{probe.DistributorName}:{probe.OrderNumber}:{probe.Mpn}").ToArray();
        Assert.Equal(new[]
        {
            "Primary:DigiKey:DK-1:RC0805FR-0710KL",
            "Primary:Farnell:FA-1:RC0805FR-0710KL",
            "Primary:Arrow Electronics::RC0805FR-0710KL",
            "Primary:Mouser Electronics:MO-1:RC0805FR-0710KL",
            "Primary:TME::RC0805FR-0710KL",
            "AlternativeMpn:TME:TME-1:CRCW080510K0FKEA",
            "AlternativeMpn:DigiKey::CRCW080510K0FKEA",
            "AlternativeMpn:Farnell::CRCW080510K0FKEA",
            "AlternativeMpn:Arrow Electronics::CRCW080510K0FKEA",
            "AlternativeMpn:Mouser Electronics::CRCW080510K0FKEA",
            "AlternativePart:Arrow Electronics:AR-1:ALT-1",
            "AlternativePart:DigiKey::ALT-1",
            "AlternativePart:Farnell::ALT-1",
            "AlternativePart:Mouser Electronics::ALT-1",
            "AlternativePart:TME::ALT-1"
        }, sequence);
    }

    [Fact]
    public void Match_UsesStoredOrderNumberAndMarksLimitedStock()
    {
        BomSupplyProbe probe = new()
        {
            Kind = BomSupplyKind.Primary,
            Provider = "digikey",
            DistributorName = "DigiKey",
            OrderNumber = "DK-1",
            StoredOrder = true,
            Manufacturer = "Yageo",
            Mpn = "RC0805FR-0710KL"
        };
        BomAvailabilityHit? shortStock = BomAvailability.Match(probe, new[]
        {
            Offer("DK-1", "RC0805FR-0710KL", 14)
        }, 10);
        Assert.NotNull(shortStock);
        Assert.Equal("ok", shortStock.Status);
        Assert.True(shortStock.Limited);
        Assert.Equal("DK-1", shortStock.OrderNumber);

        BomAvailabilityHit? enough = BomAvailability.Match(probe, new[]
        {
            Offer("DK-1", "RC0805FR-0710KL", 15)
        }, 10);
        Assert.NotNull(enough);
        Assert.False(enough.Limited);

        Assert.Null(BomAvailability.Match(probe, new[]
        {
            Offer("DK-1", "RC0805FR-0710KL", 9)
        }, 10));
    }

    [Fact]
    public void Match_OpenSearchPicksTheOrderNumberWithEnoughStock()
    {
        BomSupplyProbe probe = new()
        {
            Kind = BomSupplyKind.AlternativeMpn,
            Provider = "mouser",
            DistributorName = "Mouser Electronics",
            OrderNumber = "",
            StoredOrder = false,
            Manufacturer = "Vishay",
            Mpn = "CRCW080510K0FKEA"
        };
        BomAvailabilityHit? hit = BomAvailability.Match(probe, new[]
        {
            Offer("OTHER", "OTHER-MPN", 100),
            Offer("MO-LOW", "CRCW080510K0FKEA", 4),
            Offer("MO-OK", "CRCW080510K0FKEA", 20)
        }, 10);
        Assert.NotNull(hit);
        Assert.Equal("alternative_mpn", hit.Status);
        Assert.Equal("MO-OK", hit.OrderNumber);
        Assert.Equal("Vishay", hit.Manufacturer);
    }

    [Fact]
    public void Identity_KeepsTheSchematicPartUntilAnAlternativeIsChosen()
    {
        BomPartSupply supply = Part(1, "PRIMARY", "Yageo", Array.Empty<int>(), Array.Empty<(int, int, string)>());
        (string manufacturer, string mpn) = BomAvailability.Identity("Schematic", "PRIMARY", supply, null);
        Assert.Equal("Schematic", manufacturer);
        Assert.Equal("PRIMARY", mpn);

        BomAvailabilityHit hit = new()
        {
            Kind = BomSupplyKind.AlternativePart,
            Distributor = "TME",
            OrderNumber = "T-1",
            Manufacturer = "Panasonic",
            Mpn = "ALT-1",
            QuantityAvailable = 3,
            Limited = true
        };
        (manufacturer, mpn) = BomAvailability.Identity("Schematic", "PRIMARY", supply, hit);
        Assert.Equal("Panasonic", manufacturer);
        Assert.Equal("ALT-1", mpn);
    }

    [Fact]
    public void MouserPackQueries_FillsEachRequestWithTenPartNumbers()
    {
        string[] parts = Enumerable.Range(1, 11).Select(index => "MPN-" + index).ToArray();
        IReadOnlyList<string> packed = MouserClient.PackQueries(parts);
        Assert.Equal(2, packed.Count);
        Assert.Equal(10, packed[0].Split('|').Length);
        Assert.Equal("MPN-11", packed[1]);
        Assert.Equal(new[] { "LM358" }, MouserClient.PackQueries(new[] { "LM358", "lm358", "  " }));
    }

    [Fact]
    public void MouserAssign_SplitsOneResponseAcrossTheQueriedPartNumbers()
    {
        const string json = """
            {
              "SearchResults": {
                "Parts": [
                  { "ManufacturerPartNumber": "LM358", "MouserPartNumber": "595-LM358", "Availability": "10 In Stock" },
                  { "ManufacturerPartNumber": "RC0805", "MouserPartNumber": "603-RC0805", "AvailabilityInStock": "4" }
                ]
              }
            }
            """;

        IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>> assigned =
            MouserClient.Assign(json, new[] { "LM358", "RC0805" });
        Assert.Equal("595-LM358", Assert.Single(assigned["LM358"]).OrderNumber);
        Assert.Equal(4, Assert.Single(assigned["RC0805"]).QuantityAvailable);
    }

    [Fact]
    public void MouserDescribeFailure_ReportsAnExhaustedRequestLimit()
    {
        const string body = """
            { "Errors": [ { "Code": "MaximumNumberOfCallsExceeded", "Message": "Maximum calls per minute exceeded." } ] }
            """;
        Assert.Equal(
            "The API request limit is exhausted: Maximum calls per minute exceeded.",
            MouserClient.DescribeFailure(403, body));
        Assert.Equal("The API request limit is exhausted.", MouserClient.DescribeFailure(429, ""));
        DistributorUpstreamException error = Assert.Throws<DistributorUpstreamException>(() => MouserClient.Parse(body));
        Assert.Contains("request limit is exhausted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FarnellPackQueries_FillsEachRequestWithTenPartNumbers()
    {
        string[] parts = Enumerable.Range(1, 11).Select(index => "MPN-" + index).ToArray();
        IReadOnlyList<IReadOnlyList<string>> packed = FarnellClient.PackQueries(parts);
        Assert.Equal(2, packed.Count);
        Assert.Equal(10, packed[0].Count);
        Assert.Equal("MPN-11", Assert.Single(packed[1]));
        Assert.Equal(2, FarnellClient.PackQueries(new[] { "A|B", "LM358" }).Count);
    }

    [Fact]
    public void FarnellAssign_SplitsOneResponseAndReportsARequestLimit()
    {
        const string json = """
            { "manufacturerPartNumberSearchReturn": { "products": [
              { "manufacturerPartNumber": "LM358", "sku": "111", "stock": { "level": 9 } },
              { "manufacturerPartNumber": "RC0805", "sku": "222", "inv": 3 }
            ] } }
            """;
        IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>> assigned =
            FarnellClient.Assign(json, new[] { "LM358", "RC0805" });
        Assert.Equal("111", Assert.Single(assigned["LM358"]).OrderNumber);
        Assert.Equal(3, Assert.Single(assigned["RC0805"]).QuantityAvailable);

        string url = FarnellClient.PartNumbersUrl("de.farnell.com", "secret", new[] { "LM358", "RC0805" });
        Assert.Contains("term=manuPartNum:LM358|RC0805", url, StringComparison.Ordinal);
        Assert.Contains("numberOfResults=100", url, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", DistributorLog.Sanitize(url), StringComparison.Ordinal);

        const string limited = """{ "errorMessage": "The maximum number of requests has been exceeded" }""";
        Assert.Equal(
            "The API request limit is exhausted: The maximum number of requests has been exceeded",
            FarnellClient.DescribeFailure(403, limited));
        DistributorUpstreamException error = Assert.Throws<DistributorUpstreamException>(() =>
            FarnellClient.Assign(limited, new[] { "LM358" }));
        Assert.Contains("request limit is exhausted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArrowPackQueries_FillsEachListAndReportsARequestLimit()
    {
        string[] parts = Enumerable.Range(1, 11).Select(index => "MPN-" + index).ToArray();
        IReadOnlyList<IReadOnlyList<string>> packed = ArrowClient.PackQueries(parts);
        Assert.Equal(2, packed.Count);
        Assert.Equal(10, packed[0].Count);

        const string json = """
            { "results": [
              { "partNum": "AR-1", "mfrPartNum": "LM358", "fohQty": 6 },
              { "partNum": "AR-2", "mfrPartNum": "RC0805", "fohQty": 2 }
            ] }
            """;
        IReadOnlyDictionary<string, IReadOnlyList<DistributorProduct>> assigned =
            ArrowClient.Assign(json, new[] { "LM358", "RC0805" });
        Assert.Equal("AR-1", Assert.Single(assigned["LM358"]).OrderNumber);
        Assert.Equal(2, Assert.Single(assigned["RC0805"]).QuantityAvailable);

        string url = ArrowClient.ListRequestUrl(
            new ArrowAccess { Login = "user", ApiKey = "secret" },
            new[] { "LM358", "RC0805" });
        string decoded = Uri.UnescapeDataString(url);
        Assert.Contains("\"useExact\":true", decoded, StringComparison.Ordinal);
        Assert.Contains("LM358", decoded, StringComparison.Ordinal);
        Assert.Contains("rows=25", url, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", DistributorLog.Sanitize(url), StringComparison.Ordinal);

        Assert.Equal(
            "The API request limit is exhausted: Rate limit exceeded",
            ArrowClient.DescribeFailure(429, """{ "message": "Rate limit exceeded" }"""));
        DistributorUpstreamException error = Assert.Throws<DistributorUpstreamException>(() =>
            ArrowClient.Assign("""{ "message": "Rate limit exceeded" }""", new[] { "LM358" }));
        Assert.Contains("request limit is exhausted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DigiKeyAndTmeDescribeFailure_ReportAnExhaustedRequestLimit()
    {
        const string digikey = """
            { "StatusCode": 429, "ErrorMessage": "Too Many Requests", "ErrorDetails": "Rate limit is exceeded. Try again in 30 seconds." }
            """;
        Assert.Equal(
            "The API request limit is exhausted: Rate limit is exceeded. Try again in 30 seconds.",
            DigiKeyClient.DescribeFailure(429, digikey));
        Assert.Equal("The API request limit is exhausted.", DigiKeyClient.DescribeFailure(429, ""));

        const string tme = """
            { "status": "E_LIMIT", "error": { "code": "E_LIMIT", "message": "Too many requests" } }
            """;
        Assert.Equal(
            "The API request limit is exhausted: Too many requests",
            TmeClient.DescribeFailure(200, tme));
        Assert.Equal(
            "The API request limit is exhausted: E_LIMIT",
            TmeClient.DescribeFailure(200, """{ "status": "E_LIMIT" }"""));
    }

    [Theory]
    [InlineData("1,679 In Stock", 1679)]
    [InlineData("0 In Stock", 0)]
    [InlineData("On Order", null)]
    public void CountInText_ReadsAStockPhrase(string text, int? expected)
    {
        Assert.Equal(expected, DistributorJson.CountInText(text));
    }

    [Fact]
    public void DistributorParses_ReadStock()
    {
        DistributorProduct mouser = Assert.Single(MouserClient.Parse("""
            { "SearchResults": { "Parts": [ { "ManufacturerPartNumber": "LM358", "MouserPartNumber": "595-LM358", "Availability": "1,200 In Stock" } ] } }
            """));
        Assert.Equal(1200, mouser.QuantityAvailable);

        DistributorProduct digikey = Assert.Single(DigiKeyClient.Parse("""
            { "Products": [ { "ManufacturerProductNumber": "LM358", "DigiKeyProductNumber": "296-1-ND", "QuantityAvailable": 40, "ProductVariations": [ { "DigiKeyProductNumber": "296-1-ND", "QuantityAvailableforPackageType": 12 } ] } ] }
            """));
        Assert.Equal(12, digikey.QuantityAvailable);

        DistributorProduct farnell = Assert.Single(FarnellClient.Parse("""
            { "products": [ { "manufacturerPartNumber": "LM358", "sku": "123", "stock": { "level": 8 } } ] }
            """));
        Assert.Equal(8, farnell.QuantityAvailable);

        DistributorProduct arrow = Assert.Single(ArrowClient.ParseProducts("""
            { "partNum": "AR-1", "mfrPartNum": "LM358", "InvOrg": { "webSites": [ { "sources": [ { "sourceParts": [ { "sourcePartId": "AR-1", "Availability": [ { "fohQty": 6 }, { "fohQty": 4 } ] } ] } ] } ] } }
            """));
        Assert.Equal(10, arrow.QuantityAvailable);

        IReadOnlyDictionary<string, int> tme = TmeClient.ParseStock("""
            { "status": "OK", "data": { "elements": [ { "symbol": "TME-1", "amount": 25, "priceList": [ { "amount": 1 } ] } ] } }
            """);
        Assert.Equal(25, tme["TME-1"]);
    }

    private static BomStockOffer Offer(string orderNumber, string mpn, int available)
    {
        return new BomStockOffer
        {
            OrderNumber = orderNumber,
            Mpn = mpn,
            QuantityAvailable = available
        };
    }

    private static HashSet<string> Usable()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mouser", "digikey", "farnell", "arrow", "tme"
        };
    }

    private static List<BomDistributorInfo> Distributors()
    {
        return new List<BomDistributorInfo>
        {
            Info(1, "Mouser Electronics", false, "mouser"),
            Info(2, "DigiKey", true, "digikey"),
            Info(3, "Farnell", true, "farnell"),
            Info(4, "Arrow Electronics", false, "arrow"),
            Info(5, "TME", false, "tme"),
            Info(6, "RS Components", true, null)
        };
    }

    private static BomDistributorInfo Info(int id, string name, bool favorite, string? provider)
    {
        return new BomDistributorInfo
        {
            Id = id,
            Name = name,
            Favorite = favorite,
            Provider = provider
        };
    }

    private static BomPartSupply Part(
        int id,
        string mpn,
        string manufacturer,
        IReadOnlyList<int> alternatives,
        IReadOnlyList<(int Id, int DistributorId, string Number)> orders,
        BomPartNumberSupply? other = null)
    {
        List<BomPartNumberSupply> numbers = new()
        {
            new BomPartNumberSupply
            {
                Id = 1,
                Manufacturer = manufacturer,
                Mpn = mpn,
                Orders = orders.Select(order => new BomOrderRow
                {
                    Id = order.Id,
                    DistributorId = order.DistributorId,
                    OrderNumber = order.Number
                }).ToList()
            }
        };
        if (other != null)
        {
            numbers.Add(other);
        }

        return new BomPartSupply
        {
            PartId = id,
            PartNumbers = numbers,
            AlternativePartIds = alternatives
        };
    }
}
