using System.Text;
using System.Text.Json;
using KiComLibBackend.Services;
using KiComLibBackend.Services.Distributors;

namespace KiComLibBackend.Tests;

public class DistributorTests
{
    [Theory]
    [InlineData("End of Life", false, "eol")]
    [InlineData("obsolete", false, "eol")]
    [InlineData("Discontinued", false, "eol")]
    [InlineData("NRND", false, "nrnd")]
    [InlineData("Not Recommended for New Designs", false, "nrnd")]
    [InlineData("Not For New Designs", false, "nrnd")]
    [InlineData("Last Time Buy", false, "ltb")]
    [InlineData("LTB", false, "ltb")]
    [InlineData("Active", false, "active")]
    [InlineData("New Product", false, "active")]
    [InlineData("In Production", false, "active")]
    [InlineData("Inactive", false, null)]
    [InlineData("Custom", false, null)]
    [InlineData(null, false, null)]
    [InlineData(null, true, "eol")]
    [InlineData("Active", true, "eol")]
    public void LifecycleMapper_MapsKnownPhrases(string? status, bool discontinued, string? expected)
    {
        Assert.Equal(expected, LifecycleMapper.Map(status, discontinued));
    }

    [Fact]
    public void OrderQuantity_DefaultsToOneOnlyWhenAnOrderNumberIsPresent()
    {
        DistributorProduct withOrder = DistributorProduct.Create(
            "mouser", "Mouser Electronics", "TI", "LM358", null, null, null, null, null, "595-LM358", null, null);
        Assert.Equal(1, withOrder.QuantityMin);
        Assert.Equal(1, withOrder.QuantitySteps);

        DistributorProduct withoutOrder = DistributorProduct.Create(
            "mouser", "Mouser Electronics", null, "LM358DR2", null, null, null, null, null, null, 5, 5);
        Assert.Null(withoutOrder.OrderNumber);
        Assert.Null(withoutOrder.QuantityMin);
        Assert.Null(withoutOrder.QuantitySteps);
    }

    [Fact]
    public void MouserParse_MapsEndOfLife_SuggestedReplacement_AndDifferentPackaging()
    {
        const string json = """
            {
              "SearchResults": {
                "Parts": [
                  {
                    "Manufacturer": "Texas Instruments",
                    "ManufacturerPartNumber": "LM358DR",
                    "Description": "Op amp",
                    "DataSheetUrl": "https://example.com/lm358.pdf",
                    "ProductDetailUrl": "https://www.mouser.com/ProductDetail/lm358",
                    "ImagePath": "/images/lm358.jpg",
                    "LifecycleStatus": "End of Life",
                    "MouserPartNumber": "595-LM358DR",
                    "Min": "1",
                    "Mult": "10",
                    "SuggestedReplacement": "LM358DR2",
                    "AlternatePackagings": [
                      { "APMfrPN": "LM358DT", "AlternatePackaging": "Reel" },
                      { "APMfrPN": "LM358DR" }
                    ]
                  }
                ]
              }
            }
            """;

        IReadOnlyList<DistributorProduct> products = MouserClient.Parse(json);

        Assert.Equal(3, products.Count);
        DistributorProduct main = products[0];
        Assert.Equal("mouser", main.Provider);
        Assert.Equal("Mouser Electronics", main.DistributorName);
        Assert.Equal("Texas Instruments", main.Manufacturer);
        Assert.Equal("LM358DR", main.ManufacturerPartNumber);
        Assert.Equal("eol", main.LifecycleStatus);
        Assert.Equal("595-LM358DR", main.OrderNumber);
        Assert.Equal(1, main.QuantityMin);
        Assert.Equal(10, main.QuantitySteps);
        Assert.Null(main.DatasheetUrl);
        Assert.Null(main.ImageUrl);

        DistributorProduct replacement = Assert.Single(products, product => product.ManufacturerPartNumber == "LM358DR2");
        Assert.Null(replacement.Manufacturer);
        Assert.Null(replacement.OrderNumber);
        Assert.Null(replacement.QuantityMin);
        Assert.Null(replacement.QuantitySteps);

        DistributorProduct alternate = Assert.Single(products, product => product.ManufacturerPartNumber == "LM358DT");
        Assert.Equal("Reel", alternate.Package);
        Assert.Null(alternate.OrderNumber);
    }

    [Fact]
    public void DigiKeyParse_MapsObsoleteProductAndAlternate()
    {
        const string json = """
            {
              "Products": [
                {
                  "Manufacturer": { "Name": "Texas Instruments" },
                  "ManufacturerProductNumber": "LM358DR",
                  "Description": { "ProductDescription": "IC OPAMP" },
                  "DatasheetUrl": "https://example.com/lm358.pdf",
                  "ProductUrl": "https://www.digikey.com/en/products/detail/lm358",
                  "PhotoUrl": "https://media.digikey.com/photos/lm358.jpg",
                  "ProductStatus": { "Status": "Obsolete" },
                  "DigiKeyProductNumber": "296-1395-1-ND",
                  "MinimumOrderQuantity": 5,
                  "QuantityMultiple": 5,
                  "AlternateProducts": [
                    { "ManufacturerName": "onsemi", "ManufacturerProductNumber": "LM358DR2G" }
                  ]
                }
              ]
            }
            """;

        IReadOnlyList<DistributorProduct> products = DigiKeyClient.Parse(json);

        DistributorProduct main = products[0];
        Assert.Equal("digikey", main.Provider);
        Assert.Equal("DigiKey", main.DistributorName);
        Assert.Equal("Texas Instruments", main.Manufacturer);
        Assert.Equal("LM358DR", main.ManufacturerPartNumber);
        Assert.Equal("eol", main.LifecycleStatus);
        Assert.Equal("296-1395-1-ND", main.OrderNumber);
        Assert.Equal(5, main.QuantityMin);
        Assert.Equal(5, main.QuantitySteps);
        Assert.Equal("IC OPAMP", main.Description);
        Assert.Equal("https://media.digikey.com/photos/lm358.jpg", main.ImageUrl);

        DistributorProduct alternate = Assert.Single(products, product => product.ManufacturerPartNumber == "LM358DR2G");
        Assert.Equal("onsemi", alternate.Manufacturer);
        Assert.Null(alternate.OrderNumber);
        Assert.Null(alternate.QuantityMin);
    }

    [Fact]
    public void DigiKeySettings_UseProviderIdInJson()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        DistributorSettingsView view = new()
        {
            DigiKey = new DigiKeySettingsView
            {
                Enabled = true,
                ClientId = "client-1",
                ClientSecretSet = true,
                Locale = "de"
            }
        };

        string json = JsonSerializer.Serialize(view, options);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("digikey", out JsonElement digikey));
        Assert.False(document.RootElement.TryGetProperty("digiKey", out _));
        Assert.True(digikey.GetProperty("enabled").GetBoolean());
        Assert.Equal("client-1", digikey.GetProperty("clientId").GetString());
        Assert.True(digikey.GetProperty("clientSecretSet").GetBoolean());
        Assert.Equal("de", digikey.GetProperty("locale").GetString());

        const string writeJson = """
            {
              "lookupProvider": "digikey",
              "mouser": { "enabled": false },
              "digikey": {
                "enabled": true,
                "clientId": "client-1",
                "clientSecret": "secret",
                "clearClientSecret": false,
                "locale": "us"
              },
              "farnell": { "enabled": false },
              "arrow": { "enabled": false },
              "tme": { "enabled": false }
            }
            """;

        DistributorSettingsWriteRequest? request = JsonSerializer.Deserialize<DistributorSettingsWriteRequest>(writeJson, options);
        Assert.NotNull(request);
        Assert.NotNull(request.DigiKey);
        Assert.True(request.DigiKey.Enabled);
        Assert.Equal("client-1", request.DigiKey.ClientId);
        Assert.Equal("secret", request.DigiKey.ClientSecret);
        Assert.False(request.DigiKey.ClearClientSecret);
        Assert.Equal("us", request.DigiKey.Locale);
    }

    [Fact]
    public void FarnellSearchUrl_UsesStoreIdAndMediumResponse()
    {
        string url = FarnellClient.SearchUrl("de.farnell.com", "test-key", "LM358");

        Assert.Contains("storeInfo.id=de.farnell.com", url, StringComparison.Ordinal);
        Assert.Contains("resultsSettings.responseGroup=medium", url, StringComparison.Ordinal);
        Assert.DoesNotContain("storeInfo=de.farnell.com", url, StringComparison.Ordinal);
        Assert.Contains("callInfo.apiKey=test-key", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("newark.com", "www.newark.com")]
    [InlineData("WWW.NEWARK.COM", "www.newark.com")]
    [InlineData("de.farnell.com", "de.farnell.com")]
    [InlineData("not-a-store", null)]
    public void FarnellStore_CanonicalizesLegacyNewarkId(string store, string? expected)
    {
        Assert.Equal(expected, DistributorSettingsService.CanonicalFarnellStore(store));
    }

    [Fact]
    public void FarnellParse_MapsLifecycleAttribute()
    {
        const string json = """
            {
              "manufacturerPartNumberSearchReturn": {
                "products": [
                  {
                    "sku": "1234567",
                    "brandName": "Texas Instruments",
                    "translatedManufacturerPartNumber": "LM358DR",
                    "displayName": "Op amp",
                    "datasheets": [{ "url": "https://example.com/lm358.pdf" }],
                    "productURL": "https://de.farnell.com/lm358",
                    "translatedMinimumOrderQuality": 2,
                    "packSize": 5,
                    "attributes": [
                      { "attributeLabel": "Product Status", "attributeValue": "Active" }
                    ]
                  }
                ]
              }
            }
            """;

        DistributorProduct product = Assert.Single(FarnellClient.Parse(json));
        Assert.Equal("farnell", product.Provider);
        Assert.Equal("Farnell", product.DistributorName);
        Assert.Equal("Texas Instruments", product.Manufacturer);
        Assert.Equal("LM358DR", product.ManufacturerPartNumber);
        Assert.Equal("1234567", product.OrderNumber);
        Assert.Equal("active", product.LifecycleStatus);
        Assert.Equal(2, product.QuantityMin);
        Assert.Equal(5, product.QuantitySteps);
        Assert.Equal("https://example.com/lm358.pdf", product.DatasheetUrl);
        Assert.Equal("https://de.farnell.com/lm358", product.ProductUrl);
    }

    [Fact]
    public void ArrowParse_ReadsNestedPartList_AndSearchToken()
    {
        const string json = """
            {
              "itemserviceresult": {
                "data": [
                  {
                    "resources": [
                      {
                        "partList": [
                          {
                            "partNum": "AR-1",
                            "mfrPartNum": "LM358DR",
                            "desc": "Op amp",
                            "manufacturer": { "mfrName": "Texas Instruments" },
                            "lifeCycleStatus": "Active",
                            "minOrderQty": 1,
                            "orderMultiple": 2,
                            "documents": [
                              { "type": "Datasheet", "url": "https://example.com/lm358.pdf" }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        DistributorProduct product = Assert.Single(ArrowClient.ParseProducts(json));
        Assert.Equal("arrow", product.Provider);
        Assert.Equal("Arrow Electronics", product.DistributorName);
        Assert.Equal("Texas Instruments", product.Manufacturer);
        Assert.Equal("LM358DR", product.ManufacturerPartNumber);
        Assert.Equal("AR-1", product.OrderNumber);
        Assert.Equal("active", product.LifecycleStatus);
        Assert.Equal(1, product.QuantityMin);
        Assert.Equal(2, product.QuantitySteps);
        Assert.Equal("https://example.com/lm358.pdf", product.DatasheetUrl);
        Assert.Equal("abc123", ArrowClient.ParseToken("""{"itemserviceresult":{"data":[{"searchToken":"abc123"}]}}"""));
        Assert.Equal("xyz", ArrowClient.ParseToken("""{"token":"xyz"}"""));
        Assert.Null(ArrowClient.ParseToken("{}"));
        Assert.Empty(ArrowClient.ParseProducts("not-json"));
    }

    [Fact]
    public void TmeParse_MapsProductList()
    {
        const string json = """
            {
              "status": "OK",
              "data": {
                "products": {
                  "elements": [
                    {
                      "symbol": "LM358DR-TME",
                      "manufacturer": { "name": "Texas Instruments" },
                      "description": "Op amp",
                      "manufacturer_symbols": ["LM358DR"],
                      "minimal_amount": 1,
                      "multiples": 5
                    }
                  ]
                }
              }
            }
            """;

        DistributorProduct product = Assert.Single(TmeClient.ParseSearch(json, "DE", "EN"));
        Assert.Equal("tme", product.Provider);
        Assert.Equal("TME", product.DistributorName);
        Assert.Equal("Texas Instruments", product.Manufacturer);
        Assert.Equal("LM358DR", product.ManufacturerPartNumber);
        Assert.Equal("LM358DR-TME", product.OrderNumber);
        Assert.Equal("https://www.tme.eu/de/en/details/lm358dr-tme/", product.ProductUrl);
        Assert.Equal(1, product.QuantityMin);
        Assert.Equal(5, product.QuantitySteps);
        Assert.Null(product.LifecycleStatus);
        Assert.Null(product.DatasheetUrl);
        Assert.False(TmeClient.IsError(json));
        Assert.True(TmeClient.IsError("""{"status":"ERROR"}"""));

        const string files = """
            {
              "status": "OK",
              "data": {
                "elements": [
                  {
                    "symbol": "LM358DR-TME",
                    "documents": {
                      "elements": [
                        { "url": "//www.tme.eu/Document/manual.pdf", "type": "INS", "language": "EN" },
                        { "url": "//www.tme.eu/Document/lm358.pdf", "type": "DTE", "language": "DE" },
                        { "url": "//www.tme.eu/Document/lm358-en.pdf", "type": "DTE", "language": "EN" }
                      ]
                    }
                  }
                ]
              }
            }
            """;

        Assert.Equal(
            "https://www.tme.eu/Document/lm358-en.pdf",
            Assert.Contains("LM358DR-TME", TmeClient.ParseDatasheetUrls(files, "EN")));
    }

    [Fact]
    public void DatasheetRules_AcceptOnlyHttpPdf()
    {
        Assert.Null(DistributorDatasheetImporter.ValidateUrl("https://cdn.example.com/lm358.pdf"));
        Assert.Null(DistributorDatasheetImporter.ValidateUrl(" http://cdn.example.com/lm358.pdf "));
        Assert.Equal("url is required", DistributorDatasheetImporter.ValidateUrl("  "));
        Assert.Equal("Only http and https URLs can be imported", DistributorDatasheetImporter.ValidateUrl("ftp://cdn.example.com/lm358.pdf"));
        Assert.Equal(OutboundUrl.PrivateAddressError, DistributorDatasheetImporter.ValidateUrl("http://127.0.0.1/secret"));
        Assert.Equal(OutboundUrl.PrivateAddressError, DistributorDatasheetImporter.ValidateUrl("http://169.254.169.254/latest"));
        Assert.Equal(OutboundUrl.PrivateAddressError, DistributorDatasheetImporter.ValidateUrl("http://localhost/admin"));
        Assert.Equal("The URL must not contain credentials", DistributorDatasheetImporter.ValidateUrl("https://user:secret@cdn.example.com/a.pdf"));

        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n");
        byte[] padded = Encoding.ASCII.GetBytes("\r\n%PDF-1.4\n");
        byte[] html = Encoding.ASCII.GetBytes("<html></html>");
        Assert.Null(DistributorDatasheetImporter.ValidatePdf(pdf, "application/pdf"));
        Assert.Null(DistributorDatasheetImporter.ValidatePdf(pdf, null));
        Assert.Null(DistributorDatasheetImporter.ValidatePdf(pdf, "text/html"));
        Assert.Null(DistributorDatasheetImporter.ValidatePdf(pdf, "application/x-download"));
        Assert.Null(DistributorDatasheetImporter.ValidatePdf(padded, "application/octet-stream"));
        Assert.Equal("The URL did not return a PDF", DistributorDatasheetImporter.ValidatePdf(html, "application/pdf"));

        const string tiPage = "https://www.ti.com/general/docs/suppproductinfo.tsp?distId=10&gotoUrl=https%3A%2F%2Fwww.ti.com%2Flit%2Fgpn%2FLM358";
        Assert.Equal("https://www.ti.com/lit/gpn/LM358", DistributorDatasheetImporter.UnwrapDocumentUrl(tiPage));
        Assert.Null(DistributorDatasheetImporter.UnwrapDocumentUrl("https://cdn.example.com/lm358.pdf"));

        byte[] interstitial = Encoding.UTF8.GetBytes(
            "<html><script>var reqUrl='https://www.ti.com/lit/gpn/LM358?DCM=yes';</script></html>");
        Assert.Equal(
            "https://www.ti.com/lit/gpn/LM358?DCM=yes",
            DistributorDatasheetImporter.ExtractRedirectUrl("https://www.ti.com/general/docs/suppproductinfo.tsp", interstitial));
        Assert.Equal(
            "https://cdn.example.com/files/lm358.pdf",
            DistributorDatasheetImporter.ExtractRedirectUrl(
                "https://www.example.com/view",
                Encoding.UTF8.GetBytes("<iframe src=\"https://cdn.example.com/files/lm358.pdf\"></iframe>")));
        Assert.Null(DistributorDatasheetImporter.ExtractRedirectUrl("https://cdn.example.com/a.pdf", pdf));

        Assert.Equal("lm358.pdf", DistributorDatasheetImporter.SanitizeFilename(null, "https://cdn.example.com/files/lm358.pdf?token=1"));
        Assert.Equal("secret.txt.pdf", DistributorDatasheetImporter.SanitizeFilename("../../secret.txt", "https://example.com/a"));
        Assert.Equal("sheet.PDF", DistributorDatasheetImporter.SanitizeFilename("sheet.PDF", "https://example.com/a"));
        Assert.Equal("datasheet.pdf", DistributorDatasheetImporter.SanitizeFilename("   ", "https://example.com/"));
    }

    [Fact]
    public void DistributorLog_StripsSecretsFromUrls()
    {
        Assert.Equal(
            "https://api.mouser.com/search?apiKey=***&x=1",
            DistributorLog.Sanitize("https://api.mouser.com/search?apiKey=secret&x=1"));
        Assert.Equal(
            "https://api.arrow.com/token?login=a&apikey=***",
            DistributorLog.Sanitize("https://api.arrow.com/token?login=a&apikey=secret"));
        Assert.DoesNotContain("hunter2", DistributorLog.Sanitize("grant client_secret=hunter2"));
    }

    [Fact]
    public void ProductImage_AcceptsKnownFormatsAndRejectsOversizedOrUnknownBytes()
    {
        byte[] jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0x00 };
        Assert.Null(ProductImageStore.Validate(jpeg, out string? jpegType));
        Assert.Equal("image/jpeg", jpegType);
        Assert.Equal("image/png", ProductImageStore.DetectMediaType(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Assert.Equal("image/gif", ProductImageStore.DetectMediaType(Encoding.ASCII.GetBytes("GIF89a")));
        byte[] webp = new byte[12];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(webp, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(webp, 8);
        Assert.Equal("image/webp", ProductImageStore.DetectMediaType(webp));

        Assert.Equal("The product image must be a JPEG, PNG, GIF, or WebP file", ProductImageStore.Validate(new byte[] { 1, 2, 3, 4 }, out _));
        Assert.Equal("The product image is larger than 5 MB", ProductImageStore.Validate(new byte[ProductImageStore.MaxBytes + 1], out _));
        Assert.Null(ProductImageStore.NormalizeHash(""));
        Assert.Equal(new string('a', 64), ProductImageStore.NormalizeHash(new string('A', 64)));
        Assert.Null(ProductImageStore.NormalizeHash(new string('g', 64)));
    }

}
